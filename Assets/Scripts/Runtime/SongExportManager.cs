using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ChunithmMobile.Runtime
{
    // 歌曲导出器：只读取歌曲库目录内的文件，并把一首或多首歌曲打包成
    // 当前游戏可以再次直接导入的 ZIP。导出过程不修改歌曲库内容。
    public static class SongExportManager
    {
        public static void Export(IReadOnlyList<SongEntry> songs, string archivePath)
        {
            SongEntry[] exportSongs = songs?.Where(item => item != null).ToArray() ?? Array.Empty<SongEntry>();
            if (exportSongs.Length == 0)
                throw new InvalidOperationException("没有选择要导出的歌曲");
            if (string.IsNullOrWhiteSpace(archivePath))
                throw new ArgumentException("导出路径不可为空", nameof(archivePath));

            string output = Path.GetFullPath(archivePath);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            if (File.Exists(output)) File.Delete(output);

            try
            {
                using (FileStream stream = File.Create(output))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, false))
                {
                    var usedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (SongEntry song in exportSongs)
                    {
                        string root = ValidateSongRoot(song.Root);
                        string baseFolder = SafeName(string.IsNullOrWhiteSpace(song.Id)
                            ? Path.GetFileName(root) : song.Id);
                        string folder = baseFolder;
                        int suffix = 2;
                        while (!usedFolders.Add(folder)) folder = baseFolder + "_" + suffix++;
                        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                        {
                            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                            string entryName = folder + "/" + relative;
                            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Fastest);
                        }
                    }
                }
            }
            catch
            {
                // 不把中断或失败的半成品留在导出目录，避免下次分享时误选损坏 ZIP。
                try { if (File.Exists(output)) File.Delete(output); } catch { }
                throw;
            }
        }

        private static string ValidateSongRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                throw new DirectoryNotFoundException("歌曲目录不存在：" + root);
            string full = Path.GetFullPath(root);
            string[] allowedRoots = PlatformStoragePaths.Current.GetSongRoots()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath).ToArray();
            if (!allowedRoots.Any(allowed => full.StartsWith(
                    allowed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("拒绝导出歌曲库以外的目录：" + full);
            return full;
        }

        private static string SafeName(string value)
        {
            string result = value ?? "song";
            foreach (char invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(result) ? "song" : result.Trim();
        }
    }
}
