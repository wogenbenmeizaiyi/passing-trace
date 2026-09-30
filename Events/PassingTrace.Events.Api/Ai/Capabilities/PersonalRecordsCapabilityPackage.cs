using Microsoft.Extensions.AI;
using PassingTrace.Events.Api.Ai.Tools.Queries;

namespace PassingTrace.Events.Api.Ai.Capabilities;

public sealed class PersonalRecordsCapabilityPackage(PersonalRecordTools tools) : IAiCapabilityPackage
{
    public string Key => "personal-records";
    public bool IsAvailable => true;
    public IReadOnlyList<string> Capabilities { get; } =
        ["records", "statistics", "memories", "saved-places", "storylines"];

    public IReadOnlyList<AITool> CreateTools() =>
    [
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.SearchMyRecordsAsync), "SearchMyRecords",
            "搜索当前用户自己的记录，返回按 RRF 排序的记录及其已确认地点；适合‘我最近吃过/去过’等语义查询。"),
        new StatisticsAIFunction(AiFunctionToolFactory.Create(tools,
            nameof(PersonalRecordTools.AggregateMyRecordsAsync), "AggregateMyRecords")),
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.GetMyRecordEvidenceAsync), "GetMyRecordEvidence",
            "获取已检索记录的原文和语义证据。"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.SearchMyMemoriesAsync), "SearchMyMemories",
            "搜索当前用户有证据的长期记忆。"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.SearchMyPlacesAsync), "SearchMyPlaces",
            "搜索当前用户已确认的历史地点。"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.GetMyPlaceEvidenceAsync), "GetMyPlaceEvidence",
            "读取已检索历史地点的记录证据。"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.GetNavigationTargetAsync), "GetNavigationTarget",
            "为 SearchMyRecords 或 SearchMyPlaces 已检索且有可信坐标的历史地点生成结构化导航动作。"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.SearchMyStorylinesAsync), "SearchMyStorylines",
            "搜索当前用户自己的故事线。"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.GetMyStorylineEvidenceAsync), "GetMyStorylineEvidence",
            "读取已检索故事线的阶段、关系和固定记录修订证据。"),
    ];
}
