using System;
using System.IO;
using System.Linq;

namespace ChunithmMobile.Runtime
{
    // 歌曲音频文件解析：从歌曲根目录确定 gameplay、preview 和其他音频文件的标准路径。
    public readonly struct SongAudioSet
    {
        public readonly string GameplayMp3;
        public readonly string GameplayWav;
        public readonly string PreviewMp3;
        public readonly string PreviewWav;
        public readonly string SourceAcb;
        public readonly string SourceAwb;

        /// <summary>
        /// MP3 优先，ACB/AWB 解码器生成的 WAV 作为后备格式。
        /// </summary>
        public string Gameplay => FirstExisting(GameplayMp3, GameplayWav);

        public string SelectionPreview => FirstExisting(PreviewMp3, PreviewWav, Gameplay);

        public bool IsSelectionPreviewReady => !string.IsNullOrWhiteSpace(SelectionPreview);

        public SongAudioSet(string gameplayMp3, string gameplayWav, string previewMp3,
            string previewWav, string sourceAcb, string sourceAwb)
        {
            GameplayMp3 = gameplayMp3;
            GameplayWav = gameplayWav;
            PreviewMp3 = previewMp3;
            PreviewWav = previewWav;
            SourceAcb = sourceAcb;
            SourceAwb = sourceAwb;
        }

        public bool HasAcbSource => !string.IsNullOrWhiteSpace(SourceAcb);

        public bool HasPlayableGameplay => !string.IsNullOrWhiteSpace(Gameplay);

        public bool NeedsConversion => !HasPlayableGameplay &&
            (!string.IsNullOrWhiteSpace(SourceAcb) || !string.IsNullOrWhiteSpace(SourceAwb));

        public bool NeedsPreviewGeneration => string.IsNullOrWhiteSpace(PreviewMp3) &&
            string.IsNullOrWhiteSpace(PreviewWav) && NeedsConversion;

        // UnityWebRequest 需要根据真实扩展名选择解码器，不能把 WAV 当成 MPEG 读取。
        public static UnityEngine.AudioType GetUnityAudioType(string path)
        {
            return string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase)
                ? UnityEngine.AudioType.WAV : UnityEngine.AudioType.MPEG;
        }

        private static string FirstExisting(params string[] values)
        {
            return values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && File.Exists(value));
        }
    }

    /// <summary>
    /// Shared song-folder convention. MP3 remains preferred; WAV is accepted for
    /// ACB/AWB imports because vgmstream decodes to WAV and does not encode MP3.
    /// </summary>
    public static class SongAudioFiles
    {
        public const string GameplayFileName = "gameplay.mp3";
        public const string GameplayWavFileName = "gameplay.wav";
        public const string PreviewFileName = "preview.mp3";
        public const string PreviewWavFileName = "preview.wav";

        public static SongAudioSet Resolve(string songRoot)
        {
            string directory = Path.Combine(songRoot, "audio");
            if (!Directory.Exists(directory)) return default;
            string[] files = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly);
            string gameplay = FindNamed(files, GameplayFileName) ?? files
                .Where(path => Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                .Where(path => !Path.GetFileName(path).Equals(PreviewFileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            string gameplayWav = FindNamed(files, GameplayWavFileName);
            string preview = FindNamed(files, PreviewFileName);
            string previewWav = FindNamed(files, PreviewWavFileName);
            string acb = FindExtension(files, ".acb");
            string awb = FindExtension(files, ".awb");
            return new SongAudioSet(gameplay, gameplayWav, preview, previewWav, acb, awb);
        }

        private static string FindNamed(string[] files, string name) => files.FirstOrDefault(path =>
            Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase));

        private static string FindExtension(string[] files, string extension) => files
            .Where(path => Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }
}
