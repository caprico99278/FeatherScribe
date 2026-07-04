// Phase 0 検証用の日本語サンプル音声 (16kHz mono WAV) を Windows TTS で生成する開発補助ツール。
// 使い方: dotnet run --project tools/MakeSampleAudio -- <出力wavパス> [読み上げテキスト]
using System.Speech.AudioFormat;
using System.Speech.Synthesis;

var outputPath = args.Length > 0 ? args[0] : "samples/audio/sample_001.wav";
var text = args.Length > 1
    ? args[1]
    : "えーと、今日の打ち合わせの内容をまとめます。まず、音声入力アプリの開発についてですが、" +
      "ホットキーを押すと録音が始まって、もう一度押すと停止します。それから、ウィスパーで文字起こしをして、" +
      "ジェマで文章を整形します。えー、最後に、整形した文章をクリップボードにコピーして、" +
      "今アクティブな入力欄へ自動で貼り付けます。処理はすべてローカルで完結するので、" +
      "音声データが外部に送信されることはありません。以上です。";

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

using var synthesizer = new SpeechSynthesizer();

var japaneseVoice = synthesizer.GetInstalledVoices()
    .FirstOrDefault(v => v.VoiceInfo.Culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase));
if (japaneseVoice is null)
{
    Console.Error.WriteLine("日本語のTTS音声が見つかりません。設定 > 時刻と言語 > 音声認識 から追加してください。");
    Console.Error.WriteLine("インストール済み音声: " + string.Join(", ",
        synthesizer.GetInstalledVoices().Select(v => $"{v.VoiceInfo.Name} ({v.VoiceInfo.Culture.Name})")));
    return 1;
}

synthesizer.SelectVoice(japaneseVoice.VoiceInfo.Name);
synthesizer.Rate = 0;
synthesizer.SetOutputToWaveFile(
    outputPath,
    new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
synthesizer.Speak(text);
synthesizer.SetOutputToNull();

Console.WriteLine($"Voice : {japaneseVoice.VoiceInfo.Name}");
Console.WriteLine($"Output: {Path.GetFullPath(outputPath)}");
return 0;
