# AGENTS.md

## 项目概述

TrpgVoiceDigest 是一个面向 TRPG（DND/COC 等）跑团的语音对话摘录工具。监听系统音频输出 → Whisper 转录 → LLM 结构化摘要 → Markdown 导出。

## 核心功能

- **音频录制**：通过 ffmpeg 录制系统音频输出，支持 Windows (dshow) 和 Linux (PulseAudio/PipeWire)
- **语音转录**：通过 Python `openai-whisper` 将 WAV 转为文本
- **LLM 摘要**：按句数/时间触发，将累积转录文本提交给 OpenAI 兼容 API，输出结构化摘要
- **状态管理**：维护 Digest 条目（带标签）、活跃/已完成任务、故事进展
- **导出**：Markdown 导出摘要、一致性参考、任务、故事，JSONL 编辑日志
- **GUI**：Avalonia 跨平台桌面界面，支持开屏配置 + 实时监控（状态灯、转录列表、Markdown 渲染摘要）

## 高层架构

```
TrpgVoiceDigest.slnx
├── src/
│   ├── TrpgVoiceDigest.Core/          # 纯模型 + 静态服务，无外部依赖
│   │   ├── Config/AppConfig.cs        # 8 个配置类 (Audio, Whisper, Llm, Trigger, Storage, Prompt, Processing, Ui)
│   │   ├── Models/
│   │   │   ├── DigestModels.cs        # TranscriptSegment, DigestEntry, DigestState, DigestTagGroup
│   │   │   ├── EditProtocol.cs        # EditProtocolParser (正则解析 LLM 输出协议)
│   │   │   └── SegmentJob.cs          # 录音段任务 (WavPath + CapturedAt)
│   │   └── Services/
│   │       ├── DigestMarkdownBuilder.cs  # DigestState → Markdown 转换
│   │       ├── PromptComposer.cs         # 构建 LLM 用户提示词
│   │       ├── SessionPathBuilder.cs     # SessionPaths 记录 + 路径计算
│   │       └── TriggerState.cs           # 句数/时间触发状态机
│   │
│   ├── TrpgVoiceDigest.Infrastructure/  # I/O + 外部进程 + 平台适配
│   │   ├── Audio/                        # ffmpeg 录音 + RMS 计算 + 设备发现
│   │   │   ├── AudioCaptureService.cs    # CaptureSegmentAsync + StartMeterStream
│   │   │   ├── AudioLevelCalculator.cs   # PCM16 RMS 计算
│   │   │   ├── IAudioInputDiscovery.cs  # 设备发现接口 (2 实现: Linux + Windows)
│   │   │   ├── LinuxAudioInputDiscovery.cs  # pactl 设备发现
│   │   │   ├── LinuxAudioSourceResolver.cs   # pactl 输出解析
│   │   │   ├── PlatformAudioInputDiscovery.cs # 工厂 (按 OS 选择实现)
│   │   │   └── WindowsAudioInputDiscovery.cs  # dshow 设备发现
│   │   ├── Config/
│   │   │   └── JsonConfigLoader.cs      # JSON 配置加载/保存
│   │   ├── Llm/
│   │   │   ├── LlmClient.cs            # OpenAI 兼容 API 客户端 (重试 + 指数退避)
│   │   │   ├── IEnvironmentKeyResolver.cs        # 环境变量解析接口
│   │   │   └── PlatformEnvironmentKeyResolver.cs  # Linux: 通过 shell login session 读取环境变量
│   │   ├── Services/
│   │   │   └── DigestPipeline.cs       # 管道编排器 (3 Worker: Capture / Transcribe / LLM)
│   │   ├── Storage/
│   │   │   └── SessionStorage.cs       # 文件 I/O: 转录/状态/导出/日志
│   │   └── Whisper/
│   │       └── WhisperProcessRunner.cs # Python Whisper 子进程调用
│   │
│   └── TrpgVoiceDigest.Gui/            # Avalonia MVVM 桌面界面
│       ├── Program.cs                  # static Main() 入口
│       ├── App.axaml.cs                # Avalonia Application
│       ├── ViewLocator.cs              # ViewModel → View 命名约定
│       ├── Models/                     # MeterDiagnostics, TranscriptItem
│       ├── Services/
│       │   └── SessionRunner.cs        # 组装管道 + 启动 Worker + 仪表 Worker
│       ├── ViewModels/
│       │   ├── MainWindowViewModel.cs  # 页面导航 + 会话生命周期
│       │   ├── ConfigViewModel.cs      # 配置页面 (50+ 绑定属性)
│       │   ├── MonitorViewModel.cs     # 监控页面 (转录 + 摘要 + 诊断)
│       │   └── ViewModelBase.cs        # 基类 (CommunityToolkit ObservableObject)
│       └── Views/
│           ├── MainWindow.axaml
│           ├── ConfigView.axaml
│           └── MonitorView.axaml
│
├── tests/TrpgVoiceDigest.Tests/        # xUnit 测试 (14 个测试文件)
├── python/
│   ├── whisper_transcribe.py           # Whisper 转录脚本 (子进程调用)
│   ├── requirements.txt                # openai-whisper
│   └── venv/                           # 项目 Python 虚拟环境
├── prompts/
│   ├── system_digest.md                # LLM 系统提示词
│   ├── edit_protocol.md               # LLM 输出协议规范
│   └── character_card_template.md      # 角色卡模板
├── config/
│   ├── app.config.example.json         # 配置模板
│   └── app.config.json                 # 运行时配置 (gitignored)
├── scripts/
│   └── init_python_venv.sh             # 一键初始化 Python 环境
└── README.md                           # 用户文档
```

