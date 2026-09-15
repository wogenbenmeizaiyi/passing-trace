using System.Security.Cryptography;
using System.Text;
using PassingTrace.Core.Events;
using PassingTrace.Core.Storylines;
using PassingTrace.Events.Api.Events;
using PassingTrace.Events.Api.Storylines;

namespace PassingTrace.Events.Api.Development;

/// <summary>仅供本地开发初始化使用的固定样例；不请求外部服务或下载素材。</summary>
public static class DevelopmentDemoCatalog
{
    public const string Version = "v1";
    public const int StorylineCount = 5;
    private const string Timezone = "Asia/Shanghai";
    private static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);

    public sealed record DemoEvent(string Key, CreateEventRequest Request, EventStatus? PlanStatus = null);
    public sealed record DemoStoryline(string Key, IReadOnlyList<string> EventKeys, SaveStorylineRequest Request);

    /// <summary>日期相对首次初始化时间生成，调用方使用稳定幂等键保留之后的编辑。</summary>
    public static IReadOnlyList<DemoEvent> Events(DateTimeOffset anchor)
    {
        var local = anchor.ToOffset(ChinaOffset);
        var today = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, ChinaOffset);
        var month = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, ChinaOffset);
        var previousMonth = month.AddMonths(-1);
        var previousYear = new DateTimeOffset(local.Year - 1, 1, 1, 0, 0, 0, ChinaOffset);
        var lake = Place("西湖风景名胜区", "杭州市西湖区龙井路1号", "西湖区", 30.242887m, 120.150219m);
        var station = Place("杭州东站", "杭州市上城区天城路1号", "上城区", 30.290729m, 120.212851m);
        var museum = Place("浙江省博物馆（孤山馆区）", "杭州市西湖区孤山路25号", "西湖区", 30.250175m, 120.141114m);
        var park = Place("太子湾公园", "杭州市西湖区南山路5-1号", "西湖区", 30.220258m, 120.136642m);

        return
        [
            Plan("trip-ticket", "订好去杭州的车票", "挑了上午出发的车次，留出一整天慢慢逛。车票已订好，记得带身份证。", previousMonth.AddDays(11).AddHours(20), "transport", ["public-transit"], ["西湖周末"], EventStatus.Completed),
            Trace("trip-train", "坐高铁到杭州东站", "一路看窗外的田野，出站后先寄存行李，再坐地铁去湖边。", previousMonth.AddDays(15).AddHours(8), "transport", ["public-transit"], ["西湖周末"], station),
            Trace("trip-lake", "沿着西湖慢慢走了一上午", "从断桥走到白堤，风很舒服。朋友去博物馆，我留在湖边拍照，约好傍晚一起吃饭。", previousMonth.AddDays(15).AddHours(10), "scenery", ["walking", "photography"], ["西湖周末", "慢旅行"], lake),
            Trace("trip-museum", "朋友发来的孤山博物馆见闻", "朋友选了博物馆这条路线，分享了展厅里喜欢的器物。下次来杭州想一起认真看一遍。", previousMonth.AddDays(15).AddHours(10).AddMinutes(30), "travel", ["museum", "friends"], ["西湖周末"], museum),
            Trace("trip-dinner", "在湖边会合吃晚饭", "两条路线在晚饭时会合。点了东坡肉、清炒时蔬和一碗汤，两个人合计花了168元。", previousMonth.AddDays(15).AddHours(18), "food", ["dining", "friends"], ["西湖周末"]),
            Trace("trip-review", "把西湖周末的照片整理成相册", "选了12张照片，写下最喜欢的三个瞬间：车窗外的光、湖边的风和晚饭时聊不完的话。", previousMonth.AddDays(16).AddHours(20), "home", ["photography", "writing"], ["西湖周末", "值得再来"]),

            Trace("exercise-baseline", "第一次认真记录跑步距离", "跑了三公里，中途走了两次。先不追求速度，能规律出门就是进步。", previousMonth.AddDays(22).AddHours(7), "exercise", ["running"], ["十公里挑战"]),
            Trace("exercise-run", "晨跑五公里，终于不用中途停下", "七点前出门，配速比上次稳定。跑完拉伸十分钟，今天状态不错。", today.AddDays(-10).AddHours(7), "exercise", ["running"], ["十公里挑战", "小进步"]),
            Trace("exercise-cycle", "沿江骑行十二公里", "换一种方式活动身体，江边风有点大，最后一段放慢速度。回家后记得补水。", today.AddDays(-7).AddHours(17), "exercise", ["cycling"], ["十公里挑战", "江边"]),
            Trace("exercise-walk", "休息日到太子湾散步", "没有安排强度训练，在公园走了四十分钟。恢复也是训练的一部分。", today.AddDays(-3).AddHours(16), "exercise", ["walking"], ["十公里挑战"], park),
            Plan("exercise-goal", "第一次尝试轻松跑完十公里", "不用计较配速，准备水和补给。如果当天身体不舒服就缩短距离。", today.AddDays(7).AddHours(7), "exercise", ["running"], ["十公里挑战"]),

            Trace("project-notes", "把个人网站要改的地方列成清单", "首页内容太少，作品页左右留白太宽。先把常用路径画出来，再调整桌面端布局。", today.AddDays(-9).AddHours(21), "work", ["writing"], ["网站改版"]),
            Trace("project-design", "完成桌面端布局草图", "试了侧栏导航和双栏详情，把表单分成主体与补充信息。手机端保留纵向阅读。", today.AddDays(-6).AddHours(20), "work", ["coding"], ["网站改版", "界面设计"]),
            Trace("project-review", "请朋友试用新布局并记录反馈", "长标题会换行，筛选操作容易找到。朋友建议把空状态写得再亲切一点，已加入待办。", today.AddDays(-3).AddHours(20), "work", ["meeting", "friends"], ["网站改版", "可用性"]),
            Plan("project-release", "检查不同分辨率并发布个人网站新版本", "依次检查宽屏、普通笔记本和手机，确认链接、表单与图片都正常，再发布。", today.AddDays(4).AddHours(20), "work", ["coding"], ["网站改版", "发布检查"]),

            Plan("activity-invite", "约朋友周末去野餐", "四个人确认了时间，各自准备一点吃的；采购和买咖啡可以分头完成。", today.AddDays(-8).AddHours(19), "social", ["friends", "camping"], ["周末野餐"], EventStatus.Completed),
            Trace("activity-market", "野餐前买水果、面包和一块大野餐垫", "选了容易分着吃的水果和面包，采购合计86元。下次记得自带餐盒。", today.AddDays(-5).AddHours(10), "shopping", ["daily-goods"], ["周末野餐"]),
            Trace("activity-coffee", "另一组朋友顺路买了四杯咖啡", "大家分别点了拿铁、美式和燕麦拿铁，取餐后直接在公园入口碰头。", today.AddDays(-5).AddHours(10).AddMinutes(15), "food", ["coffee", "friends"], ["周末野餐"]),
            Trace("activity-picnic", "在太子湾树荫下聊了一个下午", "采购组和咖啡组终于会合。铺好垫子，分享这一周的小事，散场前把垃圾全部带走。", today.AddDays(-5).AddHours(12), "social", ["friends", "camping"], ["周末野餐", "户外"], park),
            Trace("activity-photos", "把野餐合照发给大家", "每个人挑了最喜欢的一张，准备下次凑齐同样的角度再拍。", today.AddDays(-4).AddHours(19), "social", ["photography", "friends"], ["周末野餐"]),

            Trace("series-noodles", "老街面馆的一碗热汤面", "周末路过老街，找了家小面馆。汤底清爽，青菜很多，一碗28元。", previousMonth.AddDays(7).AddHours(12), "food", ["restaurant"], ["街角探店", "面食"]),
            Trace("series-bbq", "和朋友试了街角新开的烤肉店", "点了牛肉拼盘、蔬菜和石锅拌饭，两个人花了198元。喜欢靠窗的位置，聊天不吵。", today.AddDays(-2).AddHours(19), "food", ["restaurant", "dining", "friends"], ["街角探店", "烤肉"]),
            Trace("series-bakery", "下班路上带回两只刚出炉的可颂", "路过面包店闻到黄油香，买了原味和杏仁各一只，合计32元。明天早餐解决了。", today.AddDays(-1).AddHours(18), "food", ["restaurant", "baking"], ["街角探店", "面包"]),
            Plan("series-cafe", "去那家有落地窗的咖啡店坐坐", "带上读到一半的书，选个不赶时间的下午。先看看座位，不一定要待很久。", today.AddDays(3).AddHours(14), "food", ["coffee", "reading"], ["街角探店"]),

            Trace("daily-reading", "为这个月留一页阅读笔记", "把最近读到的一句话抄下来：生活里的细小片段，也值得认真记住。接下来每周留一点阅读时间。", month, "study", ["reading", "writing"], ["月初整理"]),
            Trace("old-family", "去年冬天和家人一起包饺子", "从和面开始慢慢忙，第一次学会捏出像样的褶子。全家围着餐桌吃完了一大锅。", previousYear.AddMonths(11).AddDays(20).AddHours(18), "home", ["cooking", "family"], ["家庭时光"]),
            Trace("old-running", "去年夏天第一次参加城市夜跑", "没想到夜里沿河跑步这么热闹。虽然只完成了短路线，还是很开心。", previousYear.AddMonths(7).AddDays(12).AddHours(20), "exercise", ["running"], ["第一次"]),
            Trace("daily-shopping", "补齐洗衣液和厨房收纳盒", "按清单买日用品，没有顺手带回不需要的东西。本次花费72元。", today.AddDays(-4).AddHours(18), "shopping", ["daily-goods", "home-goods"], ["生活补给"]),
            Plan("cancelled-camping", "周末露营先取消，等天气稳定再约", "原本准备在郊外过夜，大家商量后决定暂缓。装备清单保留，下次还可以继续用。", today.AddDays(5).AddHours(9), "travel", ["camping", "friends"], ["等待好天气"], EventStatus.Cancelled),
            Plan("pending-dentist", "预约一次常规口腔检查", "出门前带好证件，提前十分钟到。检查结束后再决定是否安排洗牙。", today.AddDays(10).AddHours(10), "health", ["medical"], ["照顾自己"]),
        ];
    }

    /// <summary>仅用已持久化 Event 的 ID 和修订建图，缺少依赖的样例会被跳过。</summary>
    public static IReadOnlyList<DemoStoryline> Storylines(IReadOnlyDictionary<string, Event> events)
    {
        List<DemoStoryline> result = [];
        Add("west-lake-weekend", "留给西湖的一个周末", "从订票出发，到分头闲逛、晚饭会合，再把照片整理成一个完整的周末。", "trip", StorylineStatus.Completed,
            ["周末", "杭州", "慢旅行"], ["出发前", "分头逛逛", "会合与回味"],
            [new("trip-ticket", 0, 0, 0), new("trip-train", 0, 1, 0), new("trip-lake", 1, 2, 0, true), new("trip-museum", 1, 2, 1), new("trip-dinner", 2, 3, 0), new("trip-review", 2, 4, 0)],
            [new(0, 1), new(1, 2, StorylineRelationType.Branch, "沿湖散步"), new(1, 3, StorylineRelationType.Branch, "看展路线"), new(2, 4, Label: "晚饭会合"), new(3, 4, Label: "晚饭会合"), new(4, 5)]);
        Add("picnic-afternoon", "四个人的树荫下午", "邀请、分头准备、草坪会合，最后用合照记住这个不用赶路的下午。", "activity", StorylineStatus.Completed,
            ["朋友", "野餐"], ["约好出发", "分头准备", "一起度过"],
            [new("activity-invite", 0, 0, 0), new("activity-market", 1, 1, 0), new("activity-coffee", 1, 1, 1), new("activity-picnic", 2, 2, 0, true), new("activity-photos", 2, 3, 0)],
            [new(0, 1, StorylineRelationType.Branch, "采购组"), new(0, 2, StorylineRelationType.Branch, "咖啡组"), new(1, 3), new(2, 3), new(3, 4)]);
        Add("personal-site", "让个人网站更好用一点", "把零散的想法变成草图、反馈和待发布清单，记录一次小改版的来龙去脉。", "project", StorylineStatus.Ongoing,
            ["个人项目", "设计"], ["发现问题", "设计与验证", "准备发布"],
            [new("project-notes", 0, 0, 0), new("project-design", 1, 1, 0), new("project-review", 1, 2, 0), new("project-release", 2, 3, 0, true)],
            [new(0, 1), new(1, 2), new(2, 3)]);
        Add("ten-kilometres", "从三公里慢慢跑到十公里", "记录起点、每一次小进步和休息日。把未来的目标接在已经完成的训练后面。", "challenge", StorylineStatus.Ongoing,
            ["运动习惯", "循序渐进"], ["从起点开始", "保持节奏", "下一次挑战"],
            [new("exercise-baseline", 0, 0, 0), new("exercise-run", 1, 1, 0, true), new("exercise-cycle", 1, 2, 0), new("exercise-walk", 1, 2, 1), new("exercise-goal", 2, 3, 0, true)],
            [new(0, 1), new(1, 2, StorylineRelationType.Parallel, "交叉训练"), new(1, 3, StorylineRelationType.Branch, "主动恢复"), new(2, 4), new(3, 4)]);
        Add("neighbourhood-tastes", "街角还有什么好吃的：慢慢补完的探店清单", "把随手记下的面馆、烤肉和面包连在一起，也给还没去的咖啡店留一个位置。", "series", StorylineStatus.Ongoing,
            ["探店", "日常小确幸"], ["已经尝过", "下次想去"],
            [new("series-noodles", 0, 0, 0), new("series-bbq", 0, 1, 0), new("series-bakery", 0, 2, 0), new("series-cafe", 1, 3, 0)],
            [new(0, 1, StorylineRelationType.Related, "另一种口味"), new(1, 2), new(2, 3)]);
        return result;

        void Add(string key, string title, string description, string category, StorylineStatus status,
            string[] tags, string[] stageTitles, NodeSpec[] nodes, EdgeSpec[] edges)
        {
            var eventKeys = nodes.Select(x => x.EventKey).ToArray();
            if (eventKeys.Any(eventKey => !events.TryGetValue(eventKey, out var value) || value.DeletedAt is not null)) return;
            var stages = stageTitles.Select((name, order) => new StorylineStageInput(StableKey($"{key}/stage/{order}"), name, order)).ToArray();
            var inputs = nodes.Select((spec, order) => new StorylineNodeInput(
                StableKey($"{key}/node/{spec.EventKey}"), "existing-event", events[spec.EventKey].Id,
                events[spec.EventKey].CurrentSourceRevision, null, stages[spec.Stage].Key, order,
                spec.Important ? StorylineNodeEmphasis.Important : StorylineNodeEmphasis.Normal)).ToArray();
            var relations = edges.Select((spec, order) => new StorylineEdgeInput(
                StableKey($"{key}/edge/{order}"), inputs[spec.From].Key, inputs[spec.To].Key, spec.Relation, spec.Label)).ToArray();
            var positions = nodes.Select((spec, order) => new StorylineWebNodeLayoutInput(
                inputs[order].Key, 80 + spec.Column * 340, 100 + spec.Row * 240, 260, 170)).ToArray();
            var stageLayouts = stages.Select((stage, order) =>
            {
                var members = nodes.Select((spec, index) => (spec, index)).Where(x => x.spec.Stage == order).Select(x => positions[x.index]).ToArray();
                var left = members.Min(x => x.X) - 24;
                var top = members.Min(x => x.Y) - 56;
                return new StorylineWebStageLayoutInput(stage.Key, left, top,
                    members.Max(x => x.X) + 284 - left, members.Max(x => x.Y) + 194 - top);
            }).ToArray();
            result.Add(new DemoStoryline(key, eventKeys, new SaveStorylineRequest(title, description, category, status,
                null, ["开发示例", .. tags], stages, inputs, relations, new StorylineWebLayoutInput("LR", 0, 0, 0.8m, positions, stageLayouts))));
        }
    }

    private static DemoEvent Trace(string key, string title, string content, DateTimeOffset at,
        string category, string[] behaviorTags, string[] customTags, EventLocationInput? place = null) =>
        new(key, new CreateEventRequest(EventKind.Trace, title, content, at, null, Timezone,
            Classification: Classification(category, behaviorTags, customTags), Locations: place is null ? [] : [place]));

    private static DemoEvent Plan(string key, string title, string content, DateTimeOffset at,
        string category, string[] behaviorTags, string[] customTags, EventStatus status = EventStatus.Planned) =>
        new(key, new CreateEventRequest(EventKind.Plan, title, content, null, at, Timezone,
            Classification: Classification(category, behaviorTags, customTags)), status);

    private static ClassificationInput Classification(string category, string[] behaviorTags, string[] customTags) =>
        new(category, new[] { new ManualTagInput(null, "开发示例") }
            .Concat(behaviorTags.Select(key => new ManualTagInput(key, null)))
            .Concat(customTags.Select(name => new ManualTagInput(null, name))).ToArray(), []);

    private static EventLocationInput Place(string name, string address, string district, decimal latitude, decimal longitude) =>
        new(name, address, "浙江省", "杭州市", district, null, null, null, latitude, longitude, null,
            "GCJ02", EventLocationSource.ManualText, null);

    private static Guid StableKey(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"development-demo/{Version}/{value}")).AsSpan(0, 16));

    private sealed record NodeSpec(string EventKey, int Stage, int Column, int Row, bool Important = false);
    private sealed record EdgeSpec(int From, int To, StorylineRelationType Relation = StorylineRelationType.Sequence, string? Label = null);
}
