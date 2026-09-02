using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Ai.Amap;
using System.Text.Json;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AmapAiToolsTests
{
    [Fact]
    public async Task Search_NormalizesTopThreeCandidates_AndCreatesSafeNavigationAction()
    {
        var gateway = new FakeGateway(Response("""
            {"pois":[
              {"id":"p1","name":"人民广场地铁站","address":"黄浦区","cityname":"上海市","location":"121.475,31.232"},
              {"id":"p2","name":"人民广场","address":"人民大道","cityname":"上海市","location":"121.474,31.231"},
              {"id":"p3","name":"人民公园","address":"南京西路","cityname":"上海市","location":"121.469,31.233"},
              {"id":"p4","name":"第四个","address":"测试路","cityname":"上海市","location":"121.4,31.2"}
            ]}
            """));
        var tools = new AmapAiTools(gateway, new FakeQuotaGuard(), TimeProvider.System);

        var search = await tools.SearchAmapPlacesAsync("人民广场", "上海");
        var action = await tools.CreateAmapNavigationAsync("p1");

        Assert.True(search.Success);
        Assert.True(search.RequiresSelection);
        Assert.Equal(3, search.Places!.Count);
        Assert.True(action.Success);
        Assert.Equal("amap-navigation", action.Action!.Type);
        Assert.Equal("GCJ02", action.Action.CoordinateSystem);
        Assert.Null(action.Action.WebUrl);
        Assert.Single(tools.Snapshot.Actions);
    }

    [Fact]
    public async Task Navigation_RejectsUnknownCandidate_AndCanUseSeededConversationCandidate()
    {
        var tools = new AmapAiTools(new FakeGateway(Response("{}")), new FakeQuotaGuard(), TimeProvider.System);
        tools.SeedCandidates([
            new AmapPlaceEvidence("known", "poi", "西湖风景名胜区", "龙井路1号", null, "杭州市", "西湖区", 30.25m, 120.14m),
            new AmapPlaceEvidence("known-2", "poi-2", "人民广场地铁站", "人民大道", null, "上海市", "黄浦区", 31.23m, 121.47m),
        ]);

        var rejected = await tools.CreateAmapNavigationAsync("invented");
        var accepted = await tools.CreateAmapNavigationAsync("known");
        var acceptedByPoiId = await tools.CreateAmapNavigationAsync("poi-2");
        var acceptedByName = await tools.CreateAmapNavigationAsync("人民广场地铁站");

        Assert.False(rejected.Success);
        Assert.True(accepted.Success);
        Assert.Equal("西湖风景名胜区", accepted.Action!.PlaceName);
        Assert.True(acceptedByPoiId.Success);
        Assert.True(acceptedByName.Success);
        Assert.Equal("人民广场地铁站", acceptedByName.Action!.PlaceName);
    }

    [Fact]
    public async Task SingleCandidate_AllowsModelAlias_ForDetailsAndNavigation()
    {
        var place = Response("""
            {"pois":[{"id":"B000A8UHRS","name":"人民广场(地铁站)","location":"121.475,31.232"}]}
            """);
        var gateway = new FakeGateway(place);
        var tools = new AmapAiTools(gateway, new FakeQuotaGuard(), TimeProvider.System);
        tools.SeedCandidates([
            new AmapPlaceEvidence(
                "B000A8UHRS", "B000A8UHRS", "人民广场(地铁站)", "人民大道",
                null, "上海市", "黄浦区", 31.232m, 121.475m),
        ]);

        var details = await tools.GetAmapPlaceDetailsAsync("人民广场（地铁站）");
        var navigation = await tools.CreateAmapNavigationAsync("人民广场地铁站");

        Assert.True(details.Success);
        Assert.True(navigation.Success);
        Assert.Equal("B000A8UHRS", gateway.Calls[0].Arguments["id"]);
        Assert.Equal("人民广场(地铁站)", navigation.Action!.PlaceName);
    }

    [Fact]
    public async Task Search_ReadsTextWhenStructuredEnvelopeHasNoPlaces_AndFindsNestedPayloads()
    {
        using var document = JsonDocument.Parse("""
            {"result":{"providerPayload":"{\"pois\":[{\"id\":\"p1\",\"name\":\"人民广场(地铁站)\",\"location\":\"121.475,31.232\"}]}"}}
            """);
        var response = new AmapMcpResponse(
            "高德结果：{\"pois\":[{\"id\":\"p2\",\"name\":\"人民公园\",\"location\":\"121.469,31.233\"}]} 后续说明",
            document.RootElement.Clone());
        var tools = new AmapAiTools(new FakeGateway(response), new FakeQuotaGuard(), TimeProvider.System);

        var search = await tools.SearchAmapPlacesAsync("人民广场", "上海");

        Assert.True(search.Success);
        Assert.Equal(2, search.Places!.Count);
        Assert.Contains(search.Places, place => place.CandidateId == "p1");
        Assert.Contains(search.Places, place => place.CandidateId == "p2");
    }

    [Fact]
    public async Task Navigation_UsesMostRecentUnambiguousResult_AfterBroadSearch()
    {
        var broad = Response("""
            {"pois":[
              {"id":"shop-1","name":"人民广场商店","location":"121.47,31.23"},
              {"id":"shop-2","name":"人民广场餐厅","location":"121.48,31.24"}
            ]}
            """);
        var exact = Response("""
            {"pois":[{"id":"station","name":"人民广场(地铁站)","location":"121.475,31.232"}]}
            """);
        var tools = new AmapAiTools(
            new FakeGateway(broad, exact), new FakeQuotaGuard(), TimeProvider.System);

        await tools.SearchAmapPlacesAsync("人民广场", "上海");
        await tools.SearchAmapPlacesAsync("上海人民广场地铁站", "上海");
        var navigation = await tools.CreateAmapNavigationAsync("模型改写后的地点名称");

        Assert.True(navigation.Success);
        Assert.Equal("station", tools.PreferredNavigationCandidate!.CandidateId);
        Assert.Equal("人民广场(地铁站)", navigation.Action!.PlaceName);
    }

    [Fact]
    public async Task SearchWithoutCoordinates_AllowsDetailsToResolveNavigationCandidate()
    {
        var searchResponse = Response("""
            {"pois":[{"id":"BV10024678","name":"人民广场(地铁站)","address":"1号线;2号线;8号线"}]}
            """);
        var detailResponse = Response("""
            {"id":"BV10024678","name":"人民广场(地铁站)","location":"121.475108,31.232687","address":"1号线;2号线;8号线","city":"上海市"}
            """);
        var gateway = new FakeGateway(searchResponse, detailResponse);
        var tools = new AmapAiTools(gateway, new FakeQuotaGuard(), TimeProvider.System);

        var search = await tools.SearchAmapPlacesAsync("上海人民广场地铁站", "上海");
        var navigation = await tools.CreateAmapNavigationAsync("上海人民广场地铁站");

        Assert.Empty(search.Places!);
        Assert.True(navigation.Success);
        Assert.Equal("BV10024678", gateway.Calls[1].Arguments["id"]);
        Assert.Equal(121.475108m, navigation.Action!.Longitude);
        Assert.Equal(31.232687m, navigation.Action.Latitude);
    }

    [Fact]
    public async Task QuotaProtection_StopsCallBeforeGateway()
    {
        var gateway = new FakeGateway(Response("{}"));
        var tools = new AmapAiTools(gateway, new FakeQuotaGuard(allow: false), TimeProvider.System);

        var result = await tools.GetAmapWeatherAsync("杭州");

        Assert.False(result.Success);
        Assert.Contains("额度保护", result.Error);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public async Task TurnLimit_AllowsAtMostSixAmapToolCalls()
    {
        var gateway = new FakeGateway(Response("{\"forecasts\":[]}"));
        var tools = new AmapAiTools(gateway, new FakeQuotaGuard(), TimeProvider.System);

        for (var index = 0; index < 6; index++)
            Assert.True((await tools.GetAmapWeatherAsync("杭州")).Success);
        var seventh = await tools.GetAmapWeatherAsync("杭州");

        Assert.False(seventh.Success);
        Assert.Contains("最多调用 6 次", seventh.Error);
        Assert.Equal(6, gateway.CallCount);
    }

    [Fact]
    public async Task TripMap_RejectsUntrustedProviderUrl()
    {
        var gateway = new FakeGateway(Response("https://evil.example/trip"));
        var tools = new AmapAiTools(gateway, new FakeQuotaGuard(), TimeProvider.System);

        var result = await tools.CreateAmapTripMapAsync("杭州一天", "西湖 -> 灵隐寺");

        Assert.False(result.Success);
        Assert.Empty(tools.Snapshot.Actions);
    }

    [Fact]
    public async Task StableContracts_MapToProviderTools_ForDetailGeocodeWeatherRouteAndDistance()
    {
        var place = Response("""
            {"pois":[{"id":"p1","name":"人民广场地铁站","location":"121.475,31.232"}]}
            """);
        var gateway = new FakeGateway(place, place, Response("{}"), Response("{}"), Response("{}"), Response("{}"));
        var tools = new AmapAiTools(gateway, new FakeQuotaGuard(), TimeProvider.System);
        tools.SeedCandidates([
            new AmapPlaceEvidence("p1", "p1", "人民广场地铁站", null, null, "上海市", null, 31.232m, 121.475m),
        ]);

        Assert.True((await tools.GetAmapPlaceDetailsAsync("p1")).Success);
        Assert.True((await tools.GeocodeAmapAddressAsync("人民大道", "上海")).Success);
        Assert.True((await tools.ReverseGeocodeAmapLocationAsync(121.475m, 31.232m)).Success);
        Assert.True((await tools.GetAmapWeatherAsync("上海")).Success);
        Assert.True((await tools.PlanAmapRouteAsync(
            121.47m, 31.23m, 121.49m, 31.24m, "driving")).Success);
        Assert.True((await tools.MeasureAmapDistanceAsync(
            121.47m, 31.23m, 121.49m, 31.24m)).Success);

        Assert.Collection(gateway.Calls,
            call => Assert.Contains("maps_search_detail", call.ToolNames),
            call => Assert.Contains("maps_geo", call.ToolNames),
            call => Assert.Contains("maps_regeocode", call.ToolNames),
            call => Assert.Contains("maps_weather", call.ToolNames),
            call => Assert.Contains("maps_direction_driving", call.ToolNames),
            call => Assert.Contains("maps_distance", call.ToolNames));
        Assert.Equal("121.475,31.232", gateway.Calls[2].Arguments["location"]);
    }

    [Fact]
    public async Task MissingConfiguration_DegradesWithoutCallingProviderOrQuota()
    {
        var gateway = new FakeGateway(Response("{}")) { IsConfigured = false };
        var quota = new FakeQuotaGuard();
        var tools = new AmapAiTools(gateway, quota, TimeProvider.System);

        var result = await tools.GetAmapWeatherAsync("杭州");

        Assert.False(result.Success);
        Assert.Contains("尚未配置", result.Error);
        Assert.Equal(0, gateway.CallCount);
        Assert.Equal(0, quota.CallCount);
    }

    private static AmapMcpResponse Response(string text) => new(text, null);

    private sealed class FakeGateway(params AmapMcpResponse[] responses) : IAmapMcpGateway
    {
        private readonly Queue<AmapMcpResponse> _responses = new(responses);
        public bool IsConfigured { get; set; } = true;
        public int CallCount { get; private set; }
        public List<GatewayCall> Calls { get; } = [];

        public Task<AmapMcpResponse> CallAsync(
            IReadOnlyList<string> toolNames,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Calls.Add(new GatewayCall(toolNames.ToArray(), new Dictionary<string, object?>(arguments)));
            return Task.FromResult(_responses.Count == 0 ? Response("{}") : _responses.Dequeue());
        }
    }

    private sealed record GatewayCall(
        IReadOnlyList<string> ToolNames,
        IReadOnlyDictionary<string, object?> Arguments);

    private sealed class FakeQuotaGuard(bool allow = true) : IAmapQuotaGuard
    {
        public int CallCount { get; private set; }

        public Task<bool> TryConsumeAsync(AmapQuotaKind kind, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(allow);
        }
    }
}
