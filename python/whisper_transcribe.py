#!/usr/bin/env python3
"""WhisperX 分段转录 + 说话者分离（跨段说话人追踪）。支持单文件和持久服务器两种模式。"""
from __future__ import annotations

import argparse
import json
import os
import sys
import traceback
import warnings
from pathlib import Path

warnings.filterwarnings("ignore")

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
import numpy as np

_whisperx_logger = logging.getLogger("whisperx")
_whisperx_logger.propagate = False
for _h in list(_whisperx_logger.handlers):
    _whisperx_logger.removeHandler(_h)
_stderr_handler = logging.StreamHandler(sys.stderr)
_stderr_handler.setFormatter(logging.Formatter("%(asctime)s - %(name)s - %(levelname)s - %(message)s",
                                               datefmt="%Y-%m-%d %H:%M:%S"))
_stderr_handler.setLevel(logging.WARNING)
_whisperx_logger.addHandler(_stderr_handler)

logging.getLogger("pyannote").setLevel(logging.WARNING)
logging.getLogger("transformers").setLevel(logging.WARNING)
logging.getLogger("lightning").setLevel(logging.WARNING)


def _resolve_device(device: str, compute_type: str) -> tuple[str, str]:
    if device != "cuda":
        return device, compute_type
    try:
        import torch
        if torch.cuda.is_available():
            return device, compute_type
        print(f"警告: CUDA 不可用 (PyTorch {torch.__version__}, CUDA 未就绪或驱动缺失)，回退至 CPU。",
              file=sys.stderr)
        return "cpu", "int8"
    except ImportError:
        print("警告: 未安装 PyTorch，无法检测 CUDA 可用性，回退至 CPU。", file=sys.stderr)
        return "cpu", "int8"


def _clean_segments(result: dict, default_speaker: str = "") -> list[dict]:
    clean = []
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
        if speaker is not None and len(str(speaker).strip()) > 0:
            entry["speaker"] = str(speaker).strip()
        elif default_speaker:
            entry["speaker"] = default_speaker
        clean.append(entry)
    return clean


def _cosine_similarity(a, b) -> float:
    na = np.linalg.norm(a)
    nb = np.linalg.norm(b)
    if na < 1e-10 or nb < 1e-10:
        return 0.0
    return float(np.dot(a, b) / (na * nb))


DIARIZATION_MODEL_NAME = "pyannote/speaker-diarization-3.1"
SPEAKER_MATCH_THRESHOLD = 0.50
MAX_EMBEDDINGS_PER_SPEAKER = 3


class _TranscriptionServer:
    def __init__(self, model_name: str, language: str, initial_prompt: str,
                 device: str, compute_type: str, hf_token: str):
        self.language = language
        self.initial_prompt = initial_prompt
        self.hf_token = hf_token.strip() if hf_token else ""
        self.device, self.compute_type = _resolve_device(device, compute_type)
        self.batch_size = 16 if self.device == "cuda" else 1

        print(f"[server] 加载模型 {model_name} (device={self.device}, compute={self.compute_type}) ...",
              file=sys.stderr)
        asr_options = {"initial_prompt": initial_prompt} if initial_prompt else None
        self.asr_model = whisperx.load_model(model_name, self.device, compute_type=self.compute_type,
                                             language=language, asr_options=asr_options)
        print(f"[server] ASR 模型加载完成", file=sys.stderr)

        self._diarize_model = None
        self._known_speakers: dict[str, list[np.ndarray]] = {}
        self._next_speaker_id = 0

    def _gc(self):
        if self.device == "cuda":
            import torch
            torch.cuda.empty_cache()

    def _ensure_diarize_model(self):
        if self._diarize_model is not None:
            return
        print(f"[server] 加载说话者分离模型: {DIARIZATION_MODEL_NAME} ...", file=sys.stderr)
        self._diarize_model = whisperx.diarize.DiarizationPipeline(
            model_name=DIARIZATION_MODEL_NAME, token=self.hf_token, device=self.device)
        print(f"[server] 说话者分离模型加载完成", file=sys.stderr)

    def _match_speaker(self, embedding: np.ndarray) -> str | None:
        emb_norm = float(np.linalg.norm(embedding))
        if emb_norm < 0.01 or np.isnan(emb_norm):
            return None

        best_speaker = None
        best_score = -1.0
        scores_detail: list[str] = []

        for speaker_id, ref_embeddings in self._known_speakers.items():
            for ref_emb in ref_embeddings:
                score = _cosine_similarity(embedding, ref_emb)
                if score > best_score:
                    best_score = score
                    best_speaker = speaker_id
            if ref_embeddings:
                avg = sum(_cosine_similarity(embedding, r) for r in ref_embeddings) / len(ref_embeddings)
                scores_detail.append(f"{speaker_id}={avg:.3f}")

        if best_score >= SPEAKER_MATCH_THRESHOLD and best_speaker is not None:
            refs = self._known_speakers[best_speaker]
            refs.append(embedding)
            if len(refs) > MAX_EMBEDDINGS_PER_SPEAKER:
                refs.pop(0)
            print(f"[server] 匹配说话人: {best_speaker} (最佳={best_score:.3f}, 参考数={len(refs)})",
                  file=sys.stderr)
            return best_speaker

        new_id = f"speaker_{self._next_speaker_id}"
        self._next_speaker_id += 1
        self._known_speakers[new_id] = [embedding]
        print(f"[server] 新说话人: {new_id} (最佳匹配={best_score:.3f}, 嵌入norm={emb_norm:.4f})",
              file=sys.stderr)
        return new_id

    def transcribe(self, audio_path: str) -> dict:
        audio = whisperx.load_audio(audio_path)

        result = self.asr_model.transcribe(audio, batch_size=self.batch_size, language=self.language)

        detected_lang = result.get("language", self.language) or self.language

        align_model, align_metadata = whisperx.load_align_model(
            language_code=detected_lang, device=self.device)
        result = whisperx.align(result["segments"], align_model, align_metadata, audio, self.device)
        del align_model, align_metadata
        self._gc()

        if self.hf_token:
            duration = len(audio) / 16000
            if duration < 2.0:
                print(f"[server] 语音段过短 ({duration:.1f}s < 2s)，跳过嵌入匹配", file=sys.stderr)
            else:
                try:
                    self._ensure_diarize_model()
                    diarize_result = self._diarize_model(
                        audio,
                        min_speakers=1,
                        max_speakers=4,
                        return_embeddings=True,
                    )
                    if isinstance(diarize_result, tuple) and len(diarize_result) >= 2:
                        diarize_segments = diarize_result[0]
                        embeddings = diarize_result[1] or {}
                    else:
                        diarize_segments = diarize_result
                        embeddings = {}

                    label_map: dict[str, str] = {}
                    for label, emb in embeddings.items():
                        matched = self._match_speaker(np.array(emb, dtype=np.float64))
                        if matched is not None:
                            label_map[label] = matched

                    if label_map:
                        diarize_segments["speaker"] = diarize_segments["speaker"].map(label_map)
                        if diarize_segments["speaker"].isna().any():
                            diarize_segments["speaker"] = diarize_segments["speaker"].fillna("speaker_0")
                        result = whisperx.assign_word_speakers(diarize_segments, result)
                    else:
                        print(f"[server] 嵌入无效，跳过说话人标签 (时长为{duration:.1f}s)", file=sys.stderr)
                except Exception as e:
                    print(f"说话者分离失败: {e}", file=sys.stderr)
                    traceback.print_exc(file=sys.stderr)

        return {"segments": _clean_segments(result, default_speaker="speaker_0")}
        
    def run_loop(self):
        print("[server] 就绪，等待请求 ...", file=sys.stderr)
        for line in sys.stdin:
            line = line.strip()
            if not line:
                continue
            try:
                req = json.loads(line)
            except json.JSONDecodeError as e:
                print(json.dumps({"ok": False, "error": f"无效 JSON: {e}"}, ensure_ascii=False))
                sys.stdout.flush()
                continue

            if req.get("action") == "exit":
                print("[server] 收到退出指令", file=sys.stderr)
                break

            audio_path = req.get("audio", "")
            if not audio_path or not os.path.isfile(audio_path):
                print(json.dumps({"ok": False, "error": f"音频文件不存在: {audio_path}"}, ensure_ascii=False))
                sys.stdout.flush()
                continue

            try:
                result = self.transcribe(audio_path)
                print(json.dumps({"ok": True, **result}, ensure_ascii=False))
            except Exception as e:
                print(json.dumps({"ok": False, "error": str(e)}, ensure_ascii=False))
                traceback.print_exc(file=sys.stderr)
            sys.stdout.flush()


