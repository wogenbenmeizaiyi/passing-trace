using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PassingTrace.Events.Api.Security;
using System.Net.Http.Headers;

namespace PassingTrace.Events.Api.Media;

[ApiController]
[Authorize]
[Route("api/v1/media")]
public sealed class MediaController(MediaService service) : ControllerBase
{
    [HttpPost("uploads")]
    public async Task<ActionResult<MediaUploadResponse>> CreateUploadAsync(
        [FromBody] CreateMediaUploadRequest request,
        CancellationToken cancellationToken)
    {
        var upload = await service.CreateUploadAsync(User.GetUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, upload);
    }

    [HttpPost("{id:guid}/parts")]
    public async Task<ActionResult<PartUploadResponse>> CreatePartAsync(
        Guid id,
        [FromBody] CreatePartUploadRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.CreatePartUrlAsync(User.GetUserId(), id, request.PartNumber, cancellationToken));

    [HttpPut("{id:guid}/content")]
    [RequestSizeLimit(100L * 1024 * 1024)]
    public async Task<IActionResult> UploadContentAsync(Guid id, CancellationToken cancellationToken)
    {
        await service.UploadContentAsync(
            User.GetUserId(), id, Request.Body, Request.ContentLength, Request.ContentType, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/parts/{partNumber:int}/content")]
    [RequestSizeLimit(20L * 1024 * 1024)]
    public async Task<IActionResult> UploadPartContentAsync(Guid id, int partNumber, CancellationToken cancellationToken)
    {
        var eTag = await service.UploadPartContentAsync(
            User.GetUserId(), id, partNumber, Request.Body, Request.ContentLength, cancellationToken);
        Response.Headers.ETag = eTag;
        return NoContent();
    }

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

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> ContentAsync(Guid id, CancellationToken cancellationToken)
    {
        var media = await service.OpenContentAsync(User.GetUserId(), id, cancellationToken);
        var disposition = new ContentDispositionHeaderValue(media.Inline ? "inline" : "attachment")
        {
            FileNameStar = media.FileName,
        };
        Response.Headers.ContentDisposition = disposition.ToString();
        return File(media.Stream, media.ContentType, enableRangeProcessing: false);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(User.GetUserId(), id, cancellationToken);
        return NoContent();
    }
}
