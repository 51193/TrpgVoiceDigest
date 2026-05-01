#!/usr/bin/env python3
"""流式转录：从 stdin 接收 PCM16 16kHz mono，多策略分段 → 实时转录+分离说话人。

分段策略（按优先级）：
  1. Silero VAD 静音检测 → 主要切分方式
  2. End-of-Utterance 检测模型 → 辅助切分
  3. 硬时长上限 → 兜底保护

Campaign 级声纹嵌入持久化：加载/保存到 speaker_embeddings 目录。
"""
from __future__ import annotations

import argparse
import json
import os
import struct
import sys
import time
import traceback
from abc import ABC, abstractmethod
from pathlib import Path

_ORIGINAL_STDOUT = sys.stdout
sys.stdout = sys.stderr

import warnings
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
import torch

logging.getLogger("whisperx").setLevel(logging.WARNING)
logging.getLogger("pyannote").setLevel(logging.WARNING)
logging.getLogger("transformers").setLevel(logging.WARNING)
logging.getLogger("lightning").setLevel(logging.WARNING)
logging.getLogger("faster_whisper").setLevel(logging.WARNING)

SAMPLE_RATE = 16000
VAD_FRAME_SAMPLES = 512
BYTES_PER_SAMPLE = 2


def _resolve_device(device: str, compute_type: str) -> tuple[str, str]:
    if device != "cuda":
        return device, compute_type
    try:
        if torch.cuda.is_available():
            return device, compute_type
        print("警告: CUDA 不可用，回退至 CPU。", file=sys.stderr)
        return "cpu", "int8"
    except ImportError:
        print("警告: 未安装 PyTorch，无法检测 CUDA 可用性，回退至 CPU。", file=sys.stderr)
        return "cpu", "int8"


def _clean_segments(result: dict) -> list[dict]:
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
        if speaker is not None:
            entry["speaker"] = speaker
        clean.append(entry)
    return clean


def _cosine_similarity(a: np.ndarray, b: np.ndarray) -> float:
    na = np.linalg.norm(a)
    nb = np.linalg.norm(b)
    if na < 1e-10 or nb < 1e-10:
        return 0.0
    return float(np.dot(a, b) / (na * nb))


DIARIZATION_MODEL_NAME = "pyannote/speaker-diarization-3.1"
SPEAKER_MATCH_THRESHOLD = 0.48
MAX_EMBEDDINGS_PER_SPEAKER = 3
EMBEDDING_FILENAME_PATTERN = "{speaker_id}.npy"


# ──────────────────── 分段策略接口 ────────────────────

class SegmentationStrategy(ABC):
    """音频分段策略基类。所有策略在每次收到音频帧后被调用，返回是否应在此处切分。"""

    @abstractmethod
    def process_frame(self, frame: torch.Tensor) -> bool:
        """处理一个 VAD 帧 (512 samples)，返回 True 表示应在当前点切分语音段。"""
        ...

    def reset(self) -> None:
        """重置策略内部状态。"""


class VADSilenceStrategy(SegmentationStrategy):
    """基于 Silero VAD 的静音检测：连续静音超过阈值时触发切分。"""

    def __init__(self, silence_cut_ms: int = 800):
        vad_model, vad_utils = torch.hub.load(
            repo_or_dir='snakers4/silero-vad', model='silero_vad',
            force_reload=False, trust_repo=True
        )
        (_, _, _, VADIterator, _) = vad_utils
        self._iterator = VADIterator(
            vad_model, sampling_rate=SAMPLE_RATE,
            min_silence_duration_ms=silence_cut_ms
        )
        self._in_speech = False
        self._should_cut = False

    def process_frame(self, frame: torch.Tensor) -> bool:
        event = self._iterator(frame, return_seconds=False)
        if event is not None:
            if "end" in event and self._in_speech:
                self._in_speech = False
                self._should_cut = True
                return True
            elif "start" in event:
                self._in_speech = True
                self._should_cut = False
        return False

    @property
    def in_speech(self) -> bool:
        return self._in_speech

    def reset(self) -> None:
        self._iterator.reset_states()
        self._in_speech = False
        self._should_cut = False


