using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using ChunithmMobile.Platform;
using UnityEngine;

namespace ChunithmMobile.Runtime
{
    [Serializable]
    public sealed class SongChartEntry
    {
        public string Difficulty;
        public string Level;
        public string Designer;
        public string Path;
    }

    [Serializable]
    public sealed class SongEntry
    {
        public string Root;
        public string Id;
        public string Name;
        public string Artist;
        public string Category;
        public string JacketPath;
        public string Bpm;
        public string DurationText;
        public List<SongChartEntry> Charts = new List<SongChartEntry>();
    }

    public static class SongCatalog
    {
        private const int IndexFormat = 6;
        public static bool IsWorldsEnd(string difficulty) =>
            string.Equals(difficulty?.Replace("'", "").Replace(" ", ""), "WORLDSEND", StringComparison.OrdinalIgnoreCase);
        private const string IndexFileName = "song-catalog-index.json";
        private const string MetadataFileName = ".chunithm-mobile-metadata.json";

        [Serializable]
        private sealed class CatalogIndex
        {
            public int Format;
            public string Signature;
            public SongEntry[] Songs;
        }

        [Serializable]
        private sealed class MetadataOverride
        {
            public string Name;
            public string Artist;
            public string Category;
        }

        public static bool LastLoadUsedCache { get; private set; }

        public static IReadOnlyList<SongEntry> Load(bool forceRebuild = false)
        {
            IReadOnlyList<string> roots = PlatformStoragePaths.Current.GetSongRoots();
            string signature = BuildSignature(roots);
            if (!forceRebuild && TryReadIndex(signature, out IReadOnlyList<SongEntry> cached))
            {
                LastLoadUsedCache = true;
                Debug.Log($"Song catalog unchanged; reused {cached.Count} indexed songs.");
                return cached;
            }

            // 0.2.1 included the application version in the signature. Accept
            // that legacy signature once so an app upgrade does not rescan a
            // large library whose files have not changed, then rewrite it with
            // the stable file-based signature above.
            if (!forceRebuild && TryReadIndex(BuildSignature(roots, "0.2.1"), out IReadOnlyList<SongEntry> legacyCached))
            {
                LastLoadUsedCache = true;
                WriteIndex(signature, legacyCached);
                Debug.Log($"Song catalog unchanged; migrated {legacyCached.Count} indexed songs.");
                return legacyCached;
            }

            LastLoadUsedCache = false;
            IReadOnlyList<SongEntry> songs = ScanRoots(roots);
            // Save the post-scan signature so a file change during scanning can
            // never make a stale index look current on the next launch.
            WriteIndex(BuildSignature(roots), songs);
            Debug.Log($"Song catalog index rebuilt with {songs.Count} songs.");
            return songs;
        }

        public static IReadOnlyList<SongEntry> Scan()
        {
            LastLoadUsedCache = false;
            return ScanRoots(PlatformStoragePaths.Current.GetSongRoots());
        }

