# ロードマップ

指示書 `docs/work/phase1_implementation_instructions_001.md` (および `_002.md`) に基づく。

## 完了

- [x] Phase 0: CLI技術検証
  - whisper.cpp v1.9.1 + ggml-small で日本語文字起こし (実時間比約0.5x)
  - Ollama v0.31.1 + gemma4:e4b で整形 (本環境はCPU実行)
  - 調査結果: docs/research/local_stack_research.md
- [x] Phase 1: C#アプリMVP
  - グローバルホットキー録音トグル → WAV → whisper.cpp → クリップボード → Ctrl+V
- [x] Phase 2: Gemma 4整形
  - /api/chat 呼び出し、FormatResultValidator、raw transcriptへのフォールバック
- [x] Phase 3: 個人辞書
  - pre-pass / プロンプト注入 / post-pass。壊れた辞書ファイルでも継続動作
- [x] Phase 4: モード切替
  - NoFormat / Plain / Polite / Bullet / Memo / DevInstruction + ホットキー設定
- [x] Phase 5: 常駐アプリ化 (最小)
  - トレイ常駐、録音中/処理中オーバーレイ、直近結果の再コピー/再貼り付け、終了

## 今後の候補 (未実装)

- [ ] 実マイク音声での手動受け入れテスト (メモ帳 / Chrome / VSCode / メール)
- [ ] llama.cpp server (OpenAI互換 /v1/chat/completions) 経路の実装と検証
- [ ] 設定画面GUI (現状はJSON直接編集)
- [ ] 整形速度の改善 (小型モデル評価、GPU環境での num_gpu 調整、ストリーミング)
- [ ] restoreClipboard (貼り付け後に元のクリップボード内容を復元) の実装
- [ ] 録音デバイス選択
- [ ] 選択テキスト編集・リアルタイム字幕 (指示書上の将来スコープ)