class EndOfUtteranceStrategy(SegmentationStrategy):
    """基于能量下降趋势的句尾检测（轻量级，无需额外模型加载）。

    检测原理：跟踪最近帧的 RMS 能量，当能量持续低于阈值比例时判定为句尾。
    sensitivity 越高越容易切分（值域 0.0~1.0）。
    """

    def __init__(self, sensitivity: float = 0.5):
        self._sensitivity = max(0.1, min(1.0, sensitivity))
        self._energy_buffer: list[float] = []
        self._window_size = 10  # 约 320ms 的观察窗口
        self._cut_threshold_ratio = 0.15 + (1.0 - self._sensitivity) * 0.5
        self._min_active_frames = 15  # 至少 0.48s 后才允许 EOU 切分
        self._frame_count = 0

    def process_frame(self, frame: torch.Tensor) -> bool:
        energy = float(torch.mean(torch.abs(frame)))
        self._energy_buffer.append(energy)
        if len(self._energy_buffer) > self._window_size:
            self._energy_buffer.pop(0)
        self._frame_count += 1

        if self._frame_count < self._min_active_frames:
            return False
        if len(self._energy_buffer) < self._window_size:
            return False

        avg_energy = sum(self._energy_buffer) / len(self._energy_buffer)
        if avg_energy < 0.001:
            return False

        low_count = sum(1 for e in self._energy_buffer if e < avg_energy * self._cut_threshold_ratio)
        return low_count >= self._window_size - 2

    def reset(self) -> None:
        self._energy_buffer.clear()
        self._frame_count = 0


# ──────────────────── 主转录器 ────────────────────

