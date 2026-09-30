using PassingTrace.Events.Api.Ai.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Core.Media;
using PassingTrace.Core.Storylines;
using PassingTrace.Events.Api.Media;

namespace PassingTrace.Ai.Worker;

public sealed class SemanticPipeline(
    ISemanticPipelineRepository repository,
    IAnalysisOutbox outbox,
    IObjectStorage storage,
    ImageDerivativeProcessor imageProcessor,
    IChatClient chatClient,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IOptions<AiModelOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private const string SemanticSystemPrompt = """
        你是 PassingTrace 的记录分析器。只提取用户明确写下或图片中可直接观察到的事实，禁止猜测身份、健康、政治、宗教等敏感属性。
        只输出一个合法 JSON 对象，不要 Markdown、解释或其他文字。JSON 结构必须是：
        {"summary":"字符串","images":[{"mediaId":"UUID","description":"字符串"}],
        "mentions":[{"category":"字符串","normalizedValue":"字符串","originalValue":"字符串","assertion":"explicit|inferred","confidence":0.0,"textStart":null,"textLength":null,"mediaId":null}],
        "expenses":[{"amount":0.0,"currency":"CNY","purpose":"字符串","scope":"字符串","confidence":0.0,"evidence":"字符串"}],
        "memories":[{"type":"preference|background|habit|goal|constraint","content":"字符串","confidence":0.0,"evidence":"字符串"}],
        "primaryCategory":{"taxonomyKey":"other","confidence":0.0,"textStart":null,"textLength":null,"mediaId":null},
        "behaviorTags":[{"taxonomyKey":"标签Key","confidence":0.0,"textStart":null,"textLength":null,"mediaId":null}]}。
        没有内容的数组输出 []，没有主分类候选时 primaryCategory 输出 null。summary 用中文简述这一条记录；mentions 提取 location/activity/food/person 等检索项；
        PrimaryCategory.taxonomyKey 必须从 food,shopping,travel,scenery,entertainment,exercise,work,study,social,home,health,transport,other 中选择一个。
        BehaviorTags 最多 5 个，taxonomyKey 必须从系统提供的行为标签 Key 中选择，不得自创；每个分类和标签必须给出置信度及正文位置或 mediaId 证据。
        expenses 仅在金额和币种足够明确时输出；memories 只输出可由本记录证据支持、以后有稳定价值的偏好/背景/习惯/目标/约束。
        金额是跨分类标签 amount，不替代美食、购物、交通等主分类。有明确金额事实时提取 expenses 并标记 amount；日期、人数、距离、编号不是金额。
        同一笔金额不要把合计和其明细重复提取；未发生的预算、标价或他人的消费不能当作本人实际支出，不明确时保留在摘要中说明，不猜测金额。
        每条 memory 的 evidence 必须说明来自正文或哪个 mediaId。图片描述写入 images，不能把图片里看不清的内容当事实。
        """;

    public async Task IndexStorylineAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(message.PayloadJson);
        var storylineId = payload.RootElement.GetProperty("storylineId").GetGuid();
        var revision = payload.RootElement.GetProperty("revision").GetInt32();
        var index = await repository.FindCurrentStorylineIndexAsync(message.UserId, storylineId, revision, cancellationToken);
        if (index is null || string.IsNullOrWhiteSpace(index.RetrievalText)) return;
        var generated = await embeddingGenerator.GenerateAsync([index.RetrievalText], cancellationToken: cancellationToken);
        repository.SetEmbedding(index, generated[0].Vector.ToArray());
        index.UpdatedAt = DateTimeOffset.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveStorylineFromSearchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(message.PayloadJson);
        var storylineId = payload.RootElement.GetProperty("storylineId").GetGuid();
        await repository.DisableStorylineIndexesAsync(message.UserId, storylineId, cancellationToken);
    }

    public async Task ProcessMediaAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.MediaAssetId is null)
        {
            throw new InvalidOperationException("media.process 缺少 mediaAssetId。");
        }

        var asset = await repository.FindMediaAsync(message.UserId, message.MediaAssetId.Value, cancellationToken);
        if (asset is null || asset.DeletedAt is not null || asset.Kind != MediaKind.Image)
        {
            return;
        }

        asset.Status = MediaAssetStatus.Processing;
        asset.UpdatedAt = DateTimeOffset.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);

        try
        {
            await using var source = await storage.OpenReadAsync(asset.ObjectKey, cancellationToken);
            var derivatives = await imageProcessor.ProcessAsync(source, cancellationToken);
            var baseKey = $"users/{asset.UserId}/derived/{asset.Id:N}";
            var aiKey = $"{baseKey}/ai.jpg";
            var thumbnailKey = $"{baseKey}/thumbnail.jpg";
            await using var aiStream = new MemoryStream(derivatives.AiImage, writable: false);
            await storage.PutAsync(aiKey, aiStream, "image/jpeg", aiStream.Length, cancellationToken);
            await using var thumbnailStream = new MemoryStream(derivatives.Thumbnail, writable: false);
            await storage.PutAsync(thumbnailKey, thumbnailStream, "image/jpeg", thumbnailStream.Length, cancellationToken);

            asset.AiObjectKey = aiKey;
            asset.ThumbnailObjectKey = thumbnailKey;
            asset.Status = MediaAssetStatus.Ready;
            asset.ProcessingError = null;
            asset.UpdatedAt = DateTimeOffset.UtcNow;
            await IncrementWatermarkAsync(asset.UserId, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            asset.Status = MediaAssetStatus.Failed;
            asset.ProcessingError = Limit(exception.Message, 2048);
            asset.UpdatedAt = DateTimeOffset.UtcNow;
            await repository.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task AnalyzeEventAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.EventId is null || message.SourceRevision is null)
        {
            throw new InvalidOperationException("event.analyze 缺少 eventId/sourceRevision。");
        }

        var evt = await repository.FindEventAsync(message.UserId, message.EventId.Value, cancellationToken);
        if (evt is null || evt.DeletedAt is not null || evt.CurrentSourceRevision != message.SourceRevision)
        {
            await repository.MarkStaleAsync(message.UserId, message.EventId.Value, message.SourceRevision.Value, cancellationToken);
            return;
        }

        var pipeline = options.Value;
        var force = message.PayloadJson.Contains("\"force\":true", StringComparison.OrdinalIgnoreCase);
        var alreadyDone = !force && await repository.HasCompletedRunAsync(evt.UserId, evt.Id,
            message.SourceRevision.Value, pipeline.PipelineVersion, cancellationToken);
        if (alreadyDone)
        {
            return;
        }

        var source = evt.SourceRevisions.Single(x => x.Revision == message.SourceRevision);
        var run = new EventSemanticRun
        {
            UserId = evt.UserId,
            EventId = evt.Id,
            SourceRevision = source.Revision,
            PipelineVersion = pipeline.PipelineVersion,
            PromptVersion = pipeline.PromptVersion,
            SchemaVersion = "semantic-envelope-v2",
            Model = pipeline.Semantic.PrimaryModel,
            Status = SemanticRunStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        repository.Add(run);
        await repository.SaveChangesAsync(cancellationToken);
        var started = TimeProvider.System.GetTimestamp();

        try
        {
            var envelope = (await ExtractSemanticAsync(source, cancellationToken)).WithAmountTag();
            await repository.ReloadEventAsync(evt, cancellationToken);
            if (evt.DeletedAt is not null || evt.CurrentSourceRevision != source.Revision)
            {
                run.Status = SemanticRunStatus.Stale;
                run.CompletedAt = DateTimeOffset.UtcNow;
                await repository.SaveChangesAsync(cancellationToken);
                return;
            }

            run.SemanticEnvelopeJson = JsonSerializer.Serialize(envelope, JsonOptions);
            run.Summary = Limit(envelope.Summary, 4000);
            run.DurationMilliseconds = (long)TimeProvider.System.GetElapsedTime(started).TotalMilliseconds;
            run.Status = SemanticRunStatus.Completed;
            run.CompletedAt = DateTimeOffset.UtcNow;
            PublishMentions(run, evt.UserId, envelope);
            PublishExpenses(run, evt.UserId, envelope);
            await PublishEffectiveLabelsAsync(evt, source, run, envelope, cancellationToken);

            var imageDescriptions = string.Join('\n', envelope.Images.Select(x => x.Description));
            var effectiveLabels = (await repository.ReadCurrentLabelsAsync(evt.UserId, evt.Id, source.Revision, cancellationToken))
                .Select(x => x.DisplayName);
            var locationText = source.Locations.SelectMany(x => new[] { x.Name, x.Address, x.City, x.District, x.PoiType });
            var retrievalText = string.Join('\n', new[]
            {
                source.Title,
                source.RawContent,
                envelope.Summary,
                imageDescriptions,
                string.Join(' ', envelope.Mentions.Select(x => x.NormalizedValue)),
                string.Join(' ', effectiveLabels),
                string.Join(' ', locationText.Where(x => !string.IsNullOrWhiteSpace(x))),
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var embedding = await embeddingGenerator.GenerateAsync([retrievalText], cancellationToken: cancellationToken);
            await repository.DisableOlderEventIndexesAsync(evt.UserId, evt.Id, source.Revision, cancellationToken);
            var searchIndex = await repository.FindEventIndexAsync(evt.UserId, evt.Id, source.Revision, cancellationToken);
            if (searchIndex is null)
            {
                searchIndex = new EventSearchIndex { UserId = evt.UserId, EventId = evt.Id, SourceRevision = source.Revision };
                repository.Add(searchIndex);
            }
            searchIndex.SemanticRunId = run.Id;
            searchIndex.Title = source.Title ?? string.Empty;
            searchIndex.RawContent = source.RawContent ?? string.Empty;
            searchIndex.AiSummary = envelope.Summary;
            searchIndex.ImageDescriptions = imageDescriptions;
            searchIndex.RetrievalText = retrievalText;
            searchIndex.IsCurrent = true;
            searchIndex.UpdatedAt = DateTimeOffset.UtcNow;
            repository.SetEmbedding(searchIndex, embedding[0].Vector.ToArray());

            await PublishUserPlaceAsync(evt, source, cancellationToken);
            await PublishMemoriesAsync(evt.UserId, evt.Id, source.Revision, run, envelope.Memories, cancellationToken);
            await IncrementWatermarkAsync(evt.UserId, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            run.Status = SemanticRunStatus.Failed;
            run.ErrorCode = exception.GetType().Name;
            run.ErrorMessage = Limit(exception.Message, 2048);
            run.DurationMilliseconds = (long)TimeProvider.System.GetElapsedTime(started).TotalMilliseconds;
            run.CompletedAt = DateTimeOffset.UtcNow;
            await repository.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task RemoveEventFromSearchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.EventId is null) return;
        await repository.DisableEventIndexesAsync(message.UserId, message.EventId.Value, cancellationToken);
    }

    private async Task<SemanticEnvelope> ExtractSemanticAsync(
        SourceRevision source,
        CancellationToken cancellationToken)
    {
        var content = new List<AIContent>
        {
            new TextContent($"eventId={source.EventId}; sourceRevision={source.Revision}\n标题：{source.Title}\n正文：{source.RawContent}"),
        };
        var images = source.MediaAssets.OrderBy(x => x.SortOrder)
            .Where(x => x.MediaAsset.Kind == MediaKind.Image && x.MediaAsset.DeletedAt == null)
            .ToArray();
        if (images.Any(x => x.MediaAsset.Status is MediaAssetStatus.Uploaded or MediaAssetStatus.Processing))
        {
            throw new InvalidOperationException("图片衍生文件尚未处理完成，稍后重试语义分析。");
        }
        var provider = options.Value.Providers.FirstOrDefault(x =>
            string.Equals(x.Key, options.Value.Semantic.Provider, StringComparison.OrdinalIgnoreCase)).Value;
        var useRemoteMediaUrls = provider?.UseRemoteMediaUrls == true;
        foreach (var link in images.Where(x => x.MediaAsset.Status == MediaAssetStatus.Ready &&
                     !string.IsNullOrWhiteSpace(x.MediaAsset.AiObjectKey)))
        {
            content.Add(new TextContent($"下面图片的 mediaId={link.MediaAssetId}"));
            if (useRemoteMediaUrls)
            {
                var imageUrl = await storage.CreateDownloadUrlAsync(
                    link.MediaAsset.AiObjectKey!,
                    $"{link.MediaAssetId:N}.jpg",
                    "image/jpeg",
                    inline: true,
                    DateTimeOffset.UtcNow.AddMinutes(10),
                    cancellationToken);
                content.Add(new UriContent(imageUrl, "image/jpeg"));
            }
            else
            {
                await using var stream = await storage.OpenReadAsync(link.MediaAsset.AiObjectKey!, cancellationToken);
                await using var bytes = new MemoryStream();
                await stream.CopyToAsync(bytes, cancellationToken);
                content.Add(new DataContent(bytes.ToArray(), "image/jpeg"));
            }
        }

        var messages = new[]
        {
            new ChatMessage(ChatRole.System, SemanticSystemPrompt),
            new ChatMessage(ChatRole.User, content),
        };
        var response = await chatClient.GetResponseAsync(messages, new ChatOptions
        {
            Temperature = 0.1f,
            ResponseFormat = ChatResponseFormat.ForJsonSchema<SemanticEnvelope>(),
        }, cancellationToken);
        if (TryDeserialize(response.Text, out var result))
        {
            return result!;
        }

        var repair = await chatClient.GetResponseAsync([
            new ChatMessage(ChatRole.System, SemanticSystemPrompt + "\n把下面内容修复为符合上述结构的 JSON，只输出 JSON。"),
            new ChatMessage(ChatRole.User, response.Text),
        ], new ChatOptions
        {
            Temperature = 0.1f,
            ResponseFormat = ChatResponseFormat.ForJsonSchema<SemanticEnvelope>(),
        }, cancellationToken);
        if (!TryDeserialize(repair.Text, out result))
        {
            throw new InvalidDataException("语义模型连续两次返回无法解析的 SemanticEnvelope JSON。");
        }
        return result!;
    }

    private void PublishMentions(EventSemanticRun run, long userId, SemanticEnvelope envelope)
    {
        foreach (var value in envelope.Mentions.Take(100))
        {
            run.Mentions.Add(new SemanticMention
            {
                UserId = userId,
                Category = Limit(value.Category, 64),
                NormalizedValue = Limit(value.NormalizedValue, 512),
                OriginalValue = Limit(value.OriginalValue, 512),
                Assertion = Enum.TryParse<SemanticAssertion>(value.Assertion, true, out var assertion)
                    ? assertion : SemanticAssertion.Inferred,
                Confidence = Math.Clamp(value.Confidence, 0, 1),
                TextStart = value.TextStart,
                TextLength = value.TextLength,
                MediaAssetId = value.MediaId,
            });
        }
        if (envelope.PrimaryCategory is { } primary && EventTaxonomy.IsCategory(primary.TaxonomyKey))
        {
            run.Mentions.Add(LabelMention(userId, "primary_category", primary,
                EventTaxonomy.CategoryLabel(primary.TaxonomyKey)));
        }
        foreach (var tag in (envelope.BehaviorTags ?? []).Take(5).Where(x => EventTaxonomy.IsBehaviorTag(x.TaxonomyKey)))
        {
            run.Mentions.Add(LabelMention(userId, "behavior_tag", tag,
                EventTaxonomy.BehaviorTagLabel(tag.TaxonomyKey)));
        }
    }

    private static SemanticMention LabelMention(long userId, string category, SemanticLabelData value, string display) => new()
    {
        UserId = userId,
        Category = category,
        NormalizedValue = display,
        OriginalValue = display,
        Assertion = SemanticAssertion.Inferred,
        Confidence = Math.Clamp(value.Confidence, 0, 1),
        TextStart = value.TextStart,
        TextLength = value.TextLength,
        MediaAssetId = value.MediaId,
    };

    private async Task PublishEffectiveLabelsAsync(Event evt, SourceRevision source, EventSemanticRun run,
        SemanticEnvelope envelope, CancellationToken cancellationToken)
    {
        await repository.DisableOlderLabelsAsync(evt.UserId, evt.Id, source.Revision, cancellationToken);
        var current = await repository.ReadCurrentLabelsAsync(evt.UserId, evt.Id, source.Revision, cancellationToken);
        var manualPrimary = current.Any(x => x.Type == EventLabelType.PrimaryCategory && x.Origin == EventLabelOrigin.Manual);
        if (!manualPrimary)
        {
            foreach (var old in current.Where(x => x.Type == EventLabelType.PrimaryCategory)) old.IsCurrent = false;
            var key = envelope.PrimaryCategory is { } candidate && candidate.Confidence >= 0.60m && EventTaxonomy.IsCategory(candidate.TaxonomyKey)
                ? candidate.TaxonomyKey.ToLowerInvariant() : "other";
            repository.Add(NewAiLabel(evt, source.Revision, run.Id, EventLabelType.PrimaryCategory,
                key, EventTaxonomy.CategoryLabel(key), envelope.PrimaryCategory?.Confidence ?? 0));
        }

        var excluded = source.Labels.Where(x => x.Decision == SourceLabelDecision.Exclude)
            .Select(x => x.TaxonomyKey).Where(x => x is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = current.Where(x => x.Type == EventLabelType.BehaviorTag && x.Origin == EventLabelOrigin.Manual)
            .Select(x => x.NormalizedValue).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var old in current.Where(x => x.Type == EventLabelType.BehaviorTag && x.Origin == EventLabelOrigin.Ai)) old.IsCurrent = false;
        var remaining = Math.Max(0, 10 - normalized.Count);
        foreach (var tag in (envelope.BehaviorTags ?? []).Where(x => x.Confidence >= 0.70m &&
                     EventTaxonomy.IsBehaviorTag(x.TaxonomyKey) && !excluded.Contains(x.TaxonomyKey)).Take(remaining))
        {
            var key = tag.TaxonomyKey.ToLowerInvariant();
            var display = EventTaxonomy.BehaviorTagLabel(key);
            if (!normalized.Add(EventTaxonomy.NormalizedValue(display))) continue;
            repository.Add(NewAiLabel(evt, source.Revision, run.Id, EventLabelType.BehaviorTag,
                key, display, tag.Confidence));
        }
        await repository.SaveChangesAsync(cancellationToken);
    }

    private static EventLabelIndex NewAiLabel(Event evt, int revision, long runId, EventLabelType type,
        string key, string display, decimal confidence) => new()
        {
            UserId = evt.UserId,
            EventId = evt.Id,
            SourceRevision = revision,
            SemanticRunId = runId,
            Type = type,
            Origin = EventLabelOrigin.Ai,
            TaxonomyKey = key,
            DisplayName = display,
            NormalizedValue = EventTaxonomy.NormalizedValue(display),
            Confidence = Math.Clamp(confidence, 0, 1),
            IsCurrent = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private async Task PublishUserPlaceAsync(Event evt, SourceRevision source, CancellationToken cancellationToken)
    {
        foreach (var location in source.Locations.Where(x => x.UserConfirmed))
        {
            var key = !string.IsNullOrWhiteSpace(location.ProviderPoiId)
                ? $"amap:{location.ProviderPoiId}" : $"text:{location.AdCode}:{EventTaxonomy.NormalizedValue(location.Name)}";
            var place = await repository.FindPlaceAsync(evt.UserId, key, cancellationToken);
            var visitedAt = source.HappenedAt ?? source.CreatedAt;
            if (place is null)
            {
                place = new UserPlace
                {
                    UserId = evt.UserId,
                    CanonicalKey = key,
                    VisitCount = 0,
                    FirstVisitedAt = visitedAt,
                    LastVisitedAt = visitedAt
                };
                repository.Add(place);
            }
            place.Name = location.Name; place.Address = location.Address; place.AdCode = location.AdCode;
            place.ProviderPoiId = location.ProviderPoiId; place.Latitude = location.Latitude;
            place.Longitude = location.Longitude; place.CoordinateSystem = location.CoordinateSystem;
            place.VisitCount = await repository.CountPlaceVisitsAsync(evt.UserId, location, cancellationToken);
            place.FirstVisitedAt = visitedAt < place.FirstVisitedAt ? visitedAt : place.FirstVisitedAt;
            place.LastVisitedAt = visitedAt > place.LastVisitedAt ? visitedAt : place.LastVisitedAt;
            place.RetrievalText = string.Join(' ', new[] { location.Name, location.Address, location.City, location.District }.Where(x => !string.IsNullOrWhiteSpace(x)));
            place.UpdatedAt = DateTimeOffset.UtcNow;
            var vector = await embeddingGenerator.GenerateAsync([place.RetrievalText], cancellationToken: cancellationToken);
            repository.SetEmbedding(place, vector[0].Vector.ToArray());
        }
    }

    private void PublishExpenses(EventSemanticRun run, long userId, SemanticEnvelope envelope)
    {
        foreach (var value in envelope.Expenses.Where(x => x.Amount >= 0).Take(50))
        {
            run.Expenses.Add(new ExpenseFact
            {
                UserId = userId,
                Amount = value.Amount,
                Currency = Limit(value.Currency.ToUpperInvariant(), 16),
                Purpose = Limit(value.Purpose, 512),
                Scope = Limit(value.Scope, 128),
                Confidence = Math.Clamp(value.Confidence, 0, 1),
                EvidenceJson = JsonSerializer.Serialize(new { text = value.Evidence }),
            });
        }
    }

    private async Task PublishMemoriesAsync(
        long userId,
        long eventId,
        int sourceRevision,
        EventSemanticRun run,
        IReadOnlyList<MemoryCandidate> candidates,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates.Where(x => !string.IsNullOrWhiteSpace(x.Evidence)).Take(20))
        {
            var memoryType = Enum.TryParse<UserMemoryType>(candidate.Type, true, out var parsed)
                ? parsed : UserMemoryType.Other;
            var content = candidate.Content.Trim();
            if (content.Length == 0) continue;
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{memoryType}:{content}")))
                .ToLowerInvariant();
            var existing = await repository.FindMemoryAsync(userId, fingerprint, cancellationToken);
            if (existing?.Status == UserMemoryStatus.Rejected &&
                existing.Evidence.Any(x => x.EventId == eventId && x.SourceRevision == sourceRevision))
            {
                continue;
            }

            UserMemory memory;
            if (existing is null)
            {
                var vector = await embeddingGenerator.GenerateAsync([content], cancellationToken: cancellationToken);
                memory = new UserMemory
                {
                    UserId = userId,
                    Type = memoryType,
                    Content = content,
                    Confidence = Math.Clamp(candidate.Confidence, 0, 1),
                    Status = UserMemoryStatus.Automatic,
                    Fingerprint = fingerprint,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
                repository.Add(memory);
                repository.SetEmbedding(memory, vector[0].Vector.ToArray());
            }
            else
            {
                memory = existing;
                if (memory.Status == UserMemoryStatus.Rejected)
                {
                    // 用户拒绝会抑制同一 Source 的重建；Source 修订变化后允许再次形成自动候选，
                    // RejectedAt 保留为审计痕迹。
                    memory.Status = UserMemoryStatus.Automatic;
                    memory.Confidence = Math.Clamp(candidate.Confidence, 0, 1);
                    memory.UpdatedAt = DateTimeOffset.UtcNow;
                }
                if (memory.Status == UserMemoryStatus.Automatic)
                {
                    memory.Confidence = Math.Max(memory.Confidence, Math.Clamp(candidate.Confidence, 0, 1));
                    memory.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }

            if (!memory.Evidence.Any(x => x.EventId == eventId && x.SourceRevision == sourceRevision))
            {
                memory.Evidence.Add(new UserMemoryEvidence
                {
                    EventId = eventId,
                    SourceRevision = sourceRevision,
                    SemanticRunId = run.Id,
                    EvidenceJson = JsonSerializer.Serialize(new { text = candidate.Evidence }),
                });
            }
        }
    }

    private Task IncrementWatermarkAsync(long userId, CancellationToken cancellationToken) =>
        outbox.IncrementWatermarkAsync(userId, DateTimeOffset.UtcNow, cancellationToken);

    private static bool TryDeserialize(string json, out SemanticEnvelope? envelope)
    {
        var value = json.Trim();
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = value.IndexOf('\n');
            var lastFence = value.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
            {
                value = value[(firstNewLine + 1)..lastFence].Trim();
            }
        }
        try
        {
            envelope = JsonSerializer.Deserialize<SemanticEnvelope>(value, JsonOptions);
            return envelope is not null;
        }
        catch (JsonException)
        {
            envelope = null;
            return false;
        }
    }

    private static string Limit(string? value, int length)
    {
        value ??= string.Empty;
        return value.Length <= length ? value : value[..length];
    }
}
