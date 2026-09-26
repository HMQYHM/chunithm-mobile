using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChunithmMobile.Runtime
{
    // 游玩皮肤资源管理器：加载清单、纹理、Mesh、字段材质、判定效果和 UI 资源，并提供回退。
    // 资源缓存跨音符复用；切换/删除皮肤时必须清除相关缓存，避免引用已销毁纹理。
    [Serializable]
    public sealed class GameplaySkinInfo
    {
        public string Id;
        public string Name;
        public string Author;
        public string Version;
        public string RootPath;
        public bool BuiltIn;
    }

    public sealed class GameplaySkinResourceSummary
    {
        public int FileCount;
        public long TotalBytes;
        public int NoteCount;
        public int TrackCount;
        public int EffectCount;
        public int MeshCount;
        public int AudioCount;
        public int UiCount;
        public string PreviewPath;
        public readonly List<string> Files = new List<string>();
        public readonly List<string> Missing = new List<string>();
        public readonly List<string> PreviewPaths = new List<string>();
    }

    public sealed class GameplaySkinStyle
    {
        public Color ComboColor = Color.white;
        public Color ScoreColor = Color.white;
        public Color RankColor = new Color(.20f, .94f, 1f, 1f);
        public Color JudgementColor = Color.white;
        public Color TrackColor = new Color(.008f, .018f, .030f, .78f);
        public Color LaneColor = new Color(.035f, .34f, .48f, 1f);
        public Color GuideColor = new Color(.018f, .090f, .125f, 1f);
        public Color EdgeColor = new Color(.05f, .55f, .72f, 1f);
        public Color JudgementLineColor = new Color(1f, .68f, .06f, 1f);
        public Color HoldLineColor = new Color(1f, .94f, .44f, .98f);
        public Color SlideLineColor = new Color(.68f, .96f, 1f, .98f);
        public Color AirLineColor = new Color(.12f, 1f, .34f, .94f);
        public Color JusticeCriticalColor = new Color(.20f, .94f, 1f, 1f);
        public Color JusticeColor = new Color(.42f, 1f, .48f, 1f);
        public Color AttackColor = new Color(1f, .66f, .10f, 1f);
        public Color MissColor = new Color(1f, .16f, .26f, 1f);
        public Color AirUpColor = new Color(.18f, 1f, .48f, 1f);
        public Color AirDownColor = new Color(1f, .24f, .68f, 1f);
        public Color AirHoldColor = new Color(.18f, 1f, .62f, 1f);
        public Color AirActionColor = new Color(.76f, .34f, 1f, 1f);
        public int ComboSize = 58;
        public int ScoreSize = 42;
        public int JudgementSize = 16;
        public bool ScoreWarningEnabled;
        public int ScoreWarningThreshold;

        public static GameplaySkinStyle Original()
        {
            return new GameplaySkinStyle();
        }

        public Color RankColorFor(string rank)
        {
            return !string.IsNullOrWhiteSpace(rank) &&
                   (rank.StartsWith("SS", StringComparison.OrdinalIgnoreCase) || rank == "S+")
                ? RankColor : Color.white;
        }
    }

    /// <summary>
    /// Resolves gameplay skin assets without letting a skin package own game
    /// logic. Built-in resources remain the final fallback for every asset.
    /// </summary>
    public static class GameplaySkinManager
    {
        public const string BuiltinId = "current_original";
        private const string SkinFolderName = "GameplaySkins";
        private const int MaxPackageEntries = 2000;
        private const long MaxPackageBytes = 256L * 1024L * 1024L;
        private const long MaxEntryBytes = 64L * 1024L * 1024L;

        private static readonly string[] NoteKinds = {
            "tap", "extap", "hold", "slide", "flick", "dmg", "air_up", "air_down",
            "ahtap", "ahtapend", "ahact", "ahsdw", "air"
        };
        private static readonly string[] MeshKinds = {
            "tap", "extap", "hold", "holdend", "slide", "slideend", "flick", "dmg",
            "airup", "airdw", "airuptilt", "airdwtilt", "ahtap", "ahtapend", "ahact", "ahsdw", "ahroof"
        };
        private static readonly string[] RequiredTrackFiles = {
            "track/runtime/field_blue_16.png", "track/runtime/barline_blue.png", "track/runtime/goal_line.png",
            "track/field/hold_joint.png", "track/field/slide_joint.png", "track/field/air_hold_joint.png",
            "track/field/air_action_bar.png", "track/field/air_action_line.png", "track/field/heaven_roof.png",
            "track/field/heaven_roof_pink.png", "track/field/heaven_wall.png", "track/field/heaven_shadow.png"
        };
        private static readonly string[] RequiredEffectFiles = {
            "effects/air/air_action_up.png", "effects/air/air_action_down.png", "effects/air/airhold_float.png",
            "effects/air/airhold_glow.png", "effects/air/airhold_ring.png", "effects/air/airhold_success.png",
            "effects/air/bomb_belt.png", "effects/air/bomb_diamond.png", "effects/air/bomb_diamonds.png",
            "effects/air/bomb_fin.png", "effects/air/bomb_flare_orange.png", "effects/air/bomb_flare_yellow.png",
            "effects/air/bomb_glow.png", "effects/air/key_beam.png"
        };
        private static readonly string[] RequiredAudioFiles = {
            "audio/ui_entry.mp3", "audio/ui_results.mp3", "audio/delay_ready.wav", "audio/se_touch_answer.wav"
        };

        private static readonly List<GameplaySkinInfo> installed = new List<GameplaySkinInfo>();
        private static readonly Dictionary<string, Texture2D> externalTextures =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Queue<string> previewTextureOrder = new Queue<string>();
        private static readonly HashSet<Texture2D> externalNoteTextures = new HashSet<Texture2D>();
        private static readonly Dictionary<string, Mesh> externalMeshes =
            new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
        private static GameplaySkinInfo active;
        private static GameplaySkinStyle style = GameplaySkinStyle.Original();
        private static bool initialized;

        public static IReadOnlyList<GameplaySkinInfo> Installed => installed;
        public static GameplaySkinInfo Active => active ?? BuiltinInfo();
        public static GameplaySkinStyle Style => style ?? GameplaySkinStyle.Original();
        public static bool ScoreWarningEnabled => Style.ScoreWarningEnabled;
        public static int ScoreWarningThreshold => Style.ScoreWarningThreshold;

        public static void Initialize(string requestedId)
        {
            Refresh();
            SetActive(string.IsNullOrWhiteSpace(requestedId) ? BuiltinId : requestedId);
            initialized = true;
        }

        public static void Refresh()
        {
            installed.Clear();
            installed.Add(BuiltinInfo());
            string root = SkinRoot;
            try
            {
                Directory.CreateDirectory(root);
                foreach (string directory in Directory.GetDirectories(root))
                {
                    string manifestPath = Path.Combine(directory, "skin.json");
                    if (!File.Exists(manifestPath)) continue;
                    if (TryReadManifest(manifestPath, out SkinManifest manifest) &&
                        IsValidId(manifest.id) && manifest.gameplaySkin)
                    {
                        if (installed.Any(item => string.Equals(item.Id, manifest.id,
                                StringComparison.OrdinalIgnoreCase))) continue;
                        installed.Add(new GameplaySkinInfo {
                            Id = manifest.id,
                            Name = string.IsNullOrWhiteSpace(manifest.name) ? manifest.id : manifest.name,
                            Author = manifest.author ?? "",
                            Version = manifest.version ?? "",
                            RootPath = directory,
                            BuiltIn = false
                        });
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Gameplay skin scan failed: " + exception.Message);
            }

            if (initialized && (active == null || !installed.Any(item => item.Id == active.Id)))
                SetActive(BuiltinId);
        }

        public static bool SetActive(string id)
        {
            GameplaySkinInfo next = installed.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) ??
                installed.FirstOrDefault(item => item.BuiltIn);
            if (next == null) return false;

            ClearTextureCache();
            active = next;
            style = next.BuiltIn ? GameplaySkinStyle.Original() : ReadStyle(next);
            return true;
        }

        public static Texture2D LoadNoteTexture(string key)
        {
            Texture2D texture = LoadExternalTexture("notes/" + key + ".png");
            if (texture == null) return Resources.Load<Texture2D>("OriginalNotes/" + key);
            externalNoteTextures.Add(texture);
            return texture;
        }

        public static bool IsExternalNoteTexture(Texture texture) =>
            texture is Texture2D image && externalNoteTextures.Contains(image);

        public static Texture2D LoadJudgementTexture(Chart.Judgement grade)
        {
            string key = JudgementTextureKey(grade);
            return LoadTexture("ui/judgements/" + key + ".png", "OriginalUI/Judgements/" + key);
        }

        public static string JudgementTextureKey(Chart.Judgement grade)
        {
            switch (grade) {
                case Chart.Judgement.JusticeCritical: return "justice_critical";
                case Chart.Judgement.Justice: return "justice";
                case Chart.Judgement.Attack: return "attack";
                default: return "miss";
            }
        }

        public static Texture2D LoadFieldTexture(string key)
        {
            string normalized = key.Replace('\\', '/').TrimStart('/');
            Texture2D texture = LoadExternalTexture("track/" + normalized + ".png");
            if (texture != null) return texture;
            texture = LoadExternalTexture("notes/" + normalized + ".png");
            if (texture != null) return texture;
            return normalized.StartsWith("field/", StringComparison.OrdinalIgnoreCase)
                ? Resources.Load<Texture2D>("OriginalNotes/" + normalized)
                : Resources.Load<Texture2D>("OriginalField/" + normalized);
        }

        public static Texture2D LoadTrackTexture(string key)
        {
            string normalized = key.Replace('\\', '/').TrimStart('/');
            Texture2D texture = LoadExternalTexture("track/" + normalized + ".png");
            if (texture != null) return texture;
            texture = LoadExternalTexture("track/runtime/" + normalized + ".png");
            if (texture != null) return texture;
            return Resources.Load<Texture2D>("OriginalField/" + normalized);
        }

        public static Texture2D LoadEffectTexture(string relativePath)
        {
            string normalized = relativePath.Replace('\\', '/').TrimStart('/');
            Texture2D texture = LoadExternalTexture("effects/" + normalized + ".png");
            if (texture != null) return texture;
            // Android resource paths are case-sensitive. The built-in effect
            // folder uses the original Air capitalization, while packages use
            // the portable lower-case effects/air layout.
            if (normalized.StartsWith("air/", StringComparison.OrdinalIgnoreCase))
                return Resources.Load<Texture2D>("OriginalEffects/Air/" + normalized.Substring(4));
            return Resources.Load<Texture2D>("OriginalEffects/" + normalized);
        }

        public static GameplaySkinResourceSummary GetResourceSummary(GameplaySkinInfo info)
        {
            var summary = new GameplaySkinResourceSummary();
            if (info == null || info.BuiltIn || string.IsNullOrWhiteSpace(info.RootPath))
            {
                summary.PreviewPath = null;
                summary.Missing.Add("外部资源包：内置原版皮肤不需要导入文件");
                return summary;
            }

            try
            {
                foreach (string file in Directory.GetFiles(info.RootPath, "*", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(info.RootPath.Length).TrimStart(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                    if (relative.Equals("skin.json", StringComparison.OrdinalIgnoreCase) ||
                        relative.Equals("README.txt", StringComparison.OrdinalIgnoreCase) ||
                        relative.Equals("checksums.sha256", StringComparison.OrdinalIgnoreCase)) continue;
                    summary.Files.Add(relative);
                    summary.TotalBytes += new FileInfo(file).Length;
                    if (relative.StartsWith("notes/", StringComparison.OrdinalIgnoreCase)) summary.NoteCount++;
                    else if (relative.StartsWith("track/", StringComparison.OrdinalIgnoreCase)) summary.TrackCount++;
                    else if (relative.StartsWith("effects/", StringComparison.OrdinalIgnoreCase)) summary.EffectCount++;
                    else if (relative.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase)) summary.MeshCount++;
                    else if (relative.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) summary.AudioCount++;
                    else if (relative.StartsWith("ui/", StringComparison.OrdinalIgnoreCase)) summary.UiCount++;
                }
                summary.Files.Sort(StringComparer.OrdinalIgnoreCase);
                summary.FileCount = summary.Files.Count;
                foreach (string required in RequiredResources())
                    if (!File.Exists(Path.Combine(info.RootPath, required.Replace('/', Path.DirectorySeparatorChar))))
                        summary.Missing.Add(required);
                string preview = Path.Combine(info.RootPath, "preview.png");
                if (File.Exists(preview)) summary.PreviewPath = "preview.png";
                string[] previewPrefixes = { "notes/", "track/runtime/", "track/field/", "effects/air/" };
                foreach (string prefix in previewPrefixes)
                {
                    string file = summary.Files.FirstOrDefault(path =>
                        path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(file) &&
                        !summary.PreviewPaths.Contains(file, StringComparer.OrdinalIgnoreCase))
                        summary.PreviewPaths.Add(file);
                }
                foreach (string file in summary.Files.Where(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
                {
                    if (file.Equals(summary.PreviewPath, StringComparison.OrdinalIgnoreCase) ||
                        summary.PreviewPaths.Contains(file, StringComparer.OrdinalIgnoreCase)) continue;
                    summary.PreviewPaths.Add(file);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Gameplay skin detail scan failed: " + exception.Message);
                summary.Missing.Add("无法读取皮肤包资源目录");
            }
            return summary;
        }

        public static Texture2D LoadSkinImage(GameplaySkinInfo info, string relativePath)
        {
            if (info == null || info.BuiltIn || string.IsNullOrWhiteSpace(info.RootPath) ||
                string.IsNullOrWhiteSpace(relativePath)) return null;
            string normalized = relativePath.Replace('\\', '/').TrimStart('/');
            string cacheKey = "inspect|" + info.Id + "|" + normalized;
            if (externalTextures.TryGetValue(cacheKey, out Texture2D cached)) return cached;
            string path;
            try { path = SafePath(info.RootPath, normalized); }
            catch { return null; }
            if (!File.Exists(path)) return null;
            try
            {
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) {
                    name = "Gameplay skin preview " + Path.GetFileName(path),
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                if (!texture.LoadImage(File.ReadAllBytes(path), false)) {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }
                if (texture.width <= 0 || texture.height <= 0 || texture.width > 4096 || texture.height > 4096 ||
                    (long)texture.width * texture.height > 16L * 1024L * 1024L) {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }
                texture.Apply(false, false);
                externalTextures[cacheKey] = texture;
                previewTextureOrder.Enqueue(cacheKey);
                while (previewTextureOrder.Count > 24) {
                    string oldest = previewTextureOrder.Dequeue();
                    if (!externalTextures.TryGetValue(oldest, out Texture2D oldTexture)) continue;
                    externalTextures.Remove(oldest);
                    if (oldTexture != null) UnityEngine.Object.Destroy(oldTexture);
                }
                return texture;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Gameplay skin preview failed: " + normalized + " | " + exception.Message);
                return null;
            }
        }

        public static bool DeleteSkin(GameplaySkinInfo info)
        {
            if (info == null || info.BuiltIn || string.IsNullOrWhiteSpace(info.RootPath)) return false;
            string root = Path.GetFullPath(SkinRoot).TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(info.RootPath).TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(info.RootPath)) return false;
            if (active != null && string.Equals(active.Id, info.Id, StringComparison.OrdinalIgnoreCase)) {
                SetActive(BuiltinId);
            }
            Directory.Delete(info.RootPath, true);
            Refresh();
            return true;
        }

        public static string GetAudioPath(string fileName)
        {
            string normalized = (fileName ?? "").Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains("..")) return null;
            if (Active.BuiltIn || string.IsNullOrWhiteSpace(Active.RootPath)) return null;
            try {
                string relative = "audio/" + normalized;
                string path = SafePath(Active.RootPath, relative);
                if (IsReadableAudioFile(path)) return path;

                // Android storage is case-sensitive while desktop extraction is
                // often case-insensitive. Resolve each package path component
                // without weakening the traversal check, so a package written
                // as Audio/UI_ENTRY.MP3 does not randomly lose its music after
                // being copied to an Android device.
                string current = Active.RootPath;
                foreach (string component in relative.Split('/')) {
                    if (!Directory.Exists(current)) return null;
                    string match = Directory.GetFileSystemEntries(current)
                        .FirstOrDefault(item => string.Equals(Path.GetFileName(item), component,
                            StringComparison.OrdinalIgnoreCase));
                    if (string.IsNullOrWhiteSpace(match)) return null;
                    current = match;
                }
                return IsReadableAudioFile(current) ? current : null;
            }
            catch (Exception exception) {
                Debug.LogWarning("Gameplay skin audio path failed: " + normalized + " | " + exception.Message);
                return null;
            }
        }

        private static bool IsReadableAudioFile(string path) => File.Exists(path) &&
            new FileInfo(path).Length > 0;

        /// <summary>
        /// Loads a skin-provided OBJ mesh. Unity cannot import arbitrary files
        /// from persistentDataPath as assets, so the small note meshes use a
        /// deliberately limited runtime OBJ reader (v/vt/vn and triangulated
        /// faces). Unsupported or malformed files simply fall back to the
        /// built-in model in the caller.
        /// </summary>
        public static Mesh LoadMesh(string meshName)
        {
            string normalized = (meshName ?? "").Replace('\\', '/').Trim('/');
            if (normalized.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - 4);
            if (string.IsNullOrWhiteSpace(normalized)) return null;
            Mesh cached;
            if (externalMeshes.TryGetValue(Active.Id + "|" + normalized, out cached)) return cached;
            if (Active.BuiltIn || string.IsNullOrWhiteSpace(Active.RootPath)) return null;
            string path;
            try { path = SafePath(Active.RootPath, "meshes/" + normalized + ".obj"); }
            catch { return null; }
            if (!File.Exists(path)) return null;
            try
            {
                FileInfo file = new FileInfo(path);
                if (file.Length <= 0 || file.Length > MaxEntryBytes) return null;
                Mesh mesh = ParseObj(File.ReadAllLines(path), normalized);
                if (mesh != null) externalMeshes[Active.Id + "|" + normalized] = mesh;
                return mesh;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Gameplay skin mesh failed: " + normalized + " | " + exception.Message);
                return null;
            }
        }

        public static void ClearTextureCache()
        {
            externalNoteTextures.Clear();
            foreach (Texture2D texture in externalTextures.Values)
                if (texture != null) UnityEngine.Object.Destroy(texture);
            externalTextures.Clear();
            previewTextureOrder.Clear();
            foreach (Mesh mesh in externalMeshes.Values)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            externalMeshes.Clear();
        }

        private static Mesh ParseObj(string[] lines, string meshName)
        {
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var outputPositions = new List<Vector3>();
            var outputUvs = new List<Vector2>();
            var outputNormals = new List<Vector3>();
            var triangles = new List<int>();
            var vertexMap = new Dictionary<string, int>(StringComparer.Ordinal);
            bool hasNormals = true;

            foreach (string raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string line = raw.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] fields = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2) continue;
                switch (fields[0])
                {
                    case "v":
                        if (fields.Length < 4) continue;
                        positions.Add(new Vector3(ParseFloat(fields[1]), ParseFloat(fields[2]), ParseFloat(fields[3])));
                        break;
                    case "vt":
                        if (fields.Length < 3) continue;
                        uvs.Add(new Vector2(ParseFloat(fields[1]), ParseFloat(fields[2])));
                        break;
                    case "vn":
                        if (fields.Length < 4) continue;
                        normals.Add(new Vector3(ParseFloat(fields[1]), ParseFloat(fields[2]), ParseFloat(fields[3])));
                        break;
                    case "f":
                        if (fields.Length < 4) continue;
                        int first = AddObjVertex(fields[1], positions, uvs, normals, vertexMap,
                            outputPositions, outputUvs, outputNormals, ref hasNormals);
                        for (int i = 2; i + 1 < fields.Length; i++)
                        {
                            int second = AddObjVertex(fields[i], positions, uvs, normals, vertexMap,
                                outputPositions, outputUvs, outputNormals, ref hasNormals);
                            int third = AddObjVertex(fields[i + 1], positions, uvs, normals, vertexMap,
                                outputPositions, outputUvs, outputNormals, ref hasNormals);
                            if (first < 0 || second < 0 || third < 0) continue;
                            triangles.Add(first); triangles.Add(second); triangles.Add(third);
                        }
                        break;
                }
            }

            if (outputPositions.Count == 0 || triangles.Count == 0) return null;
            var mesh = new Mesh { name = "Gameplay skin " + meshName };
            if (outputPositions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(outputPositions);
            if (outputUvs.Count == outputPositions.Count) mesh.SetUVs(0, outputUvs);
            if (hasNormals && outputNormals.Count == outputPositions.Count) mesh.SetNormals(outputNormals);
            mesh.SetTriangles(triangles, 0, true);
            if (!hasNormals || outputNormals.Count != outputPositions.Count) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int AddObjVertex(string token, List<Vector3> positions, List<Vector2> uvs,
            List<Vector3> normals, Dictionary<string, int> vertexMap, List<Vector3> outputPositions,
            List<Vector2> outputUvs, List<Vector3> outputNormals, ref bool hasNormals)
        {
            if (string.IsNullOrWhiteSpace(token)) return -1;
            if (vertexMap.TryGetValue(token, out int existing)) return existing;
            string[] parts = token.Split('/');
            if (!TryObjIndex(parts, 0, positions.Count, out int positionIndex)) return -1;
            Vector3 position = positions[positionIndex];
            Vector2 uv = Vector2.zero;
            if (parts.Length > 1 && TryObjIndex(parts, 1, uvs.Count, out int uvIndex)) uv = uvs[uvIndex];
            Vector3 normal = Vector3.up;
            if (parts.Length <= 2 || !TryObjIndex(parts, 2, normals.Count, out int normalIndex)) hasNormals = false;
            else normal = normals[normalIndex];
            int result = outputPositions.Count;
            outputPositions.Add(position);
            outputUvs.Add(uv);
            outputNormals.Add(normal);
            vertexMap[token] = result;
            return result;
        }

        private static bool TryObjIndex(string[] parts, int part, int count, out int index)
        {
            index = -1;
            if (part >= parts.Length || string.IsNullOrWhiteSpace(parts[part]) || count == 0) return false;
            if (!int.TryParse(parts[part], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int value) || value == 0) return false;
            index = value > 0 ? value - 1 : count + value;
            return index >= 0 && index < count;
        }

        private static float ParseFloat(string value)
        {
            return float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static GameplaySkinInfo InstallPackage(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                throw new FileNotFoundException("找不到皮肤包", archivePath);

            string root = SkinRoot;
            Directory.CreateDirectory(root);
            string staging = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N"));
            long expandedBytes = 0;
            try
            {
                using (FileStream input = File.OpenRead(archivePath))
                using (var archive = new ZipArchive(input, ZipArchiveMode.Read, false, Encoding.UTF8))
                {
                    if (archive.Entries.Count > MaxPackageEntries)
                        throw new InvalidDataException("皮肤包文件数量过多");
                    Directory.CreateDirectory(staging);
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (string.IsNullOrWhiteSpace(entry.FullName) || IsDirectoryEntry(entry)) continue;
                        if (!IsAllowedPackageFile(entry.FullName))
                            throw new InvalidDataException("皮肤包包含不允许的文件类型：" + entry.FullName);
                        if (entry.Length < 0 || entry.Length > MaxEntryBytes ||
                            expandedBytes > MaxPackageBytes - entry.Length)
                            throw new InvalidDataException("皮肤包过大，导入已取消");
                        string destination = SafeExtractPath(staging, entry.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? staging);
                        using Stream source = entry.Open();
                        using FileStream output = File.Create(destination);
                        source.CopyTo(output);
                        expandedBytes += entry.Length;
                    }
                }

                string manifestPath = Path.Combine(staging, "skin.json");
                if (!TryReadManifest(manifestPath, out SkinManifest manifest) ||
                    !manifest.gameplaySkin || !IsValidId(manifest.id) || manifest.formatVersion != 1)
                    throw new InvalidDataException("皮肤包缺少有效的 skin.json");
                if (installed.Any(item => string.Equals(item.Id, manifest.id,
                        StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("皮肤 ID 已存在：" + manifest.id);

                string destinationRoot = Path.Combine(root, manifest.id);
                Directory.Move(staging, destinationRoot);
                Refresh();
                return installed.First(item => string.Equals(item.Id, manifest.id,
                    StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                throw;
            }
        }

        private static IEnumerable<string> RequiredResources()
        {
            foreach (string kind in NoteKinds)
                for (int width = 1; width <= 16; width++)
                    yield return "notes/" + kind + "_" + width.ToString("00") + ".png";
            foreach (string kind in MeshKinds)
                for (int width = 1; width <= 16; width++)
                    yield return "meshes/ntt_" + kind + "_" + width.ToString("00") + ".obj";
            foreach (string path in RequiredTrackFiles) yield return path;
            foreach (string path in RequiredEffectFiles) yield return path;
            foreach (string path in RequiredAudioFiles) yield return path;
            yield return "ui/gameplay-ui.json";
            foreach (Chart.Judgement grade in Enum.GetValues(typeof(Chart.Judgement)))
                yield return "ui/judgements/" + JudgementTextureKey(grade) + ".png";
        }

        public static string SkinRoot => Path.Combine(Application.persistentDataPath, SkinFolderName);

        private static Texture2D LoadTexture(string packagePath, string builtinPath)
        {
            Texture2D texture = LoadExternalTexture(packagePath);
            return texture ?? Resources.Load<Texture2D>(builtinPath);
        }

        private static Texture2D LoadExternalTexture(string relativePath)
        {
            if (active == null || active.BuiltIn || string.IsNullOrWhiteSpace(active.RootPath)) return null;
            string fullPath;
            try { fullPath = SafePath(active.RootPath, relativePath); }
            catch { return null; }
            if (!File.Exists(fullPath)) return null;
            string key = active.Id + "|" + relativePath;
            if (externalTextures.TryGetValue(key, out Texture2D cached)) return cached;
            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                if (bytes.Length == 0 || bytes.Length > MaxEntryBytes) return null;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) {
                    name = "Gameplay skin " + Path.GetFileName(fullPath),
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                if (!texture.LoadImage(bytes, false))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }
                if (texture.width <= 0 || texture.height <= 0 || texture.width > 4096 || texture.height > 4096 ||
                    (long)texture.width * texture.height > 16L * 1024L * 1024L) {
                    UnityEngine.Object.Destroy(texture);
                    Debug.LogWarning("Gameplay skin texture exceeds the safe size limit: " + relativePath);
                    return null;
                }
                texture.Apply(false, false);
                externalTextures[key] = texture;
                return texture;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Gameplay skin texture failed: " + relativePath + " | " + exception.Message);
                return null;
            }
        }

        private static GameplaySkinStyle ReadStyle(GameplaySkinInfo info)
        {
            GameplaySkinStyle result = GameplaySkinStyle.Original();
            string path = Path.Combine(info.RootPath, "ui", "gameplay-ui.json");
            if (!File.Exists(path)) return result;
            try
            {
                SkinUiManifest manifest = JsonUtility.FromJson<SkinUiManifest>(File.ReadAllText(path));
                if (manifest == null) return result;
                result.ComboColor = ParseColor(manifest.combo?.color, result.ComboColor);
                result.ScoreColor = ParseColor(manifest.score?.color, result.ScoreColor);
                result.JudgementColor = ParseColor(manifest.judgements?.color, result.JudgementColor);
                if (manifest.combo != null && manifest.combo.defaultSize > 0) result.ComboSize = manifest.combo.defaultSize;
                if (manifest.score != null && manifest.score.defaultSize > 0) result.ScoreSize = manifest.score.defaultSize;
                if (manifest.judgements != null && manifest.judgements.defaultSize > 0)
                    result.JudgementSize = manifest.judgements.defaultSize;
                if (manifest.effects != null) {
                    result.ScoreWarningEnabled = manifest.effects.scoreWarningEnabled;
                    result.ScoreWarningThreshold = Mathf.Clamp(manifest.effects.scoreWarningThreshold, 0, 1010000);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Gameplay skin UI style failed: " + exception.Message);
            }
            return result;
        }

        private static Color ParseColor(string value, Color fallback)
        {
            return !string.IsNullOrWhiteSpace(value) && ColorUtility.TryParseHtmlString(value, out Color color)
                ? color : fallback;
        }

        private static GameplaySkinInfo BuiltinInfo()
        {
            return new GameplaySkinInfo {
                Id = BuiltinId,
                Name = "当前原版皮肤",
                Author = "HMQYHM & Codex",
                Version = "0.2.7",
                RootPath = "",
                BuiltIn = true
            };
        }

        private static bool TryReadManifest(string path, out SkinManifest manifest)
        {
            manifest = null;
            try
            {
                if (!File.Exists(path)) return false;
                manifest = JsonUtility.FromJson<SkinManifest>(File.ReadAllText(path));
                return manifest != null;
            }
            catch { return false; }
        }

        private static bool IsValidId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return false;
            return id.All(character => char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.');
        }

        private static bool IsDirectoryEntry(ZipArchiveEntry entry)
        {
            return entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                   entry.FullName.EndsWith("\\", StringComparison.Ordinal);
        }

        private static bool IsAllowedPackageFile(string path)
        {
            string extension = Path.GetExtension(path ?? "").ToLowerInvariant();
            string name = Path.GetFileName(path ?? "");
            return extension == ".json" || extension == ".png" || extension == ".jpg" ||
                extension == ".jpeg" || extension == ".webp" || extension == ".obj" ||
                extension == ".wav" || extension == ".mp3" || extension == ".ogg" ||
                name.Equals("README.txt", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("LICENSE-MIT.txt", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("checksums.sha256", StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeExtractPath(string root, string relative)
        {
            return SafePath(root, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string SafePath(string root, string relative)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative));
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("皮肤包包含非法路径");
            return fullPath;
        }

        [Serializable]
        private sealed class SkinManifest
        {
            public int formatVersion;
            public string id;
            public string name;
            public string author;
            public string version;
            public bool gameplaySkin;
        }

        [Serializable]
        private sealed class SkinUiManifest
        {
            public SkinUiPart combo;
            public SkinUiPart score;
            public SkinUiPart judgements;
            public SkinUiEffects effects;
        }

        [Serializable]
        private sealed class SkinUiEffects
        {
            public bool scoreWarningEnabled;
            public int scoreWarningThreshold;
        }

        [Serializable]
        private sealed class SkinUiPart
        {
            public string color;
            public int defaultSize;
        }
    }
}
