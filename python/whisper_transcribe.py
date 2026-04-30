#!/usr/bin/env python3
"""WhisperX 分段转录 + 说话者分离。输出 JSON 到 stdout。"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path


def _ensure_project_venv_on_path() -> None:
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
    import whisperx
except ImportError as e:
    msg = (
        "未找到 whisperx 模块（请安装）。\n"
        "在项目根目录执行: ./scripts/init_python_venv.sh\n"
        "并将 whisper.pythonExecutable 设为 python/venv/bin/python（或本机 venv 的 python）。\n"
        f"当前解释器: {sys.executable}\n"
        f"原始错误: {e}"
    )
    print(msg, file=sys.stderr)
    sys.exit(1)

import logging

_whisperx_logger = logging.getLogger("whisperx")
_whisperx_logger.propagate = False
for _h in list(_whisperx_logger.handlers):
    _whisperx_logger.removeHandler(_h)
_stderr_handler = logging.StreamHandler(sys.stderr)
_stderr_handler.setFormatter(logging.Formatter("%(asctime)s - %(name)s - %(levelname)s - %(message)s",
                                               datefmt="%Y-%m-%d %H:%M:%S"))
_stderr_handler.setLevel(logging.INFO)
_whisperx_logger.addHandler(_stderr_handler)


def _transcribe(audio_path: str, model_name: str, language: str, initial_prompt: str,
                device: str, compute_type: str, hf_token: str = "") -> dict:
    audio = whisperx.load_audio(audio_path)
    asr_options = {"initial_prompt": initial_prompt} if initial_prompt else None
    model = whisperx.load_model(model_name, device, compute_type=compute_type,
                                language=language, asr_options=asr_options)
    result = model.transcribe(audio, batch_size=16 if device == "cuda" else 1,
                              language=language)
    del model

    model_a, metadata = whisperx.load_align_model(language_code=result.get("language", language) or language,
                                                   device=device)
    result = whisperx.align(result["segments"], model_a, metadata, audio, device)
    del model_a

    if hf_token:
        try:
            diarize_model = whisperx.diarize.DiarizationPipeline(token=hf_token, device=device)
            diarize_segments = diarize_model(audio)
            result = whisperx.assign_word_speakers(diarize_segments, result)
        except Exception as e:
            print(f"说话者分离失败 ({e})，继续使用纯转录。", file=sys.stderr)

    clean_segments = []
    for seg in result.get("segments", []):
        text = (seg.get("text", "") or "").strip()
        if not text:
            continue
        entry = {
            "start": seg.get("start", 0.0),
            "end": seg.get("end", 0.0),
            "text": text,
        }
        speaker = seg.get("speaker")
        if speaker is not None:
            entry["speaker"] = speaker
        clean_segments.append(entry)
    return {"segments": clean_segments}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--audio", required=True)
    parser.add_argument("--model", default="medium")
    parser.add_argument("--language", default="zh")
    parser.add_argument("--initial-prompt", default="")
    parser.add_argument("--diarize", action="store_true", default=False)
    parser.add_argument("--hf-token", default="")
    parser.add_argument("--device", default="cpu")
    parser.add_argument("--compute-type", default="int8")
    args = parser.parse_args()

    initial_prompt = args.initial_prompt
    if not initial_prompt and args.language.lower().startswith("zh"):
        initial_prompt = "以下是普通话的句子。"

    hf_token = args.hf_token.strip()
    if not hf_token:
        hf_token = os.environ.get("HF_TOKEN", "").strip()

    if args.diarize and not hf_token:
        print("警告: 未提供 HuggingFace token，说话者分离已禁用。"
              "请设置环境变量 HF_TOKEN 或通过 --hf-token 传入。", file=sys.stderr)

    payload = _transcribe(
        audio_path=args.audio,
        model_name=args.model,
        language=args.language,
        initial_prompt=initial_prompt,
        device=args.device,
        compute_type=args.compute_type,
        hf_token=hf_token if args.diarize else "",
    )

    print(json.dumps(payload, ensure_ascii=False))


if __name__ == "__main__":
    main()
