using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using ChunithmMobile.Chart;

namespace ChunithmMobile.Runtime
{
    public sealed class SongImportDetail
    {
        public string SongId;
        public string SongName;
        public string Status;
        public string Message;
        public string Destination;
    }

    public sealed class SongImportProgress
    {
        public float Fraction;
        public string Stage;
        public string CurrentItem;
        public string Message;
        public int ProcessedItems;
        public int TotalItems;
        public long ProcessedBytes;
        public long TotalBytes;
        public SongImportDetail Detail;
    }

    public sealed class SongImportResult
    {
        public int Imported;
        public int Skipped;
        public string LogPath;
        public readonly List<string> FailedSongs = new List<string>();
        public readonly List<SongImportDetail> Details = new List<SongImportDetail>();
        // These entries have already passed validation and were atomically
        // committed, so callers can update the catalog without rescanning it.
        public readonly List<SongEntry> ImportedSongs = new List<SongEntry>();
    }

    public sealed class SongImportException : Exception
    {
        public string LogPath { get; }

        public SongImportException(string message, string logPath, Exception innerException)
            : base(message, innerException)
        {
            LogPath = logPath;
        }
    }

    public sealed class ImportMemoryException : IOException
    {
        public ImportMemoryException(string message)
            : base(message) { }

        public ImportMemoryException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    /// <summary>
    /// Transactional ZIP/folder importer. Files are validated in cache first and
    /// each song becomes visible only after an atomic move into the song library.
    /// </summary>
    public static class SongImportManager
    {
        private const long MaxArchiveBytes = 32L * 1024 * 1024 * 1024;
        private const int MaxArchiveEntries = 100000;
        private const long MinimumImportMemoryHeadroom = 96L * 1024 * 1024;

        private sealed class ValidatedSong
        {
            public SongEntry Song;
            public long SourceBytes;
        }

        public static Task<SongImportResult> ImportAsync(string source, string persistentDataPath,
            string temporaryCachePath, string destinationRoot, Action<SongImportProgress> progress = null,
            long availableMemoryBytes = -1)
        {
            return Task.Run(() => Import(source, persistentDataPath, temporaryCachePath, destinationRoot,
                progress, availableMemoryBytes));
        }

        private static SongImportResult Import(string source, string persistentDataPath,
            string temporaryCachePath, string destinationRoot, Action<SongImportProgress> progress,
            long availableMemoryBytes)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("没有选择导入文件", nameof(source));
            if (string.IsNullOrWhiteSpace(destinationRoot)) throw new ArgumentException("歌曲目录不可用", nameof(destinationRoot));

            Directory.CreateDirectory(destinationRoot);
            Directory.CreateDirectory(temporaryCachePath);
            CleanupPartialImports(destinationRoot);
            CleanupImportCache(temporaryCachePath, source);