class StreamingTranscriber:
    def __init__(
        self,
        model_name: str,
        language: str,
        initial_prompt: str,
        device: str,
        compute_type: str,
        hf_token: str,
        min_speech_sec: float = 0.4,
        max_speech_sec: float = 120.0,
        silence_cut_ms: int = 800,
        eou_enabled: bool = False,
        eou_sensitivity: float = 0.5,
        speaker_embeddings_dir: str = "",
    ):
        self.language = language
        self.initial_prompt = initial_prompt
        self.hf_token = hf_token.strip() if hf_token else ""
        self.device, self.compute_type = _resolve_device(device, compute_type)
        self.batch_size = 16 if self.device == "cuda" else 1

        self._min_speech_frames = max(1, int(min_speech_sec * SAMPLE_RATE / VAD_FRAME_SAMPLES))
        self._max_speech_frames = int(max_speech_sec * SAMPLE_RATE / VAD_FRAME_SAMPLES)

        self._embeddings_dir = speaker_embeddings_dir
        self._seq_counter = 0

        # ── 构建分段策略链 ──
        self._strategies: list[SegmentationStrategy] = []
        self._vad_strategy = VADSilenceStrategy(silence_cut_ms=silence_cut_ms)
        self._strategies.append(self._vad_strategy)
        print(f"[stream] 分段策略 1: Silero VAD (静音={silence_cut_ms}ms)", file=sys.stderr)

        self._eou_strategy: EndOfUtteranceStrategy | None = None
        if eou_enabled:
            self._eou_strategy = EndOfUtteranceStrategy(sensitivity=eou_sensitivity)
            self._strategies.append(self._eou_strategy)
            print(f"[stream] 分段策略 2: EOU 句尾检测 (灵敏度={eou_sensitivity})", file=sys.stderr)

        print(f"[stream] 分段策略 3 (兜底): 硬时长上限={max_speech_sec}s", file=sys.stderr)

        # ── 加载 ASR ──
        print(f"[stream] 加载 ASR 模型 {model_name} (device={self.device}, compute={self.compute_type}) ...",
              file=sys.stderr)
        asr_options = {"initial_prompt": initial_prompt} if initial_prompt else None
        self.asr_model = whisperx.load_model(model_name, self.device, compute_type=self.compute_type,
                                             language=language, asr_options=asr_options)
        print(f"[stream] ASR 模型加载完成", file=sys.stderr)
        sys.stderr.flush()

        self._diarize_model = None
        self._known_speakers: dict[str, list[np.ndarray]] = {}
        self._next_speaker_id = 0

        # ── 加载 Campaign 级声纹 ──
        if self._embeddings_dir:
            self._load_persisted_embeddings()

    def _gc(self):
        if self.device == "cuda":
            torch.cuda.empty_cache()

    def _load_persisted_embeddings(self):
        emb_dir = Path(self._embeddings_dir)
        if not emb_dir.is_dir():
            return
        loaded = 0
        for npy_file in sorted(emb_dir.glob("*.npy")):
            try:
                emb = np.load(str(npy_file))
                speaker_id = npy_file.stem
                if speaker_id not in self._known_speakers:
                    self._known_speakers[speaker_id] = [emb]
                else:
                    refs = self._known_speakers[speaker_id]
                    refs.append(emb)
                    if len(refs) > MAX_EMBEDDINGS_PER_SPEAKER:
                        refs.pop(0)
                loaded += 1
                self._next_speaker_id = max(self._next_speaker_id, int(speaker_id.split("_")[-1]) + 1)
            except Exception as e:
                print(f"[stream] 加载嵌入失败 {npy_file}: {e}", file=sys.stderr)
        if loaded > 0:
            print(f"[stream] 从 Campaign 目录加载了 {loaded} 个声纹嵌入 (已知说话人: {len(self._known_speakers)})",
                  file=sys.stderr)

    def _persist_embedding(self, speaker_id: str, embedding: np.ndarray):
        if not self._embeddings_dir:
            return
        emb_dir = Path(self._embeddings_dir)
        emb_dir.mkdir(parents=True, exist_ok=True)
        npy_path = emb_dir / EMBEDDING_FILENAME_PATTERN.format(speaker_id=speaker_id)
        try:
            np.save(str(npy_path), embedding)
            print(f"[stream] 声纹已保存: {npy_path}", file=sys.stderr)
        except Exception as e:
            print(f"[stream] 保存声纹失败 {npy_path}: {e}", file=sys.stderr)

    def _ensure_diarize_model(self):
        if self._diarize_model is not None:
            return
        print(f"[stream] 加载说话者分离模型: {DIARIZATION_MODEL_NAME} ...", file=sys.stderr)
        self._diarize_model = whisperx.diarize.DiarizationPipeline(
            model_name=DIARIZATION_MODEL_NAME, token=self.hf_token, device=self.device)
        print(f"[stream] 说话者分离模型加载完成", file=sys.stderr)
        sys.stderr.flush()

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
            print(f"[stream] 匹配说话人: {best_speaker} (最佳={best_score:.3f}, 参考数={len(refs)}, 所有={scores_detail})",
                  file=sys.stderr)
            return best_speaker

        new_id = f"speaker_{self._next_speaker_id}"
        self._next_speaker_id += 1
        self._known_speakers[new_id] = [embedding]
        self._persist_embedding(new_id, embedding)
        print(f"[stream] 新说话人: {new_id} (最佳匹配={best_score:.3f}, 嵌入norm={emb_norm:.4f}, 已有={list(self._known_speakers.keys())})",
              file=sys.stderr)
        return new_id

    def _transcribe_audio(self, pcm_bytes: bytes) -> dict:
        pcm = np.frombuffer(pcm_bytes, dtype=np.int16).astype(np.float32) / 32768.0
        duration = len(pcm) / SAMPLE_RATE
        all_segments: list[dict] = []

        if duration < 2.0 or not self.hf_token:
            result = self.asr_model.transcribe(pcm, batch_size=self.batch_size, language=self.language)
            detected_lang = result.get("language", self.language) or self.language
            align_model, align_metadata = whisperx.load_align_model(
                language_code=detected_lang, device=self.device)
            result = whisperx.align(result["segments"], align_model, align_metadata, pcm, self.device)
            del align_model, align_metadata
            self._gc()
            return {"segments": _clean_segments(result)}

        try:
            self._ensure_diarize_model()
            diarize_result = self._diarize_model(
                pcm,
                min_speakers=1, max_speakers=8,
                return_embeddings=True,
            )
            if isinstance(diarize_result, tuple) and len(diarize_result) >= 2:
                diarize_df = diarize_result[0]
                embeddings = diarize_result[1] or {}
            else:
                diarize_df = diarize_result
                embeddings = {}

            label_map: dict[str, str] = {}
            for label, emb in embeddings.items():
                emb_arr = np.array(emb, dtype=np.float64)
                if float(np.linalg.norm(emb_arr)) < 0.01 or np.isnan(float(np.linalg.norm(emb_arr))):
                    continue
                matched = self._match_speaker(emb_arr)
                if matched is not None:
                    label_map[label] = matched

            speaker_groups: dict[str, list[tuple[float, float]]] = {}
            for _, row in diarize_df.iterrows():
                spk_label = label_map.get(row["speaker"], row["speaker"])
                speaker_groups.setdefault(spk_label, []).append(
                    (float(row["start"]), float(row["end"])))

            num_sub = sum(len(v) for v in speaker_groups.values())
            print(f"[stream] 声纹拆分: {len(speaker_groups)} 说话人, {num_sub} 子段 (总{duration:.1f}s)",
                  file=sys.stderr)

            for spk_id, ranges in speaker_groups.items():
                for seg_start, seg_end in ranges:
                    sub_start = max(0, int(seg_start * SAMPLE_RATE))
                    sub_end = min(len(pcm), int(seg_end * SAMPLE_RATE))
                    sub_pcm = pcm[sub_start:sub_end]
                    sub_dur = len(sub_pcm) / SAMPLE_RATE
                    if sub_dur < 0.3:
                        continue

                    seg_result = self.asr_model.transcribe(
                        sub_pcm, batch_size=self.batch_size, language=self.language)
                    detected_lang = seg_result.get("language", self.language) or self.language
                    align_model, align_metadata = whisperx.load_align_model(
                        language_code=detected_lang, device=self.device)
                    seg_result = whisperx.align(
                        seg_result["segments"], align_model, align_metadata, sub_pcm, self.device)
                    del align_model, align_metadata
                    self._gc()

                    for seg in _clean_segments(seg_result):
                        seg["speaker"] = spk_id
                        seg["start"] = seg_start + seg["start"]
                        seg["end"] = seg_start + seg["end"]
                        all_segments.append(seg)

        except Exception as e:
            print(f"说话者分离/拆分失败: {e}", file=sys.stderr)
            traceback.print_exc(file=sys.stderr)

        return {"segments": all_segments}

    def _flush_speech(self, speech_buffer: list[bytes], speech_frame_count: int):
        if speech_frame_count < self._min_speech_frames:
            return
        audio_data = b"".join(speech_buffer)
        duration = speech_frame_count * VAD_FRAME_SAMPLES / SAMPLE_RATE
        print(f"[stream] 语音段 #{self._seq_counter}: {speech_frame_count}帧 ({duration:.1f}s) → 转录中",
              file=sys.stderr)
        sys.stderr.flush()

        try:
            result = self._transcribe_audio(audio_data)
            self._seq_counter += 1
            self._write_json({"ok": True, "seq": self._seq_counter - 1, **result})
        except Exception as e:
            self._write_json({"ok": False, "seq": self._seq_counter, "error": str(e)})
            traceback.print_exc(file=sys.stderr)

    def _write_json(self, obj: dict):
        print(json.dumps(obj, ensure_ascii=False), file=_ORIGINAL_STDOUT, flush=True)

    def _any_strategy_triggers_cut(self, frame: torch.Tensor) -> bool:
        for strategy in self._strategies:
            if strategy.process_frame(frame):
                print(f"[stream] 分段触发: {type(strategy).__name__}", file=sys.stderr)
                return True
        return False

    def _reset_all_strategies(self):
        for s in self._strategies:
            s.reset()

    def run(self):
        strategies_desc = " + ".join(type(s).__name__ for s in self._strategies)
        print(f"[stream] 就绪: 最短语音={self._min_speech_frames}frames, 最长={self._max_speech_frames}frames, 策略=({strategies_desc})",
              file=sys.stderr)
        sys.stderr.flush()

        stdin = sys.stdin.buffer
        frame_bytes = VAD_FRAME_SAMPLES * BYTES_PER_SAMPLE

        speech_buffer: list[bytes] = []
        speech_frame_count = 0
        in_speech = False

        while True:
            chunk = stdin.read(frame_bytes)
            if not chunk or len(chunk) < frame_bytes:
                break

            pcm = np.frombuffer(chunk, dtype=np.int16).astype(np.float32) / 32768.0
            tensor = torch.from_numpy(pcm)

            # ── 所有策略共同决策 ──
            should_cut = self._any_strategy_triggers_cut(tensor)

            # 使用 VAD 策略的状态判断语音开始
            if self._vad_strategy.in_speech and not in_speech:
                in_speech = True
                speech_buffer = list(speech_buffer[-3:]) if speech_buffer else []

            if should_cut and in_speech:
                speech_buffer.append(chunk)
                self._flush_speech(speech_buffer, speech_frame_count + 1)
                speech_buffer.clear()
                speech_frame_count = 0
                in_speech = False
                self._reset_all_strategies()
                continue

            if in_speech:
                speech_buffer.append(chunk)
                speech_frame_count += 1

                # 硬时长兜底
                if speech_frame_count >= self._max_speech_frames:
                    print(f"[stream] 硬时长上限触发: {speech_frame_count}帧", file=sys.stderr)
                    self._flush_speech(speech_buffer, speech_frame_count)
                    self._reset_all_strategies()
                    speech_buffer.clear()
                    speech_frame_count = 0
                    in_speech = False

        if in_speech and speech_frame_count >= self._min_speech_frames:
            self._flush_speech(speech_buffer, speech_frame_count)

        print("[stream] 输入流结束", file=sys.stderr)


