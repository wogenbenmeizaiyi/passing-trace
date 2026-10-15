using System.Buffers.Binary;
using System.Text.Json;
using PassingTrace.Core.Events;

namespace PassingTrace.Events.Api.Media;

/// <summary>Accept GLB 2 with embedded resources only. No model-controlled network URLs.</summary>
public static class GlbValidator
{
    public static async Task ValidateAsync(Stream stream, long size, CancellationToken ct)
    {
        try
        {
            var header = new byte[12]; await stream.ReadExactlyAsync(header, ct);
            if (!header.AsSpan(0, 4).SequenceEqual("glTF"u8) || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) != 2 ||
                BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)) != size)
                throw new DomainValidationException("仅支持长度正确的 GLB 2 模型。");
            long consumed = 12; bool jsonSeen = false, binSeen = false;
            while (consumed < size)
            {
                var chunk = new byte[8]; await stream.ReadExactlyAsync(chunk, ct); consumed += 8;
                var length = BinaryPrimitives.ReadUInt32LittleEndian(chunk);
                var type = BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4));
                if (length % 4 != 0 || length > size - consumed) throw new DomainValidationException("GLB 数据块长度无效。");
                if (!jsonSeen)
                {
                    if (type != 0x4E4F534A || length > 8 * 1024 * 1024) throw new DomainValidationException("GLB 必须以 JSON 数据块开始。");
                    var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, ct);
                    using var document = JsonDocument.Parse(bytes);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("asset", out var asset) || !asset.TryGetProperty("version", out var version) || version.GetString() != "2.0")
                        throw new DomainValidationException("GLB asset 版本必须为 2.0。");
                    foreach (var name in new[] { "buffers", "images" })
                        if (root.TryGetProperty(name, out var resources))
                            foreach (var resource in resources.EnumerateArray())
                                if (resource.TryGetProperty("uri", out var uri) &&
                                    (uri.ValueKind != JsonValueKind.String || !uri.GetString()!.StartsWith("data:", StringComparison.OrdinalIgnoreCase)))
                                    throw new DomainValidationException("GLB 的贴图和缓冲区必须内嵌，不能引用外部资源。");
                    jsonSeen = true;
                }
                else
                {
                    if (type != 0x004E4942 || binSeen) throw new DomainValidationException("GLB 数据块顺序或类型无效。");
                    binSeen = true;
                    var buffer = new byte[64 * 1024]; long remaining = length;
                    while (remaining > 0) { var n = (int)Math.Min(buffer.Length, remaining); await stream.ReadExactlyAsync(buffer.AsMemory(0, n), ct); remaining -= n; }
                }
                consumed += length;
            }
            if (!jsonSeen || consumed != size) throw new DomainValidationException("GLB 数据不完整。");
        }
        catch (Exception ex) when (ex is EndOfStreamException or JsonException or InvalidOperationException)
        { throw new DomainValidationException("GLB 模型格式无效或数据不完整。"); }
    }
}
