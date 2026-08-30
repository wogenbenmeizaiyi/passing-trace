using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PassingTrace.Events.Api.Ai;

[ApiController]
[Authorize]
[Route("api/v1/ai/conversations")]
public sealed class AssistantController(AssistantService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AiConversationResponse>>> ListAsync(CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<AiConversationResponse>> CreateAsync(
        [FromBody] CreateConversationRequest request,
        CancellationToken cancellationToken)
    {
        var created = await service.CreateAsync(request.Title, cancellationToken);
        return CreatedAtAction(nameof(GetAsync), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AiConversationDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/messages")]
    public async Task SendMessageAsync(
        Guid id,
        [FromBody] SendAssistantMessageRequest request,
        CancellationToken cancellationToken)
    {
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Append("X-Accel-Buffering", "no");
        try
        {
            await foreach (var item in service.SendAsync(id, request.Content, cancellationToken))
            {
                await WriteEventAsync(item.Type, item.Data, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await WriteEventAsync("error", new { message = exception.Message }, cancellationToken);
        }
    }

    private async Task WriteEventAsync(string type, object? data, CancellationToken cancellationToken)
    {
        await Response.WriteAsync($"event: {type}\n", cancellationToken);
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(data)}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
