#!/usr/bin/env bash
# TrpgVoiceDigest 首次运行环境初始化（Linux）
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PY_DIR="$PROJECT_ROOT/python"
VENV_DIR="$PY_DIR/venv"

echo "=============================================="
echo " TrpgVoiceDigest 环境初始化"
echo "=============================================="
echo ""

# ── 1. 检查 Python ──────────────────────────────────────
if ! command -v python3 >/dev/null 2>&1; then
    echo "❌ 未找到 python3。请先安装 Python 3.10+。"
    echo ""
    echo "   Ubuntu/Debian: sudo apt install python3 python3-venv python3-pip"
    echo "   Fedora:        sudo dnf install python3"
    echo "   Arch:          sudo pacman -S python"
    exit 1
fi
PYTHON_VERSION=$(python3 --version 2>&1)
echo "✓ 已找到: $PYTHON_VERSION"
echo ""

# ── 2. 检测 GPU ─────────────────────────────────────────
HAS_CUDA=false
if command -v nvidia-smi >/dev/null 2>&1; then
    echo "✓ 检测到 NVIDIA GPU（将安装 CUDA 版 PyTorch）"
    HAS_CUDA=true
else
    echo "ℹ  未检测到 NVIDIA GPU，使用 CPU 模式"
fi
echo ""

# ── 3. 创建虚拟环境 ──────────────────────────────────────
if [ ! -d "$VENV_DIR" ]; then
    echo ">>> 创建 Python 虚拟环境 ..."
    python3 -m venv "$VENV_DIR"
else
    echo ">>> 复用已有虚拟环境: $VENV_DIR"
fi
source "$VENV_DIR/bin/activate"
python -m pip install --upgrade pip --quiet
echo ""

# ── 4. 安装依赖 ─────────────────────────────────────────
echo ">>> 安装 WhisperX 及依赖（首次约需 2-5 分钟） ..."
if $HAS_CUDA; then
    pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu124
fi
pip install -r "$PY_DIR/requirements.txt"
echo ""

# ── 5. 确认捆绑的 ffmpeg ─────────────────────────────────
BUNDLED_FFMPEG="$PROJECT_ROOT/tools/ffmpeg/ffmpeg"
if [ -f "$BUNDLED_FFMPEG" ] && [ -x "$BUNDLED_FFMPEG" ]; then
    echo "✓ 已检测到捆绑的 ffmpeg: $BUNDLED_FFMPEG"
else
    if command -v ffmpeg >/dev/null 2>&1; then
        echo "✓ 使用系统的 ffmpeg: $(which ffmpeg)"
    else
        echo "⚠ 未找到 ffmpeg。录音功能将不可用。"
        echo "  安装方法: sudo apt install ffmpeg"
    fi
fi
echo ""

# ── 6. 预下载 Whisper 模型（可选但推荐）───────────────────
echo "=============================================="
echo " 预下载语音模型（可选）"
echo "=============================================="
echo ""
echo "模型约 1.5 GB，下载后首次转录无需等待。"
echo "你可以在任何时候通过重新运行此脚本并使用 --download-models 来下载。"
echo ""

if [ "${1:-}" = "--download-models" ]; then
    echo ">>> 正在下载 Whisper 模型（约需数分钟） ..."
    python -c "
import whisperx
print('下载 Whisper turbo 模型 ...')
model = whisperx.load_model(
    'turbo',
    device='cuda' if $HAS_CUDA else 'cpu',
    compute_type='float16' if $HAS_CUDA else 'int8'
)
print('模型下载完成')
" || echo "⚠ 模型下载失败，首次转录时将自动重试"
    echo ""
fi

# ── 7. 切换为 CPU 模式的提示 ─────────────────────────────
if ! $HAS_CUDA; then
    echo "=============================================="
    echo " CPU 模式配置提示"
    echo "=============================================="
    echo ""
    echo "你的 config/app.config.json 中 Device 默认为 cuda。"
    echo "由于未检测到 GPU，请将以下配置项改为："
    echo ""
    echo "  \"Device\": \"cpu\","
    echo "  \"ComputeType\": \"int8\","
    echo ""
fi

# ── 8. 后续配置提示 ──────────────────────────────────────
echo "=============================================="
echo " 后续手动配置"
echo "=============================================="
echo ""
echo "1. API Key 设置："
echo "   将以下行添加到 ~/.bashrc 或 ~/.zshrc："
echo ""
echo "   export OPENAI_API_KEY=\"sk-your-key-here\""
echo ""
echo "2. 说话人分离（可选）："
echo "   a. 访问 https://huggingface.co/pyannote/speaker-diarization-3.1"
echo "      登录后点击「Agree and access repository」"
echo "   b. 访问 https://huggingface.co/pyannote/segmentation-3.0"
echo "      同样接受协议"
echo "   c. 访问 https://huggingface.co/settings/tokens"
echo "      创建 Access Token（read 权限）"
echo "   d. 添加到 ~/.bashrc："
echo ""
echo "   export HF_TOKEN=\"hf_your_token_here\""
echo ""
echo "3. 启动应用："
echo ""
echo "   ./TrpgVoiceDigest.Gui"
echo ""
echo "   或命令行版本："
echo ""
echo "   ./TrpgVoiceDigest.Cli -n \"MyCampaign\""
echo ""
echo "=============================================="
echo " 环境初始化完成"
echo "=============================================="
