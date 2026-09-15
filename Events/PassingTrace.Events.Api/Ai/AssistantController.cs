using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PassingTrace.Events.Api.Ai;

[ApiController]
[Authorize]
[Route("api/v1/ai/conversations")]
public sealed class AssistantController(
    AssistantService service,
    ILogger<AssistantController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AiConversationResponse>>> ListAsync(CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(cancellationToken));

    [HttpGet("page")]
    public async Task<ActionResult<AiConversationPageResponse>> ListPageAsync(
        CancellationToken cancellationToken, [FromQuery] int limit = 20, [FromQuery] string? cursor = null)
    {
        try
        {
            return Ok(await service.ListPageAsync(limit, cursor, cancellationToken));
        }
        catch (ArgumentException)
        {
            return BadRequest(new { message = "会话列表的位置已失效，请刷新后重试。" });
        }
    }

    [HttpGet("{id:guid}/summary")]
    public async Task<ActionResult<AiConversationResponse>> GetSummaryAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetSummaryAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/messages-page")]
    public async Task<ActionResult<AiMessagePageResponse>> GetMessagesPageAsync(
        Guid id, CancellationToken cancellationToken, [FromQuery] int limit = 30, [FromQuery] long? beforeId = null)
    {
        if (beforeId is <= 0) return BadRequest(new { message = "聊天记录的位置已失效，请重新打开对话。" });
        var result = await service.GetMessagesPageAsync(id, limit, beforeId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

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
            await foreach (var item in service.SendAsync(id, request.Content, cancellationToken, request.Timezone))
            {
                await WriteEventAsync(item.Type, item.Data, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "AI conversation {ConversationId} failed while streaming a response.", id);
            await WriteEventAsync("error", AssistantErrorPresenter.Present(exception), cancellationToken);
        }
    }

    private async Task WriteEventAsync(string type, object? data, CancellationToken cancellationToken)
    {
        await Response.WriteAsync($"event: {type}\n", cancellationToken);
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(data, SseJsonOptions)}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
