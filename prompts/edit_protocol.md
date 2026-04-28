只允许以下行协议，一行一个操作：

1) 摘要区（digest）可用 add/edit/remove
digest add "Key": {"content":"具体内容","tags":["标签A","标签B"]}
digest edit "Key": {"content":"新内容","tags":["标签A"]}
digest remove "Key"

2) 任务区（task）可用 add/edit/remove/complete
task add "Key": {"content":"任务内容"}
task edit "Key": {"content":"更新后的任务内容"}
task remove "Key"
task complete "Key"

3) 故事区（story）可用 add/edit/remove
story add "Key": {"content":"故事推进内容"}
story edit "Key": {"content":"更新后的故事内容"}
story remove "Key"

4) 空返回
EMPTY

限制：
- 输出只能是以上协议行，不能包含解释文字。
- digest 区的 tags 必须是字符串数组，task/story 区不允许输出 tags。
- 若没有需要改动的内容，只返回 EMPTY。
- 必须严格区分区域：摘要信息写 digest；活跃任务写 task；故事进展写 story。
- `task complete "Key"` 仅用于把活跃任务转移到已完成任务区，不能用于 digest/story。
- 原文是语音转录文本，允许对同音字和明显错别字做合理纠正，但不得编造原文中不存在的剧情事实。
- digest 的 tags 需要严格考虑，只推荐使用“世界观”、“故事主线”、“人物主线（可以使用人物名称-个人线作为 tag ）”、“人物描述（可以使用人物名称作为tag）”。除此之外的 tag 名仅在必要情况创建。
- 推荐维护一个 LLM 一致性参考表格，建议使用 `LLM_Consistency` tag，尽可能详细记录人物名称、地点、组织、关键物品、事件线索、时间顺序、别名/称呼映射等潜在冲突信息，优先覆盖未来可能重复出现的人事物。
