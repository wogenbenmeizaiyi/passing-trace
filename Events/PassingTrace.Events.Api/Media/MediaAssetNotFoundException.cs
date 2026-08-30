namespace PassingTrace.Events.Api.Media;

public sealed class MediaAssetNotFoundException(Guid id)
    : Exception($"附件 {id} 不存在或不属于当前用户。");
