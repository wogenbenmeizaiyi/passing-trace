using PassingTrace.Core.Media;

namespace PassingTrace.Events.Api.Media;

public sealed record CreateMediaUploadRequest(
    string FileName,
    string ContentType,
    long Size,
    string Sha256);

public sealed record MediaUploadResponse(
    Guid Id,
    MediaKind Kind,
    MediaUploadMode Mode,
    Uri? UploadUrl,
    long? PartSize,
    int? PartCount,
    DateTimeOffset ExpiresAt);

public sealed record CreatePartUploadRequest(int PartNumber);
public sealed record PartUploadResponse(int PartNumber, Uri UploadUrl, DateTimeOffset ExpiresAt);
public sealed record ConfirmedUploadPart(int PartNumber, string ETag);
public sealed record ConfirmMediaUploadRequest(IReadOnlyList<ConfirmedUploadPart>? Parts);

public sealed record MediaResponse(
    Guid Id,
    string FileName,
    MediaKind Kind,
    string ContentType,
    long Size,
    MediaAssetStatus Status,
    int SortOrder);

public sealed record MediaAccessResponse(Uri Url, DateTimeOffset ExpiresAt, bool Inline);