            var result = new SongImportResult();
            string logDirectory = ResolveLogDirectory(persistentDataPath, destinationRoot);
            Directory.CreateDirectory(logDirectory);
            result.LogPath = Path.Combine(logDirectory, "import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            TrimLogs(logDirectory);
            Write(result, "IMPORT_START", $"source={source} destination={destinationRoot}");

            string staging = Path.Combine(temporaryCachePath, "chunithm-import-" + Guid.NewGuid().ToString("N"));
            var committedDestinations = new List<string>();
            try
            {
                Directory.CreateDirectory(staging);
                Report(progress, .01f, "准备", Path.GetFileName(source), "正在读取导入文件");
                if (Directory.Exists(source)) CopySourceToStage(source, staging, progress);
                else if (File.Exists(source)) ExtractZip(source, staging, result, progress, availableMemoryBytes);
                else throw new FileNotFoundException("选择的导入文件不存在", source);

                Report(progress, .43f, "扫描", "歌曲数据", "正在识别歌曲目录");
                List<string> songFolders = FindSongFolders(staging);
                if (songFolders.Count == 0)
                    throw new InvalidDataException("压缩包中未找到 Music.xml 或 C2S 谱面");
                Write(result, "SCAN", $"found={songFolders.Count}");

                Dictionary<string, string> existingIds = ReadExistingIds(destinationRoot);
                for (int index = 0; index < songFolders.Count; index++)
                {
                    string folder = songFolders[index];
                    float start = .48f + .47f * index / Math.Max(1, songFolders.Count);
                    Report(progress, start, "校验", new DirectoryInfo(folder).Name,
                        $"正在校验第 {index + 1}/{songFolders.Count} 首", index, songFolders.Count);
                    ImportSong(folder, destinationRoot, existingIds, result, progress, index, songFolders.Count,
                        committedDestinations);
                }

                Report(progress, 1f, "完成", "歌曲索引", $"成功 {result.Imported} 首，跳过 {result.Skipped} 首",
                    songFolders.Count, songFolders.Count);
                Write(result, "IMPORT_END", $"imported={result.Imported} skipped={result.Skipped}");
                return result;
            }
            catch (Exception exception)
            {
                if (IsMemoryFailure(exception))
                {
                    RollbackCommittedImports(committedDestinations, result);
                    Write(result, "IMPORT_MEMORY_CANCEL", "导入因内存不足取消，已回滚本次已提交歌曲");
                }
                Write(result, "IMPORT_FATAL", $"type={exception.GetType().FullName} reason={exception.Message}\n{exception}");
                throw new SongImportException("导入失败：" + exception.Message + "\n详细日志：" + result.LogPath,
                    result.LogPath, exception);
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                catch (Exception exception) { Write(result, "CLEANUP_FAIL", $"folder={staging} reason={exception.Message}"); }
            }
        }

        private static void ImportSong(string folder, string destinationRoot, IDictionary<string, string> existingIds,
            SongImportResult result, Action<SongImportProgress> progress, int index, int total,
            ICollection<string> committedDestinations)
        {
            string temporaryDestination = null;
            SongEntry song = null;
            try
            {
                ValidatedSong validated = ValidateSong(folder, progress, index, total);
                song = validated.Song;
                string normalizedId = NormalizeId(song.Id);
                if (existingIds.TryGetValue(normalizedId, out string existing))
                {
                    var duplicate = new SongImportDetail {
                        SongId = song.Id, SongName = song.Name, Status = "跳过",
                        Message = "歌曲 ID 已存在", Destination = existing
                    };
                    result.Skipped++;
                    result.Details.Add(duplicate);
                    Write(result, "SONG_DUPLICATE", $"id={song.Id} name={song.Name} existing={existing}");
                    ReportSong(progress, duplicate, index + 1, total);
                    return;
                }

                EnsureFreeSpace(destinationRoot, validated.SourceBytes);
                temporaryDestination = Path.Combine(destinationRoot, ".importing-" + Guid.NewGuid().ToString("N"));
                CopyTree(folder, temporaryDestination);
                string directoryName = SafeName(string.IsNullOrWhiteSpace(song.Id) ? new DirectoryInfo(folder).Name : song.Id);
                string destination = UniqueDestination(destinationRoot, directoryName);
                Directory.Move(temporaryDestination, destination);
                temporaryDestination = null;
                committedDestinations?.Add(destination);
                existingIds[normalizedId] = destination;
                RebaseSongPaths(song, folder, destination);

                var success = new SongImportDetail {
                    SongId = song.Id, SongName = song.Name, Status = "成功",
                    Message = $"已导入 {song.Charts.Count} 个谱面", Destination = destination
                };
                result.Imported++;
                result.Details.Add(success);
                result.ImportedSongs.Add(song);
                Write(result, "SONG_OK", $"id={song.Id} name={song.Name} destination={destination}");
                ReportSong(progress, success, index + 1, total);
            }
            catch (Exception exception)
            {
                try { if (!string.IsNullOrWhiteSpace(temporaryDestination) && Directory.Exists(temporaryDestination)) Directory.Delete(temporaryDestination, true); }
                catch { }
                if (IsMemoryFailure(exception)) throw;
                string displayName = song?.Name ?? new DirectoryInfo(folder).Name;
                var failed = new SongImportDetail {
                    SongId = song?.Id ?? "", SongName = displayName, Status = "失败",
                    Message = exception.Message, Destination = ""
                };
                result.Skipped++;
                result.FailedSongs.Add(folder);
                result.Details.Add(failed);
                Write(result, "SONG_FAIL", $"folder={folder} type={exception.GetType().FullName} reason={exception.Message}");
                ReportSong(progress, failed, index + 1, total);
            }
            finally
            {
                // Imported data is already committed atomically. Releasing the
                // staged source here keeps large archives near 2x, not 3x, disk use.
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
                catch (Exception exception) { Write(result, "STAGE_CLEANUP_FAIL", $"folder={folder} reason={exception.Message}"); }
            }
        }

