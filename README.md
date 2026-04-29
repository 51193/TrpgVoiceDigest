# TrpgVoiceDigest

面向 TRPG（DND/COC 等）的语音对话摘录工具：
- 监听系统输出音频（支持 Windows + Linux）
- Whisper 转录（准确率优先，非严格实时）
- 按时间轮询触发 LLM 结构化摘录（Edit 协议）
- 支持独立任务系统（活跃/已完成）与故事进展记录
- 持久化 Campaign/Session 数据并导出 Markdown
- 提供 GUI（开屏配置 + 状态灯 + 转录列表）

## 当前限制

- 当前版本只支持“听”，不支持“听+说”混音。
- 会议内建议放置一个不说话机器人账号，用于稳定接收全量语音。

## 环境要求

- .NET 10 SDK
- `ffmpeg`（用于录制系统输出片段）
- `python3`（Linux）或 `python`（Windows）
- Python 依赖：`openai-whisper`
- OpenAI-Compatible 接口可用

## 安装 Python 依赖

推荐一键脚本（会创建 `python/venv`、安装依赖并检查 `ffmpeg`）：

```bash
./scripts/init_python_venv.sh
```

脚本完成后，默认配置会使用 `python/venv/bin/python`。

编译 GUI（`dotnet build` / `dotnet run --project src/TrpgVoiceDigest.Gui`）时，会把仓库根目录下的**整个** `python/` 目录复制到输出目录（例如 `src/TrpgVoiceDigest.Gui/bin/Debug/net10.0/python/`），**包括已创建的 `venv`**，便于发布包内自带解释器与依赖。若本机尚未创建 `venv`，输出里仅有脚本与 `requirements.txt`；发布前请先执行上述初始化脚本。首次完整复制含 PyTorch 的 venv 时编译可能较慢，属正常现象。

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
- `trigger.llmPollingSeconds`：LLM 摘要轮询间隔（秒）
- `processing.transcribePollingMs`：转录轮询间隔（毫秒）
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
程序会优先读取当前进程环境变量。

## 启动

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
- 运行时采用异步链路（录音/转录/摘要通过文件系统解耦），状态灯不会被转录或 LLM 阻塞
- 转录链路使用轮询扫描 + 单消费者串行消费（一个片段处理完再处理下一个），避免并发转录进程冲突

应用在开屏配置页选择/创建 `Campaign` 与 `Session`，并将当前设置写入 `config/app.config.json`。

## 输出结构

```text
Campaigns/
  DND_Campaign_A/
    campaign_consistency_lexicon.md
    character_cards/
      alice_ranger.md
    campaign_digest.md
    campaign_consistency.md
    campaign_tasks.md
    campaign_story.md
    Session_01/
      audio_segments/
      dialogue.log
      digest_state.json
      llm_edit_log.jsonl
      submit_cursor.json
```

- `campaign_digest.md`：摘要区导出（不含 `LLM_Consistency`），按 tag 分组，同一条目可在多个标签下重复出现。
- `campaign_consistency_lexicon.md`：Campaign 级一致性词汇表（每行一个条目）。
- `character_cards/*.md`：Campaign 级人物卡，作为长期上下文输入。
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

---

## LLM 操作协议

系统通过 `prompts/edit_protocol.md` 向 LLM 下发输出规范。LLM 响应必须严格遵循以下格式，一行一个操作：

### 区域 (Area)

| 区域 | 语义 | 数据结构 |
|:---|:---|:---|
| `digest` | 摘要条目 | K + (Content, Tags[]) |
| `task` | 活跃任务 | K + Text |
| `story` | 故事进展 | K + Text |

### 操作 (Action)

| 操作 | 适用区域 | 格式 | 效果 |
|:---|:---|:---|:---|
| `add` | digest, task, story | `<area> add "Key": {"content":"..."}` | 新建条目（task/story 仅需 `content`；digest 需 `content` + `tags`） |
| `edit` | digest, task, story | `<area> edit "Key": {"content":"..."}` | 覆盖已有条目 |
| `remove` | digest, task, story | `<area> remove "Key"` | 删除条目 |
| `complete` | task | `task complete "Key"` | 将活跃任务移至已完成任务列表 |
| — | — | `EMPTY` | 无需操作时返回此单行 |

### 协议示例

```
digest add "NPC_埃尔文": {"content":"红发的矮人铁匠","tags":["人物","铁匠铺"]}
digest edit "NPC_埃尔文": {"content":"红发的矮人铁匠，右手有灼伤疤痕","tags":["人物"]}
task add "任务_寻找钥匙": {"content":"在铁匠铺地下室寻找青铜钥匙"}
task complete "任务_寻找钥匙"
story add "章节_抵达港口": {"content":"队伍乘坐破浪号抵达风暴港"}
digest remove "NPC_废弃角色"
EMPTY
```

### 强制约束

- 只能输出协议行或 `EMPTY`，**禁止**任何解释、Markdown、JSON 包裹文本
- `digest` 的 `tags` 必须是字符串数组；`task`/`story` **不允许**输出 `tags`
- 用户语音转录中允许做最小必要同音字/错别字纠正，**不得编造原文中不存在的剧情事实**
- `digest` tags 推荐使用：`世界观`、`故事主线`、`人物描述`（`人物名称`）、`人物主线`（`人物名称-个人线`）
- 建议维护 `LLM_Consistency` tag 记录人物/地点/组织/物品/别名/时间线等一致性参考
