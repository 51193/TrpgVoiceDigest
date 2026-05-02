#!/usr/bin/env python3
"""流式转录：从 stdin 接收 PCM16 16kHz mono，多策略分段 → 实时转录+分离说话人。

分段策略（按优先级）：
  1. Silero VAD 静音检测 → 主要切分方式
  2. End-of-Utterance 检测模型 → 辅助切分
  3. 硬时长上限 → 兜底保护

后台线程持续读取 stdin 避免转录期间音频丢失。
Campaign 级声纹嵌入持久化：加载/保存到 speaker_embeddings 目录。
"""
from __future__ import annotations

import argparse
import collections
import json
import os
import struct
import sys
import threading
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


def _cosine_similarity(a: np.ndarray, b: np.ndarray) -> float:
    na = np.linalg.norm(a)
    nb = np.linalg.norm(b)
    if na < 1e-10 or nb < 1e-10:
        return 0.0
    return float(np.dot(a, b) / (na * nb))


DIARIZATION_MODEL_NAME = "pyannote/speaker-diarization-3.1"
SPEAKER_MATCH_THRESHOLD = 0.50
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
        min_speech_sec: float = 0.3,
        max_speech_sec: float = 120.0,
        silence_cut_ms: int = 400,
        eou_enabled: bool = False,
        eou_sensitivity: float = 0.5,
        speaker_embeddings_dir: str = "",
        skip_align: bool = False,
    ):
        self.language = language
        self.initial_prompt = initial_prompt
        self.hf_token = hf_token.strip() if hf_token else ""
        self.device, self.compute_type = _resolve_device(device, compute_type)
        self.batch_size = 16 if self.device == "cuda" else 1
        self._skip_align = skip_align

        self._min_speech_frames = max(1, int(min_speech_sec * SAMPLE_RATE / VAD_FRAME_SAMPLES))
        self._max_speech_frames = int(max_speech_sec * SAMPLE_RATE / VAD_FRAME_SAMPLES)

        self._embeddings_dir = speaker_embeddings_dir
        self._seq_counter = 0
        self._output_lock = threading.Lock()

        # ── 后台缓冲：读取线程持续从 stdin 接收音频，避免转录期间丢失数据 ──
        self._audio_buffer: collections.deque[bytes | None] = collections.deque()
        self._buffer_cond = threading.Condition()

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

        fallback_num = len(self._strategies) + 1
        print(f"[stream] 分段策略 {fallback_num} (兜底): 硬时长上限={max_speech_sec}s", file=sys.stderr)

        # ── 加载 ASR ──
        print(f"[stream] 加载 ASR 模型 {model_name} (device={self.device}, compute={self.compute_type}) ...",
              file=sys.stderr)
        asr_options = {"initial_prompt": initial_prompt} if initial_prompt else None
        self.asr_model = whisperx.load_model(model_name, self.device, compute_type=self.compute_type,
                                             language=language, asr_options=asr_options)
        print(f"[stream] ASR 模型加载完成", file=sys.stderr)
        self._gc()
        sys.stderr.flush()

        # ── 预加载 diarization 模型到 CUDA ──
        self._diarize_model = None
        if self.hf_token:
            print(f"[stream] 预加载说话者分离模型 {DIARIZATION_MODEL_NAME} (device={self.device}) ...", file=sys.stderr)
            self._diarize_model = whisperx.diarize.DiarizationPipeline(
                model_name=DIARIZATION_MODEL_NAME, token=self.hf_token, device=self.device)
            self._gc()
            print(f"[stream] 说话者分离模型加载完成", file=sys.stderr)
        sys.stderr.flush()
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

    def _create_speaker_id(self) -> str:
        new_id = f"speaker_{self._next_speaker_id}"
        self._next_speaker_id += 1
        return new_id

    def _get_or_load_align_model(self, language_code: str):
        cache_key = f"align_{language_code}"
        if hasattr(self, "_align_model_cache") and cache_key in self._align_model_cache:
            return self._align_model_cache[cache_key]
        if not hasattr(self, "_align_model_cache"):
            self._align_model_cache = {}
        align_model, align_metadata = whisperx.load_align_model(
            language_code=language_code, device=torch.device("cpu"))
        self._align_model_cache[cache_key] = (align_model, align_metadata)
        return align_model, align_metadata

    def _transcribe_audio(self, pcm_bytes: bytes) -> dict:
        """转录音频段。策略：先转录+对齐全段一次，再用 diarization + assign_word_speakers 分配说话人。
        避免逐子段重复转录和重复加载对齐模型，显著提升性能。"""
        t_start = time.monotonic()
        pcm = np.frombuffer(pcm_bytes, dtype=np.int16).astype(np.float32) / 32768.0
        duration = len(pcm) / SAMPLE_RATE

        # ── 步骤 1: 转录全段（一次） ──
        t_trans_start = time.monotonic()
        result = self.asr_model.transcribe(pcm, batch_size=self.batch_size, language=self.language)
        t_trans_end = time.monotonic()
        detected_lang = result.get("language", self.language) or self.language

        # ── 步骤 2: 对齐全段（一次，可跳过以提速） ──
        t_align_start = time.monotonic()
        if not self._skip_align:
            align_model, align_metadata = self._get_or_load_align_model(detected_lang)
            result = whisperx.align(result["segments"], align_model, align_metadata, pcm, "cpu")
        t_align_end = time.monotonic()
        self._gc()

        # ── 步骤 3: 说话者分离（仅当启用且时长充分） ──
        t_dia_total = 0.0
        t_match_total = 0.0
        if self._diarize_model is not None and duration >= 2.0:
            try:
                t_dia_start = time.monotonic()
                diarize_result = self._diarize_model(
                    pcm,
                    min_speakers=1, max_speakers=6,
                    return_embeddings=True,
                )
                t_dia_end = time.monotonic()
                t_dia_total = t_dia_end - t_dia_start

                if isinstance(diarize_result, tuple) and len(diarize_result) >= 2:
                    diarize_df = diarize_result[0]
                    embeddings = diarize_result[1] or {}
                else:
                    diarize_df = diarize_result
                    embeddings = {}

                t_match_start = time.monotonic()
                label_map: dict[str, str] = {}
                for label, emb in embeddings.items():
                    emb_arr = np.array(emb, dtype=np.float64)
                    norm = float(np.linalg.norm(emb_arr))
                    if norm < 0.01 or np.isnan(norm):
                        label_map[label] = self._create_speaker_id()
                    else:
                        matched = self._match_speaker(emb_arr)
                        label_map[label] = matched if matched is not None else self._create_speaker_id()

                for _, row in diarize_df.iterrows():
                    spk_raw = row["speaker"]
                    if spk_raw not in label_map:
                        label_map[spk_raw] = self._create_speaker_id()

                t_match_end = time.monotonic()
                t_match_total = t_match_end - t_match_start

                if label_map:
                    diarize_df["speaker"] = diarize_df["speaker"].map(label_map)
                    if diarize_df["speaker"].isna().any():
                        diarize_df["speaker"] = diarize_df["speaker"].fillna("speaker_0")
                    result = whisperx.assign_word_speakers(diarize_df, result)
                    print(f"[stream] 说话人分配完成: {len(label_map)} 个标签映射", file=sys.stderr)
            except Exception as e:
                print(f"说话者分离失败: {e}", file=sys.stderr)
                traceback.print_exc(file=sys.stderr)

        t_total = time.monotonic() - t_start
        segments = _clean_segments(result, default_speaker="speaker_0")
        print(f"[stream] 计时: 转录={t_trans_end - t_trans_start:.2f}s 对齐={t_align_end - t_align_start:.2f}s "
              f"分离={t_dia_total:.2f}s 匹配={t_match_total:.2f}s "
              f"总计={t_total:.2f}s 音频={duration:.1f}s 倍率={t_total / max(duration, 0.1):.1f}x",
              file=sys.stderr)

        return {"segments": segments,
                "timing": {"transcribe": round(t_trans_end - t_trans_start, 3),
                           "align": round(t_align_end - t_align_start, 3),
                           "diarize": round(t_dia_total, 3),
                           "match": round(t_match_total, 3),
                           "total": round(t_total, 3),
                           "audio_duration": round(duration, 3)}}

    def _flush_speech(self, speech_buffer: list[bytes], speech_frame_count: int):
        if speech_frame_count < self._min_speech_frames:
            return
        t_buffered = time.monotonic()
        audio_data = b"".join(speech_buffer)
        duration = speech_frame_count * VAD_FRAME_SAMPLES / SAMPLE_RATE
        print(f"[stream] 语音段 #{self._seq_counter}: {speech_frame_count}帧 ({duration:.1f}s) → 转录中",
              file=sys.stderr)
        sys.stderr.flush()

        try:
            result = self._transcribe_audio(audio_data)
            self._seq_counter += 1
            t_total = time.monotonic() - t_buffered
            timing = result.get("timing", {})
            timing["segment_total"] = round(t_total, 3)
            timing["segment_audio_duration"] = round(duration, 3)
            timing["segment_ratio"] = round(t_total / max(duration, 0.1), 2)
            self._write_json({"ok": True, "seq": self._seq_counter - 1, "segments": result.get("segments", []),
                              "timing": timing})
        except Exception as e:
            self._write_json({"ok": False, "seq": self._seq_counter, "error": str(e)})
            traceback.print_exc(file=sys.stderr)

    def _write_json(self, obj: dict):
        with self._output_lock:
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

    def _stdin_reader(self):
        """后台线程：持续从 stdin 读取音频帧到缓冲区，避免转录期间丢失数据。"""
        frame_bytes = VAD_FRAME_SAMPLES * BYTES_PER_SAMPLE
        try:
            while True:
                chunk = sys.stdin.buffer.read(frame_bytes)
                if not chunk or len(chunk) < frame_bytes:
                    with self._buffer_cond:
                        self._audio_buffer.append(None)
                        self._buffer_cond.notify()
                    break
                with self._buffer_cond:
                    self._audio_buffer.append(chunk)
                    self._buffer_cond.notify()
        except Exception as e:
            print(f"[stream] 后台读取线程异常: {e}", file=sys.stderr)
            with self._buffer_cond:
                self._audio_buffer.append(None)
                self._buffer_cond.notify()

    def _pop_frame(self) -> bytes | None:
        """从缓冲区取出一帧（阻塞至有数据）。返回 None 表示输入流结束。"""
        with self._buffer_cond:
            while not self._audio_buffer:
                self._buffer_cond.wait()
            return self._audio_buffer.popleft()

    def run(self):
        strategies_desc = " + ".join(type(s).__name__ for s in self._strategies)
        print(f"[stream] 就绪: 最短语音={self._min_speech_frames}frames, 最长={self._max_speech_frames}frames, 策略=({strategies_desc})",
              file=sys.stderr)
        sys.stderr.flush()

        # 启动后台读取线程
        reader_thread = threading.Thread(target=self._stdin_reader, daemon=True)
        reader_thread.start()

        speech_buffer: list[bytes] = []
        speech_frame_count = 0
        in_speech = False

        while True:
            chunk = self._pop_frame()
            if chunk is None:
                break

            pcm = np.frombuffer(chunk, dtype=np.int16).astype(np.float32) / 32768.0
            tensor = torch.from_numpy(pcm)

            # ── 所有策略共同决策 ──
            should_cut = self._any_strategy_triggers_cut(tensor)

            # 使用 VAD 策略的状态判断语音开始
            if self._vad_strategy.in_speech and not in_speech:
                in_speech = True
                # 回溯少量帧以获得完整的句首
                backtrack = 3
                if len(self._audio_buffer) >= backtrack:
                    pre = [self._audio_buffer[i] for i in range(len(self._audio_buffer) - backtrack, len(self._audio_buffer))]
                speech_buffer = list(speech_buffer[-backtrack:]) if speech_buffer else []

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
    parser.add_argument("--min-speech-sec", type=float, default=0.3,
                        help="最短有效语音时长 (秒)")
    parser.add_argument("--max-speech-sec", type=float, default=120.0,
                        help="最长允许的语音段 (秒) — 硬上限兜底")
    parser.add_argument("--silence-cut-ms", type=int, default=400,
                        help="VAD 静音切分灵敏度 (毫秒，默认400)")
    parser.add_argument("--eou", action="store_true", default=False,
                        help="启用句尾检测 (End-of-Utterance) 策略")
    parser.add_argument("--eou-sensitivity", type=float, default=0.5,
                        help="EOU 灵敏度 (0.0~1.0，越高越容易切分)")
    parser.add_argument("--speaker-embeddings-dir", default="",
                        help="Campaign 级声纹嵌入持久化目录")
    parser.add_argument("--skip-align", action="store_true", default=False,
                        help="跳过 Wav2Vec2 对齐，仅使用 ASR 原始时间戳（大幅提升速度）")
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
        skip_align=args.skip_align,
    )
    transcriber.run()


if __name__ == "__main__":
    main()
