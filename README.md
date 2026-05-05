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

| 依赖 | 用途 | 便携包已包含 |
|------|------|:---:|
| .NET 10 Runtime | 构建与运行核心程序 | ✓ 自包含 |
| ffmpeg | 录制系统音频片段 | ✓ 已捆绑 |
| python3（Linux）或 python（Windows） | 运行 Whisper 转录 | 需系统安装 |
| WhisperX (openai-whisper) | Whisper 模型推理 | 由初始化脚本安装 |
| OpenAI 兼容 API | LLM 摘要生成 | 用户自行提供 |

## 快速开始

### 分发包（推荐，无需编译）

1. 从 [Releases](https://github.com/51193/TrpgVoiceDigest/releases) 下载对应平台的包
2. 解压到任意目录
3. 运行初始化脚本：
   - **Linux**：终端中执行 `./scripts/setup.sh`
   - **Windows**：右键 `scripts/setup.ps1` → 使用 PowerShell 运行
4. 设置 API 密钥（见下方）
5. 双击 `TrpgVoiceDigest.Gui`（Linux）或 `TrpgVoiceDigest.Gui.exe`（Windows）

详细说明请阅读包内 `SETUP.md`。

### 源码构建（开发者）

```bash
git clone https://github.com/51193/TrpgVoiceDigest.git
cd TrpgVoiceDigest

# 安装 Python 依赖（创建虚拟环境 + 安装 WhisperX）
./scripts/init_python_venv.sh

# 创建本地配置
cp config/app.config.example.json config/app.config.json

# 设置 API 密钥
export OPENAI_API_KEY="sk-your-key-here"

# 启动
dotnet run --project src/TrpgVoiceDigest.Gui
```

源码运行时需要系统安装 ffmpeg（或放置到 `tools/ffmpeg/ffmpeg` 路径下）。

### 设置 API 密钥

`ApiKeyEnv` 填写环境变量名（不是密钥字符串本身）。程序启动时从进程环境变量读取，Linux 下会自动尝试从 login shell（bash/zsh）读取。

**Linux / macOS**：
```bash
export OPENAI_API_KEY="sk-your-key-here"
# 建议写入 ~/.bashrc 或 ~/.zshrc 持久化
```

**Windows**（PowerShell）：
```powershell
[Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "sk-your-key-here", "User")
```

## 配置

完整配置项见 `config/app.config.example.json`。关键字段：

| 节 | 字段 | 说明 |
|---|------|------|
| `Audio` | `RecorderExecutable` | ffmpeg 路径，默认指向捆绑版本 `tools/ffmpeg/ffmpeg` |
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

## Web 看板

可将本地跑团的精炼结果、故事进展、任务状态实时同步到网页，其他人通过浏览器直接查看。

### 架构

```
本地机器                          服务器（VPS/云主机）          浏览器
┌──────────────────┐     POST      ┌──────────────┐  SignalR  ┌──────────┐
│ Upload 上传服务    │ ──AES加密──→ │ Server 后端   │ ←─实时──→ │ 前端页面  │
│ (每2秒扫描文件变更) │              │ (内存缓存+推送)│           │ (对话+标签)│
└──────────────────┘              └──────────────┘           └──────────┘
```

- **上传服务**：轻量进程，持续扫描本地 Campaign 目录，SHA256 检测变更后 AES-256-GCM 加密上传
- **后端**：ASP.NET Core 无状态服务，接收文件 → 内存缓存 → SignalR 推送到所有连接的浏览器
- **后端完全可抛弃**：所有数据源头仍是本地文件，后端仅做中转镜像，挂掉重启后自动恢复
- **前端**：左侧对话式精炼展示，右侧标签页（故事进展 / 任务）

### 服务器部署

**前置条件**：服务器需安装 .NET 10 Runtime（约 70 MB）。Server 包不含运行时，体积很小。

```bash
# Ubuntu / Debian
wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 10.0 --runtime aspnetcore
export PATH="$HOME/.dotnet:$PATH"
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc
```

1. 从 [Releases](https://github.com/51193/TrpgVoiceDigest/releases) 下载 `TrpgVoiceDigest-Server-{平台}.tar.gz`
2. 解压到服务器任意目录
3. 生成加密密钥：

```bash
./scripts/generate_key.sh
```

4. 编辑 `appsettings.json`，填入密钥：

```json
{
  "SharedSecret": "生成的密钥",
  "Urls": "http://0.0.0.0:5000"
}
```

5. 启动：

```bash
chmod +x TrpgVoiceDigest.Server
./TrpgVoiceDigest.Server
```

6. 对外暴露端口（如使用 nginx 反代或直接开放 5000 端口），可用 `systemd` 或 `screen` 保持后台运行。

### 本地同步配置

1. 从 Releases 下载 `TrpgVoiceDigest-Upload-{平台}.tar.gz`（自包含，无需安装 .NET）
2. 解压到本地任意目录
3. 编辑 `appsettings.json`：

```json
{
  "CampaignDirectory": "/path/to/Campaigns/MyGame",
  "ServerUrl": "http://你的服务器IP:5000",
  "SharedSecret": "与服务器相同的密钥",
  "ScanIntervalSeconds": 2
}
```

4. 启动上传服务：

```bash
chmod +x TrpgVoiceDigest.Upload
./TrpgVoiceDigest.Upload
```

上传服务会每 2 秒扫描指定 Campaign 目录中的 `refinement.md`、`story_progress.md`、`tasks.md` 等文件，发现变更后加密上传。浏览器打开 `http://服务器IP:5000` 即可实时查看。

### 前端说明

- **左侧**：以聊天对话形式展示精炼结果，不同角色分左右两侧，头像为角色名首字
- **右侧标签页**：故事进展（序号列表）、任务（进行中 / 已完成双栏）

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

# 发布单文件可执行包（分发包会自动捆绑 ffmpeg）
dotnet publish src/TrpgVoiceDigest.Gui -c Release -r linux-x64 --self-contained true
dotnet publish src/TrpgVoiceDigest.Gui -c Release -r win-x64 --self-contained true
```

发布包自带 .NET 运行时与 ffmpeg，用户仅需安装 Python 3 并运行初始化脚本。分发包由 CI 自动构建，见 `.github/workflows/release.yml`。

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
