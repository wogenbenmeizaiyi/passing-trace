namespace PassingTrace.Events.Api.Places;

public sealed class AmapOptions
{
    public const string SectionName = "Amap";
    public string WebServiceKey { get; set; } = string.Empty;
    public string McpKey { get; set; } = string.Empty;
    public string McpEndpoint { get; set; } = "https://mcp.amap.com/mcp";
    public int SearchMonthlyLimit { get; set; } = 4500;
    public int LbsMonthlyLimit { get; set; } = 135000;

    public string EffectiveMcpKey => string.IsNullOrWhiteSpace(McpKey) ? WebServiceKey : McpKey;
}
