using System.Text.Json;
using Microsoft.Extensions.AI;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Ai.Amap;
using PassingTrace.Events.Api.Ai.Capabilities;
using PassingTrace.Events.Api.Ai.Skills;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AssistantSkillTests
{
    [Fact]
    public void Catalog_is_embedded_unique_and_versioned()
    {
        Assert.Equal(10, AssistantSkillCatalog.All.Count);
        Assert.Equal(10, AssistantSkillCatalog.All.Select(x => x.Key).Distinct().Count());
        Assert.All(AssistantSkillCatalog.All, skill => Assert.False(string.IsNullOrWhiteSpace(skill.Instructions)));
        Assert.Equal(64, AssistantSkillCatalog.Version.Length);
    }

    [Theory]
    [InlineData("../../appsettings.json")]
    [InlineData("https://example.com/SKILL.md")]
    [InlineData("unknown")]
    public void Skill_reader_never_accepts_paths_or_external_rules(string key)
    {
        var session = new AssistantSkillSession();
        Assert.False(session.ReadAssistantSkill(key).Success);
        Assert.Empty(session.LoadedSkills);
        Assert.False(session.Allows("SearchMyRecords"));
    }

    [Theory]
    [InlineData("conversation")]
    [InlineData("conversation-summary")]
    [InlineData("planning")]
    public async Task Text_only_scenarios_do_not_unlock_queries_or_overwrite_answers_without_evidence(string key)
    {
        var session = new AssistantSkillSession();
        Assert.True(session.ReadAssistantSkill(key).Success);
        var called = false;
        var tool = session.Protect(AIFunctionFactory.Create(() => { called = true; return "private data"; }, "SearchMyRecords"));
        var denial = Assert.IsType<JsonElement>(await tool.InvokeAsync(new()));
        Assert.Equal("assistant_skill_required", denial.GetProperty("code").GetString());
        Assert.False(called);
        Assert.False(session.HasSuccessfulLookup);
        Assert.False(session.NeedsEvidenceFallback(new([], [])));
        Assert.False(session.CanCacheAnswer);
    }

    [Theory]
    [InlineData("records", "SearchMyRecords", "AggregateMyRecords")]
    [InlineData("statistics", "AggregateMyRecords", "SearchSharedContent")]
    [InlineData("storylines", "GetMyStorylineEvidence", "GetAmapWeather")]
    [InlineData("friends", "AggregateMyFriendActivities", "SearchSharedContent")]
    [InlineData("shared-content", "SearchSharedContent", "SearchMyRecords")]
    [InlineData("amap", "GetNavigationTarget", "SearchSharedContent")]
    [InlineData("record-summary", "AggregateMyRecords", "SearchSharedContent")]
    public void Each_scenario_has_a_limited_tool_grant(string key, string allowed, string denied)
    {
        var session = new AssistantSkillSession();
        session.ReadAssistantSkill(key);
        Assert.True(session.Allows(allowed));
        Assert.False(session.Allows(denied));
        Assert.False(session.Allows("SaveMyRecords"));
        Assert.False(session.Allows("SendMessage"));
        Assert.False(session.Allows("FutureUnregisteredTool"));
    }

    [Fact]
    public async Task Skill_selection_and_queries_use_real_mcp_and_keep_input_validation()
    {
        var session = new AssistantSkillSession();
        var calls = 0;
        var read = AIFunctionFactory.Create((string query) => { calls++; return query; }, "SearchMyRecords");
        await using var mcp = await InternalMcpToolSession.CreateAsync([session.CreateReader(), read]);
        var reader = (AIFunction)mcp.Tools.Single(x => x.Name == "ReadAssistantSkill");
        var data = session.Protect((AIFunction)mcp.Tools.Single(x => x.Name == "SearchMyRecords"));
        Assert.Equal(read.JsonSchema.GetProperty("properties").GetRawText(), data.JsonSchema.GetProperty("properties").GetRawText());

        await data.InvokeAsync(new() { ["query"] = "午饭" });
        Assert.Equal(0, calls);
        var guide = Assert.IsType<JsonElement>(await reader.InvokeAsync(new() { ["key"] = "records" }));
        Assert.True(guide.GetProperty("structuredContent").GetProperty("success").GetBoolean());

        var invalid = Assert.IsType<JsonElement>(await data.InvokeAsync(new() { ["query"] = 42 }));
        Assert.True(invalid.GetProperty("isError").GetBoolean());
        Assert.Equal(0, calls);
        Assert.False(session.HasSuccessfulLookup);
        await data.InvokeAsync(new() { ["query"] = "午饭" });
        Assert.Equal(1, calls);
        Assert.True(session.HasSuccessfulLookup);
        Assert.True(session.CanCacheAnswer);
        Assert.True(session.NeedsEvidenceFallback(new([], [])));
        Assert.False(session.NeedsEvidenceFallback(new([], [], Aggregate: "有核实过的统计")));
    }

    [Fact]
    public async Task Map_guard_blocks_provider_before_skill_and_disables_cache_even_when_unavailable()
    {
        var session = new AssistantSkillSession();
        var calls = 0;
        var tool = session.Protect(AIFunctionFactory.Create(() =>
        {
            calls++;
            return new AmapToolResponse(false, "weather", "amap-live", "暂不可用");
        }, "GetAmapWeather"));
        Assert.IsType<JsonElement>(await tool.InvokeAsync(new()));
        Assert.Equal(0, calls);
        session.ReadAssistantSkill("amap");
        var result = Assert.IsType<JsonElement>(await tool.InvokeAsync(new()));
        Assert.False(result.Deserialize<AmapToolResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Success);
        Assert.Equal(1, calls);
        Assert.False(session.HasSuccessfulLookup);
        Assert.False(session.CanCacheAnswer);
        Assert.False(session.NeedsEvidenceFallback(new([], [])));
    }

    [Fact]
    public async Task Grants_reset_every_turn_and_repeated_unguarded_calls_stop()
    {
        var previous = new AssistantSkillSession();
        previous.ReadAssistantSkill("records");
        var current = new AssistantSkillSession();
        var tool = current.Protect(AIFunctionFactory.Create(new Func<string>(() => throw new InvalidOperationException()), "SearchMyRecords"));
        await tool.InvokeAsync(new());
        await tool.InvokeAsync(new());
        await Assert.ThrowsAsync<AssistantToolInvocationException>(() => tool.InvokeAsync(new()).AsTask());
        Assert.Empty(current.LoadedSkills);
    }

    [Fact]
    public async Task Tool_calls_are_bounded_and_cancellation_prevents_invocation()
    {
        var session = new AssistantSkillSession();
        session.ReadAssistantSkill("records");
        var calls = 0;
        var tool = session.Protect(AIFunctionFactory.Create(() => ++calls, "SearchMyRecords"));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.InvokeAsync(new(), canceled.Token).AsTask());
        Assert.Equal(0, calls);
        for (var index = 0; index < 16; index++) await tool.InvokeAsync(new());
        await Assert.ThrowsAsync<AssistantToolInvocationException>(() => tool.InvokeAsync(new()).AsTask());
        Assert.Equal(16, calls);
    }
}
