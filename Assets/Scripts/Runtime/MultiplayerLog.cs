using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// Small persistent log for multiplayer diagnostics. Wire messages are summarized from
    /// typed fields so credentials never need to be written to disk.
    /// </summary>
    public static class MultiplayerLog
    {
        private const int MaxFileBytes = 2 * 1024 * 1024;
        private const int MaxRecentLines = 4000;
        private static readonly object Sync = new object();
        private static readonly LinkedList<string> Recent = new LinkedList<string>();
        private static string currentFilePath;
        private static bool initialized;

        [Serializable]
        private sealed class SafeWireMessage
        {
            public string type;
            public string code;
            public string message;
            public string serverVersion;
            public string clientVersion;
            public string gameId;
            public string playerId;
            public string playerName;
            public string roomId;
            public string roundId;
            public string hostPlayerId;
            public string hostName;
            public string reason;
            public string expectedVersion;
            public string actualVersion;
            public string packageHash;
            public string direction;
            public string transferState;
            public string roomName;
            public string roomMode;
            public string selectionMode;
            public string search;
            public string songId;
            public string songName;
            public string artist;
            public string chartPath;
            public string difficulty;
            public string level;
            public string chartHash;
            public string designer;
            public string bpm;
            public string durationText;
            public bool ready;
            public bool listed;
            public int maxPlayers;
            public int score;
            public int justiceCritical;
            public int justice;
            public int attack;
            public int miss;
            public int maxCombo;
            public long packageSize;
            public float progress;
        }

        public static void Initialize()
        {
            lock (Sync) EnsureInitializedLocked();
        }

        public static void Info(string action, string detail = null) => Write("INFO", action, detail);
        public static void Warn(string action, string detail = null) => Write("WARN", action, detail);
        public static void Error(string action, string detail = null) => Write("ERROR", action, detail);

        public static string SummarizeJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "empty message";
            SafeWireMessage value;
            try { value = JsonUtility.FromJson<SafeWireMessage>(json); }
            catch { return "invalid json length=" + json.Length; }
            if (value == null || string.IsNullOrWhiteSpace(value.type))
                return "unrecognized message length=" + json.Length;

            var fields = new List<string> { "type=" + Clean(value.type) };
            Add(fields, "code", value.code);
            Add(fields, "message", value.message);
            Add(fields, "serverVersion", value.serverVersion);
            Add(fields, "clientVersion", value.clientVersion);
            Add(fields, "gameId", ShortId(value.gameId));
            Add(fields, "playerId", ShortId(value.playerId));
            Add(fields, "playerName", value.playerName);
            Add(fields, "roomId", value.roomId);
            Add(fields, "roundId", value.roundId);
            Add(fields, "hostPlayerId", ShortId(value.hostPlayerId));
            Add(fields, "hostName", value.hostName);
            Add(fields, "reason", value.reason);
            Add(fields, "expectedVersion", value.expectedVersion);
            Add(fields, "actualVersion", value.actualVersion);
            Add(fields, "packageHash", value.packageHash);
            Add(fields, "direction", value.direction);
            Add(fields, "transferState", value.transferState);
            Add(fields, "roomName", value.roomName);
            Add(fields, "roomMode", value.roomMode);
            Add(fields, "selectionMode", value.selectionMode);
            Add(fields, "search", value.search);
            Add(fields, "songId", value.songId);
            Add(fields, "songName", value.songName);
            Add(fields, "artist", value.artist);
            Add(fields, "chartPath", value.chartPath);
            Add(fields, "difficulty", value.difficulty);
            Add(fields, "level", value.level);
            Add(fields, "chartHash", value.chartHash);
            Add(fields, "designer", value.designer);
            Add(fields, "bpm", value.bpm);
            Add(fields, "duration", value.durationText);
            if (value.maxPlayers > 0) Add(fields, "maxPlayers", value.maxPlayers.ToString());
            if (value.packageSize > 0) Add(fields, "packageBytes", value.packageSize.ToString());
            if (value.progress > 0f || value.transferState == "running" || value.transferState == "completed")
                Add(fields, "progress", (Mathf.Clamp01(value.progress) * 100f).ToString("0.0") + "%");
            if (value.ready) Add(fields, "ready", "true");
            if (value.listed) Add(fields, "listed", "true");
            if (value.score != 0 || value.maxCombo != 0 || value.miss != 0)
            {
                Add(fields, "score", value.score.ToString());
                Add(fields, "jc", value.justiceCritical.ToString());
                Add(fields, "justice", value.justice.ToString());
                Add(fields, "attack", value.attack.ToString());
                Add(fields, "miss", value.miss.ToString());
                Add(fields, "maxCombo", value.maxCombo.ToString());
            }
            return string.Join(" ", fields.ToArray());
        }

        public static string ShortId(string value)
        {
            string clean = Clean(value);
            if (string.IsNullOrWhiteSpace(clean)) return "-";
            return clean.Length <= 16 ? clean : clean.Substring(0, 8) + "..." + clean.Substring(clean.Length - 4);
        }

        public static string ReadRecent(int maxLines = 2000)
        {
            maxLines = Math.Max(1, maxLines);
            lock (Sync)
            {
                EnsureInitializedLocked();
                try
                {
                    var lines = new Queue<string>(maxLines);
                    if (File.Exists(currentFilePath))
                    {
                        foreach (string line in File.ReadLines(currentFilePath, Encoding.UTF8))
                        {
                            if (lines.Count >= maxLines) lines.Dequeue();
                            lines.Enqueue(line);
                        }
                    }
                    return string.Join(Environment.NewLine, lines.ToArray());
                }
                catch (Exception exception)
                {
                    return "[联机日志读取失败: " + Clean(exception.Message) + "]";
                }
            }
        }

        private static void Add(List<string> fields, string key, string value)
        {
            string clean = Clean(value);
            if (!string.IsNullOrWhiteSpace(clean)) fields.Add(key + "=" + clean);
        }

        private static void Write(string level, string action, string detail)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " +
                          Clean(action) + (string.IsNullOrWhiteSpace(detail) ? "" : " | " + Clean(detail));
            lock (Sync)
            {
                EnsureInitializedLocked();
                Recent.AddLast(line);
                while (Recent.Count > MaxRecentLines) Recent.RemoveFirst();
                try
                {
                    RotateIfNeededLocked();
                    File.AppendAllText(currentFilePath, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch (Exception exception)
                {
                    // Diagnostics must never interrupt the game because storage can be full or read-only.
                    Debug.LogWarning("Unable to persist multiplayer log: " + exception.Message);
                }
            }
        }

        private static void EnsureInitializedLocked()
        {
            if (initialized && !string.IsNullOrWhiteSpace(currentFilePath)) return;
            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "Logs");
                Directory.CreateDirectory(directory);
                currentFilePath = Path.Combine(directory,
                    "multiplayer-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                initialized = true;
            }
            catch (Exception exception)
            {
                currentFilePath = Path.Combine(Application.temporaryCachePath, "multiplayer.log");
                initialized = true;
                Debug.LogWarning("Unable to prepare multiplayer log directory: " + exception.Message);
            }
        }

        private static void RotateIfNeededLocked()
        {
            if (!File.Exists(currentFilePath) || new FileInfo(currentFilePath).Length < MaxFileBytes) return;
            string backup = currentFilePath + ".1";
            try
            {
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(currentFilePath, backup);
            }
            catch (Exception exception) { Debug.LogWarning("Unable to rotate multiplayer log: " + exception.Message); }
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string result = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            result = Regex.Replace(result,
                "(?i)(password|sessionToken|authToken|token)\\s*[:=]\\s*[^ ]+", "$1=<redacted>");
            return result.Length > 320 ? result.Substring(0, 320) + "..." : result;
        }
    }
}
