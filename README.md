# TrpgVoiceDigest

面向 TRPG（DND/COC 等）跑团的语音对话摘录工具：自动监听系统音频，通过 Whisper 转写为文本，再由 LLM 产出结构化摘要与一致性参考。
## 功能

- **系统音频录制**：通过 ffmpeg 录制系统输出音频（Windows dshow / Linux PulseAudio+PipeWire）
- **Whisper 语音转录**：Python openai-whisper 高准确率转写（中文优化，可控简繁体）
- **LLM 结构化摘要**：按时间轮询，检测对话增量后提交 OpenAI 兼容 API，输出最小必要操作协议
- **Campaign 级一致性管理**：LLM 自行维护人物/地点/组织/物品命名一致性词汇表
- **Markdown 导出**：摘要（按 tag 分组）、一致性参考、JSONL 编辑日志
- **GUI 桌面界面**：跨平台 Avalonia 应用，开屏配置 + 实时状态灯 + 转录列表 + Markdown 摘要渲染

## 限制

- 仅支持"听"系统音频（不支持"听+说"混音）
- 建议会议中放置一个不说话机器人账号，用于稳定接收全量语音

## 环境要求

| 依赖 | 用途 |
|------|------|
| .NET 10 SDK / Runtime | 构建与运行核心程序 |
| ffmpeg | 录制系统音频片段 |
| python3（Linux）或 python（Windows） | 运行 Whisper 转录 |
| openai-whisper | Whisper 模型推理 |
| OpenAI 兼容 API | LLM 摘要生成 |

## 快速开始

### 1. 克隆仓库

```bash
git clone https://github.com/51193/TrpgVoiceDigest.git
cd TrpgVoiceDigest
```

### 2. 安装 Python 依赖

```bash
./scripts/init_python_venv.sh
```

此脚本自动创建 `python/venv` 虚拟环境并安装 `openai-whisper`。

### 3. 创建本地配置

```bash
cp config/app.config.example.json config/app.config.json
```

编辑 `config/app.config.json`，至少修改以下项：

```json
{
  "Llm": {
    "BaseUrl": "https://api.openai.com/v1/chat/completions",
    "ApiKeyEnv": "OPENAI_API_KEY",
    "Model": "gpt-4o-mini"
  }
}
```

### 4. 设置 API 密钥

```bash
export OPENAI_API_KEY="sk-your-key-here"
```

`ApiKeyEnv` 填写环境变量名（不是密钥字符串本身）。程序启动时从进程环境变量读取。

### 5. 启动

```bash
dotnet run --project src/TrpgVoiceDigest.Gui
```

或从 [Releases](https://github.com/51193/TrpgVoiceDigest/releases) 下载预编译包直接运行。

## 配置

完整配置项见 `config/app.config.example.json`。关键字段：

| 节 | 字段 | 说明 |
|---|------|------|
| `Audio` | `InputFormat` | `pulse`（Linux）/ `dshow`（Windows） |
| `Audio` | `InputDevice` | 录音设备名，`default` 自动选 monitor 源 |
| `Audio` | `VoiceRmsThreshold` | 语音检测 RMS 阈值，默认 0.015 |
| `Whisper` | `Model` | Whisper 模型名，推荐 `turbo` |
| `Whisper` | `Language` | 语言代码，中文用 `zh` |
| `Whisper` | `InitialPrompt` | 初始提示词，简体中文建议 `以下是普通话的句子。` |
| `Llm` | `BaseUrl` | LLM API 完整地址（含 `/v1/chat/completions`） |
| `Llm` | `ApiKeyEnv` | 存放 API Key 的环境变量名 |
| `Llm` | `Model` | 模型名，如 `gpt-4o-mini`、`deepseek-v4-flash` |
| `Refinement` | `PollingSeconds` | LLM 摘要轮询间隔（秒），默认 60 |
| `Storage` | `CampaignRoot` | 数据存储根目录，默认 `Campaigns` |
| `Processing` | `DeleteAudioAfterTranscribe` | 转录后是否删除音频段，默认 `true` |
| `AudioSegmentation` | `HardMaxSpeechSec` | 硬上限最大说话时长（秒），默认 120 |
| `AudioSegmentation` | `SilenceCutMs` | 尾部静音切除（毫秒），默认 400 |

### 输入源选择

1. 若 `InputDevice` 显式配置且非 `default`，直接使用
2. 若为 `default`，优先使用系统默认输出 Monitor 源
3. 若未找到，回退到首个可用 monitor 源

GUI 可刷新设备列表并自动推荐 Monitor 源。

## 使用流程

1. **开屏配置页** — 选择/新建 Campaign，配置录音设备、LLM 参数等
2. **点击「开始监听」** — 自动保存配置并进入监控页
3. **监控页** — 实时查看：
   - 声音状态灯（绿色=检测到语音）
   - 逐条转录文本
   - LLM 摘要（按标签分组 Markdown 渲染）
   - 一致性参考
   - 音频设备诊断信息

应用程序支持 Campaign 级一致性词汇表，可在 GUI 中编辑和维护。

## 输出结构

```
{CampaignRoot}/{CampaignName}/
  dialogue.log                      # 完整对话文本
  refinement.md                     # 摘要导出（按 tag 分组）
  consistency.md                    # 一致性词汇表 (Markdown)
  _system/
    audio_segments/                 # 临时音频段
    campaign_speakers.json          # 声音到角色映射表
    speaker_embeddings/             # 说话人声纹向量
    refinement_state.json           # 精炼状态
    refinement_cursor.json          # 提交去重游标
    refinement_edit_log.jsonl       # LLM 编辑日志
    consistency.json                # 一致性词汇表 (JSON)
    processed_sequence.txt          # 已处理音频段序号
    runtime.log                     # 运行时日志
```

系统生成的所有内部文件统一存放于 `_system/` 子目录，人类可读的产出文件直接放在 Campaign 根目录。

## 构建

```bash
# 构建
dotnet build

# 运行测试
dotnet test

# 发布单文件可执行包
dotnet publish src/TrpgVoiceDigest.Gui -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/TrpgVoiceDigest.Gui -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

发布包自带 .NET 运行时，用户无需额外安装。Python venv 会在构建时自动复制到输出目录，但 ffmpeg 仍需系统安装。

## 提示词

LLM 行为由外置 Markdown 提示词控制，位于 `prompts/` 目录：

- `system_refinement.md` — 角色定义与精炼质量要求
- `system_consistency.md` — 一致性词汇表维护规则
- `refinement_requirements.md` — 每轮精炼处理强制步骤
- `refinement_protocol.md` — 输出协议规范（refine 操作格式）
- `refinement_user_static.md` — 用户模板静态前缀（处理步骤 + 协议 + 说话人映射）
- `refinement_user_dynamic.md` — 用户模板动态部分（本轮对话 + 精炼状态）
- `consistency_user_template.md` — 一致性检查用户模板

静态/动态拆分可提高 DeepSeek Context Caching 缓存命中率。可根据需求直接编辑这些文件，无需修改代码。

## 许可

[GNU General Public License v3.0](LICENSE)
