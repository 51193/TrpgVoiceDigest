#!/usr/bin/env python3
import argparse
import json
import whisper


def main():
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

    transcribe_kwargs = {"language": args.language}
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
