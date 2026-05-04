# TrpgVoiceDigest 首次运行指南

本指南帮助你完成 TrpgVoiceDigest 的后台服务环境配置。完成后即可启动桌面应用进行语音转录与摘要。

## 包内容

| 目录/文件 | 说明 |
|:---|:---|
| `TrpgVoiceDigest.Gui` / `.exe` | 桌面应用程序主程序 |
| `TrpgVoiceDigest.Cli` / `.exe` | 命令行版本（高级用户） |
| `tools/ffmpeg/` | 已捆绑的 ffmpeg 录音工具（含 ffprobe） |
| `python/` | Python 转录脚本与依赖清单 |
| `scripts/` | 环境初始化脚本 |
| `config/` | 应用配置模板 |
| `prompts/` | LLM 提示词模板 |
| `example/` | 示例人物卡 |

## 第一步：初始化 Python 转录环境

TrpgVoiceDigest 依赖 Python 虚拟环境运行 Whisper 语音转录模型。

### Linux

打开终端，进入解压目录，运行：

```bash
chmod +x scripts/setup.sh
./scripts/setup.sh
```

脚本会自动：
1. 检测系统 Python（需 Python 3.10+）
2. 创建 `python/venv` 虚拟环境
3. 安装 WhisperX 及所有依赖（约需 2-5 分钟）
4. 可选：预下载 Whisper 语音模型（约 1.5 GB，约需 5-10 分钟）
5. 检测 NVIDIA GPU，如有则自动安装 CUDA 版 PyTorch 加速转录

**如果你的系统尚未安装 Python 3**：

```bash
# Ubuntu / Debian
sudo apt install python3 python3-venv python3-pip

# Fedora
sudo dnf install python3

# Arch Linux
sudo pacman -S python
```

### Windows

右键点击 `scripts/setup.ps1`，选择「使用 PowerShell 运行」。如果弹出执行策略限制，先以管理员身份运行 PowerShell 执行：

```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

然后重新运行 `setup.ps1`。

脚本会自动：
1. 检测 Python（需 Python 3.10+，建议从 https://www.python.org/downloads/ 安装）
2. 创建 `python/venv` 虚拟环境
3. 安装 WhisperX 及所有依赖
4. 检测 NVIDIA GPU，如有则自动安装 CUDA 版 PyTorch

## 第二步：配置 LLM API Key

TrpgVoiceDigest 需要一个 OpenAI 兼容 API 来生成摘要。在终端中设置环境变量：

**Linux / macOS**（添加到 `~/.bashrc` 或 `~/.zshrc`）：

```bash
export OPENAI_API_KEY="sk-your-api-key"
```

**Windows**（PowerShell）：

```powershell
[Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "sk-your-api-key", "User")
```

之后重新打开终端或注销重新登录使环境变量生效。

支持的 API 提供商：
- [OpenAI](https://platform.openai.com/api-keys)（需付费）
- [DeepSeek](https://platform.deepseek.com/api_keys)（推荐，便宜且支持上下文缓存）
- 任何 OpenAI 兼容 API（如 vLLM、Ollama、LocalAI 等自部署方案）

## 第三步（可选）：开启说话人分离

说话人分离（Speaker Diarization）可自动区分不同玩家的发言。此功能需要 HuggingFace 账号和 token。

1. 打开 https://huggingface.co/pyannote/speaker-diarization-3.1，登录后点击「Agree and access repository」接受协议
2. 打开 https://huggingface.co/pyannote/segmentation-3.0，同样接受协议
3. 打开 https://huggingface.co/settings/tokens，创建一个 Access Token（选 `read` 权限即可）
4. 设置环境变量：

**Linux / macOS**：
```bash
export HF_TOKEN="hf_your_token_here"
```

**Windows**（PowerShell）：
```powershell
[Environment]::SetEnvironmentVariable("HF_TOKEN", "hf_your_token_here", "User")
```

5. 确保 `config/app.config.json` 中 `DiarizationEnabled` 为 `true`（默认已开启）

若不开启说话人分离，所有发言将标记为匿名说话人，不影响转录和摘要功能。

## 第四步：启动应用

环境准备完成后，双击 `TrpgVoiceDigest.Gui`（Linux）或 `TrpgVoiceDigest.Gui.exe`（Windows）即可启动桌面界面。

### 命令行版本

高级用户可使用命令行版本（适用于服务器或无桌面环境）：

```bash
./TrpgVoiceDigest.Cli -n "我的战役"
```

## 首次转录

首次启动转录时，Whisper 会自动从 HuggingFace 下载所需模型（如未在初始化脚本中预下载）。此过程仅执行一次，后续转录将直接使用缓存。

**建议**：在正式跑团前先启动应用，等待模型下载完成（约 5-15 分钟，取决于网络速度和硬件），避免游戏中途等待。

## 常见问题

### Q: 提示 "未找到 ffmpeg"

本包已在 `tools/ffmpeg/` 中捆绑了 ffmpeg，应用会自动使用。若仍提示未找到，请确认 `tools/ffmpeg/ffmpeg`（Linux）或 `tools/ffmpeg/ffmpeg.exe`（Windows）存在且可执行。

### Q: 转录速度很慢

- 确保已检测到 NVIDIA GPU 且安装了 CUDA 版 PyTorch。检查 `config/app.config.json` 中 `Device` 是否为 `"cuda"`。
- 若无 GPU，将 `Device` 改为 `"cpu"`，`ComputeType` 改为 `"int8"`。CPU 模式速度较慢但可用。
- 将 `SkipAlign` 设为 `true`（默认）跳过 Wav2Vec2 对齐可提升 2-3x 速度。
- 将 `Model` 从 `"turbo"` 改为 `"small"` 可使用更小的模型（精度降低但速度更快）。

### Q: 转录内容乱码或识别不准确

- 中文转录请确保 `Language` 为 `"zh"`。
- 设置 `InitialPrompt` 为 `"以下是普通话的句子。"` 可改善简体中文识别。
- 录音音量过低时调整 `VoiceRmsThreshold`（默认 0.015，调低可检测更轻的声音）。

### Q: 摘要不生成

- 确认 `OPENAI_API_KEY` 环境变量已设置且 API 可用（有余额/配额）。
- 检查 `Llm.BaseUrl` 是否正确指向 API 地址。
- 查看 `_system/runtime.log` 获取错误详情。

### Q: Python 虚拟环境损坏

删除 `python/venv` 目录，重新运行 `scripts/setup.sh`（Linux）或 `scripts/setup.ps1`（Windows）。

### Q: 如何卸载

删除解压目录即可。运行过程中产生的数据在 `Campaigns/` 目录中（默认在解压目录下，不会污染系统）。
