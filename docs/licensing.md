# ライセンス方針

## FeatherScribe本体

- **Proprietary / All rights reserved** (ルートの `LICENSE` を参照)
- CyPhoenix業務向けの内部利用を想定。再配布・公開はしない

## サードパーティ成果物の取り扱い

方針: **第三者のモデル・バイナリはリポジトリにコミットしない。**

- whisper.cpp / Ollama / llama.cpp のバイナリは `local/` に配置し、`.gitignore` で除外
- モデルファイルは `*.bin` / `*.gguf` / `*.onnx` および `models/`, `local/` を `.gitignore` で除外
- 録音・TTS生成の音声 (`samples/audio/*.wav` 等) もコミットしない
- 一覧と各ライセンスは `THIRD_PARTY_NOTICES.md` を参照

## 再配布に関する注意

将来アプリを配布する場合は、以下を再確認すること。

1. whisper.cpp / NAudio / Ollama (MIT), xUnit (Apache-2.0) — 同梱配布する場合はライセンス文の同梱が必要
2. Gemma 4 — Apache 2.0 だが、Googleの利用規約 (禁止用途ポリシー) の確認が必要
3. Whisperモデル — MIT (OpenAI) だが、配布形態に応じて出典明記が望ましい
4. System.Speech — 開発補助ツールのみで使用。アプリ本体には含まれない

## 社内利用時の遵守事項

- モデル・バイナリのダウンロードは各自が公式配布元から行う (セットアップスクリプトは公式URLのみを参照)
- ライセンス上の疑義がある依存を追加する場合は、この文書と THIRD_PARTY_NOTICES.md を先に更新すること
