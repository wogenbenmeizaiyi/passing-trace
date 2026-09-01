using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace PassingTrace.Events.Api.Ai;

/// <summary>
/// Builds MEAI clients from named OpenAI-compatible providers. Application code remains provider agnostic.
/// </summary>
public sealed class AiClientFactory : IDisposable
{
    private readonly IChatClient _assistantChatClient;
    private readonly IChatClient _semanticChatClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public AiClientFactory(IOptions<AiModelOptions> options)
    {
        var value = options.Value;
        _assistantChatClient = CreateChatClient(value, value.Assistant);
        _semanticChatClient = CreateChatClient(value, value.Semantic);
        _embeddingGenerator = CreateEmbeddingGenerator(value, value.Embedding);
    }

    public IChatClient AssistantChatClient => _assistantChatClient;
    public IChatClient SemanticChatClient => _semanticChatClient;
    public IEmbeddingGenerator<string, Embedding<float>> EmbeddingGenerator => _embeddingGenerator;

    public void Dispose()
    {
        _assistantChatClient.Dispose();
        _semanticChatClient.Dispose();
        _embeddingGenerator.Dispose();
    }

    private static IChatClient CreateChatClient(AiModelOptions options, ChatModelSelection selection)
    {
        if (!TryGetProvider(options, selection.Provider, out var provider, out var error))
        {
            return new UnavailableChatClient(error);
        }
        if (string.IsNullOrWhiteSpace(selection.PrimaryModel))
        {
            return new UnavailableChatClient($"AI Provider {selection.Provider} 未配置主模型。");
        }

        var client = CreateOpenAiClient(provider!);
        IChatClient selected = client.GetChatClient(selection.PrimaryModel).AsIChatClient();
        if (!string.IsNullOrWhiteSpace(selection.FallbackModel))
        {
            var fallback = client.GetChatClient(selection.FallbackModel).AsIChatClient();
            selected = new PreOutputFallbackChatClient(selected, fallback);
        }
        return provider!.StripThinkingTags ? new ThinkingTagFilteringChatClient(selected) : selected;
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        AiModelOptions options,
        EmbeddingModelSelection selection)
    {
        if (!TryGetProvider(options, selection.Provider, out var provider, out var error))
        {
            return new UnavailableEmbeddingGenerator(error);
        }
        if (string.IsNullOrWhiteSpace(selection.Model))
        {
            return new UnavailableEmbeddingGenerator($"AI Provider {selection.Provider} 未配置 Embedding 模型。");
        }

        var client = CreateOpenAiClient(provider!);
        return client.GetEmbeddingClient(selection.Model).AsIEmbeddingGenerator(selection.Dimensions);
    }

    private static OpenAIClient CreateOpenAiClient(OpenAiCompatibleProviderOptions provider) =>
        new(
            new ApiKeyCredential(provider.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(provider.Endpoint.TrimEnd('/') + "/") });

    private static bool TryGetProvider(
        AiModelOptions options,
        string providerName,
        out OpenAiCompatibleProviderOptions? provider,
        out string error)
    {
        provider = options.Providers.FirstOrDefault(x =>
            string.Equals(x.Key, providerName, StringComparison.OrdinalIgnoreCase)).Value;
        if (provider is null)
        {
            error = $"未找到 AI Provider {providerName} 的配置。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(provider.Endpoint))
        {
            error = $"AI Provider {providerName} 未配置 Endpoint。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            error = $"AI Provider {providerName} 未配置 API Key。";
            return false;
        }
        error = string.Empty;
        return true;
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
            if (!hasFirst) yield break;

            yield return primaryEnumerator.Current;
            while (await primaryEnumerator.MoveNextAsync()) yield return primaryEnumerator.Current;
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

    private sealed class ThinkingTagFilteringChatClient(IChatClient inner) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var response = await inner.GetResponseAsync(messages, options, cancellationToken);
            foreach (var message in response.Messages)
            {
                for (var index = 0; index < message.Contents.Count; index++)
                {
                    if (message.Contents[index] is TextContent text)
                    {
                        message.Contents[index] = new TextContent(AiTextSanitizer.RemoveThinking(text.Text));
                    }
                }
            }
            return response;
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var filter = new StreamingThinkingFilter();
            await foreach (var update in inner.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                for (var index = 0; index < update.Contents.Count; index++)
                {
                    if (update.Contents[index] is TextContent text)
                    {
                        update.Contents[index] = new TextContent(filter.Push(text.Text));
                    }
                }
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(ThinkingTagFilteringChatClient) ? this : inner.GetService(serviceType, serviceKey);

        public void Dispose() => inner.Dispose();
    }

    private sealed class UnavailableChatClient(string message) : IChatClient
    {
        private InvalidOperationException Error() => new(message);
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

    private sealed class UnavailableEmbeddingGenerator(string message) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<GeneratedEmbeddings<Embedding<float>>>(new InvalidOperationException(message));
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType == GetType() ? this : null;
        public void Dispose() { }
    }
}

public static class AiTextSanitizer
{
    public static string RemoveThinking(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var start = value.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return value;
        var end = value.IndexOf("</think>", start, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return value[..start];
        return string.Concat(value.AsSpan(0, start), value.AsSpan(end + "</think>".Length)).TrimStart();
    }
}

public sealed class StreamingThinkingFilter
{
    private const string StartTag = "<think>";
    private const string EndTag = "</think>";
    private readonly System.Text.StringBuilder _pending = new();
    private bool _decided;

    public string Push(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (_decided) return value;

        _pending.Append(value);
        var buffered = _pending.ToString();
        var trimmed = buffered.TrimStart();
        if (trimmed.StartsWith(StartTag, StringComparison.OrdinalIgnoreCase))
        {
            var end = trimmed.IndexOf(EndTag, StringComparison.OrdinalIgnoreCase);
            if (end < 0) return string.Empty;
            _decided = true;
            _pending.Clear();
            return trimmed[(end + EndTag.Length)..].TrimStart();
        }

        if (StartTag.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)) return string.Empty;
        _decided = true;
        _pending.Clear();
        return buffered;
    }
}
