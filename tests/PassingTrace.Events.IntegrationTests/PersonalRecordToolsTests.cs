using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Events.Api.Ai;
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
