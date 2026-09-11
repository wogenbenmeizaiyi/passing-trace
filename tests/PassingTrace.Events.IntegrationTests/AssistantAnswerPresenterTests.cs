using PassingTrace.Events.Api.Ai;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AssistantAnswerPresenterTests
{
    [Fact]
    public void PersonalNavigation_UsesRecordTitleWithoutExposingTechnicalFields()
    {
        var evidence = Bundle(
            places:
            [
                new PlaceEvidence(2, 2, "和朋友吃烤肉", "东北烧烤地带", "杭州市富阳区九龙大道187号",
                    null, new DateTimeOffset(2026, 8, 31, 18, 0, 0, TimeSpan.FromHours(8)), 1),
            ]);
        var action = Navigation("东北烧烤地带", "personal-record", eventId: 2, locationId: 2);
        const string generated =
            "我刚才把 candidateId 当成 locationId 了，真正的 locationId 是 2，坐标为 GCJ02。[导航](amap-navigation://open?id=2)";

        var answer = AssistantAnswerPresenter.Present(generated, "重新试一下", evidence, [action]);

        Assert.Contains("东北烧烤地带", answer);
        Assert.Contains("和朋友吃烤肉", answer);
        Assert.Contains("[Event #2]", answer);
        Assert.Contains("点击下方的“高德导航”", answer);
        Assert.DoesNotContain("locationId", answer);
        Assert.DoesNotContain("candidateId", answer);
        Assert.DoesNotContain("GCJ02", answer);
        Assert.DoesNotContain("amap-navigation://", answer);
    }

    [Fact]
    public void LiveAmapNavigation_IsClearlyAttributedWithoutPoiOrCoordinates()
    {
        var action = Navigation("人民广场地铁站", "amap-live");

        var answer = AssistantAnswerPresenter.Present(
            "POI ID 是 B000A8UHRS，坐标系 GCJ02。", "导航到人民广场地铁站", Bundle(), [action]);

        Assert.Contains("来自高德地图", answer);
        Assert.Contains("人民广场地铁站", answer);
        Assert.DoesNotContain("POI", answer);
        Assert.DoesNotContain("B000A8UHRS", answer);
        Assert.DoesNotContain("GCJ02", answer);
    }

    [Fact]
    public void NonNavigationAnswer_RemovesRawProtocolAndBareEventIdentifier()
    {
        const string generated = "根据 Event #2 已处理。[打开](amap://route?to=120,30)";

        var answer = AssistantAnswerPresenter.Present(generated, "再试试", Bundle(), []);

        Assert.Contains("根据这条记录", answer);
        Assert.DoesNotContain("Event #2", answer);
        Assert.DoesNotContain("amap://", answer);
    }

    private static EvidenceBundle Bundle(IReadOnlyList<PlaceEvidence>? places = null) =>
        new([], [], Places: places ?? []);

    private static AssistantAction Navigation(
        string name,
        string source,
        long? eventId = null,
        long? locationId = null) =>
        new("amap-navigation", "amap", $"导航到{name}", name, "测试地址", 30.2m, 120.1m,
            "GCJ02", "B000A8UHRS", source, eventId, locationId);
}
