using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PassingTrace.Core.Events;

namespace PassingTrace.Events.Api.Events;

[ApiController]
[Authorize]
[Route("api/v1/event-taxonomy")]
public sealed class EventTaxonomyController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        version = EventTaxonomy.Version,
        categories = EventTaxonomy.Categories.Select(x => new { key = x.Key, label = x.Value }),
        behaviorTags = EventTaxonomy.BehaviorTags.Select(x => new { key = x.Key, label = x.Value }),
    });
}
