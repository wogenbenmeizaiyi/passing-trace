using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Ai.Capabilities;
using PassingTrace.Infrastructure;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class PersonalRecordToolsTests : IClassFixture<StorylinePostgresFixture>, IAsyncLifetime
{
    private readonly StorylinePostgresFixture _fixture;
    private TraceDbContext _db = null!;

    public PersonalRecordToolsTests(StorylinePostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _db = new TraceDbContext(_fixture.Options);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task RecordSearch_ExposesConfirmedLocation_ForClickableNavigation()
    {
        const long userId = 91_001;
        var happenedAt = new DateTimeOffset(2026, 9, 1, 19, 30, 0, TimeSpan.FromHours(8)).ToUniversalTime();
        var now = DateTimeOffset.UtcNow;
        var evt = Event.Create(userId, EventKind.Trace, "和朋友吃烤肉", "昨晚聚餐吃了烤肉，味道不错。",
            happenedAt, null, "Asia/Shanghai", $"personal-place-{Guid.NewGuid():N}", now);
        var revision = SourceRevision.Create(0, 1, evt.Title, evt.RawContent, happenedAt, null, now);
        evt.SourceRevisions.Add(revision);
        evt.SearchIndexes.Add(new EventSearchIndex
        {
            UserId = userId,
            SourceRevision = 1,
            Title = evt.Title!,
            RawContent = evt.RawContent!,
            RetrievalText = $"{evt.Title} {evt.RawContent}",
            IsCurrent = true,
            UpdatedAt = now,
        });
        var location = new EventLocation
        {
            UserId = userId,
            SourceRevision = 1,
            Name = "山野炉端烧",
            Address = "上海市静安区南京西路100号",
            ProviderPoiId = "test-poi-1",
            Latitude = 31.229100m,
            Longitude = 121.455200m,
            CoordinateSystem = "GCJ02",
            Source = EventLocationSource.KeywordSearch,
            UserConfirmed = true,
            CreatedAt = now,
            Revision = revision,
        };
        evt.Locations.Add(location);
        _db.Events.Add(evt);
        await _db.SaveChangesAsync();

        var tools = new PersonalRecordTools(_db, CreateCurrentUser(userId), new UnavailableEmbeddingGenerator());
        var search = await tools.SearchMyRecordsAsync("定位出我最近吃过的一家烤肉店", limit: 5);

        var place = Assert.Single(search.Places!);
        Assert.Equal(evt.Id, place.EventId);
        Assert.Equal("山野炉端烧", place.Name);
        Assert.Equal(location.Id, tools.ResolvePreferredNavigationLocationId($"就是这条 [Event #{evt.Id}]"));

        var action = await tools.GetNavigationTargetAsync(location.Id);
        Assert.NotNull(action);
        Assert.Equal("amap-navigation", action.Type);
        Assert.Equal("personal-record", action.Source);
        Assert.Equal(evt.Id, action.EventId);
        Assert.Equal(location.Id, action.LocationId);
        Assert.Equal(31.229100m, action.Latitude);
        Assert.Equal(121.455200m, action.Longitude);
    }

    [Fact]
    public async Task Expense_tool_repairs_invalid_metric_and_totals_only_owned_current_non_deleted_facts()
    {
        const long userId = 91_002;
        AddExpenseEvent(userId, 12.50m);
        var updated = AddExpenseEvent(userId, 37.50m);
        updated.CurrentSourceRevision = 2;
        updated.SemanticRuns[0].SourceRevision = 2;
        updated.SemanticRuns.Add(CreateExpenseRun(userId, 800m, sourceRevision: 1));
        AddExpenseEvent(userId, 900m, deleted: true);
        AddExpenseEvent(userId + 1, 900m);
        AddExpenseEvent(userId, 90m, "USD");
        AddExpenseEvent(userId, 700m).SemanticRuns[0].Status = SemanticRunStatus.Failed;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var tools = new PersonalRecordTools(_db, CreateCurrentUser(userId), new UnavailableEmbeddingGenerator());
        var function = new PersonalRecordsCapabilityPackage(tools).CreateTools().OfType<AIFunction>()
            .Single(tool => tool.Name == "AggregateMyRecords");

        var invalid = Assert.IsType<JsonElement>(await function.InvokeAsync(new AIFunctionArguments { ["metric"] = "sum" }));
        Assert.False(invalid.GetProperty("success").GetBoolean());
        Assert.Null(tools.Snapshot.Aggregate);

        var result = Assert.IsType<JsonElement>(await function.InvokeAsync(new AIFunctionArguments
        {
            ["metric"] = JsonSerializer.SerializeToElement("expense_total"),
        }));
        using var aggregate = JsonDocument.Parse(result.GetProperty("aggregate").GetString()!);
        Assert.Equal("expense_total", aggregate.RootElement.GetProperty("metric").GetString());
        Assert.Equal(50m, aggregate.RootElement.GetProperty("value").GetDecimal());
        Assert.Equal("CNY", aggregate.RootElement.GetProperty("currency").GetString());
        Assert.Equal(result.GetProperty("aggregate").GetString(), tools.Snapshot.Aggregate);
        Assert.Empty(_db.ChangeTracker.Entries());

        // Repairing a bad request must not lock the user out of other valid statistics.
        var countResult = Assert.IsType<JsonElement>(await function.InvokeAsync(new AIFunctionArguments { ["metric"] = "count" }));
        using var count = JsonDocument.Parse(countResult.GetProperty("aggregate").GetString()!);
        Assert.Equal(4, count.RootElement.GetProperty("value").GetInt32());
    }

    [Theory]
    [InlineData("count")]
    [InlineData("expense_total")]
    [InlineData("trend")]
    [InlineData("plan_completion_rate")]
    public async Task Every_registered_metric_is_invocable_as_a_stable_json_string(string metric)
    {
        var tools = new PersonalRecordTools(_db, CreateCurrentUser(91_099), new UnavailableEmbeddingGenerator());
        var function = new PersonalRecordsCapabilityPackage(tools).CreateTools().OfType<AIFunction>()
            .Single(tool => tool.Name == "AggregateMyRecords");
        var result = Assert.IsType<JsonElement>(await function.InvokeAsync(new AIFunctionArguments
        {
            ["metric"] = JsonSerializer.SerializeToElement(metric),
        }));
        using var aggregate = JsonDocument.Parse(result.GetProperty("aggregate").GetString()!);
        Assert.Equal(metric, aggregate.RootElement.GetProperty("metric").GetString());
    }

    [Fact]
    public async Task Mcp_statistics_roundtrip_uses_owned_database_facts_and_original_evidence_snapshot()
    {
        const long userId = 91_303;
        AddExpenseEvent(userId, 31m);
        AddExpenseEvent(userId, 19m);
        AddExpenseEvent(userId + 1, 999m);
        AddExpenseEvent(userId, 888m, deleted: true);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var tools = new PersonalRecordTools(_db, CreateCurrentUser(userId), new UnavailableEmbeddingGenerator());
        await using var session = await InternalMcpToolSession.CreateAsync(
            new PersonalRecordsCapabilityPackage(tools).CreateTools().OfType<AIFunction>());
        var function = session.Tools.OfType<AIFunction>().Single(x => x.Name == "AggregateMyRecords");
        var invalid = Assert.IsType<JsonElement>(await function.InvokeAsync(new() { ["metric"] = "sum" }));
        Assert.True(invalid.GetProperty("isError").GetBoolean());
        Assert.Null(tools.Snapshot.Aggregate);

        var result = Assert.IsType<JsonElement>(await function.InvokeAsync(new() { ["metric"] = "expense_total" }));
        Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean());
        var evidence = result.GetProperty("structuredContent");
        using var aggregate = JsonDocument.Parse(evidence.GetProperty("aggregate").GetString()!);
        Assert.Equal(50m, aggregate.RootElement.GetProperty("value").GetDecimal());
        Assert.Equal(evidence.GetProperty("aggregate").GetString(), tools.Snapshot.Aggregate);
        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Trend_tool_groups_and_orders_months_on_postgres_before_formatting()
    {
        const long userId = 91_100;
        AddExpenseEvent(userId, 1m).CreatedAt = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);
        AddExpenseEvent(userId, 1m).CreatedAt = new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero);
        AddExpenseEvent(userId, 1m).CreatedAt = new DateTimeOffset(2025, 12, 2, 0, 0, 0, TimeSpan.Zero);
        await _db.SaveChangesAsync();
        var tools = new PersonalRecordTools(_db, CreateCurrentUser(userId), new UnavailableEmbeddingGenerator());
        var result = await tools.AggregateMyRecordsAsync(RecordAggregateMetric.Trend);
        using var aggregate = JsonDocument.Parse(result.Aggregate!);
        var points = aggregate.RootElement.GetProperty("points").EnumerateArray().ToArray();
        Assert.Equal(new[] { "2025-12", "2026-01" }, points.Select(point => point.GetProperty("month").GetString()));
        Assert.Equal(new[] { 2, 1 }, points.Select(point => point.GetProperty("count").GetInt32()));
    }

    [Fact]
    public async Task Current_month_expense_query_replaces_a_hallucinated_month_and_preserves_actual_coverage()
    {
        const long userId = 91_201;
        var calendar = AssistantCalendarContext.Create(DateTimeOffset.Parse("2026-09-14T02:00:00Z"), "Asia/Shanghai");
        AddExpenseEvent(userId, 1000m).HappenedAt = DateTimeOffset.Parse("2025-01-05T02:00:00Z");
        AddExpenseEvent(userId, 900m).HappenedAt = calendar.CurrentMonth.From.ToUniversalTime().AddTicks(-10);
        AddExpenseEvent(userId, 12m).HappenedAt = calendar.CurrentMonth.From.ToUniversalTime();
        AddExpenseEvent(userId, 38m).HappenedAt = calendar.CurrentMonth.To.ToUniversalTime();
        AddExpenseEvent(userId, 800m).HappenedAt = calendar.CurrentMonth.To.ToUniversalTime().AddTicks(10);
        await _db.SaveChangesAsync();
        var tools = new PersonalRecordTools(_db, CreateCurrentUser(userId), new UnavailableEmbeddingGenerator());
        tools.ConfigureCalendarContext(calendar, "统计一下这个月消费金额");

        var result = await tools.AggregateMyRecordsAsync(RecordAggregateMetric.ExpenseTotal,
            "2025-01-01T00:00:00+08:00", "2025-01-31T23:59:59+08:00");

        using var aggregate = JsonDocument.Parse(result.Aggregate!);
        Assert.Equal(50m, aggregate.RootElement.GetProperty("value").GetDecimal());
        Assert.Equal(2, aggregate.RootElement.GetProperty("amountFactCount").GetInt32());
        Assert.True(aggregate.RootElement.GetProperty("hasAmountData").GetBoolean());
        Assert.Contains(calendar.CurrentMonth.FromText, result.TimeRange);
        Assert.Contains(calendar.CurrentMonth.ToText, result.TimeRange);
        Assert.Equal(result.TimeRange, tools.Snapshot.TimeRange);
    }

    [Fact]
    public async Task An_explicit_historical_month_and_an_all_time_request_keep_their_requested_scope()
    {
        const long userId = 91_202;
        AddExpenseEvent(userId, 10m).HappenedAt = DateTimeOffset.Parse("2025-01-05T02:00:00Z");
        AddExpenseEvent(userId, 40m).HappenedAt = DateTimeOffset.Parse("2026-09-05T02:00:00Z");
        await _db.SaveChangesAsync();
        var calendar = AssistantCalendarContext.Create(DateTimeOffset.Parse("2026-09-14T02:00:00Z"));
        var tools = new PersonalRecordTools(_db, CreateCurrentUser(userId), new UnavailableEmbeddingGenerator());
        tools.ConfigureCalendarContext(calendar, "2025年1月，把这个月的消费统计一下");
        var historical = await tools.AggregateMyRecordsAsync(RecordAggregateMetric.ExpenseTotal,
            "2025-01-01T00:00:00+08:00", "2025-01-31T23:59:59+08:00");
        using var historicalAggregate = JsonDocument.Parse(historical.Aggregate!);
        Assert.Equal(10m, historicalAggregate.RootElement.GetProperty("value").GetDecimal());

        tools.ConfigureCalendarContext(calendar, "统计所有消费金额");
        var all = await tools.AggregateMyRecordsAsync(RecordAggregateMetric.ExpenseTotal);
        using var allAggregate = JsonDocument.Parse(all.Aggregate!);
        Assert.Equal(50m, allAggregate.RootElement.GetProperty("value").GetDecimal());
        Assert.Equal("未限定 - 未限定", all.TimeRange);
    }

    [Fact]
    public async Task Missing_amount_evidence_is_unknown_instead_of_false_zero()
    {
        const long userId = 91_203;
        var evt = AddExpenseEvent(userId, 20m);
        evt.SemanticRuns.Clear();
        await _db.SaveChangesAsync();
        var tools = new PersonalRecordTools(_db, CreateCurrentUser(userId), new UnavailableEmbeddingGenerator());

        var result = await tools.AggregateMyRecordsAsync(RecordAggregateMetric.ExpenseTotal);

        using var aggregate = JsonDocument.Parse(result.Aggregate!);
        Assert.Equal(JsonValueKind.Null, aggregate.RootElement.GetProperty("value").ValueKind);
        Assert.Equal(0, aggregate.RootElement.GetProperty("amountFactCount").GetInt32());
        Assert.False(aggregate.RootElement.GetProperty("hasAmountData").GetBoolean());
        Assert.Contains("无法确认", aggregate.RootElement.GetProperty("explanation").GetString());
    }

    [Fact]
    public async Task An_explicit_zero_amount_is_still_known_data()
    {
        const long userId = 91_204;
        AddExpenseEvent(userId, 0m);
        await _db.SaveChangesAsync();
        var tools = new PersonalRecordTools(_db, CreateCurrentUser(userId), new UnavailableEmbeddingGenerator());

        var result = await tools.AggregateMyRecordsAsync(RecordAggregateMetric.ExpenseTotal);

        using var aggregate = JsonDocument.Parse(result.Aggregate!);
        Assert.Equal(0m, aggregate.RootElement.GetProperty("value").GetDecimal());
        Assert.Equal(1, aggregate.RootElement.GetProperty("amountFactCount").GetInt32());
        Assert.True(aggregate.RootElement.GetProperty("hasAmountData").GetBoolean());
    }

    private Event AddExpenseEvent(long userId, decimal amount, string currency = "CNY", bool deleted = false)
    {
        var now = DateTimeOffset.UtcNow;
        var evt = Event.Create(userId, EventKind.Trace, "一笔消费", "已记录消费金额", now, null,
            "Asia/Shanghai", $"statistics-{Guid.NewGuid():N}", now);
        evt.DeletedAt = deleted ? now : null;
        evt.SemanticRuns.Add(CreateExpenseRun(userId, amount, currency));
        _db.Events.Add(evt);
        return evt;
    }

    private static EventSemanticRun CreateExpenseRun(long userId, decimal amount, string currency = "CNY", int sourceRevision = 1) => new()
    {
        UserId = userId,
        SourceRevision = sourceRevision,
        Status = SemanticRunStatus.Completed,
        CreatedAt = DateTimeOffset.UtcNow,
        Expenses = [new ExpenseFact { UserId = userId, Amount = amount, Currency = currency }],
    };

    private static CurrentUserContext CreateCurrentUser(long userId)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test")),
        };
        return new CurrentUserContext(new HttpContextAccessor { HttpContext = context });
    }

    private sealed class UnavailableEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromException<GeneratedEmbeddings<Embedding<float>>>(new InvalidOperationException("not configured"));

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == GetType() ? this : null;

        public void Dispose() { }
    }
}
