using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace PassingTrace.Events.Api.Ai;

/// <summary>
/// 将百炼 OpenAI-compatible endpoint 隔离在适配层；业务层只依赖 MEAI 抽象。
/// </summary>
public sealed class QwenClientFactory : IDisposable
{
    private readonly IChatClient _chatClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public QwenClientFactory(IOptions<QwenAiOptions> options)
    {
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.ApiKey))
        {
            _chatClient = new UnavailableChatClient();
            _embeddingGenerator = new UnavailableEmbeddingGenerator();
            return;
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(value.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(value.Endpoint.TrimEnd('/') + "/") });
        var primary = client.GetChatClient(value.PrimaryModel).AsIChatClient();
        var fallback = client.GetChatClient(value.FallbackModel).AsIChatClient();
        _chatClient = new PreOutputFallbackChatClient(primary, fallback);
        _embeddingGenerator = client.GetEmbeddingClient(value.EmbeddingModel)
            .AsIEmbeddingGenerator(value.EmbeddingDimensions);
    }

    public IChatClient ChatClient => _chatClient;
    public IEmbeddingGenerator<string, Embedding<float>> EmbeddingGenerator => _embeddingGenerator;

    public void Dispose()
    {
        _chatClient.Dispose();
        _embeddingGenerator.Dispose();
    }

    private sealed class PreOutputFallbackChatClient(IChatClient primary, IChatClient fallback) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var materialized = messages as IReadOnlyList<ChatMessage> ?? messages.ToArray();
            try
            {
                return await primary.GetResponseAsync(materialized, options, cancellationToken);
            }
            catch (ClientResultException exception) when (CanFallback(exception))
            {
                return await fallback.GetResponseAsync(materialized, options, cancellationToken);
            }
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var materialized = messages as IReadOnlyList<ChatMessage> ?? messages.ToArray();
            await using var primaryEnumerator = primary
                .GetStreamingResponseAsync(materialized, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            bool hasFirst = false;
            var useFallback = false;
            try
            {
                hasFirst = await primaryEnumerator.MoveNextAsync();
            }
            catch (ClientResultException exception) when (CanFallback(exception))
            {
                useFallback = true;
            }

            if (useFallback)
            {
                await foreach (var update in fallback.GetStreamingResponseAsync(materialized, options, cancellationToken))
                {
                    yield return update;
                }
                yield break;
            }

            if (!hasFirst)
            {
                yield break;
            }

            yield return primaryEnumerator.Current;
            while (await primaryEnumerator.MoveNextAsync())
            {
                yield return primaryEnumerator.Current;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(PreOutputFallbackChatClient) ? this : primary.GetService(serviceType, serviceKey);

        public void Dispose()
        {
            primary.Dispose();
            fallback.Dispose();
        }

        private static bool CanFallback(ClientResultException exception) =>
            exception.Status == 429 || exception.Status >= 500;
    }

    private sealed class UnavailableChatClient : IChatClient
    {
        private static InvalidOperationException Error() =>
            new("Qwen API Key 未配置。请通过 User Secrets 或部署 Secret 设置 Qwen:ApiKey。");
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromException<ChatResponse>(Error());
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.FromException(Error());
            yield break;
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType == GetType() ? this : null;
        public void Dispose() { }
    }

    private sealed class UnavailableEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<GeneratedEmbeddings<Embedding<float>>>(
                new InvalidOperationException("Qwen API Key 未配置，无法生成向量。"));
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType == GetType() ? this : null;
        public void Dispose() { }
    }
}
