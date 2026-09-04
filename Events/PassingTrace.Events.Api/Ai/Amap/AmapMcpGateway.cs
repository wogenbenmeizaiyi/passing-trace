using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using PassingTrace.Events.Api.Places;

namespace PassingTrace.Events.Api.Ai.Amap;

public sealed record AmapMcpResponse(string Text, JsonElement? StructuredContent);

public interface IAmapMcpGateway
{
    bool IsConfigured { get; }
    Task<AmapMcpResponse> CallAsync(
        IReadOnlyList<string> toolNames,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);
}

/// <summary>
/// 高德官方 Streamable HTTP MCP 的最薄传输层。上层只使用星期八自己的稳定合同，
/// 不把供应商工具对象或返回 URL 直接交给模型和客户端。
/// </summary>
public sealed class AmapMcpGateway(
    HttpClient httpClient,
    IOptions<AmapOptions> options,
    ILogger<AmapMcpGateway> logger) : IAmapMcpGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.EffectiveMcpKey);

    public async Task<AmapMcpResponse> CallAsync(
        IReadOnlyList<string> toolNames,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new AmapCapabilityUnavailableException("高德地图能力尚未配置。");

        var endpoint = BuildEndpoint(options.Value);
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            Name = "amap",
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(8),
            EnableStandaloneGetStream = false,
            MaxReconnectionAttempts = 0,
        };

        try
        {
            await using var transport = new HttpClientTransport(
                transportOptions, httpClient, NullLoggerFactory.Instance, ownsHttpClient: false);
            await using var client = await McpClient.CreateAsync(
                transport, loggerFactory: NullLoggerFactory.Instance, cancellationToken: cancellationToken);
            var availableTools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            var toolName = toolNames.FirstOrDefault(candidate =>
                availableTools.Any(tool => string.Equals(tool.Name, candidate, StringComparison.Ordinal)));
            if (toolName is null)
                throw new AmapCapabilityUnavailableException("高德地图暂不支持这项能力。");
            var result = await client.CallToolAsync(
                toolName, arguments, progress: null, options: null, cancellationToken: cancellationToken);
            var envelope = JsonSerializer.SerializeToElement(result, JsonOptions);
            var isError = envelope.TryGetProperty("isError", out var errorElement) &&
                errorElement.ValueKind == JsonValueKind.True;
            var text = ExtractText(envelope);
            if (isError)
                throw new AmapCapabilityUnavailableException("高德地图暂时无法完成这次查询。");
            JsonElement? structured = envelope.TryGetProperty("structuredContent", out var structuredElement) &&
                structuredElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
                ? structuredElement.Clone()
                : null;
            return new AmapMcpResponse(text, structured);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AmapCapabilityUnavailableException)
        {
            throw;
        }
        catch (Exception)
        {
            // 不记录原始异常：MCP endpoint 的查询参数包含 Key。
            logger.LogWarning("高德 MCP 工具调用失败。候选工具数量：{ToolCount}。", toolNames.Count);
            throw new AmapCapabilityUnavailableException("高德地图暂时不可用，请稍后再试。");
        }
    }

    private static Uri BuildEndpoint(AmapOptions value)
    {
        if (!Uri.TryCreate(value.McpEndpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(endpoint.Host, "mcp.amap.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new AmapCapabilityUnavailableException("高德 MCP 地址配置无效。");
        }

        var builder = new UriBuilder(endpoint)
        {
            Query = $"key={Uri.EscapeDataString(value.EffectiveMcpKey)}",
        };
        return builder.Uri;
    }

    private static string ExtractText(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return string.Empty;
        return string.Join('\n', content.EnumerateArray()
            .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "text" &&
                item.TryGetProperty("text", out _))
            .Select(item => item.GetProperty("text").GetString())
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }
}

public sealed class AmapCapabilityUnavailableException(string message) : Exception(message);
