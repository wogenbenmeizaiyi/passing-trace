using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PassingTrace.Events.Api.Ai.Capabilities;

public sealed record AiCapabilityStatus(bool Available, IReadOnlyList<string> Capabilities);
public sealed record AiCapabilitiesResponse(AiCapabilityStatus Amap);

[ApiController]
[Authorize]
[Route("api/v1/ai/capabilities")]
public sealed class AiCapabilitiesController(IEnumerable<IAiCapabilityPackage> packages) : ControllerBase
{
    [HttpGet]
    public ActionResult<AiCapabilitiesResponse> Get()
    {
        var amap = packages.Single(x => x.Key == "amap");
        return Ok(new AiCapabilitiesResponse(new AiCapabilityStatus(amap.IsAvailable, amap.Capabilities)));
    }
}
