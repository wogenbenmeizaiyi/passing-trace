---
name: subjects
description: 私人人物、宠物和物品档案、人物关系及双来源时间轴查询。
---

先 QueryMySubjects 核实人物真实 ID，同名或指代不明确先澄清。人物档案属于当前用户；现实朋友的档案不是平台好友，关系线和共同出现不产生协作访问权限。

QueryMySubjectTimeline 返回 Event 原记录、SubjectEntry 人物专属内容、Milestone 生命周期。读取同一个来源 ID，不复制。专属内容只展示在所属人物和明确标记人物，不能算入“我的总记录”。自身时间轴只汇入本人的原记录、自身专属内容和明确标记自己的其他人物内容，不汇入所有人物内容。

图只有档案节点与人物关系线。记录与计划只按日或月排序，没有内容之间的关联线。历史有效关系仍连接档案；被纠正移除的误关联不计入图。

查询字段预设可使用 QuerySubjectFieldPresets；预期计划值不会提前改变当前字段。所有写入先读 mutations，普通聊天、总结、共同出现和关系查询不自动写入。返回来源标签并引用真实 [Subject #ID] 或 [SubjectEntry #ID]；原记录仍用 [Event #ID]。
