using System.Text;
using PassingTrace.Core.Events;
using PassingTrace.Events.Api.Media;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class GlbValidatorTests
{
    private static byte[] Model(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json.PadRight((Encoding.UTF8.GetByteCount(json) + 3) / 4 * 4));
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("glTF"u8); writer.Write(2u); writer.Write((uint)(20 + bytes.Length));
        writer.Write((uint)bytes.Length); writer.Write(0x4E4F534Au); writer.Write(bytes);
        return stream.ToArray();
    }
    [Fact]
    public async Task Embedded_glb_is_accepted_but_external_assets_are_rejected()
    {
        var valid = Model("""{"asset":{"version":"2.0"},"images":[{"uri":"data:image/png;base64,AA=="}]}""");
        await GlbValidator.ValidateAsync(new MemoryStream(valid), valid.Length, default);
        foreach (var resource in new[] { "images", "buffers" })
        {
            var external = Model($$"""{"asset":{"version":"2.0"},"{{resource}}":[{"uri":"https://example.com/asset"}]}""");
            await Assert.ThrowsAsync<DomainValidationException>(() => GlbValidator.ValidateAsync(new MemoryStream(external), external.Length, default));
        }
    }
    [Fact]
    public async Task Length_version_and_truncation_are_checked()
    {
        var valid = Model("""{"asset":{"version":"2.0"}}""");
        await Assert.ThrowsAsync<DomainValidationException>(() => GlbValidator.ValidateAsync(new MemoryStream(valid), valid.Length + 1, default));
        await Assert.ThrowsAsync<DomainValidationException>(() => GlbValidator.ValidateAsync(new MemoryStream(valid[..^4]), valid.Length, default));
        valid[4] = 1;
        await Assert.ThrowsAsync<DomainValidationException>(() => GlbValidator.ValidateAsync(new MemoryStream(valid), valid.Length, default));
    }
}
