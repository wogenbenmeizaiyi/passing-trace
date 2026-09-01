using System.Security.Cryptography;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Events;
using PassingTrace.Core.Media;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.Media;

public interface IEventMediaService
{
    Task<IReadOnlyList<MediaAsset>> ResolveAsync(long userId, IReadOnlyList<Guid>? mediaIds, CancellationToken cancellationToken);
    void ReplaceCurrent(Event evt, SourceRevision revision, IReadOnlyList<MediaAsset> media, DateTimeOffset now);
}

public sealed class MediaService(
    TraceDbContext dbContext,
    IObjectStorage storage,
    IAnalysisOutbox outbox,
    TimeProvider clock) : IEventMediaService
{
    public const long MultipartThreshold = 100L * 1024 * 1024;
    public const long MultipartPartSize = 16L * 1024 * 1024;
    private const int MaxAttachments = 10;
    private static readonly TimeSpan UploadLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(15);

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".sh", ".bash",
        ".js", ".mjs", ".cjs", ".vbs", ".vbe", ".wsf", ".scr", ".msi", ".apk",
        ".jar", ".appx", ".deb", ".rpm", ".reg", ".lnk",
    };

    private static readonly IReadOnlyDictionary<string, MediaKind> ImageMimeTypes =
        new Dictionary<string, MediaKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = MediaKind.Image,
            ["image/png"] = MediaKind.Image,
            ["image/webp"] = MediaKind.Image,
        };

    private static readonly IReadOnlyDictionary<string, MediaKind> VideoMimeTypes =
        new Dictionary<string, MediaKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["video/mp4"] = MediaKind.Video,
            ["video/quicktime"] = MediaKind.Video,
            ["video/webm"] = MediaKind.Video,
        };

    public async Task<MediaUploadResponse> CreateUploadAsync(long userId, CreateMediaUploadRequest request, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(request.FileName?.Trim());
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 512)
        {
            throw new DomainValidationException("文件名为空或过长。");
        }

        var extension = Path.GetExtension(fileName);
        if (BlockedExtensions.Contains(extension))
        {
            throw new DomainValidationException($"不允许上传 {extension} 类型的可执行文件或脚本。");
        }

        var contentType = NormalizeMime(request.ContentType);
        var kind = GetKind(contentType);
        EnsureSize(kind, request.Size);
        var sha256 = request.Sha256?.Trim().ToLowerInvariant() ?? string.Empty;
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            throw new DomainValidationException("Sha256 必须是 64 位十六进制字符串。");
        }

        var now = clock.GetUtcNow();
        var id = Guid.NewGuid();
        var mode = request.Size < MultipartThreshold ? MediaUploadMode.Single : MediaUploadMode.Multipart;
        var objectKey = $"users/{userId}/media/{now:yyyy/MM}/{id:N}{extension.ToLowerInvariant()}";
        var asset = new MediaAsset
        {
            Id = id,
            UserId = userId,
            ObjectKey = objectKey,
            OriginalFileName = fileName,
            Kind = kind,
            DeclaredMimeType = contentType,
            ExpectedSize = request.Size,
            ExpectedSha256 = sha256,
            Status = MediaAssetStatus.PendingUpload,
            UploadMode = mode,
            UploadExpiresAt = now.Add(UploadLifetime),
            CreatedAt = now,
            UpdatedAt = now,
        };

        Uri? uploadUrl = null;
        if (mode == MediaUploadMode.Single)
        {
            uploadUrl = await storage.CreateUploadUrlAsync(objectKey, contentType, now.Add(UrlLifetime), cancellationToken);
        }
        else
        {
            asset.MultipartUploadId = await storage.CreateMultipartUploadAsync(objectKey, contentType, cancellationToken);
        }

        dbContext.MediaAssets.Add(asset);
        await dbContext.SaveChangesAsync(cancellationToken);
        int? partCount = mode == MediaUploadMode.Multipart
            ? checked((int)Math.Ceiling(request.Size / (double)MultipartPartSize))
            : null;
        return new MediaUploadResponse(id, kind, mode, uploadUrl,
            mode == MediaUploadMode.Multipart ? MultipartPartSize : null,
            partCount,
            asset.UploadExpiresAt);
    }

    public async Task<PartUploadResponse> CreatePartUrlAsync(long userId, Guid mediaId, int partNumber, CancellationToken cancellationToken)
    {
        var asset = await FindOwnedAsync(userId, mediaId, cancellationToken);
        EnsurePending(asset);
        if (asset.UploadMode != MediaUploadMode.Multipart || string.IsNullOrWhiteSpace(asset.MultipartUploadId))
        {
            throw new DomainValidationException("该附件不是分片上传。");
        }

        var partCount = checked((int)Math.Ceiling(asset.ExpectedSize / (double)MultipartPartSize));
        if (partNumber < 1 || partNumber > partCount)
        {
            throw new DomainValidationException($"分片序号必须在 1 到 {partCount} 之间。");
        }

        var expires = clock.GetUtcNow().Add(UrlLifetime);
        var url = await storage.CreatePartUploadUrlAsync(
            asset.ObjectKey, asset.MultipartUploadId, partNumber, expires, cancellationToken);
        return new PartUploadResponse(partNumber, url, expires);
    }

    public async Task<MediaAsset> ConfirmAsync(long userId, Guid mediaId, ConfirmMediaUploadRequest request, CancellationToken cancellationToken)
    {
        var asset = await FindOwnedAsync(userId, mediaId, cancellationToken);
        if (asset.Status is MediaAssetStatus.Uploaded or MediaAssetStatus.Processing or MediaAssetStatus.Ready)
        {
            return asset;
        }

        EnsurePending(asset);
        if (asset.UploadMode == MediaUploadMode.Multipart)
        {
            var expectedCount = checked((int)Math.Ceiling(asset.ExpectedSize / (double)MultipartPartSize));
            var parts = request.Parts?.OrderBy(x => x.PartNumber).ToArray() ?? [];
            if (parts.Length != expectedCount || parts.Select(x => x.PartNumber).Distinct().Count() != expectedCount ||
                parts[0].PartNumber != 1 || parts[^1].PartNumber != expectedCount || parts.Any(x => string.IsNullOrWhiteSpace(x.ETag)))
            {
                throw new DomainValidationException("分片列表不完整或存在重复序号。");
            }

            await storage.CompleteMultipartUploadAsync(
                asset.ObjectKey,
                asset.MultipartUploadId!,
                parts.Select(x => new CompletedPart(x.PartNumber, x.ETag)).ToArray(),
                cancellationToken);
        }

        try
        {
            var info = await storage.GetInfoAsync(asset.ObjectKey, cancellationToken);
            if (info.Size != asset.ExpectedSize)
            {
                throw new DomainValidationException($"上传对象大小不符，期望 {asset.ExpectedSize}，实际 {info.Size}。");
            }

            await using var stream = await storage.OpenReadAsync(asset.ObjectKey, cancellationToken);
            var inspection = await InspectAsync(stream, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(asset.ExpectedSha256),
                    Convert.FromHexString(inspection.Sha256)))
            {
                throw new DomainValidationException("上传对象的 SHA-256 与申请值不一致。");
            }

            EnsureMimeMatches(asset.Kind, asset.DeclaredMimeType, inspection.MimeType);
            var now = clock.GetUtcNow();
            asset.ActualSize = info.Size;
            asset.ActualSha256 = inspection.Sha256;
            asset.VerifiedMimeType = inspection.MimeType;
            asset.Status = asset.Kind == MediaKind.Image ? MediaAssetStatus.Uploaded : MediaAssetStatus.Ready;
            asset.ConfirmedAt = now;
            asset.UpdatedAt = now;
            if (asset.Kind == MediaKind.Image)
            {
                outbox.EnqueueMedia(userId, asset.Id, now);
            }
            await outbox.IncrementWatermarkAsync(userId, now, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return asset;
        }
        catch (DomainValidationException exception)
        {
            await storage.DeleteAsync(asset.ObjectKey, cancellationToken);
            asset.Status = MediaAssetStatus.Failed;
            asset.ProcessingError = exception.Message;
            asset.UpdatedAt = clock.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<MediaAccessResponse> CreateAccessAsync(long userId, Guid mediaId, CancellationToken cancellationToken)
    {
        // 正常数据以 MediaAsset.UserId 判定归属。早期导入数据中曾出现附件所有者
        // 未同步、但附件已经合法关联到当前用户 Event 的情况；Event 详情能看到附件，
        // access 却返回 404。允许当前用户通过自己未删除的 Event 关联读取该附件，
        // 仍然不会向未关联用户开放对象。
        var asset = await dbContext.MediaAssets.FirstOrDefaultAsync(
            x => x.Id == mediaId && x.DeletedAt == null &&
                 (x.UserId == userId || x.EventLinks.Any(link =>
                     link.Event.UserId == userId && link.Event.DeletedAt == null)),
            cancellationToken) ?? throw new MediaAssetNotFoundException(mediaId);
        if (asset.Status is not (MediaAssetStatus.Uploaded or MediaAssetStatus.Processing or MediaAssetStatus.Ready))
        {
            throw new MediaAssetNotFoundException(mediaId);
        }

        var inline = asset.Kind is MediaKind.Image or MediaKind.Video;
        var expires = clock.GetUtcNow().Add(UrlLifetime);
        var url = await storage.CreateDownloadUrlAsync(
            asset.ObjectKey,
            asset.OriginalFileName,
            asset.VerifiedMimeType ?? asset.DeclaredMimeType,
            inline,
            expires,
            cancellationToken);
        return new MediaAccessResponse(url, expires, inline);
    }

    public async Task DeleteAsync(long userId, Guid mediaId, CancellationToken cancellationToken)
    {
        var asset = await dbContext.MediaAssets
            .Include(x => x.EventLinks)
            .Include(x => x.RevisionLinks)
            .FirstOrDefaultAsync(x => x.Id == mediaId && x.UserId == userId && x.DeletedAt == null, cancellationToken)
            ?? throw new MediaAssetNotFoundException(mediaId);
        if (asset.EventLinks.Count > 0)
        {
            throw new DomainValidationException("附件仍被当前记录引用，请先从记录中移除。");
        }

        var now = clock.GetUtcNow();
        asset.DeletedAt = now;
        asset.Status = MediaAssetStatus.Deleted;
        asset.UpdatedAt = now;
        if (asset.RevisionLinks.Count == 0)
        {
            await storage.DeleteAsync(asset.ObjectKey, cancellationToken);
        }
        await outbox.IncrementWatermarkAsync(userId, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MediaAsset>> ResolveAsync(long userId, IReadOnlyList<Guid>? mediaIds, CancellationToken cancellationToken)
    {
        var ids = mediaIds?.ToArray() ?? [];
        if (ids.Length > MaxAttachments)
        {
            throw new DomainValidationException($"每条记录最多包含 {MaxAttachments} 个附件。");
        }
        if (ids.Distinct().Count() != ids.Length)
        {
            throw new DomainValidationException("mediaIds 不能包含重复附件。");
        }
        if (ids.Length == 0)
        {
            return [];
        }

        var byId = await dbContext.MediaAssets
            .Where(x => ids.Contains(x.Id) && x.UserId == userId && x.DeletedAt == null &&
                (x.Status == MediaAssetStatus.Uploaded || x.Status == MediaAssetStatus.Processing || x.Status == MediaAssetStatus.Ready))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (byId.Count != ids.Length)
        {
            throw new DomainValidationException("存在未确认、已删除或不属于当前用户的附件。");
        }
        return ids.Select(id => byId[id]).ToArray();
    }

    public void ReplaceCurrent(Event evt, SourceRevision revision, IReadOnlyList<MediaAsset> media, DateTimeOffset now)
    {
        evt.MediaAssets.Clear();
        for (var index = 0; index < media.Count; index++)
        {
            evt.MediaAssets.Add(new EventMediaAsset
            {
                Event = evt,
                MediaAsset = media[index],
                MediaAssetId = media[index].Id,
                SortOrder = index,
                CreatedAt = now,
            });
            revision.MediaAssets.Add(new SourceRevisionMedia
            {
                SourceRevision = revision,
                MediaAsset = media[index],
                MediaAssetId = media[index].Id,
                SortOrder = index,
            });
        }
    }

    private async Task<MediaAsset> FindOwnedAsync(long userId, Guid mediaId, CancellationToken cancellationToken) =>
        await dbContext.MediaAssets.FirstOrDefaultAsync(
            x => x.Id == mediaId && x.UserId == userId && x.DeletedAt == null,
            cancellationToken) ?? throw new MediaAssetNotFoundException(mediaId);

    private void EnsurePending(MediaAsset asset)
    {
        if (asset.Status != MediaAssetStatus.PendingUpload || asset.UploadExpiresAt <= clock.GetUtcNow())
        {
            throw new DomainValidationException("上传会话已失效或附件状态不允许继续上传。");
        }
    }

    private static string NormalizeMime(string? contentType) =>
        (contentType ?? "application/octet-stream").Split(';', 2)[0].Trim().ToLowerInvariant();

    private static MediaKind GetKind(string mimeType)
    {
        if (ImageMimeTypes.TryGetValue(mimeType, out var imageKind)) return imageKind;
        if (VideoMimeTypes.TryGetValue(mimeType, out var videoKind)) return videoKind;
        return MediaKind.File;
    }

    private static void EnsureSize(MediaKind kind, long size)
    {
        var max = kind switch
        {
            MediaKind.Image => 20L * 1024 * 1024,
            MediaKind.Video => 1024L * 1024 * 1024,
            _ => 200L * 1024 * 1024,
        };
        if (size <= 0 || size > max)
        {
            throw new DomainValidationException($"{kind} 文件大小必须大于 0 且不超过 {max / 1024 / 1024}MB。");
        }
    }

    private static void EnsureMimeMatches(MediaKind expectedKind, string declaredMime, string actualMime)
    {
        var actualKind = GetKind(actualMime);
        if (expectedKind != actualKind || actualMime == "application/x-executable")
        {
            throw new DomainValidationException($"文件真实类型 {actualMime} 与申请类型不符或不允许上传。");
        }

        // 浏览器提供的 MIME 只能作为声明；图片和视频必须精确匹配文件魔数。
        // Office Open XML 本质是 ZIP，文本类格式也会统一探测为 text/plain。
        var declared = NormalizeMime(declaredMime);
        var compatible = declared == actualMime ||
            (expectedKind == MediaKind.File && IsContainerOrTextAlias(declared, actualMime)) ||
            (expectedKind == MediaKind.File && declared == "application/octet-stream");
        if (!compatible)
        {
            throw new DomainValidationException($"文件声明类型 {declared} 与真实类型 {actualMime} 不一致。");
        }
    }

    private static bool IsContainerOrTextAlias(string declaredMime, string actualMime) =>
        (actualMime == "application/zip" &&
            (declaredMime == "application/zip" || declaredMime.EndsWith("+zip", StringComparison.Ordinal) ||
             declaredMime.StartsWith("application/vnd.openxmlformats-officedocument.", StringComparison.Ordinal))) ||
        (actualMime == "text/plain" &&
            (declaredMime.StartsWith("text/", StringComparison.Ordinal) ||
             declaredMime is "application/json" or "application/xml")) ||
        (actualMime == "application/x-ole-storage" &&
            declaredMime is "application/msword" or "application/vnd.ms-excel" or "application/vnd.ms-powerpoint");

    private static async Task<(string Sha256, string MimeType)> InspectAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        var prefix = new byte[512];
        var prefixLength = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            if (prefixLength < prefix.Length)
            {
                var copy = Math.Min(prefix.Length - prefixLength, read);
                Buffer.BlockCopy(buffer, 0, prefix, prefixLength, copy);
                prefixLength += copy;
            }
        }
        return (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), DetectMime(prefix.AsSpan(0, prefixLength)));
    }

    private static string DetectMime(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 4 && bytes[..4].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 })) return "image/png";
        if (bytes.Length >= 3 && bytes[..3].SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF })) return "image/jpeg";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (bytes.Length >= 12 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return bytes.Slice(8, 4).SequenceEqual("qt  "u8) ? "video/quicktime" : "video/mp4";
        }
        if (bytes.Length >= 4 && bytes[..4].SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 })) return "video/webm";
        if (bytes.Length >= 4 && bytes[..4].SequenceEqual("%PDF"u8)) return "application/pdf";
        if (bytes.Length >= 4 && bytes[..4].SequenceEqual(new byte[] { 0x50, 0x4B, 0x03, 0x04 })) return "application/zip";
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 })) return "application/x-ole-storage";
        if (bytes.Length >= 2 && bytes[..2].SequenceEqual(new byte[] { 0x1F, 0x8B })) return "application/gzip";
        if (bytes.Length >= 2 && bytes[..2].SequenceEqual("MZ"u8)) return "application/x-executable";
        if (bytes.Length >= 4 && bytes[..4].SequenceEqual(new byte[] { 0x7F, 0x45, 0x4C, 0x46 })) return "application/x-executable";
        if (bytes.Length >= 2 && bytes[..2].SequenceEqual("#!"u8)) return "application/x-executable";
        return IsProbablyText(bytes) ? "text/plain" : "application/octet-stream";
    }

    private static bool IsProbablyText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return false;
        var controls = 0;
        foreach (var value in bytes)
        {
            if (value == 0) return false;
            if (value < 0x09 || value is > 0x0D and < 0x20) controls++;
        }
        return controls * 20 < bytes.Length;
    }
}
