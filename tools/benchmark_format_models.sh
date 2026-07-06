#!/usr/bin/env bash
set -euo pipefail

MODELS="${MODELS:-qwen3:0.6b,gemma3:1b,qwen3:1.7b,hf.co/SakanaAI/TinySwallow-1.5B-Instruct-GGUF:Q5_K_M,qwen3:4b,llama3.2:3b,phi4-mini:3.8b,gemma3:4b,gemma4:e2b,gemma4:e4b}"
INPUT_DIR="${INPUT_DIR:-samples/format_benchmark/input}"
OUTPUT_DIR="${OUTPUT_DIR:-reports}"
ENDPOINT="${ENDPOINT:-http://localhost:11434}"
TIMEOUT_FAST="${TIMEOUT_FAST:-30}"
TIMEOUT_QUALITY="${TIMEOUT_QUALITY:-120}"
TIMEOUT_REFERENCE="${TIMEOUT_REFERENCE:-180}"

cd "$(dirname "$0")/.."

dotnet run --project src/FeatherScribe.Benchmarks -- \
  --models "$MODELS" \
  --input "$INPUT_DIR" \
  --output "$OUTPUT_DIR" \
  --endpoint "$ENDPOINT" \
  --timeout-fast "$TIMEOUT_FAST" \
  --timeout-quality "$TIMEOUT_QUALITY" \
  --timeout-reference "$TIMEOUT_REFERENCE"
