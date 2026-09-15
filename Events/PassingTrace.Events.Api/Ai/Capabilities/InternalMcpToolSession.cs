using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PassingTrace.Events.Api.Ai.Capabilities;

/// <summary>
/// A private, request-owned MCP connection. Discovery and invocation use the official SDK's
/// JSON-RPC stream transport; no port, subprocess, credentials or public tool endpoint exists.
/// The server retains this request's tool instances, authenticated user and evidence snapshot.
/// </summary>
public sealed class InternalMcpToolSession : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime;
    private readonly McpServer _server;
    private readonly Task _serverTask;
    private readonly SemaphoreSlim _invocationGate = new(1, 1);
    private McpClient? _client;
    private int _invalidCalls;
    private int _disposed;

    private InternalMcpToolSession(McpServer server, CancellationTokenSource lifetime)
    {
        _server = server;
        _lifetime = lifetime;
        _serverTask = server.RunAsync(lifetime.Token);
    }

    public IReadOnlyList<AITool> Tools { get; private set; } = [];

    public static async Task<InternalMcpToolSession> CreateAsync(
        IEnumerable<AIFunction> functions, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registered = functions.Select(NullableMcpResultFunction.WrapIfNullable).ToArray();
        if (registered.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != registered.Length)
            throw new InvalidOperationException("内部工具名称不能重复。");
        var serverOptions = new McpServerOptions
        {
            ServerInfo = new() { Name = "passingtrace-personal-records", Version = "1.0.0" },
            ScopeRequests = false,
            ToolCollection = [],
        };
        foreach (var function in registered)
            serverOptions.ToolCollection.Add(new ValidatedMcpServerTool(McpServerTool.Create(function, new()
            {
                ReadOnly = true,
                Destructive = false,
                OpenWorld = false,
                UseStructuredContent = true,
            })));

        var requests = new Pipe();
        var responses = new Pipe();
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Deliberately omit a protocol logger: frames contain private search terms and evidence.
        var server = McpServer.Create(new StreamServerTransport(
            requests.Reader.AsStream(), responses.Writer.AsStream()), serverOptions);
        var session = new InternalMcpToolSession(server, lifetime);
        try
        {
            using var setupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            setupTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            session._client = await McpClient.CreateAsync(new StreamClientTransport(
                    requests.Writer.AsStream(), responses.Reader.AsStream()),
                new McpClientOptions
                {
                    ClientInfo = new() { Name = "passingtrace-assistant", Version = "1.0.0" },
                    ProtocolVersion = "2026-07-28",
                    InitializationTimeout = TimeSpan.FromSeconds(5),
                }, cancellationToken: setupTimeout.Token);
            var discovered = await session._client.ListToolsAsync(cancellationToken: setupTimeout.Token);
            if (!registered.Select(x => x.Name).Order().SequenceEqual(discovered.Select(x => x.Name).Order()))
                throw new AssistantToolInvocationException();
            session.Tools = discovered.Select(tool => (AITool)new CheckedClientTool(tool, session)).ToArray();
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifetime.CancelAsync();
        try
        {
            if (_client is not null) await _client.DisposeAsync();
        }
        finally
        {
            await _server.DisposeAsync();
            try { await _serverTask; }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            finally
            {
                _lifetime.Dispose();
                _invocationGate.Dispose();
            }
        }
    }

    private sealed class CheckedClientTool(McpClientTool tool, InternalMcpToolSession session) : DelegatingAIFunction(tool)
    {
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref session._disposed) != 0, session);
            using var invocationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, session._lifetime.Token);
            var invocationToken = invocationCancellation.Token;
            // All tools in this session share one DbContext and snapshot; never invoke them in parallel.
            await session._invocationGate.WaitAsync(invocationToken);
            try
            {
                var result = await tool.CallAsync(arguments, cancellationToken: invocationToken);
                if (result.IsError == true)
                {
                    var invalid = result.StructuredContent is JsonElement { ValueKind: JsonValueKind.Object } content &&
                        content.TryGetProperty("code", out var code) && code.GetString() == "invalid_tool_arguments";
                    if (!invalid || Interlocked.Increment(ref session._invalidCalls) > 1)
                        throw new AssistantToolInvocationException();
                }
                // Preserve the standard MCP result envelope (including isError); never reinterpret
                // a tool failure as an empty result or a zero statistic.
                return JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
            }
            catch (OperationCanceledException)
            {
                // The connection belongs to one answer. End it when a tool is canceled so an
                // interrupted client request cannot leave a database operation running unseen.
                await session._lifetime.CancelAsync();
                throw;
            }
            catch (Exception exception) when (exception is not AssistantToolInvocationException)
            {
                throw new AssistantToolInvocationException();
            }
            finally { session._invocationGate.Release(); }
        }
    }
}