def _transcribe_one(audio_path: str, model_name: str, language: str, initial_prompt: str,
                    device: str, compute_type: str, hf_token: str = "") -> dict:
    device, compute_type = _resolve_device(device, compute_type)

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

    if hf_token and hf_token.strip():
        try:
            diarize_model = whisperx.diarize.DiarizationPipeline(
                model_name=DIARIZATION_MODEL_NAME, token=hf_token.strip(), device=device)
            diarize_segments = diarize_model(audio)
            result = whisperx.assign_word_speakers(diarize_segments, result)
        except Exception as e:
            print(f"说话者分离失败: {e}", file=sys.stderr)
            print("详细错误:", file=sys.stderr)
            traceback.print_exc(file=sys.stderr)
            print("继续使用纯转录。", file=sys.stderr)

    return {"segments": _clean_segments(result, default_speaker="speaker_0")}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--audio", default="")
    parser.add_argument("--model", default="medium")
    parser.add_argument("--language", default="zh")
    parser.add_argument("--initial-prompt", default="")
    parser.add_argument("--diarize", action="store_true", default=False)
    parser.add_argument("--hf-token", default="")
    parser.add_argument("--device", default="cpu")
    parser.add_argument("--compute-type", default="int8")
    parser.add_argument("--server", action="store_true", default=False,
                        help="持久服务器模式 — 从 stdin 读取请求，模型常驻内存，跨段追踪说话人")
    args = parser.parse_args()

    initial_prompt = args.initial_prompt
    if not initial_prompt and args.language.lower().startswith("zh"):
        initial_prompt = "以下是普通话的句子。"

    hf_token = args.hf_token.strip()
    if not hf_token:
        hf_token = os.environ.get("HF_TOKEN", "").strip()

    if args.diarize and not hf_token:
        print("警告: 未提供 HuggingFace token，说话者分离已禁用。\n"
              "请通过以下任一方式提供 token:\n"
              "  1. 设置环境变量: export HF_TOKEN=<your_token>\n"
              "  2. 创建配置文件并将 HuggingFaceTokenEnv 指向包含 token 的环境变量\n"
              "获取 token: https://huggingface.co/settings/tokens (需先接受 pyannote 模型使用协议)", file=sys.stderr)

    if args.server:
        server = _TranscriptionServer(
            model_name=args.model,
            language=args.language,
            initial_prompt=initial_prompt,
            device=args.device,
            compute_type=args.compute_type,
            hf_token=hf_token if args.diarize else "",
        )
        server.run_loop()
        return

    if not args.audio:
        print("错误: 单文件模式需要 --audio 参数", file=sys.stderr)
        sys.exit(1)

    payload = _transcribe_one(
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
