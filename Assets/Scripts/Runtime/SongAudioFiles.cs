using System;
using System.IO;
using System.Linq;

namespace ChunithmMobile.Runtime
{
    public readonly struct SongAudioSet
    {
        public readonly string GameplayMp3;
        public readonly string PreviewMp3;
        public readonly string SourceAcb;
        public readonly string SourceAwb;

        /// <summary>
        /// Audio used while browsing the song list. An ACB-derived preview wins;
        /// songs without ACB fall back to the same MP3 used for gameplay.
        /// </summary>
        public string SelectionPreview => !string.IsNullOrWhiteSpace(PreviewMp3)
            ? PreviewMp3
            : GameplayMp3;

        public bool IsSelectionPreviewReady => !string.IsNullOrWhiteSpace(SelectionPreview);

        public SongAudioSet(string gameplayMp3, string previewMp3, string sourceAcb, string sourceAwb)
        {
            GameplayMp3 = gameplayMp3;
            PreviewMp3 = previewMp3;
            SourceAcb = sourceAcb;
            SourceAwb = sourceAwb;
        }

        public bool HasAcbSource => !string.IsNullOrWhiteSpace(SourceAcb);

        public bool NeedsPreviewGeneration => string.IsNullOrWhiteSpace(PreviewMp3) &&
            !string.IsNullOrWhiteSpace(SourceAcb) && !string.IsNullOrWhiteSpace(SourceAwb);
    }

    /// <summary>
    /// Shared song-folder convention. ACB/AWB remains the preview import source;
    /// every runtime platform plays MP3 so song selection behaves identically.
    /// </summary>
    public static class SongAudioFiles
    {
        public const string GameplayFileName = "gameplay.mp3";
        public const string PreviewFileName = "preview.mp3";

        public static SongAudioSet Resolve(string songRoot)
        {
            string directory = Path.Combine(songRoot, "audio");
            if (!Directory.Exists(directory)) return default;
            string[] files = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly);
            string gameplay = FindNamed(files, GameplayFileName) ?? files
                .Where(path => Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                .Where(path => !Path.GetFileName(path).Equals(PreviewFileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            string preview = FindNamed(files, PreviewFileName);
            string acb = FindExtension(files, ".acb");
            string awb = FindExtension(files, ".awb");
            return new SongAudioSet(gameplay, preview, acb, awb);
        }

        private static string FindNamed(string[] files, string name) => files.FirstOrDefault(path =>
            Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase));

        private static string FindExtension(string[] files, string extension) => files
            .Where(path => Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }
}