        private static void ExtractZip(string zipPath, string destination, SongImportResult result,
            Action<SongImportProgress> progress, long availableMemoryBytes)
        {
            string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;
            using var file = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
                FileOptions.SequentialScan);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, false, Encoding.UTF8);
            if (archive.Entries.Count > MaxArchiveEntries)
                throw new InvalidDataException($"ZIP 文件数量过多：{archive.Entries.Count}");

            long expandedBytes = 0;
            long largestEntryBytes = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (entry.Length < 0 || expandedBytes > MaxArchiveBytes - entry.Length)
                    throw new InvalidDataException("ZIP 解压后超过 32 GB 限制");
                expandedBytes += entry.Length;
                largestEntryBytes = Math.Max(largestEntryBytes, entry.Length);
            }
            EnsureImportMemory(archive.Entries.Count, largestEntryBytes, availableMemoryBytes, result);
            EnsureFreeSpace(destination, expandedBytes);
            Write(result, "ARCHIVE", $"entries={archive.Entries.Count} expandedBytes={expandedBytes} " +
                $"largestEntryBytes={largestEntryBytes} availableMemoryBytes={availableMemoryBytes}");

            long written = 0;
            var buffer = new byte[1024 * 1024];
            long lastReportedBytes = 0;
            long lastReportTicks = 0;
            for (int index = 0; index < archive.Entries.Count; index++)
            {
                ZipArchiveEntry entry = archive.Entries[index];
                string entryName = entry.FullName ?? "";
                string safeEntryName = SafeArchivePath(entryName);
                string target = Path.GetFullPath(Path.Combine(destination,
                    safeEntryName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("ZIP 包含不安全路径：" + entryName);
                if (IsSymbolicLink(entry)) throw new InvalidDataException("ZIP 包含不支持的符号链接：" + entryName);

                if (entryName.EndsWith("/", StringComparison.Ordinal) || entryName.EndsWith("\\", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(target);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
                    using Stream input = entry.Open();
                    using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None,
                        1024 * 1024, FileOptions.SequentialScan);
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        written += read;
                        long nowTicks = DateTime.UtcNow.Ticks;
                        if (ShouldReportByteProgress(written, expandedBytes, lastReportedBytes, lastReportTicks, nowTicks))
                        {
                            float fraction = expandedBytes <= 0 ? (index + 1f) / Math.Max(1, archive.Entries.Count) :
                                Math.Min(1f, written / (float)expandedBytes);
                            Report(progress, .02f + .38f * fraction, "解压",
                                string.IsNullOrWhiteSpace(entryName) ? "ZIP 数据" : entryName,
                                $"正在解压 {index + 1}/{archive.Entries.Count}", index + 1,
                                archive.Entries.Count, null, written, expandedBytes);
                            lastReportedBytes = written;
                            lastReportTicks = nowTicks;
                        }
                    }
                }

                if (ShouldReportByteProgress(written, expandedBytes, lastReportedBytes, lastReportTicks,
                    DateTime.UtcNow.Ticks, true))
                {
                    float fraction = expandedBytes <= 0 ? (index + 1f) / Math.Max(1, archive.Entries.Count) :
                        Math.Min(1f, written / (float)expandedBytes);
                    Report(progress, .02f + .38f * fraction, "解压",
                        string.IsNullOrWhiteSpace(entryName) ? "ZIP 数据" : entryName,
                        $"正在解压 {index + 1}/{archive.Entries.Count}", index + 1,
                        archive.Entries.Count, null, written, expandedBytes);
                    lastReportedBytes = written;
                    lastReportTicks = DateTime.UtcNow.Ticks;
                }
            }
        }

