#!/usr/bin/env bash
# CI 发布打包脚本
# 用法: ./scripts/ci_publish_package.sh [--rid linux-x64|win-x64] [--skip-ffmpeg]
# 本地测试: ./scripts/ci_publish_package.sh --rid linux-x64
# CI 调用:   RUNTIME_ID=linux-x64 ./scripts/ci_publish_package.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$PROJECT_ROOT"

# ── 参数解析 ─────────────────────────────────────────────
RID="${RUNTIME_ID:-linux-x64}"
SKIP_FFMPEG=false
SKIP_TEST=false
for arg in "$@"; do
  case "$arg" in
    --rid) RID="${2:-$RID}"; shift ;;
    --rid=*) RID="${arg#*=}" ;;
    --skip-ffmpeg) SKIP_FFMPEG=true ;;
    --skip-test) SKIP_TEST=true ;;
  esac
done

PUBLISH_DIR="$PROJECT_ROOT/publish"
ARTIFACT_NAME="TrpgVoiceDigest-${RID}"

echo "===== Publish ($RID) ====="
echo "Target RID: $RID"
echo "Publish dir: $PUBLISH_DIR"
echo ""

# 清理旧的 publish 目录
rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

# ── 1. 构建与测试 ────────────────────────────────────────
echo "========== Step 1: Restore & Build & Test =========="
dotnet restore

if ! $SKIP_TEST; then
  echo "--- Running tests ---"
  dotnet test --configuration Release --no-restore --verbosity normal
fi

# ── 2. 发布 Gui + Cli ─────────────────────────────────────
echo ""
echo "========== Step 2: Publish Gui == ========="
dotnet publish src/TrpgVoiceDigest.Gui/TrpgVoiceDigest.Gui.csproj \
  --configuration Release \
  --runtime "$RID" \
  --self-contained true \
  -p:DebugType=none \
  -o "$PUBLISH_DIR"

echo ""
echo "========== Step 3: Publish Cli =========="
dotnet publish src/TrpgVoiceDigest.Cli/TrpgVoiceDigest.Cli.csproj \
  --configuration Release \
  --runtime "$RID" \
  --self-contained true \
  -p:DebugType=none \
  -o "$PUBLISH_DIR"

# ── 3. 下载并捆绑 ffmpeg ─────────────────────────────────
if ! $SKIP_FFMPEG; then
  echo ""
  echo "========== Step 4: Bundle ffmpeg =========="
  FFMPEG_DIR="$PUBLISH_DIR/tools/ffmpeg"
  mkdir -p "$FFMPEG_DIR"

  case "$RID" in
    linux-*)
      echo "Downloading ffmpeg (Linux) ..."
      FFMPEG_URL="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linux64-gpl.tar.xz"
      ARCHIVE_PATH="/tmp/ffmpeg_download.tar.xz"
      curl -fsSL "$FFMPEG_URL" -o "$ARCHIVE_PATH"

      # 获取 tar 顶层目录名
      FFMPEG_SRC_DIR=$(tar -tf "$ARCHIVE_PATH" 2>/dev/null | head -1 | cut -d/ -f1)
      tar -xf "$ARCHIVE_PATH" -C /tmp
      cp "/tmp/${FFMPEG_SRC_DIR}/bin/ffmpeg" "$FFMPEG_DIR/ffmpeg"
      cp "/tmp/${FFMPEG_SRC_DIR}/bin/ffprobe" "$FFMPEG_DIR/ffprobe"
      chmod +x "$FFMPEG_DIR/ffmpeg" "$FFMPEG_DIR/ffprobe"
      rm -rf "/tmp/${FFMPEG_SRC_DIR}" "$ARCHIVE_PATH"

      echo "Bundled ffmpeg version:"
      "$FFMPEG_DIR/ffmpeg" -version 2>&1 | head -1 || echo "(version check failed)"
      ;;

    win-*)
      echo "Downloading ffmpeg (Windows) ..."
      FFMPEG_URL="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
      ARCHIVE_PATH="/tmp/ffmpeg_download.zip"
      curl -fsSL "$FFMPEG_URL" -o "$ARCHIVE_PATH"

      FFMPEG_SRC_DIR=$(unzip -Z1 "$ARCHIVE_PATH" 2>/dev/null | head -1 | cut -d/ -f1)
      mkdir -p /tmp/ffmpeg_extract
      unzip -qo "$ARCHIVE_PATH" -d /tmp/ffmpeg_extract
      cp "/tmp/ffmpeg_extract/${FFMPEG_SRC_DIR}/bin/ffmpeg.exe" "$FFMPEG_DIR/ffmpeg.exe"
      cp "/tmp/ffmpeg_extract/${FFMPEG_SRC_DIR}/bin/ffprobe.exe" "$FFMPEG_DIR/ffprobe.exe"
      rm -rf /tmp/ffmpeg_extract "$ARCHIVE_PATH"
      ;;

    *)
      echo "WARNING: Unknown RID '$RID' — skipping ffmpeg bundle"
      ;;
  esac
else
  echo "===== Skipping ffmpeg download ====="
fi

# ── 4. 复制文档到发布包 ──────────────────────────────────
echo ""
echo "========== Step 5: Copy docs =========="
cp "$PROJECT_ROOT/SETUP.md" "$PUBLISH_DIR/SETUP.md"

# ── ─. 设置可执行权限 ─────────────────────────────────────
if [[ "$RID" == linux-* ]]; then
  chmod +x "$PUBLISH_DIR/TrpgVoiceDigest.Gui"
  chmod +x "$PUBLISH_DIR/TrpgVoiceDigest.Cli"
  chmod +x "$PUBLISH_DIR/scripts"/*.sh 2>/dev/null || true
fi

# ── 6. 打包 ───────────────────────────────────────────────
echo ""
echo "========== Step 6: Package =========="

case "$RID" in
  linux-*)
    ARCHIVE_FILE="${PROJECT_ROOT}/${ARTIFACT_NAME}.tar.gz"
    echo "Creating $ARCHIVE_FILE ..."
    tar -czf "$ARCHIVE_FILE" -C "$PUBLISH_DIR" .
    echo "Package: $ARCHIVE_FILE ($(du -h "$ARCHIVE_FILE" | cut -f1))"
    ;;

  win-*)
    ARCHIVE_FILE="${PROJECT_ROOT}/${ARTIFACT_NAME}.zip"
    echo "Creating $ARCHIVE_FILE ..."
    if command -v 7z >/dev/null 2>&1; then
      7z a "$ARCHIVE_FILE" "$PUBLISH_DIR"/*
    elif command -v zip >/dev/null 2>&1; then
      (cd "$PUBLISH_DIR" && zip -r "$ARCHIVE_FILE" .)
    else
      echo "WARNING: No archiver found (7z or zip). Skipping package creation."
    fi
    echo "Package: $ARCHIVE_FILE ($(du -h "$ARCHIVE_FILE" 2>/dev/null | cut -f1 || echo "unknown"))"
    ;;

  *)
    echo "WARNING: Unknown RID '$RID' — skipping packaging"
    ;;
esac

echo ""
echo "===== Publish complete ====="
echo "Publish dir: $PUBLISH_DIR"
echo "Artifact:    ${ARTIFACT_NAME}.tar.gz (or .zip)"
