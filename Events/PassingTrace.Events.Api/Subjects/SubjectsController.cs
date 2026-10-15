using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PassingTrace.Core.Events;
using PassingTrace.Events.Api.Security;
using PassingTrace.Events.Api.Common;

namespace PassingTrace.Events.Api.Subjects;

[ApiController, Authorize, Route("api/v1/subjects")]
public sealed class SubjectsController(SubjectService service) : ControllerBase
{
    [HttpPost("self")]
    public Task<SubjectResponse> Self(CancellationToken ct) => service.EnsureSelfAsync(User.GetUserId(), ct);
    [HttpGet]
    public Task<IReadOnlyList<SubjectResponse>> List(CancellationToken ct) => service.ListAsync(User.GetUserId(), ct);
    [HttpGet("graph")]
    public Task<SubjectGraphResponse> Graph(CancellationToken ct) => service.GraphAsync(User.GetUserId(), ct);
    [HttpGet("presets")]
    public IReadOnlyList<SubjectField> Presets([FromQuery] Core.Subjects.SubjectKind kind, [FromQuery] string? itemType) => SubjectService.Presets(kind, itemType);
    [HttpGet("{id:guid}")]
    public Task<SubjectResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(User.GetUserId(), id, ct);
    [HttpPost]
    public Task<SubjectResponse> Create(CreateSubjectRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        service.CreateAsync(User.GetUserId(), request, key, ct);
    [HttpPatch("{id:guid}")]
    public Task<SubjectResponse> Update(Guid id, UpdateSubjectRequest request, CancellationToken ct) => service.UpdateAsync(User.GetUserId(), id, Version(), request, ct);
    [HttpPost("{id:guid}/relations")]
    public Task<SubjectGraphResponse> Relate(Guid id, SubjectRelationInput request, CancellationToken ct) => service.AddRelationAsync(User.GetUserId(), id, Version(), request, ct);
    [HttpPatch("relations/{id:guid}")]
    public Task<Core.Subjects.SubjectRelation> UpdateRelation(Guid id, UpdateSubjectRelationRequest request, CancellationToken ct) =>
        service.UpdateRelationAsync(User.GetUserId(), id, Version(), request, ct);
    [HttpGet("{id:guid}/history")]
    public Task<IReadOnlyList<Core.Subjects.SubjectHistory>> History(Guid id, CancellationToken ct) => service.HistoryAsync(User.GetUserId(), "Subject", id, ct);
    [HttpGet("{id:guid}/timeline")]
    public Task<SubjectTimelineResponse> Timeline(Guid id, [FromQuery] string groupBy = "month", [FromQuery] string? timezone = null,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null, [FromQuery] string? kind = null,
        [FromQuery] string? state = null, [FromQuery] int limit = 50, [FromQuery] string? cursor = null, CancellationToken ct = default) =>
        service.TimelineAsync(User.GetUserId(), id, groupBy, timezone, from, to, kind, state, limit, cursor, ct);
    [HttpGet("{id:guid}/lifecycle/preview")]
    public Task<SubjectLifecyclePreview> Preview(Guid id, CancellationToken ct) => service.PreviewLifecycleAsync(User.GetUserId(), id, ct);
    [HttpPost("{id:guid}/lifecycle")]
    public Task<SubjectResponse> Lifecycle(Guid id, SubjectLifecycleRequest request, CancellationToken ct) => service.LifecycleAsync(User.GetUserId(), id, Version(), request, ct);
    [HttpPost("{id:guid}/entries")]
    public Task<SubjectEntryResponse> AddEntry(Guid id, SubjectEntryRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        service.CreateEntryAsync(User.GetUserId(), id, request, key, ct);
    [HttpGet("entries/{id:guid}")]
    public Task<SubjectEntryResponse> Entry(Guid id, CancellationToken ct) => service.GetEntryAsync(User.GetUserId(), id, ct);
    [HttpPatch("entries/{id:guid}")]
    public Task<SubjectEntryResponse> UpdateEntry(Guid id, SubjectEntryRequest request, CancellationToken ct) =>
        service.UpdateEntryAsync(User.GetUserId(), id, Version(), request, ct);
    [HttpPost("entries/{id:guid}/decision")]
    public Task<SubjectEntryResponse> DecideEntry(Guid id, SubjectEntryDecision request, CancellationToken ct) =>
        service.DecideEntryAsync(User.GetUserId(), id, Version(), request, ct);

    // Destructive actions are deliberately absent. Both clients request the same persisted button approval as AI.
    private int Version() => int.TryParse(Request.Headers.IfMatch.ToString().Trim('"'), out var version) && version > 0
        ? version : throw new PreconditionRequiredException();
}
