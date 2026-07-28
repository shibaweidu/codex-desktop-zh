#!/bin/bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT/macos"
export CODEX_ZH_SHARED_DIR="$ROOT/shared"
swift test
