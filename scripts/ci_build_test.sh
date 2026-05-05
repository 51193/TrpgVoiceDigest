#!/usr/bin/env bash
# CI 构建与测试脚本
# 用法: ./scripts/ci_build_test.sh [--skip-test]
# 可用于 CI (ci.yml) 和本地开发验证
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$PROJECT_ROOT"

CONFIG="${CONFIGURATION:-Release}"
SKIP_TEST=false
for arg in "$@"; do
  case "$arg" in
    --skip-test) SKIP_TEST=true ;;
    --debug) CONFIG="Debug" ;;
  esac
done

echo "===== Restore dependencies ====="
dotnet restore

echo "===== Build ($CONFIG) ====="
dotnet build --no-restore --configuration "$CONFIG"

if ! $SKIP_TEST; then
  echo "===== Test ====="
  dotnet test --no-build --configuration "$CONFIG" --verbosity normal
else
  echo "===== Test skipped ====="
fi

echo "===== CI Build & Test complete ====="
