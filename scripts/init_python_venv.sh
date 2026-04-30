#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PY_DIR="$PROJECT_ROOT/python"
VENV_DIR="$PY_DIR/venv"

if ! command -v python3 >/dev/null 2>&1; then
  echo "未找到 python3，请先安装。"
  exit 1
fi

if [ ! -d "$VENV_DIR" ]; then
  echo "创建虚拟环境: $VENV_DIR"
  python3 -m venv "$VENV_DIR"
else
  echo "复用已有虚拟环境: $VENV_DIR"
fi

source "$VENV_DIR/bin/activate"
python -m pip install --upgrade pip

echo "安装 WhisperX 及依赖..."
pip install -r "$PY_DIR/requirements.txt"

if ! command -v ffmpeg >/dev/null 2>&1; then
  echo "警告: 未检测到 ffmpeg，可执行转录/录音可能失败。"
else
  ffmpeg -version >/dev/null 2>&1 || true
fi

echo
echo "Python 环境准备完成。"
echo "建议将 config/app.config.json 中 whisper.pythonExecutable 设置为:"
echo "$VENV_DIR/bin/python"
echo
echo "若需要使用说话者分离 (Speaker Diarization)，请:"
echo "  1. 前往 https://huggingface.co/pyannote/speaker-diarization-3.1 接受用户协议"
echo "  2. 前往 https://huggingface.co/pyannote/segmentation-3.0 接受用户协议"
echo "  3. 在 https://huggingface.co/settings/tokens 创建 Access Token"
echo "  4. 设置环境变量: export HF_TOKEN=<your_token>"
