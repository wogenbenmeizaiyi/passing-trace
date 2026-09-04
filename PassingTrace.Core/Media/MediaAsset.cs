namespace PassingTrace.Core.Media;

/// <summary>附件类型。普通文件不参与本期 AI 内容分析。</summary>
public enum MediaKind
{
    Image = 1,
    Video = 2,
    File = 3,
}

/// <summary>对象从申请上传到可被 Event 使用的生命周期。</summary>
public enum MediaAssetStatus
{
    PendingUpload = 1,
    Uploaded = 2,
    Processing = 3,
    Ready = 4,
    Failed = 5,
    Deleted = 6,
}

public enum MediaUploadMode
{
    Single = 1,
    Multipart = 2,
}

/// <summary>私有 S3 对象的元数据。ObjectKey 永远不通过公共 API 返回。</summary>
public sealed class MediaAsset
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public MediaKind Kind { get; set; }
    public string DeclaredMimeType { get; set; } = string.Empty;
    public string? VerifiedMimeType { get; set; }
    public long ExpectedSize { get; set; }
    public long? ActualSize { get; set; }
    public string ExpectedSha256 { get; set; } = string.Empty;
    public string? ActualSha256 { get; set; }
    public string? AiObjectKey { get; set; }
    public string? ThumbnailObjectKey { get; set; }
    public MediaAssetStatus Status { get; set; }
    public MediaUploadMode UploadMode { get; set; }
    public string? MultipartUploadId { get; set; }
    public DateTimeOffset UploadExpiresAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? ProcessingError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<EventMediaAsset> EventLinks { get; set; } = [];
    public List<SourceRevisionMedia> RevisionLinks { get; set; } = [];
}

/// <summary>Event 当前修订使用的附件及显示顺序。</summary>
public sealed class EventMediaAsset
{
    public long EventId { get; set; }
    public Guid MediaAssetId { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Events.Event Event { get; set; } = null!;
    public MediaAsset MediaAsset { get; set; } = null!;
}

/// <summary>SourceRevision 对附件集合的不可变快照。</summary>
public sealed class SourceRevisionMedia
{
    public long SourceRevisionId { get; set; }
    public Guid MediaAssetId { get; set; }
    public int SortOrder { get; set; }

    public Events.SourceRevision SourceRevision { get; set; } = null!;
    public MediaAsset MediaAsset { get; set; } = null!;
}
