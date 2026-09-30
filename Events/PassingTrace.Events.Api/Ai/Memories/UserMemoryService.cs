using PassingTrace.Events.Api.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;

namespace PassingTrace.Events.Api.Ai.Memories;

public sealed class UserMemoryService(
    IUserMemoryRepository repository,
    CurrentUserContext currentUser,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<UserMemoryResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var values = await repository.ListAsync(currentUser.UserId, cancellationToken);
        return values.Select(ToResponse).ToArray();
    }

    public async Task<UserMemoryResponse> UpdateAsync(long id, UpdateUserMemoryRequest request, CancellationToken cancellationToken)
    {
        var memory = await FindOwnedAsync(id, cancellationToken);
        var changedContent = !string.IsNullOrWhiteSpace(request.Content) && request.Content.Trim() != memory.Content;
        if (changedContent)
        {
            var content = request.Content!.Trim();
            if (content.Length > 2000) throw new DomainValidationException("记忆内容不能超过 2000 字符。");
            memory.Content = content;
            memory.Status = UserMemoryStatus.Corrected;
        }
        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            if (!Enum.TryParse<UserMemoryType>(request.Type, true, out var type))
                throw new DomainValidationException("未知的记忆类型。");
            memory.Type = type;
        }
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<UserMemoryStatus>(request.Status, true, out var status) || status == UserMemoryStatus.Automatic)
                throw new DomainValidationException("用户只能把记忆设为 Confirmed、Corrected 或 Rejected。");
            memory.Status = status;
        }
        if (memory.Status == UserMemoryStatus.Rejected) memory.RejectedAt = clock.GetUtcNow();
        memory.Fingerprint = Fingerprint(memory.Type, memory.Content);
        var duplicate = await repository.HasDuplicateAsync(currentUser.UserId, memory.Id, memory.Fingerprint, cancellationToken);
        if (duplicate) throw new DomainValidationException("已经存在相同的记忆。");
        if (changedContent)
        {
            try
            {
                var vector = await embeddings.GenerateAsync([memory.Content], cancellationToken: cancellationToken);
                repository.SetEmbedding(memory, vector[0].Vector.ToArray());
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 用户修正是事实操作，不能因为 Embedding 服务暂时不可用而回滚。
                // 向量为空时仍可通过关系型字段读取，后续补算再生成向量。
                repository.SetEmbedding(memory, null);
            }
        }
        memory.UpdatedAt = clock.GetUtcNow();
        await repository.SaveAsync(currentUser.UserId, clock.GetUtcNow(), cancellationToken);
        return ToResponse(memory);
    }

    public async Task RejectAsync(long id, CancellationToken cancellationToken)
    {
        var memory = await FindOwnedAsync(id, cancellationToken);
        memory.Status = UserMemoryStatus.Rejected;
        memory.RejectedAt = clock.GetUtcNow();
        memory.UpdatedAt = clock.GetUtcNow();
        await repository.SaveAsync(currentUser.UserId, clock.GetUtcNow(), cancellationToken);
    }

    public Task RejectAllAsync(CancellationToken cancellationToken) =>
        repository.RejectAllAsync(currentUser.UserId, clock.GetUtcNow(), cancellationToken);

    private async Task<UserMemory> FindOwnedAsync(long id, CancellationToken cancellationToken) =>
        await repository.FindAsync(currentUser.UserId, id, cancellationToken) ?? throw new KeyNotFoundException("记忆不存在。");

    private static UserMemoryResponse ToResponse(UserMemory x) => new(x.Id, x.Type.ToString(), x.Content,
        x.Confidence, x.Status.ToString(), x.UpdatedAt, x.Evidence.Select(e => e.EventId).Distinct().ToArray());
    private static string Fingerprint(UserMemoryType type, string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{type}:{content}"))).ToLowerInvariant();
}
