# TrpgVoiceDigest

面向 TRPG（DND/COC 等）的语音对话摘录 CLI 工具：
- 监听系统输出音频（Linux 优先）
- Whisper 转录（准确率优先，非严格实时）
- 按句数/时间触发 LLM 结构化摘录（Edit 协议）
- 支持独立任务系统（活跃/已完成）与故事进展记录
- 持久化 Campaign/Session 数据并导出 Markdown
- 提供最小化 GUI（开屏配置 + 状态灯 + 转录列表）

## 当前限制

- 当前版本只支持“听”，不支持“听+说”混音。
- 会议内建议放置一个不说话机器人账号，用于稳定接收全量语音。

## 环境要求

- .NET 10 SDK
- `ffmpeg`（用于录制系统输出片段）
- `python3`
- Python 依赖：`openai-whisper`
- OpenAI-Compatible 接口可用

## 安装 Python 依赖

推荐一键脚本（会创建 `python/venv`、安装依赖并检查 `ffmpeg`）：

```bash
./scripts/init_python_venv.sh
```

脚本完成后，默认配置会使用 `python/venv/bin/python`。

手动方式：

```bash
cd python
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
```

## 配置

运行配置在 `config/app.config.json`：
- `llm.baseUrl`：LLM 接口地址
- `llm.apiKeyEnv`：API Key 环境变量名（例如 `OPENAI_API_KEY`）
- `audio.inputDevice`：Linux PulseAudio/PipeWire 输入设备。建议使用 `*.monitor` 源，尽量保证“你听到什么，它就听到什么”。
- `audio.voiceRmsThreshold`：声音阈值（RMS），超过后 GUI 状态灯变绿
- `whisper.initialPrompt`：Whisper 初始提示词。对于中文，建议填写简体句式（例如 `以下是普通话的句子。`）以提高简体输出概率（Whisper 官方文档建议通过 prompt 控制简/繁体风格，且属于“倾向控制”不是强制保证）
- `whisper.model`：默认建议 `turbo`（本项目默认值已改为 `turbo`）
- `trigger.everySentences` / `trigger.everySeconds`：摘录触发策略
- `processing.segmentQueueCapacity`：录音段队列容量
- `processing.meterIntervalMs` / `processing.meterWindowMs`：状态灯高频采样参数
- `processing.deleteAudioAfterTranscribe`：转录成功后是否删除 `audio_segments` 里的音频段

输入源选择优先级：
1. 若 `audio.inputDevice` 显式配置且非 `default`，严格使用该值
2. 若为 `default`，优先使用 `Default Sink + .monitor`
3. 若未找到，再回退到首个可用 monitor 源

仓库仅提交示例配置：`config/app.config.example.json`。  
请复制生成本地配置并按需修改：

```bash
cp config/app.config.example.json config/app.config.json
```

示例（设置密钥）：

```bash
export OPENAI_API_KEY="your_key_here"
```

注意：`llm.apiKeyEnv` 必须填写“环境变量名”，不是完整 API Key 字符串本身。  
程序会优先读取当前进程环境变量；若当前进程没有，会继续尝试从 shell 启动环境读取（`bash -ic` / `bash -lc`）。

## 启动

```bash
dotnet run --project src/TrpgVoiceDigest.Cli -- DND_Campaign_A Session_01
```

### GUI 启动（Avalonia）

```bash
dotnet run --project src/TrpgVoiceDigest.Gui
```

GUI 使用流程：
- 开屏配置页：选择或新建 `Campaign` / `Session`，并配置录音设备和阈值（可刷新设备列表并查看推荐 monitor 源）
- 监控页：查看声音状态灯、逐条转录文本，以及当前摘录（Markdown 渲染）
- 监控页诊断区：可查看 `EffectiveInputDevice`、`LastRms`、阈值、采样成功/失败计数与最近采样时间
- 开屏页点击“保存配置”或“开始监听”都会把当前页面设置写回 `config/app.config.json`，下次启动自动恢复上次填写值
- 开屏页当前可编辑的所有配置项（Audio/Whisper/Llm/Trigger/Storage/Prompts/Ui）都会写回本地配置文件
- 运行时采用异步链路（录音/转录/摘要解耦），状态灯不会被转录或 LLM 阻塞
- 转录链路使用单消费者队列串行消费（一个片段处理完再处理下一个），避免并发转录进程冲突

可选第三个参数指定配置路径：

```bash
dotnet run --project src/TrpgVoiceDigest.Cli -- DND_Campaign_A Session_01 config/app.config.json
```

## 输出结构

```text
Campaigns/
  DND_Campaign_A/
    campaign_digest.md
    campaign_consistency.md
    campaign_tasks.md
    campaign_story.md
    Session_01/
      audio_segments/
      digest_state.json
      llm_edit_log.jsonl
      submit_cursor.json
      transcripts/
        20260427_221500.md
```

- `campaign_digest.md`：摘要区导出（不含 `LLM_Consistency`），按 tag 分组，同一条目可在多个标签下重复出现。
- `campaign_consistency.md`：一致性参考导出，仅包含 `LLM_Consistency` 标签条目。
- `campaign_tasks.md`：任务区导出，分为活跃任务与已完成任务。
- `campaign_story.md`：故事区导出，按 KVP 列出故事推进。
- `llm_edit_log.jsonl`：每轮 LLM 返回后的操作日志，记录 digest/task/story 的 add/remove/edit/complete/empty 结果与原始响应，便于 debug。

`digest_state.json` 现包含四块状态：
- `digestEntries`（原摘要条目，带 tags）
- `activeTasks`（活跃任务 KVP）
- `completedTasks`（已完成任务 KVP）
- `storyEntries`（故事进展 KVP）

## 提示词维护

提示词全部外置为 Markdown，位于：
- `prompts/system_digest.md`
- `prompts/edit_protocol.md`

协议重点：
- 摘要区：`digest add/edit/remove`
- 任务区：`task add/edit/remove/complete`（`complete` 会把活跃任务转移到已完成任务）
- 故事区：`story add/edit/remove`
- ASR 文本允许在提示词约束下做最小必要同音/错别字纠正，不得改写剧情事实
- 建议使用 `LLM_Consistency` 标签维护一致性参考（人物/地点/组织/物品/别名等）
