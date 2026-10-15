using Microsoft.Extensions.AI;

namespace PassingTrace.Events.Api.Ai.Capabilities;

public interface IAiCapabilityPackage
{
    string Key { get; }
    bool IsAvailable { get; }
    IReadOnlyList<string> Capabilities { get; }
    bool UsesInternalMcp => true;
    IReadOnlyList<string> WriteTools => [];
    IReadOnlyList<AITool> CreateTools();
}
