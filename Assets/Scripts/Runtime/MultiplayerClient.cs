using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ChunithmMobile.Runtime
{
    [Serializable]
    public sealed class MultiplayerHelloMessage
    {
        public string type = "hello";
        public int protocol = 2;
        public string clientVersion;
        public string playerName;
        public string deviceId;
        public string legacyDeviceId;
        public string authToken;
    }

    [Serializable]
    public sealed class MultiplayerModsMessage
    {
        public bool autoPlay;
        public bool airAreaAuto;
        public bool directionalAirAuto;
        public bool airLongNoMotion;
        // Legacy wire field for deployed servers; never enables a gameplay MOD.
        public bool flickSimpleMode;
        public bool mineImmunity;
        public bool mirrorChart;
        public float musicRate = 1f;
    }

    [Serializable]
    public sealed class MultiplayerCreateRoomMessage
    {
        public string type = "createRoom";
        public string roomName;
        public string roomMode;
        public int maxPlayers = 8;
        public string selectionMode = "host";
        public bool listed = true;
        public string password = "";
        public string reservedRoomId;
        public MultiplayerModsMessage mods = new MultiplayerModsMessage();
    }

    [Serializable]
    public sealed class MultiplayerJoinRoomMessage
    {
        public string type = "joinRoom";
        public string roomId;
        public string password = "";
    }

    [Serializable]
    public sealed class MultiplayerListRoomsMessage
    {
        public string type = "listRooms";
        public string search = "";
        public string roomId = "";
        public bool idleOnly;
        public bool notFullOnly;
        public bool unencryptedOnly;
        public string mode = "";
    }

    [Serializable]
    public sealed class MultiplayerRoomActionMessage
    {
        public string type;
        public bool ready;
    }

    [Serializable]
    public sealed class MultiplayerSelectSongMessage
    {
        public string type = "selectSong";
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
        public string packageHash;
        public long packageSize;
    }

    [Serializable]
    public sealed class MultiplayerKickPlayerMessage
    {
        public string type = "kickPlayer";
        public string playerId;
    }

    [Serializable]
    public sealed class MultiplayerTransferHostMessage
    {
        public string type = "transferHost";
        public string playerId;
    }

    [Serializable]
    public sealed class MultiplayerTransferProgressMessage
    {
        public string type = "transferProgress";
        public string packageHash;
        public string direction;
        public string state;
        public float progress;
    }

    [Serializable]
    public sealed class MultiplayerPingMessage
    {
        public string type = "ping";
        public long clientTime;
    }

    [Serializable]
    public sealed class MultiplayerPongMessage
    {
        public string type;
        public long serverTime;
    }

    [Serializable]
    public sealed class MultiplayerEnvelope
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
        public string sessionToken;
        public string packageHash;
        public string downloadPath;
        public string expectedVersion;
        public string actualVersion;
        public string direction;
        public string transferState;
        public long cooldownUntil;
        public int remainingSeconds;
        public long serverTime;
        public long startAt;
        public long expiresAt;
        public long packageSize;
        public float progress;
        public MultiplayerQuotaDto quota;
        public MultiplayerErrorDetailsDto details;
        public MultiplayerRoomDto room;
        public MultiplayerRoomDto[] rooms;
        public MultiplayerRoomListFiltersDto filters;
        public MultiplayerSongDto song;
        public MultiplayerModsMessage mods;
        public MultiplayerResultDto result;
        public MultiplayerLeaderboardEntryDto[] leaderboard;
    }

    [Serializable]
    public sealed class MultiplayerRoomListFiltersDto
    {
        public bool idleOnly;
        public bool notFullOnly;
        public bool unencryptedOnly;
        public string mode;
    }

    [Serializable]
    public sealed class MultiplayerErrorDetailsDto
    {
        public string expectedVersion;
        public string actualVersion;
        public long cooldownUntil;
        public int remainingSeconds;
        public MultiplayerQuotaDto quota;
    }

    [Serializable]
    public sealed class MultiplayerQuotaDto
    {
        public string day;
        public string groupId;
        public string groupName;
        public int createRoomLimit;
        public int createRoomUsed;
        public int createRoomRemaining;
        public int joinRoomLimit;
        public int joinRoomUsed;
        public int joinRoomRemaining;
    }

    [Serializable]
    public sealed class MultiplayerRoomDto
    {
        public string roomId;
        public string roomName;
        public string hostPlayerId;
        public string hostName;
        public string ownerPlayerId;
        public string ownerName;
        public string clientVersion;
        public string mode;
        public string selectionMode;
        public string state;
        public int maxPlayers;
        public int memberCount;
        public bool listed;
        public bool hasPassword;
        public MultiplayerModsMessage mods;
        public MultiplayerRoomMemberDto[] members;
        public MultiplayerSongDto song;
        public string roundId;
        public long startAt;
        public MultiplayerLeaderboardEntryDto[] leaderboard;
    }

    [Serializable]
    public sealed class MultiplayerRoomMemberDto
    {
        public string playerId;
        public string gameId;
        public string name;
        public string clientVersion;
        public bool isHost;
        public bool isOwner;
        public bool ready;
        public bool connected;
        public string status;
        public bool songReady;
        public float syncProgress;
        public int bestScore;
        public string bestRank;
        public MultiplayerResultDto result;
        public bool personalResultConfirmed;
        public bool groupResultConfirmed;
        public long kickCooldownUntil;
    }

    [Serializable]
    public sealed class MultiplayerSongDto
    {
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
        public string packageHash;
        public long packageSize;
        public bool transferReady;
        public float transferProgress;
        public string transferDirection;
        public string transferState;
        public string transferPlayerId;
        public string downloadPath;
        public string selectedBy;
        public string selectedAt;
    }

    [Serializable]
    public sealed class MultiplayerResultDto
    {
        public int score;
        public string rank;
        public int justiceCritical;
        public int justice;
        public int attack;
        public int miss;
        public int maxCombo;
        public bool recordsScore;
    }

    [Serializable]
    public sealed class MultiplayerLeaderboardEntryDto
    {
        public string playerId;
        public string name;
        public bool exited;
        public int placement;
        public int score;
        public string rank;
        public int justiceCritical;
        public int justice;
        public int attack;
        public int miss;
        public int maxCombo;
    }

    [Serializable]
    public sealed class MultiplayerSyncStatusMessage
    {
        public string type = "setSongSyncStatus";
        public string packageHash;
        public bool ready;
        public bool failed;
        public float progress;
    }

    [Serializable]
    public sealed class MultiplayerMemberBestMessage
    {
        public string type = "setMemberBest";
        public string packageHash;
        public int score;
        public string rank;
    }

    [Serializable]
    public sealed class MultiplayerRoundMessage
    {
        public string type;
        public string roundId;
    }

    [Serializable]
    public sealed class MultiplayerResultMessage
    {
        public string type = "submitResult";
        public string roundId;
        public int score;
        public int justiceCritical;
        public int justice;
        public int attack;
        public int miss;
        public int maxCombo;
    }

    [Serializable]
    public sealed class MultiplayerAppStateMessage
    {
        public string type = "appState";
        public string state;
    }

    public sealed class MultiplayerClient : MonoBehaviour
    {
        private enum EventKind { Connected, Message, Error, Disconnected }

        private struct QueuedEvent
        {
            public EventKind Kind;
            public string Value;
        }

        private readonly ConcurrentQueue<QueuedEvent> events = new ConcurrentQueue<QueuedEvent>();
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
        private ClientWebSocket socket;
        private CancellationTokenSource cancellation;
        private float nextPingAt;
        private float heartbeatDeadline;
        private bool heartbeatAwaiting;
        private bool destroying;

        public bool IsConnected { get; private set; }
        public string ConnectedAddress { get; private set; }
        public event Action Connected;
        public event Action<string> MessageReceived;
        public event Action<string> ConnectionError;
        public event Action Disconnected;
        public event Action HeartbeatTimeout;

        private void Update()
        {
            while (events.TryDequeue(out QueuedEvent queued))
            {
                switch (queued.Kind)
                {
                    case EventKind.Connected:
                        IsConnected = true;
                        nextPingAt = Time.unscaledTime + 5f;
                        heartbeatAwaiting = false;
                        MultiplayerLog.Info("transport-connected", "server=" + ConnectedAddress);
                        Connected?.Invoke();
                        break;
                    case EventKind.Message:
                        if (IsPong(queued.Value))
                        {
                            heartbeatAwaiting = false;
                            heartbeatDeadline = 0f;
                            nextPingAt = Time.unscaledTime + 5f;
                            MultiplayerLog.Info("heartbeat-pong", MultiplayerLog.SummarizeJson(queued.Value));
                        }
                        else MultiplayerLog.Info("network-receive", MultiplayerLog.SummarizeJson(queued.Value));
                        MessageReceived?.Invoke(queued.Value);
                        break;
                    case EventKind.Error:
                        IsConnected = false;
                        MultiplayerLog.Error("transport-error", queued.Value);
                        ConnectionError?.Invoke(queued.Value);
                        break;
                    case EventKind.Disconnected:
                        IsConnected = false;
                        MultiplayerLog.Warn("transport-disconnected", "server=" + ConnectedAddress);
                        Disconnected?.Invoke();
                        break;
                }
            }

            if (IsConnected && heartbeatAwaiting && Time.unscaledTime >= heartbeatDeadline)
            {
                heartbeatAwaiting = false;
                heartbeatDeadline = 0f;
                MultiplayerLog.Warn("heartbeat-timeout", "server=" + ConnectedAddress);
                HeartbeatTimeout?.Invoke();
            }

            if (IsConnected && !heartbeatAwaiting && Time.unscaledTime >= nextPingAt)
            {
                nextPingAt = Time.unscaledTime + 5f;
                heartbeatAwaiting = true;
                heartbeatDeadline = Time.unscaledTime + 5f;
                string ping = JsonUtility.ToJson(new MultiplayerPingMessage { clientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                MultiplayerLog.Info("heartbeat-ping", "server=" + ConnectedAddress);
                Send(ping);
            }
        }

        public void Connect(string address, string playerName, string deviceId, string clientVersion, string authToken = "")
        {
            MultiplayerLog.Info("connect-request", "server=" + (address ?? "") + " player=" + (playerName ?? "") +
                " device=" + MultiplayerLog.ShortId(deviceId) + " version=" + (clientVersion ?? ""));
            Disconnect();
            if (!TryBuildWebSocketUri(address, out Uri uri, out string error))
            {
                MultiplayerLog.Error("connect-validation-failed", error);
                events.Enqueue(new QueuedEvent { Kind = EventKind.Error, Value = error });
                return;
            }
            cancellation = new CancellationTokenSource();
            ConnectedAddress = address == null ? "" : address.Trim();
            _ = ConnectAndReceive(uri, playerName, deviceId, GetLegacyDeviceId(), clientVersion, authToken, cancellation.Token);
        }

        public void Send(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            ClientWebSocket connection = socket;
            if (connection == null || connection.State != WebSocketState.Open)
            {
                MultiplayerLog.Warn("network-send-skipped", MultiplayerLog.SummarizeJson(json));
                return;
            }
            MultiplayerLog.Info("network-send", MultiplayerLog.SummarizeJson(json));
            CancellationToken token = cancellation == null ? CancellationToken.None : cancellation.Token;
            _ = SendText(connection, json, token);
        }

        public void RequestRoomList(string search = "", string roomId = "", bool idleOnly = false,
            bool notFullOnly = false, bool unencryptedOnly = false, string mode = "")
        {
            Send(JsonUtility.ToJson(new MultiplayerListRoomsMessage {
                search = search ?? "",
                roomId = roomId ?? "",
                idleOnly = idleOnly,
                notFullOnly = notFullOnly,
                unencryptedOnly = unencryptedOnly,
                mode = mode ?? ""
            }));
        }

        public void SetReady(bool ready)
        {
            Send(JsonUtility.ToJson(new MultiplayerRoomActionMessage { type = "setReady", ready = ready }));
        }

        public void ReserveRoomId() => Send("{\"type\":\"reserveRoomId\"}");
        public void ReleaseRoomId() => Send("{\"type\":\"releaseRoomId\"}");

        public void SetSongSyncStatus(string packageHash, bool ready, float progress, bool failed = false)
        {
            Send(JsonUtility.ToJson(new MultiplayerSyncStatusMessage {
                packageHash = packageHash, ready = ready, failed = failed, progress = Mathf.Clamp01(progress)
            }));
        }

        public void SetMemberBest(string packageHash, int score, string rank)
        {
            Send(JsonUtility.ToJson(new MultiplayerMemberBestMessage {
                packageHash = packageHash, score = score, rank = rank ?? ""
            }));
        }

        public void LeaveRoom()
        {
            Send("{\"type\":\"leaveRoom\"}");
        }

        public void SelectSong(MultiplayerSelectSongMessage song)
        {
            if (song != null) Send(JsonUtility.ToJson(song));
        }

        public void StartRound()
        {
            Send("{\"type\":\"startRound\"}");
        }

        public void SetRoundLoaded(string roundId) => Send(JsonUtility.ToJson(new MultiplayerRoundMessage { type = "setRoundLoaded", roundId = roundId }));
        public void RoundLoadFailed(string roundId) => Send(JsonUtility.ToJson(new MultiplayerRoundMessage { type = "roundLoadFailed", roundId = roundId }));
        public void ExitRound() => Send("{\"type\":\"exitRound\"}");
        public void ConfirmPersonalResult() => Send("{\"type\":\"confirmPersonalResult\"}");
        public void ConfirmGroupResult() => Send("{\"type\":\"confirmGroupResult\"}");
        public void SendAppState(bool background) => Send(JsonUtility.ToJson(new MultiplayerAppStateMessage { state = background ? "background" : "active" }));

        public void SubmitResult(string roundId, int score, int justiceCritical, int justice, int attack, int miss, int maxCombo)
        {
            Send(JsonUtility.ToJson(new MultiplayerResultMessage {
                roundId = roundId, score = score, justiceCritical = justiceCritical, justice = justice,
                attack = attack, miss = miss, maxCombo = maxCombo
            }));
        }

        public void KickPlayer(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;
            Send(JsonUtility.ToJson(new MultiplayerKickPlayerMessage { playerId = playerId }));
        }

        public void TransferHost(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;
            Send(JsonUtility.ToJson(new MultiplayerTransferHostMessage { playerId = playerId }));
        }

        public void ReportTransferProgress(string packageHash, string direction, string state, float progress)
        {
            if (string.IsNullOrWhiteSpace(packageHash)) return;
            Send(JsonUtility.ToJson(new MultiplayerTransferProgressMessage {
                packageHash = packageHash,
                direction = direction,
                state = state,
                progress = Mathf.Clamp01(progress)
            }));
        }

        public void Disconnect()
        {
            if (IsConnected || socket != null || !string.IsNullOrWhiteSpace(ConnectedAddress))
                MultiplayerLog.Info("disconnect-request", "server=" + ConnectedAddress);
            IsConnected = false;
            heartbeatAwaiting = false;
            heartbeatDeadline = 0f;
            try { cancellation?.Cancel(); } catch { }
            try { socket?.Abort(); } catch { }
            try { socket?.Dispose(); } catch { }
            cancellation = null;
            socket = null;
            ConnectedAddress = "";
        }

        private static bool IsPong(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                MultiplayerPongMessage pong = JsonUtility.FromJson<MultiplayerPongMessage>(json);
                return pong != null && pong.type == "pong";
            }
            catch { return false; }
        }

        public void LeaveLobby() => Send("{\"type\":\"leaveLobby\"}");

        private async Task ConnectAndReceive(Uri uri, string playerName, string deviceId, string legacyDeviceId, string clientVersion,
            string authToken, CancellationToken token)
        {
            ClientWebSocket connection = new ClientWebSocket();
            socket = connection;
            try
            {
                await connection.ConnectAsync(uri, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                events.Enqueue(new QueuedEvent { Kind = EventKind.Connected });
                MultiplayerHelloMessage hello = new MultiplayerHelloMessage {
                    playerName = playerName,
                    deviceId = deviceId,
                    legacyDeviceId = legacyDeviceId,
                    clientVersion = clientVersion,
                    authToken = authToken
                };
                string helloJson = JsonUtility.ToJson(hello);
                MultiplayerLog.Info("network-send", MultiplayerLog.SummarizeJson(helloJson) + " authToken=<redacted>");
                await SendText(connection, helloJson, token).ConfigureAwait(false);
                byte[] buffer = new byte[65536];
                while (!token.IsCancellationRequested && connection.State == WebSocketState.Open)
                {
                    var builder = new StringBuilder();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await connection.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        if (result.MessageType != WebSocketMessageType.Text) continue;
                        builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);
                    if (builder.Length > 0) events.Enqueue(new QueuedEvent { Kind = EventKind.Message, Value = builder.ToString() });
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (!token.IsCancellationRequested)
                {
                    MultiplayerLog.Error("transport-exception", exception.Message);
                    events.Enqueue(new QueuedEvent { Kind = EventKind.Error, Value = exception.Message });
                }
            }
            finally
            {
                if (!destroying && !token.IsCancellationRequested)
                    events.Enqueue(new QueuedEvent { Kind = EventKind.Disconnected });
                try { connection.Dispose(); } catch { }
                if (ReferenceEquals(socket, connection)) socket = null;
            }
        }

        private async Task SendText(ClientWebSocket connection, string json, CancellationToken token)
        {
            if (connection == null || connection.State != WebSocketState.Open) return;
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await sendGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (connection.State == WebSocketState.Open)
                    await connection.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (token.IsCancellationRequested || connection == null) { }
            catch (Exception exception)
            {
                if (!token.IsCancellationRequested)
                {
                    MultiplayerLog.Error("send-exception", exception.Message);
                    events.Enqueue(new QueuedEvent { Kind = EventKind.Error, Value = exception.Message });
                }
            }
            finally { sendGate.Release(); }
        }

        private static bool TryBuildWebSocketUri(string address, out Uri uri, out string error)
        {
            uri = null;
            error = "服务器地址不能为空。";
            if (string.IsNullOrWhiteSpace(address)) return false;
            string value = address.Trim();
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) value = "ws://" + value.Substring(7);
            else if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) value = "wss://" + value.Substring(8);
            else if (!value.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
                value = "ws://" + value;
            if (value.IndexOf("/ws", StringComparison.OrdinalIgnoreCase) < 0) value = value.TrimEnd('/') + "/ws";
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || (uri.Scheme != "ws" && uri.Scheme != "wss"))
            {
                uri = null;
                error = "服务器地址格式无效，请填写 IP:端口 或 ws:// 地址。";
                return false;
            }
            error = null;
            return true;
        }

        public static bool TryBuildHttpBaseUri(string address, out Uri uri)
        {
            uri = null;
            if (!TryBuildWebSocketUri(address, out Uri websocketUri, out _)) return false;
            var builder = new UriBuilder(websocketUri) {
                Scheme = websocketUri.Scheme == "wss" ? "https" : "http",
                Path = "",
                Query = "",
                Fragment = ""
            };
            if ((builder.Scheme == "http" && builder.Port == 80) || (builder.Scheme == "https" && builder.Port == 443))
                builder.Port = -1;
            uri = builder.Uri;
            return true;
        }

        public static string GetStableDeviceId()
        {
            const string key = "chunithm_mobile_device_id";
            string stored = PlayerPrefs.GetString(key, "");
            if (stored.StartsWith("unity-device-v3-", StringComparison.Ordinal)) return stored;
            if (!string.IsNullOrWhiteSpace(stored))
                PlayerPrefs.SetString("chunithm_mobile_legacy_device_id", stored);

            string systemId = NormalizeHardwareId(SystemInfo.deviceUniqueIdentifier);
            string androidId = NormalizeHardwareId(ReadAndroidId());
            string hardwareId = !string.IsNullOrWhiteSpace(androidId) ? "android|" + androidId :
                                !string.IsNullOrWhiteSpace(systemId) ? "unity|" + systemId : "";
            if (string.IsNullOrWhiteSpace(hardwareId))
            {
                const string seedKey = "chunithm_mobile_device_seed";
                string installSeed = PlayerPrefs.GetString(seedKey, "");
                if (string.IsNullOrWhiteSpace(installSeed))
                {
                    installSeed = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(seedKey, installSeed);
                }
                hardwareId = "install|" + installSeed + "|" + (SystemInfo.deviceModel ?? "");
            }
            string material = "chunithm-mobile-device-v3|" + Application.identifier + "|" + hardwareId;
            string value = "unity-device-v3-" + Sha256(material);
            PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
            return value;
        }

        public static string GetLegacyDeviceId()
        {
            return PlayerPrefs.GetString("chunithm_mobile_legacy_device_id", "");
        }

        private static string ReadAndroidId()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var secure = new AndroidJavaClass("android.provider.Settings$Secure"))
                {
                    return secure.CallStatic<string>("getString", resolver, "android_id") ?? "";
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unable to read Android secure ID: " + exception.Message);
            }
#endif
            return "";
        }

        private static string NormalizeHardwareId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string normalized = value.Trim();
            if (normalized.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("0", StringComparison.OrdinalIgnoreCase)) return "";
            return normalized;
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (byte item in bytes) builder.Append(item.ToString("x2"));
                return builder.ToString();
            }
        }

        private void OnDestroy()
        {
            destroying = true;
            Disconnect();
            sendGate.Dispose();
        }
    }
}
