#!/usr/bin/env python3
"""Whisper 分段转录：输出 JSON 到 stdout。依赖 openai-whisper（pip 包名），导入名为 whisper。"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def _ensure_project_venv_on_path() -> None:
    """若用系统 python 直接跑本脚本，尝试把同目录下 venv 的 site-packages 加入 sys.path。"""
    script_dir = Path(__file__).resolve().parent
    venv_root = script_dir / "venv"
    if not venv_root.is_dir():
        return
    for site in sorted(venv_root.glob("lib/python*/site-packages")):
        if site.is_dir():
            s = str(site)
            if s not in sys.path:
                sys.path.insert(0, s)
            return


_ensure_project_venv_on_path()

try:
    import whisper  # noqa: E402  — 须在 _ensure_project_venv_on_path 之后
except ImportError as e:
    msg = (
        "未找到 whisper 模块（请安装 openai-whisper）。\n"
        "在项目根目录执行: ./scripts/init_python_venv.sh\n"
        "并将 whisper.pythonExecutable 设为 python/venv/bin/python（或本机 venv 的 python）。\n"
        f"当前解释器: {sys.executable}\n"
        f"原始错误: {e}"
    )
    print(msg, file=sys.stderr)
    sys.exit(1)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--audio", required=True)
    parser.add_argument("--model", default="medium")
    parser.add_argument("--language", default="zh")
    parser.add_argument("--initial-prompt", default="")
    args = parser.parse_args()

    model = whisper.load_model(args.model)
    initial_prompt = args.initial_prompt
    if not initial_prompt and args.language.lower().startswith("zh"):
        # Whisper 官方建议用 prompt 引导简/繁体风格；默认倾向简体。
        initial_prompt = "以下是普通话的句子。"

    transcribe_kwargs: dict = {"language": args.language}
    if initial_prompt:
        transcribe_kwargs["initial_prompt"] = initial_prompt

    result = model.transcribe(args.audio, **transcribe_kwargs)
    payload = {
        "segments": [
            {
                "start": seg.get("start", 0.0),
                "end": seg.get("end", 0.0),
                "text": seg.get("text", "").strip(),
            }
            for seg in result.get("segments", [])
        ]
    }
    print(json.dumps(payload, ensure_ascii=False))


if __name__ == "__main__":
    main()
