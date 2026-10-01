using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassingTrace.Core.Ai;
using PassingTrace.Events.Api.Ai.Amap;
using PassingTrace.Events.Api.Ai.Assistant;
using PassingTrace.Events.Api.Ai.Assistant.Context;
using PassingTrace.Events.Api.Ai.Assistant.Presentation;
using PassingTrace.Events.Api.Ai.Models;
using PassingTrace.Events.Api.Ai.Tools.Queries;
using PassingTrace.Events.Api.Common;
using PassingTrace.Events.Api.Places;
using PassingTrace.Infrastructure;
using PassingTrace.Infrastructure.Persistence.Ai;
using StackExchange.Redis;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AssistantLocationTests(StorylinePostgresFixture fixture) : IClassFixture<StorylinePostgresFixture>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-01T01:00:00Z");
    private static AssistantLocationRequest Location => new(30.123456m, 120.654321m, 25, Now, "GCJ02");

    [Theory]
    [InlineData(-91, 120, 20, "GCJ02", 0)]
    [InlineData(30, 181, 20, "GCJ02", 0)]
    [InlineData(30, 120, 0, "GCJ02", 0)]
    [InlineData(30, 120, double.NaN, "GCJ02", 0)]
    [InlineData(30, 120, 100001, "GCJ02", 0)]
    [InlineData(30, 120, 20, "BD09", 0)]
    [InlineData(30, 120, 20, "GCJ02", -301)]
    [InlineData(30, 120, 20, "GCJ02", 61)]
    public async Task Invalid_or_stale_location_is_rejected_before_conversion(int lat, int lon, double accuracy, string system, int age)
    {
        var error = await Assert.ThrowsAsync<AssistantLocationException>(() => AssistantLocationContext.CreateAsync(
            new(lat, lon, accuracy, Now.AddSeconds(age), system), Now, null, default));
        Assert.Equal(age == 0 ? "invalid_location" : "expired_location", error.Code);
        Assert.Contains("重新", AssistantErrorPresenter.Present(error).Message);
    }

    [Fact]
    public async Task Gcj02_is_not_converted_and_absent_location_does_not_reuse_it()
    {
        var context = await AssistantLocationContext.CreateAsync(Location, Now, null, default);
        Assert.Equal(Location, context.Location);
        Assert.Contains("30.123456", context.Instructions);
        Assert.Contains("精度约 25 米", context.Instructions);
        Assert.Contains("ReverseGeocodeAmapLocation", context.Instructions);
        var empty = await AssistantLocationContext.CreateAsync(null, Now, null, default);
        Assert.Null(empty.Location);
        Assert.Contains("使用当前位置", empty.Instructions);
        Assert.DoesNotContain("30.123456", empty.Instructions);
    }

    [Fact]
    public async Task Browser_gps_uses_official_conversion_and_preserves_accuracy_and_capture_time()
    {
        var handler = new ConversionHandler("""{"status":"1","locations":"120.659,30.128"}""");
        var quota = new Quota();
        var converter = Converter(handler, quota);
        var context = await AssistantLocationContext.CreateAsync(Location with { CoordinateSystem = "WGS84" }, Now, converter, default);
        Assert.Equal(30.128m, context.Location!.Latitude);
        Assert.Equal(120.659m, context.Location.Longitude);
        Assert.Equal("GCJ02", context.Location.CoordinateSystem);
        Assert.Equal(Location.AccuracyMeters, context.Location.AccuracyMeters);
        Assert.Equal(Now, context.Location.CapturedAt);
        Assert.Equal(AmapQuotaKind.Lbs, Assert.Single(quota.Calls));
        Assert.Equal("/v3/assistant/coordinate/convert", handler.Uri!.AbsolutePath);
        Assert.Contains("coordsys=gps", handler.Uri.Query);
        Assert.Contains("locations=120.654321%2C30.123456", handler.Uri.Query);
    }

    [Theory]
    [InlineData("{\"status\":\"0\",\"info\":\"secret-key\"}")]
    [InlineData("{\"status\":\"1\",\"locations\":\"wrong\"}")]
    [InlineData("{\"status\":\"1\",\"locations\":\"181,30\"}")]
    [InlineData("broken json")]
    public async Task Conversion_failure_is_actionable_and_does_not_expose_coordinates_or_keys(string json)
    {
        var error = await Assert.ThrowsAsync<AssistantLocationException>(() => Converter(new(json), new()).ConvertAsync(30, 120, default));
        Assert.Equal("location_conversion_unavailable", error.Code);
        Assert.DoesNotContain("secret-key", error.ToString());
        Assert.DoesNotContain("locations=", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task Missing_key_or_exhausted_quota_does_not_send_http_request()
    {
        var handler = new ConversionHandler("{}");
        await Assert.ThrowsAsync<AssistantLocationException>(() => Converter(handler, new(), "").ConvertAsync(30, 120, default));
        await Assert.ThrowsAsync<AssistantLocationException>(() => Converter(handler, new Quota { Allowed = false }).ConvertAsync(30, 120, default));
        Assert.Null(handler.Uri);
    }

    [Fact]
    public async Task Real_chat_injects_location_only_for_current_turn_bypasses_cache_and_does_not_save_device_coordinates()
    {
        await using var db = new TraceDbContext(fixture.Options);
        var userId = Random.Shared.NextInt64(10_000_000, 20_000_000);
        var conversation = new AiConversation { Id = Guid.NewGuid(), UserId = userId, Title = "位置测试", CreatedAt = Now, UpdatedAt = Now };
        db.AiConversations.Add(conversation);
        await db.SaveChangesAsync();
        var cacheReads = 0;
        var cache = AssistantSkillServiceTests.StrictProxy.Create<IDatabase>(method => method.Name == "StringGetAsync"
            ? ReadCache() : throw new InvalidOperationException("Location responses must not be cached"));
        Task<RedisValue> ReadCache() { cacheReads++; return Task.FromResult(RedisValue.Null); }
        var redis = AssistantSkillServiceTests.StrictProxy.Create<IConnectionMultiplexer>(method => method.Name == "GetDatabase" ? cache : throw new InvalidOperationException());
        var user = new CurrentUserContext(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test")) } });
        var embedding = AssistantSkillServiceTests.StrictProxy.Create<IEmbeddingGenerator<string, Embedding<float>>>(_ => throw new InvalidOperationException("No background record search"));
        var gateway = AssistantSkillServiceTests.StrictProxy.Create<IAmapMcpGateway>(method => method.Name == "get_IsConfigured" ? false : throw new InvalidOperationException());
        var personal = new PersonalRecordTools(new PersonalRecordQueries(db), user, embedding);
        using var model = new LocationModel();
        using var services = new ServiceCollection().BuildServiceProvider();
        AssistantService Service() => new(new AiConversationRepository(db), user, personal, new AmapAiTools(gateway, new Quota(), new Clock()), [],
            model, redis, Options.Create(new AiModelOptions()), NullLoggerFactory.Instance, services, new Clock());
        await foreach (var _ in Service().SendAsync(conversation.Id, "从这里出发", default, location: Location)) { }
        Assert.Equal(0, cacheReads);
        Assert.Contains("30.123456", model.Contexts[0]);
        Assert.Contains("GCJ02", model.Contexts[0]);
        await foreach (var _ in Service().SendAsync(conversation.Id, "继续聊", default)) { }
        Assert.Equal(1, cacheReads);
        Assert.Contains("本条消息未附加设备定位", model.Contexts[1]);
        Assert.DoesNotContain("30.123456", model.Contexts[1]);
        var saved = await db.AiMessages.Where(x => x.ConversationId == conversation.Id).ToListAsync();
        Assert.All(saved, x => Assert.DoesNotContain("30.123456", x.Content + x.EvidenceSnapshotJson));
        Assert.False(await db.Events.AnyAsync(x => x.UserId == userId));
        Assert.False(await db.UserMemories.AnyAsync(x => x.UserId == userId));
        var before = saved.Count;
        await Assert.ThrowsAsync<AssistantLocationException>(async () => { await foreach (var _ in Service().SendAsync(conversation.Id, "位置", default, location: Location with { CapturedAt = Now.AddMinutes(-6) })) { } });
        Assert.Equal(before, await db.AiMessages.CountAsync(x => x.ConversationId == conversation.Id));
        var foreign = new AiConversation { Id = Guid.NewGuid(), UserId = userId + 1, Title = "其他用户", CreatedAt = Now, UpdatedAt = Now };
        db.AiConversations.Add(foreign);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(async () => { await foreach (var _ in Service().SendAsync(foreign.Id, "位置", default, location: Location)) { } });
    }

    private static AmapCoordinateConverter Converter(ConversionHandler handler, Quota quota, string key = "secret-key") =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://restapi.amap.com") }, Options.Create(new AmapOptions { WebServiceKey = key }), quota);
    private sealed class ConversionHandler(string json) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
    private sealed class Quota : IAmapQuotaGuard
    {
        public bool Allowed { get; init; } = true;
        public List<AmapQuotaKind> Calls { get; } = [];
        public Task<bool> TryConsumeAsync(AmapQuotaKind kind, CancellationToken cancellationToken) { Calls.Add(kind); return Task.FromResult(Allowed); }
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class LocationModel : IChatClient
    {
        public List<string> Contexts { get; } = [];
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Contexts.Add(string.Join('\n', messages.Select(x => x.Text).Prepend(options?.Instructions)));
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent("已收到本条消息。")], FinishReason = ChatFinishReason.Stop };
            await Task.CompletedTask;
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