def main() -> None:
    parser = argparse.ArgumentParser(
        description="流式转录：stdin PCM16 → 多策略分段 → WhisperX → stdout JSON")
    parser.add_argument("--model", default="medium")
    parser.add_argument("--language", default="zh")
    parser.add_argument("--initial-prompt", default="")
    parser.add_argument("--diarize", action="store_true", default=False)
    parser.add_argument("--hf-token", default="")
    parser.add_argument("--device", default="cpu")
    parser.add_argument("--compute-type", default="int8")
    parser.add_argument("--min-speech-sec", type=float, default=0.4,
                        help="最短有效语音时长 (秒)")
    parser.add_argument("--max-speech-sec", type=float, default=120.0,
                        help="最长允许的语音段 (秒) — 硬上限兜底")
    parser.add_argument("--silence-cut-ms", type=int, default=800,
                        help="VAD 静音切分灵敏度 (毫秒，默认800)")
    parser.add_argument("--eou", action="store_true", default=False,
                        help="启用句尾检测 (End-of-Utterance) 策略")
    parser.add_argument("--eou-sensitivity", type=float, default=0.5,
                        help="EOU 灵敏度 (0.0~1.0，越高越容易切分)")
    parser.add_argument("--speaker-embeddings-dir", default="",
                        help="Campaign 级声纹嵌入持久化目录")
    args = parser.parse_args()

    initial_prompt = args.initial_prompt
    if not initial_prompt and args.language.lower().startswith("zh"):
        initial_prompt = "以下是普通话的句子。"

    hf_token = args.hf_token.strip()
    if not hf_token:
        hf_token = os.environ.get("HF_TOKEN", "").strip()

    if args.diarize and not hf_token:
        print("警告: 未提供 HuggingFace token，说话者分离已禁用。", file=sys.stderr)

    transcriber = StreamingTranscriber(
        model_name=args.model,
        language=args.language,
        initial_prompt=initial_prompt,
        device=args.device,
        compute_type=args.compute_type,
        hf_token=hf_token if args.diarize else "",
        min_speech_sec=args.min_speech_sec,
        max_speech_sec=args.max_speech_sec,
        silence_cut_ms=args.silence_cut_ms,
        eou_enabled=args.eou,
        eou_sensitivity=args.eou_sensitivity,
        speaker_embeddings_dir=args.speaker_embeddings_dir,
    )
    transcriber.run()


if __name__ == "__main__":
    main()
