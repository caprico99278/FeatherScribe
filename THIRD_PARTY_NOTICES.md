# Third-Party Notices

FeatherScribe本体はproprietaryだが、以下のサードパーティ製ソフトウェア・モデルを利用する。
**これらはリポジトリに含めず**、セットアップスクリプトで利用者が各自ダウンロードする。

## 実行時に利用する外部ソフトウェア (リポジトリ非同梱)

| コンポーネント | ライセンス | 入手元 |
| --- | --- | --- |
| whisper.cpp v1.9.1 (whisper-cli.exe ほか) | MIT | https://github.com/ggml-org/whisper.cpp |
| Whisper ggml モデル (ggml-small.bin 等) | MIT (whisper.cpp配布物) / 元モデルは OpenAI Whisper (MIT) | https://huggingface.co/ggerganov/whisper.cpp |
| Ollama v0.31.1 | MIT | https://github.com/ollama/ollama |
| Gemma 4 モデル (gemma4:e2b / e4b) | Apache License 2.0 (Google DeepMind, 2026-04-02公開) | https://ollama.com/library/gemma4 |
| llama.cpp (代替経路・未使用) | MIT | https://github.com/ggml-org/llama.cpp |

## NuGetパッケージ (ビルド時に取得)

| パッケージ | ライセンス | 用途 |
| --- | --- | --- |
| NAudio 2.3.0 | MIT | マイク録音 |
| System.Speech 9.0.0 | MIT | 開発補助ツール (TTSサンプル音声生成) のみ |
| xunit 2.9.3 | Apache-2.0 | テスト |
| xunit.runner.visualstudio 3.1.4 | Apache-2.0 | テスト |
| Microsoft.NET.Test.Sdk 17.14.1 | MIT | テスト |
| coverlet.collector 6.0.4 | MIT | カバレッジ |

## 備考

- モデルファイル (*.bin, *.gguf, *.onnx)、外部バイナリ、音声ファイルは `.gitignore` によりコミット対象外。
- 各ライセンスの全文は各配布元を参照のこと。
- Gemma利用時は Google の利用規約 (Gemma Terms of Use / Apache 2.0) に従うこと。詳細は docs/licensing.md。
