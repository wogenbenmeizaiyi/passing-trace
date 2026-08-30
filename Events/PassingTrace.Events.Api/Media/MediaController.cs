using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PassingTrace.Events.Api.Security;

namespace PassingTrace.Events.Api.Media;

[ApiController]
[Authorize]
[Route("api/v1/media")]
public sealed class MediaController(MediaService service) : ControllerBase
{
    [HttpPost("uploads")]
    public async Task<ActionResult<MediaUploadResponse>> CreateUploadAsync(
        [FromBody] CreateMediaUploadRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.CreateUploadAsync(User.GetUserId(), request, cancellationToken));

    [HttpPost("{id:guid}/parts")]
    public async Task<ActionResult<PartUploadResponse>> CreatePartAsync(
        Guid id,
        [FromBody] CreatePartUploadRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.CreatePartUrlAsync(User.GetUserId(), id, request.PartNumber, cancellationToken));

    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<MediaResponse>> ConfirmAsync(
        Guid id,
        [FromBody] ConfirmMediaUploadRequest request,
        CancellationToken cancellationToken)
    {
        var media = await service.ConfirmAsync(User.GetUserId(), id, request, cancellationToken);
        return Ok(new MediaResponse(media.Id, media.OriginalFileName, media.Kind,
            media.VerifiedMimeType ?? media.DeclaredMimeType, media.ActualSize ?? media.ExpectedSize,
            media.Status, 0));
    }

    [HttpGet("{id:guid}/access")]
    public async Task<ActionResult<MediaAccessResponse>> AccessAsync(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.CreateAccessAsync(User.GetUserId(), id, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(User.GetUserId(), id, cancellationToken);
        return NoContent();
    }
}