        private static void CopySourceToStage(string source, string destination, Action<SongImportProgress> progress)
        {
            long totalBytes = 0;
            int fileCount = 0;
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                totalBytes += new FileInfo(file).Length;
                fileCount++;
            }
            EnsureFreeSpace(destination, totalBytes);
            long copiedBytes = 0;
            long lastReportedBytes = 0;
            long lastReportTicks = 0;
            var buffer = new byte[1024 * 1024];
            int index = 0;
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, file);
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                    buffer.Length, FileOptions.SequentialScan))
                using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None,
                    buffer.Length, FileOptions.SequentialScan))
                {
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        copiedBytes += read;
                        long nowTicks = DateTime.UtcNow.Ticks;
                        if (ShouldReportByteProgress(copiedBytes, totalBytes, lastReportedBytes, lastReportTicks,
                            nowTicks))
                        {
                            float fraction = totalBytes <= 0 ? (index + 1f) / Math.Max(1, fileCount) :
                                Math.Min(1f, copiedBytes / (float)totalBytes);
                            Report(progress, .02f + .38f * fraction, "读取", relative,
                                $"正在读取 {index + 1}/{fileCount}", index + 1, fileCount, null,
                                copiedBytes, totalBytes);
                            lastReportedBytes = copiedBytes;
                            lastReportTicks = nowTicks;
                        }
                    }
                }
                if (ShouldReportByteProgress(copiedBytes, totalBytes, lastReportedBytes, lastReportTicks,
                    DateTime.UtcNow.Ticks, true))
                {
                    float fraction = totalBytes <= 0 ? (index + 1f) / Math.Max(1, fileCount) :
                        Math.Min(1f, copiedBytes / (float)totalBytes);
                    Report(progress, .02f + .38f * fraction, "读取", relative,
                        $"正在读取 {index + 1}/{fileCount}", index + 1, fileCount, null,
                        copiedBytes, totalBytes);
                    lastReportedBytes = copiedBytes;
                    lastReportTicks = DateTime.UtcNow.Ticks;
                }
                index++;
            }
        }

        private static List<string> FindSongFolders(string root)
        {
            var folders = new List<string>();
            foreach (string xml in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Where(path => Path.GetFileName(path).Equals("Music.xml", StringComparison.OrdinalIgnoreCase)))
            {
                DirectoryInfo xmlDirectory = Directory.GetParent(xml);
                string songRoot = xmlDirectory != null && xmlDirectory.Name.Equals("chart", StringComparison.OrdinalIgnoreCase)
                    ? xmlDirectory.Parent?.FullName
                    : xmlDirectory?.FullName;
                if (!string.IsNullOrWhiteSpace(songRoot)) folders.Add(songRoot);
            }

            if (folders.Count == 0)
            {
                folders.AddRange(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(path => Path.GetExtension(path).Equals(".c2s", StringComparison.OrdinalIgnoreCase))
                    .Select(path => FindSongRoot(Path.GetDirectoryName(path), root))
                    .Where(path => path != null));
            }
            return folders.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string FindSongRoot(string chartFolder, string boundary)
        {
            DirectoryInfo cursor = new DirectoryInfo(chartFolder);
            string fullBoundary = Path.GetFullPath(boundary);
            while (cursor != null && !string.Equals(cursor.FullName, fullBoundary, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(Path.Combine(cursor.FullName, "audio")) ||
                    File.Exists(Path.Combine(cursor.FullName, "manifest.json"))) return cursor.FullName;
                if (cursor.Name.Equals("chart", StringComparison.OrdinalIgnoreCase)) return cursor.Parent?.FullName;
                cursor = cursor.Parent;
            }
            return chartFolder;
        }

        private static ValidatedSong ValidateSong(string folder, Action<SongImportProgress> progress, int songIndex, int songTotal)
        {
            var charts = new List<string>();
            bool hasMusic = false;
            long sourceBytes = 0;
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                FileInfo info = new FileInfo(file);
                if (info.Length > 0 && sourceBytes <= long.MaxValue - info.Length) sourceBytes += info.Length;
                string extension = Path.GetExtension(file);
                if (extension.Equals(".c2s", StringComparison.OrdinalIgnoreCase)) charts.Add(file);
                else if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)) hasMusic = true;
            }
            charts.Sort(StringComparer.OrdinalIgnoreCase);
            if (charts.Count == 0) throw new InvalidDataException("未找到 C2S 谱面");
            if (!hasMusic)
                throw new InvalidDataException("未找到 MP3 音乐；当前 Unity 版不导入仅含 ACB/AWB 的歌曲");
            for (int chartIndex = 0; chartIndex < charts.Count; chartIndex++)
            {
                string chart = charts[chartIndex];
                float songFraction = (chartIndex + 1f) / Math.Max(1, charts.Count + 1);
                float fraction = .48f + .47f * (songIndex + songFraction) / Math.Max(1, songTotal);
                Report(progress, fraction, "校验", new DirectoryInfo(folder).Name,
                    $"正在解析谱面 {chartIndex + 1}/{charts.Count}", songIndex, songTotal);
                try { C2sParser.Parse(chart); }
                catch (Exception exception)
                {
                    throw new InvalidDataException("谱面解析失败 " + Path.GetFileName(chart) + "：" + exception.Message, exception);
                }
            }

            SongEntry song = SongCatalog.ReadSongDirectory(folder);
            if (song == null || song.Charts == null || song.Charts.Count == 0)
                throw new InvalidDataException("Music.xml 中没有可用谱面");
            if (string.IsNullOrWhiteSpace(song.Id)) throw new InvalidDataException("Music.xml 中没有歌曲 ID");
            if (string.IsNullOrWhiteSpace(song.Name)) song.Name = song.Id;
            return new ValidatedSong { Song = song, SourceBytes = sourceBytes };
        }

        private static string SafeArchivePath(string entryName)
        {
            string[] segments = entryName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var safe = new List<string>(segments.Length);
            foreach (string segment in segments)
            {
                if (segment == ".") continue;
                if (segment == "..") throw new InvalidDataException("ZIP 包含不安全路径：" + entryName);
                safe.Add(SafeArchiveSegment(segment));
            }
            return safe.Count == 0 ? "_archive-root" : Path.Combine(safe.ToArray());
        }

        private static string SafeArchiveSegment(string value)
        {
            if (value.IndexOf('\uFFFD') >= 0)
                return "_encoded-" + StableHash(value).ToString("x8");
            foreach (char character in Path.GetInvalidFileNameChars()) value = value.Replace(character, '_');
            return string.IsNullOrWhiteSpace(value) ? "_empty" : value;
        }

        private static uint StableHash(string value)
        {
            uint hash = 2166136261;
            foreach (char character in value) hash = (hash ^ character) * 16777619;
            return hash;
        }

        private static Dictionary<string, string> ReadExistingIds(string destinationRoot)
        {
            var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string directory in Directory.GetDirectories(destinationRoot))
            {
                if (Path.GetFileName(directory).StartsWith(".importing-", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    string id = ReadExistingSongId(directory);
                    if (!string.IsNullOrWhiteSpace(id)) ids[NormalizeId(id)] = directory;
                }
                catch { }
            }
            return ids;
        }

        private static string ReadExistingSongId(string root)
        {
            string xmlPath = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .FirstOrDefault(path => Path.GetFileName(path).Equals("Music.xml", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(xmlPath)) return Path.GetFileName(root);

            var document = new XmlDocument { XmlResolver = null };
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(xmlPath, settings)) document.Load(reader);
            return document.SelectSingleNode("/MusicData/name/id")?.InnerText?.Trim() ?? Path.GetFileName(root);
        }

        private static void RebaseSongPaths(SongEntry song, string sourceRoot, string destinationRoot)
        {
            if (song == null) return;
            song.Root = destinationRoot;
            song.JacketPath = RebaseSongPath(song.JacketPath, sourceRoot, destinationRoot);
            if (song.Charts == null) return;
            foreach (SongChartEntry chart in song.Charts)
                if (chart != null) chart.Path = RebaseSongPath(chart.Path, sourceRoot, destinationRoot);
        }

        private static string RebaseSongPath(string path, string sourceRoot, string destinationRoot)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            try
            {
                string source = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string fullPath = Path.GetFullPath(path);
                if (!fullPath.StartsWith(source, StringComparison.OrdinalIgnoreCase)) return path;
                return Path.Combine(destinationRoot, fullPath.Substring(source.Length));
            }
            catch { return path; }
        }

        private static void CopyTree(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, file);
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
                File.Copy(file, target, true);
            }
        }

        private static void EnsureImportMemory(int entryCount, long largestEntryBytes, long availableMemoryBytes,
            SongImportResult result)
        {
            if (availableMemoryBytes <= 0)
            {
                Write(result, "MEMORY_CHECK", "可用内存不可用，跳过预检");
                return;
            }

            // The archive and all payload files remain streamed. The peak here
            // is central-directory metadata, the reusable 1 MB copy buffer and
            // one chart parser result, never the archive's expanded size.
            long centralDirectoryBytes = Math.Min(32L * 1024 * 1024,
                Math.Max(2L * 1024 * 1024, entryCount * 512L));
            long parserBytes = Math.Min(48L * 1024 * 1024,
                Math.Max(8L * 1024 * 1024, largestEntryBytes / 2));
            long importerBudget = 24L * 1024 * 1024 + centralDirectoryBytes + parserBytes;
            long requiredAvailable = MinimumImportMemoryHeadroom + importerBudget;
            Write(result, "MEMORY_CHECK", $"available={availableMemoryBytes} required={requiredAvailable} " +
                $"entries={entryCount} largestEntry={largestEntryBytes}");
            if (availableMemoryBytes >= requiredAvailable) return;

            throw new ImportMemoryException($"设备可用内存不足，导入需要至少约 {ToMegabytes(requiredAvailable)} MB " +
                $"可用内存，当前约 {ToMegabytes(availableMemoryBytes)} MB。已取消本次导入，避免软件闪退。");
        }

        private static bool ShouldReportByteProgress(long processedBytes, long totalBytes, long lastReportedBytes,
            long lastReportTicks, long nowTicks, bool force = false)
        {
            if (force || processedBytes >= totalBytes) return true;
            if (processedBytes - lastReportedBytes >= 4L * 1024 * 1024) return true;
            return lastReportTicks == 0 || nowTicks - lastReportTicks >= TimeSpan.TicksPerSecond / 3;
        }

        private static bool IsMemoryFailure(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
                if (current is ImportMemoryException || current is OutOfMemoryException) return true;
            return false;
        }

        private static void RollbackCommittedImports(IEnumerable<string> destinations, SongImportResult result)
        {
            if (destinations == null) return;
            var committed = new HashSet<string>(destinations, StringComparer.OrdinalIgnoreCase);
            foreach (string destination in committed)
            {
                try
                {
                    if (Directory.Exists(destination)) Directory.Delete(destination, true);
                }
                catch (Exception exception)
                {
                    Write(result, "MEMORY_ROLLBACK_FAIL", $"folder={destination} reason={exception.Message}");
                }
            }
            result.Details.RemoveAll(detail => detail != null && !string.IsNullOrWhiteSpace(detail.Destination) &&
                committed.Contains(detail.Destination));
            result.ImportedSongs.RemoveAll(song => song != null && !string.IsNullOrWhiteSpace(song.Root) &&
                committed.Contains(song.Root));
            result.Imported = Math.Max(0, result.Imported - committed.Count);
        }

        private static long ToMegabytes(long bytes) => Math.Max(1, bytes / (1024 * 1024));

        private static void CleanupPartialImports(string destinationRoot)
        {
            foreach (string directory in Directory.GetDirectories(destinationRoot, ".importing-*", SearchOption.TopDirectoryOnly))
            {
                try { Directory.Delete(directory, true); }
                catch { }
            }
        }

        private static void CleanupImportCache(string cacheRoot, string activeSource)
        {
            string active = null;
            try { active = Path.GetFullPath(activeSource); } catch { }
            foreach (string directory in Directory.GetDirectories(cacheRoot, "chunithm-import-*", SearchOption.TopDirectoryOnly))
            {
                try { Directory.Delete(directory, true); } catch { }
            }
            foreach (string file in Directory.GetFiles(cacheRoot, "song-import-*.zip", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (active == null || !string.Equals(Path.GetFullPath(file), active, StringComparison.OrdinalIgnoreCase))
                        File.Delete(file);
                }
                catch { }
            }
        }

        private static string ResolveLogDirectory(string persistentDataPath, string destinationRoot)
        {
            string parent = Directory.GetParent(Path.GetFullPath(destinationRoot))?.FullName;
            return string.IsNullOrWhiteSpace(parent)
                ? Path.Combine(persistentDataPath, "Logs")
                : Path.Combine(parent, "Logs");
        }

        private static void EnsureFreeSpace(string path, long requiredBytes)
        {
            if (requiredBytes <= 0) return;
            try
            {
                string full = Path.GetFullPath(path);
                string root = Path.GetPathRoot(full);
                if (string.IsNullOrWhiteSpace(root)) return;
                var drive = new DriveInfo(root);
                long requiredWithMargin = requiredBytes + Math.Max(64L * 1024 * 1024, requiredBytes / 10);
                if (drive.AvailableFreeSpace < requiredWithMargin)
                    throw new IOException($"存储空间不足，需要约 {requiredWithMargin / (1024 * 1024)} MB，可用 {drive.AvailableFreeSpace / (1024 * 1024)} MB");
            }
            catch (IOException) { throw; }
            catch { }
        }

        private static bool IsSymbolicLink(ZipArchiveEntry entry)
        {
            int unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            return unixMode == 0xA000;
        }

        private static string NormalizeId(string value) => (value ?? "").Trim().ToUpperInvariant();

        private static string UniqueDestination(string root, string name)
        {
            string candidate = Path.Combine(root, name);
            int suffix = 2;
            while (Directory.Exists(candidate) || File.Exists(candidate)) candidate = Path.Combine(root, name + "-" + suffix++);
            return candidate;
        }

        private static string SafeName(string value)
        {
            foreach (char character in Path.GetInvalidFileNameChars()) value = value.Replace(character, '_');
            value = value.Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(value) ? "song-" + Guid.NewGuid().ToString("N") : value;
        }

        private static void ReportSong(Action<SongImportProgress> progress, SongImportDetail detail, int processed, int total)
        {
            float fraction = .48f + .47f * processed / Math.Max(1, total);
            Report(progress, fraction, detail.Status, detail.SongName,
                $"{detail.Status}：{detail.Message}", processed, total, detail);
        }

        private static void Report(Action<SongImportProgress> callback, float fraction, string stage,
            string currentItem, string message, int processed = 0, int total = 0, SongImportDetail detail = null,
            long processedBytes = 0, long totalBytes = 0)
        {
            if (callback == null) return;
            try
            {
                callback(new SongImportProgress {
                    Fraction = Math.Max(0, Math.Min(1, fraction)), Stage = stage, CurrentItem = currentItem,
                    Message = message, ProcessedItems = processed, TotalItems = total,
                    ProcessedBytes = processedBytes, TotalBytes = totalBytes, Detail = detail
                });
            }
            catch { }
        }

        private static void Write(SongImportResult result, string tag, string message)
        {
            File.AppendAllText(result.LogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{tag}] {message}{Environment.NewLine}",
                new UTF8Encoding(false));
        }

        private static void TrimLogs(string directory)
        {
            foreach (FileInfo file in new DirectoryInfo(directory).GetFiles("import-*.log")
                         .OrderByDescending(value => value.LastWriteTimeUtc).Skip(5))
                try { file.Delete(); } catch { }
        }
    }
}