## 数据流

```
[ffmpeg 录音] → Channel<SegmentJob> (有界, SingleWriter/SingleReader)
                     ↓
[Python Whisper 转录] → Channel<int> (无界, 信号: 新转录句数)
                     ↓
[LLM API 调用] → DigestState → 导出 (Markdown + JSON)
```

- 三阶段通过 `System.Threading.Channels` 解耦
- 转录使用单消费者串行消费（避免并发子进程冲突）
- LLM 通过 `TriggerState` 按句数 (`EverySentences`) 和时间 (`EverySeconds`) 双重阈值触发
- 幂等保护：通过 `submit_cursor.json` 的 SHA256 哈希去重，避免重复提交相同转录文本

## 关键设计原则

1. **管道解耦**：录音 / 转录 / LLM 三个 Worker 通过 Channel 通信，各自独立运行，转录不会阻塞录音，LLM 不会阻塞转录
2. **配置驱动**：所有行为参数从 `config/app.config.json` 读取，GUI 配置页可编辑并回写
3. **外置提示词**：LLM 系统提示词和输出协议均为独立 Markdown 文件 (`prompts/`)，便于维护和迭代
4. **单消费者串行转录**：避免多个 Python 子进程同时运行导致资源竞争
5. **平台适配**：音频设备发现通过 `IAudioInputDiscovery` 接口支持 Linux/Windows 双平台
6. **无 DI 容器**：依赖通过构造函数手动注入，带 null 默认值回退
7. **Core 零依赖**：Core 层无 IO/Http/平台代码，纯模型+静态服务，可完全单测

## 运行说明

### 环境要求
- .NET 10 SDK
- `ffmpeg` (命令行可用)
- `python3` + 项目 venv (含 `openai-whisper`)

### 初始化
```bash
./scripts/init_python_venv.sh          # 创建 Python venv + 安装依赖
cp config/app.config.example.json config/app.config.json  # 复制配置模板
```

### 启动
```bash
dotnet run --project src/TrpgVoiceDigest.Gui
```

### 测试
```bash
dotnet test
```

### 配置文件关键项
| 节 | 关键字段 |
|---|---------|
| `Audio` | `InputFormat` (pulse/dshow), `InputDevice`, `SegmentSeconds`, `VoiceRmsThreshold` |
| `Whisper` | `PythonExecutable`, `ScriptPath`, `Model`, `Language`, `InitialPrompt` |
| `Llm` | `BaseUrl`, `ApiKeyEnv` (环境变量名), `Model`, `Temperature`, `MaxTokens` |
| `Trigger` | `EverySentences`, `EverySeconds` |
| `Storage` | `CampaignRoot` |
| `Processing` | `SegmentQueueCapacity`, `DeleteAudioAfterTranscribe` |

## LLM 操作协议

LLM 输出必须遵循 `prompts/edit_protocol.md` 格式，按行输出操作：

| 操作 | 格式示例 |
|------|---------|
| add | `digest add "Key": {"content":"...","tags":["tag1"]}` |
| edit | `digest edit "Key": {"content":"...","tags":["tag1"]}` |
| remove | `digest remove "Key"` |
| complete | `task complete "Key"` |
| 无操作 | `EMPTY` |

支持三个区域：`digest` (带标签摘要), `task` (任务), `story` (故事进展)。一行一个操作，不能输出解释性文本。

## 输出结构

```
{CampaignRoot}/{CampaignName}/
  campaign_consistency_lexicon.md       # Campaign 级一致性词汇表
  character_cards/                      # 人物卡目录 (*.md)
  campaign_digest.md                    # 摘要导出 (不含 LLM_Consistency)
  campaign_consistency.md               # 一致性参考导出
  campaign_tasks.md                     # 任务导出 (活跃/已完成)
  campaign_story.md                     # 故事导出
  {SessionName}/
    audio_segments/                     # 临时音频段 (转录后自动删除)
    transcripts/                        # 转录文本
    digest_state.json                   # 摘要状态
    submit_cursor.json                  # 提交去重游标
    llm_edit_log.jsonl                  # LLM 编辑日志
```

## 已知限制与边界

- 只支持"听"（系统输出音频），不支持"听+说"混音
- 录音段固定时长，不存在语音活动检测驱动的动态分段
- 转录后自动删除 `audio_segments` 中的音频（可通过 `DeleteAudioAfterTranscribe` 配置）
- Windows 音频设备发现未充分测试
- 无远程/服务化部署，仅支持本地桌面 GUI
- 不保留原始完整录音，仅保留转录文本