        public static int DeleteSongDirectories(IEnumerable<string> songRoots)
        {
            string[] allowedRoots = PlatformStoragePaths.Current.GetSongRoots()
                .Select(Path.GetFullPath)
                .ToArray();
            int deleted = 0;
            foreach (string songRoot in songRoots.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string fullPath = Path.GetFullPath(songRoot);
                bool allowed = allowedRoots.Any(root =>
                    fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase));
                if (!allowed)
                {
                    Debug.LogError("Refused to delete a directory outside the song library: " + fullPath);
                    continue;
                }

                try
                {
                    if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true);
                    deleted++;
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Unable to delete song directory {fullPath}: {exception.Message}");
                }
            }
            return deleted;
        }

        public static int UpdateMetadata(IEnumerable<string> songRoots, string name, string artist, string category)
        {
            int updated = 0;
            foreach (string root in songRoots.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!IsInsideSongLibrary(root) || !Directory.Exists(root)) continue;
                string path = Path.Combine(root, MetadataFileName);
                MetadataOverride metadata = ReadMetadata(root) ?? new MetadataOverride();
                if (!string.IsNullOrWhiteSpace(name)) metadata.Name = name.Trim();
                if (!string.IsNullOrWhiteSpace(artist)) metadata.Artist = artist.Trim();
                if (!string.IsNullOrWhiteSpace(category)) metadata.Category = category.Trim();
                File.WriteAllText(path, JsonUtility.ToJson(metadata), new UTF8Encoding(false));
                updated++;
            }
            return updated;
        }

        public static SongEntry ReadSongDirectory(string root) => ReadSong(root);

        /// <summary>
        /// Merges songs which were already parsed and validated by the importer.
        /// The full file signature is still written, so normal launch-time data
        /// change detection stays authoritative without reparsing every song.
        /// </summary>
        public static IReadOnlyList<SongEntry> ApplyImportedSongs(IReadOnlyList<SongEntry> current,
            IEnumerable<SongEntry> imported)
        {
            SongEntry[] additions = (imported ?? Enumerable.Empty<SongEntry>())
                .Where(song => song != null && !string.IsNullOrWhiteSpace(song.Root) &&
                               song.Charts != null && song.Charts.Count > 0)
                .ToArray();
            if (additions.Length == 0)
                return (current ?? Array.Empty<SongEntry>())
                    .OrderBy(song => song.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

            var replacedRoots = new HashSet<string>(additions.Select(song => Path.GetFullPath(song.Root)),
                StringComparer.OrdinalIgnoreCase);
            var songs = (current ?? Array.Empty<SongEntry>())
                .Where(song => song != null && !string.IsNullOrWhiteSpace(song.Root) &&
                               !replacedRoots.Contains(Path.GetFullPath(song.Root)))
                .Concat(additions)
                .OrderBy(song => song.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            IReadOnlyList<string> roots = PlatformStoragePaths.Current.GetSongRoots();
            WriteIndex(BuildSignature(roots), songs);
            LastLoadUsedCache = false;
            Debug.Log($"Song catalog incrementally updated with {additions.Length} imported songs; total={songs.Length}.");
            return songs;
        }

        private static bool IsInsideSongLibrary(string path)
        {
            string fullPath = Path.GetFullPath(path);
            return PlatformStoragePaths.Current.GetSongRoots().Select(Path.GetFullPath).Any(root =>
                fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<SongEntry> ScanRoots(IReadOnlyList<string> roots)
        {
            var songs = new List<SongEntry>();
            foreach (string library in roots)
            {
                if (!Directory.Exists(library)) continue;
                string[] directories;
                try { directories = Directory.GetDirectories(library); }
                catch (Exception exception)
                {
                    Debug.LogError($"Song catalog could not read {library}: {exception.Message}");
                    continue;
                }
                foreach (string root in directories)
                {
                    try
                    {
                        SongEntry song = ReadSong(root);
                        if (song != null && song.Charts.Count > 0) songs.Add(song);
                    }
                    catch (Exception exception)
                    {
                        UnityEngine.Debug.LogError($"Song catalog skipped {root}: {exception.Message}");
                    }
                }
            }
            return songs.OrderBy(song => song.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }

        private static string BuildSignature(IReadOnlyList<string> roots, string legacyAppVersion = null)
        {
            using SHA256 sha = SHA256.Create();
            void Add(string value)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
                sha.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                byte[] separator = { 0 };
                sha.TransformBlock(separator, 0, separator.Length, separator, 0);
            }

            Add("format=" + IndexFormat);
            if (!string.IsNullOrWhiteSpace(legacyAppVersion)) Add("app=" + legacyAppVersion);
            foreach (string configuredRoot in roots)
            {
                string root;
                try { root = Path.GetFullPath(configuredRoot); }
                catch (Exception exception) { Add("invalid-root=" + configuredRoot + ":" + exception.GetType().Name); continue; }
                Add("root=" + root.Replace('\\', '/'));
                if (!Directory.Exists(root)) { Add("missing"); continue; }

                try
                {
                    foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                                 .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    {
                        var info = new DirectoryInfo(directory);
                        Add($"d={info.Name};utc={info.LastWriteTimeUtc.Ticks}");
                        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                                     .Where(IsIndexedSongFile)
                                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                        {
                            var fileInfo = new FileInfo(file);
                            string relative = file.Substring(directory.Length)
                                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                .Replace('\\', '/');
                            Add($"f={relative};len={fileInfo.Length};utc={fileInfo.LastWriteTimeUtc.Ticks}");
                        }
                    }
                }
                catch (Exception exception)
                {
                    // An unreadable tree must not accidentally match an older,
                    // complete signature.
                    Add("scan-error=" + exception.GetType().FullName + ":" + exception.Message);
                }
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return string.Concat(sha.Hash.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static bool IsIndexedSongFile(string path)
        {
            string name = Path.GetFileName(path);
            if (name.Equals("Music.xml", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(MetadataFileName, StringComparison.OrdinalIgnoreCase)) return true;
            string extension = Path.GetExtension(path);
            return extension.Equals(".c2s", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".dds", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadIndex(string signature, out IReadOnlyList<SongEntry> songs)
        {
            songs = null;
            try
            {
                string path = Path.Combine(PlatformServices.Current.PersistentDataPath, IndexFileName);
                if (!File.Exists(path)) return false;
                CatalogIndex index = JsonUtility.FromJson<CatalogIndex>(File.ReadAllText(path, Encoding.UTF8));
                if (index == null || index.Format != IndexFormat || index.Signature != signature || index.Songs == null)
                    return false;
                if (index.Songs.Any(song => song == null || song.Charts == null)) return false;
                songs = index.Songs.OrderBy(song => song.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Song catalog index is invalid and will be rebuilt: " + exception.Message);
                return false;
            }
        }

        private static void WriteIndex(string signature, IReadOnlyList<SongEntry> songs)
        {
            string path = Path.Combine(PlatformServices.Current.PersistentDataPath, IndexFileName);
            string temporary = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string json = JsonUtility.ToJson(new CatalogIndex {
                    Format = IndexFormat,
                    Signature = signature,
                    Songs = songs.ToArray()
                });
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
            }
            catch (Exception exception)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                Debug.LogWarning("Song catalog index could not be saved: " + exception.Message);
            }
        }

        private static SongEntry ReadSong(string root)
        {
            string xmlPath = FindFile(root, "Music.xml");
            if (xmlPath == null) return ReadFallback(root);

            var document = new XmlDocument { XmlResolver = null };
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(xmlPath, settings)) document.Load(reader);

            var song = new SongEntry {
                Root = root,
                Id = Text(document, "/MusicData/name/id") ?? Path.GetFileName(root),
                Name = Text(document, "/MusicData/name/str") ?? Path.GetFileName(root),
                Artist = Text(document, "/MusicData/artistName/str") ?? "",
                Category = Text(document, "/MusicData/genreNames/list/StringID/str") ?? "",
                JacketPath = FindJacket(root)
            };
            string chartDirectory = Path.GetDirectoryName(xmlPath);
            foreach (XmlNode node in document.SelectNodes("/MusicData/fumens/MusicFumenData"))
            {
                if (!string.Equals(node.SelectSingleNode("enable")?.InnerText, "true", StringComparison.OrdinalIgnoreCase))
                    continue;
                string file = node.SelectSingleNode("file/path")?.InnerText?.Trim();
                if (string.IsNullOrWhiteSpace(file)) continue;
                string normalized = file.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                string path = Path.Combine(chartDirectory, normalized);
                if (!File.Exists(path))
                {
                    string fileName = file.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                    path = string.IsNullOrWhiteSpace(fileName) ? null : FindFile(root, fileName);
                }
                if (!File.Exists(path)) continue;
                string difficulty = node.SelectSingleNode("type/str")?.InnerText?.Trim() ?? "CHART";
                string level = node.SelectSingleNode("level")?.InnerText?.Trim() ?? "?";
                // WORLD'S END stores its actual text badge (狂/光/避/...) at
                // the song level. The fumen's numeric level is only a
                // placeholder and must never be shown to the player.
                bool worldEnd = IsWorldsEnd(difficulty) ||
                    string.Equals(node.SelectSingleNode("type/data")?.InnerText?.Trim(), "WORLD'S END", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Text(document, "/MusicData/stageName/str"), "WORLD'S END", StringComparison.OrdinalIgnoreCase);
                if (worldEnd) {
                    string worldEndLevel = Text(document, "/MusicData/worldsEndTagName/str");
                    if (!string.IsNullOrWhiteSpace(worldEndLevel)) level = worldEndLevel.Trim();
                }
                string decimalLevel = node.SelectSingleNode("levelDecimal")?.InnerText ?? "0";
                // Music.xml stores the fractional part in hundredths (50 =
                // .5). Fractions .50-.99 belong to the plus bucket.
                if (!worldEnd && int.TryParse(decimalLevel, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out int hundredths) && hundredths >= 50 && hundredths <= 99) level += "+";
                song.Charts.Add(new SongChartEntry {
                    Difficulty = difficulty.ToUpperInvariant(),
                    Level = level,
                    Designer = node.SelectSingleNode("notesDesigner/str")?.InnerText?.Trim() ??
                               node.SelectSingleNode("notesDesigner")?.InnerText?.Trim() ?? "",
                    Path = path
                });
            }
            song.Bpm = song.Charts.Select(chart => ReadBpm(chart.Path)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "—";
            song.DurationText = ReadDuration(root);
            ApplyMetadata(root, song);
            if (IsMultiplayerSongRoot(root)) song.Category = "联机";
            return song;
        }

        private static SongEntry ReadFallback(string root)
        {
            string[] charts = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetExtension(path).Equals(".c2s", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (charts.Length == 0) return null;
            Array.Sort(charts, StringComparer.OrdinalIgnoreCase);
            var song = new SongEntry { Root = root, Id = Path.GetFileName(root), Name = Path.GetFileName(root), JacketPath = FindJacket(root) };
            string[] names = { "BASIC", "ADVANCED", "EXPERT", "MASTER", "ULTIMA" };
            for (int i = 0; i < charts.Length; i++) song.Charts.Add(new SongChartEntry {
                Difficulty = i < names.Length ? names[i] : $"CHART {i + 1}", Level = "?", Path = charts[i]
            });
            song.Bpm = charts.Select(ReadBpm).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "—";
            song.DurationText = ReadDuration(root);
            ApplyMetadata(root, song);
            if (IsMultiplayerSongRoot(root)) song.Category = "联机";
            return song;
        }

        private static bool IsMultiplayerSongRoot(string songRoot)
        {
            string fullPath = Path.GetFullPath(songRoot);
            return PlatformStoragePaths.GetMultiplayerSongRoots().Select(Path.GetFullPath).Any(root =>
                fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }

        private static void ApplyMetadata(string root, SongEntry song)
        {
            MetadataOverride metadata = ReadMetadata(root);
            if (metadata == null) return;
            if (!string.IsNullOrWhiteSpace(metadata.Name)) song.Name = metadata.Name.Trim();
            if (!string.IsNullOrWhiteSpace(metadata.Artist)) song.Artist = metadata.Artist.Trim();
            if (!string.IsNullOrWhiteSpace(metadata.Category)) song.Category = metadata.Category.Trim();
        }

        private static MetadataOverride ReadMetadata(string root)
        {
            try
            {
                string path = Path.Combine(root, MetadataFileName);
                return File.Exists(path) ? JsonUtility.FromJson<MetadataOverride>(File.ReadAllText(path, Encoding.UTF8)) : null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Song metadata could not be read from {root}: {exception.Message}");
                return null;
            }
        }

        private static string FindFile(string root, string name) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase));

        private static string ReadDuration(string root)
        {
            string mp3 = SongAudioFiles.Resolve(root).GameplayMp3;
            if (string.IsNullOrWhiteSpace(mp3) || !Mp3DurationReader.TryReadSeconds(mp3, out double seconds)) return "";
            int rounded = Math.Max(0, (int)Math.Round(seconds));
            return (rounded / 60).ToString("00", CultureInfo.InvariantCulture) + ":" +
                   (rounded % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        private static string Text(XmlDocument document, string xpath) => document.SelectSingleNode(xpath)?.InnerText?.Trim();

        private static string FindJacket(string root)
        {
            string[] extensions = { ".png", ".jpg", ".jpeg", ".dds" };
            return Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(path => Path.GetFileName(path).IndexOf("jacket", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenBy(path => path.Length)
                .FirstOrDefault();
        }

        private static string ReadBpm(string chartPath)
        {
            if (string.IsNullOrWhiteSpace(chartPath) || !File.Exists(chartPath)) return null;
            foreach (string line in File.ReadLines(chartPath).Take(64))
            {
                if (!line.TrimStart().StartsWith("BPM_DEF", StringComparison.OrdinalIgnoreCase)) continue;
                string[] parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string part in parts.Skip(1))
                    if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm) && bpm > 0)
                        return Math.Abs(bpm - Math.Round(bpm)) < .001 ? Math.Round(bpm).ToString(CultureInfo.InvariantCulture) : bpm.ToString("0.###", CultureInfo.InvariantCulture);
            }
            return null;
        }
    }
}
