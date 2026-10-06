using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ChunithmMobile.Runtime
{
    // 联机传输层：维护连接、消息队列、心跳、重试和 DTO 编解码。
    // 网络线程只入队事件，Unity 对象和 UI 状态必须在主线程消费；服务端数据始终视为不可信。
    [Serializable]
    public sealed class MultiplayerHelloMessage
    {
        public string type = "hello";
        public int protocol = 2;
        public string clientVersion;
        public string playerName;
        public string deviceId;
        public string legacyDeviceId;
    }

    [Serializable]
    public sealed class MultiplayerModsMessage
    {
        public bool autoPlay;
        public bool airAreaAuto;
        public bool directionalAirAuto;
        public bool airLongNoMotion;
        public bool mineImmunity;
        public bool mirrorChart;
        public bool flickAllTap;
        public bool fullscreenTouchpad;
        public bool nonMissJusticeCritical;
        public bool uniformSpeed;
        public float musicRate = 1f;
    }

    [Serializable]
    public sealed class MultiplayerCreateRoomMessage
    {
        public string type = "createRoom";
        public string roomName;
        public string roomMode;
        public string roomType = "personal";
        public int maxPlayers = 20;
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
        public string roomType = "";
    }

    [Serializable]
    public sealed class MultiplayerRoomActionMessage
    {
        public string type;
        public bool ready;
    }

    [Serializable]
    public sealed class MultiplayerVoteMessage
    {
        public string type;
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
        public string coverPath;
        public string coverHash;
        public long coverSize;
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
        public long clientTime;
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
        public float requiredRatio;
        public bool active;
        public int threshold;
        public int voteCount;
        public int eligibleCount;
        public string previousSongId;
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
        public string roomType;
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
        public string roomType;
        public string selectionMode;
        public string state;
        public int maxPlayers;
        public int memberCount;
        public bool listed;
        public bool hasPassword;
        public bool persistent;
        public MultiplayerSongChangeVoteDto songChangeVote;
        public MultiplayerModsMessage mods;
        public MultiplayerRoomMemberDto[] members;
        public MultiplayerSongDto song;
        public string roundId;
        public long startAt;
        public MultiplayerLeaderboardEntryDto[] leaderboard;
    }

    [Serializable]
    public sealed class MultiplayerSongChangeVoteDto
    {
        public bool active;
        public float requiredRatio;
        public int threshold;
        public int count;
        public int eligible;
        public string[] voters;
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
        public string coverPath;
        public string coverHash;
        public long coverSize;
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
        private bool hasServerClockSample;
        private double serverMinusLocalMilliseconds;
        private double bestClockRoundTripMilliseconds = double.PositiveInfinity;
#if UNITY_IOS && !UNITY_EDITOR
        private bool iosNativeSocketOpen;
        private string iosPendingPlayerName = "";
        private string iosPendingDeviceId = "";
        private string iosPendingLegacyDeviceId = "";
        private string iosPendingClientVersion = "";
#endif

        public bool IsConnected { get; private set; }
        public string ConnectedAddress { get; private set; }
        public bool HasServerClockSample => hasServerClockSample;
        public double ServerClockOffsetMilliseconds => serverMinusLocalMilliseconds;
        public double ServerClockRoundTripMilliseconds => bestClockRoundTripMilliseconds;
        public event Action Connected;
        public event Action<string> MessageReceived;
        public event Action<string> ConnectionError;
        public event Action Disconnected;
        public event Action HeartbeatTimeout;

        // 主线程事件泵：网络接收任务只入队，Unity 对象和 UI 回调在这里消费。
        // 这条边界避免后台线程直接访问 GameObject，也让断线事件按顺序进入 UI。
        private void Update()
        {
            while (events.TryDequeue(out QueuedEvent queued))
            {
                switch (queued.Kind)
                {
                    case EventKind.Connected:
                        IsConnected = true;
                        // Calibrate before a room can start. Waiting five
                        // seconds for the first ping made devices with an
                        // inaccurate wall clock schedule different start times.
                        nextPingAt = Time.unscaledTime;
                        heartbeatAwaiting = false;
                        MultiplayerLog.Info("transport-connected", "server=" + ConnectedAddress);
                        Connected?.Invoke();
                        break;
                    case EventKind.Message:
                        if (TryReadPong(queued.Value, out MultiplayerPongMessage pong))
                        {
                            ObserveServerClock(pong, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
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

        /// <summary>
        /// Converts a server deadline to this device's Unix clock. Before the
        /// first NTP-style pong, use the deadline's distance from serverTime so
        /// a badly configured device clock cannot create a multi-second error.
        /// </summary>
        public long ServerDeadlineToLocalUnixMilliseconds(long serverDeadline, long observedServerTime)
        {
            if (hasServerClockSample)
                return (long)Math.Round(serverDeadline - serverMinusLocalMilliseconds,
                    MidpointRounding.AwayFromZero);
            long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return observedServerTime > 0
                ? localNow + Math.Max(0, serverDeadline - observedServerTime)
                : serverDeadline;
        }

        /// <summary>
        /// Returns the remaining real time until a server deadline. Callers
        /// should add this once to their monotonic DSP clock; using wall-clock
        /// timestamps again when playback starts would reintroduce drift when
        /// Android adjusts its system clock in the meantime.
        /// </summary>
        public double ServerDeadlineDelayMilliseconds(long serverDeadline, long observedServerTime,
            long localNowUnixMilliseconds)
        {
            double estimatedServerNow = hasServerClockSample
                ? localNowUnixMilliseconds + serverMinusLocalMilliseconds
                : observedServerTime > 0 ? observedServerTime : localNowUnixMilliseconds;
            return Math.Max(0, serverDeadline - estimatedServerNow);
        }

        private void ObserveServerClock(MultiplayerPongMessage pong, long receivedLocalMilliseconds)
        {
            if (pong == null || pong.clientTime <= 0 || pong.serverTime <= 0) return;
            double roundTrip = receivedLocalMilliseconds - pong.clientTime;
            if (roundTrip < 0 || roundTrip > 10_000) return;
            double midpoint = pong.clientTime + roundTrip * .5;
            double sample = pong.serverTime - midpoint;

            // Prefer the least-delayed samples. Blend near-best samples to
            // smooth millisecond jitter without following congested packets.
            if (!hasServerClockSample) {
                serverMinusLocalMilliseconds = sample;
                bestClockRoundTripMilliseconds = roundTrip;
                hasServerClockSample = true;
            } else if (roundTrip <= bestClockRoundTripMilliseconds + 12.0) {
                serverMinusLocalMilliseconds = serverMinusLocalMilliseconds * .75 + sample * .25;
                bestClockRoundTripMilliseconds = Math.Min(bestClockRoundTripMilliseconds, roundTrip);
            }
            MultiplayerLog.Info("clock-sync", $"offset={serverMinusLocalMilliseconds:F2}ms " +
                $"rtt={roundTrip:F2}ms bestRtt={bestClockRoundTripMilliseconds:F2}ms");
        }

        /// <summary>
        /// 发起异步 WebSocket 连接并发送 hello。连接结果通过 Connected/ConnectionError/Disconnected
        /// 事件回传，本方法不会阻塞当前帧，也不会保证服务器已经接受登录。
        /// </summary>
        public void Connect(string address, string playerName, string deviceId, string clientVersion)
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
#if UNITY_IOS && !UNITY_EDITOR
            iosPendingPlayerName = playerName ?? "";
            iosPendingDeviceId = deviceId ?? "";
            iosPendingLegacyDeviceId = GetLegacyDeviceId();
            iosPendingClientVersion = clientVersion ?? "";
            MultiplayerLog.Info("connect-target", uri.GetLeftPart(UriPartial.Path) + " transport=NSURLSessionWebSocketTask");
            ChunithmWebSocketConnect(uri.AbsoluteUri);
#else
            MultiplayerLog.Info("connect-target", uri.GetLeftPart(UriPartial.Path) + " transport=ClientWebSocket");
            _ = ConnectAndReceive(uri, playerName, deviceId, GetLegacyDeviceId(), clientVersion, cancellation.Token);
#endif
        }

        /// <summary>
        /// 发送一条 JSON 消息。调用方只能认为消息已交给传输层，房间权限和参数合法性仍由服务端决定。
        /// </summary>
        public void Send(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
#if UNITY_IOS && !UNITY_EDITOR
            if (!iosNativeSocketOpen)
            {
                MultiplayerLog.Warn("network-send-skipped", MultiplayerLog.SummarizeJson(json));
                return;
            }
            MultiplayerLog.Info("network-send", MultiplayerLog.SummarizeJson(json));
            ChunithmWebSocketSend(json);
            return;
#else
            ClientWebSocket connection = socket;
            if (connection == null || connection.State != WebSocketState.Open)
            {
                MultiplayerLog.Warn("network-send-skipped", MultiplayerLog.SummarizeJson(json));
                return;
            }
            MultiplayerLog.Info("network-send", MultiplayerLog.SummarizeJson(json));
            CancellationToken token = cancellation == null ? CancellationToken.None : cancellation.Token;
            _ = SendText(connection, json, token);
#endif
        }

        /// <summary>请求大厅列表；过滤参数只用于缩小查询范围，不提供绕过服务端规则的能力。</summary>
        public void RequestRoomList(string search = "", string roomId = "", bool idleOnly = false,
            bool notFullOnly = false, bool unencryptedOnly = false, string mode = "", string roomType = "")
        {
            Send(JsonUtility.ToJson(new MultiplayerListRoomsMessage {
                search = search ?? "",
                roomId = roomId ?? "",
                idleOnly = idleOnly,
                notFullOnly = notFullOnly,
                unencryptedOnly = unencryptedOnly,
                mode = mode ?? "",
                roomType = roomType ?? ""
            }));
        }

        /// <summary>上报本玩家准备状态；房间当前阶段是否允许修改由服务端最终判断。</summary>
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

        public void RequestSongChangeVote() => Send(JsonUtility.ToJson(new MultiplayerVoteMessage { type = "requestSongChangeVote" }));
        public void CastSongChangeVote() => Send(JsonUtility.ToJson(new MultiplayerVoteMessage { type = "castSongChangeVote" }));

        /// <summary>提交选歌结果；房主专选模式下只有房主提交才应被服务器接受。</summary>
        public void SelectSong(MultiplayerSelectSongMessage song)
        {
            if (song != null) Send(JsonUtility.ToJson(song));
        }

        /// <summary>请求开始回合；服务端应检查版本、歌曲同步、准备状态、房主权限和 MOD 合法性。</summary>
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

        /// <summary>主动取消接收任务并关闭连接；关闭后旧消息不能再改变当前房间状态。</summary>
        public void Disconnect()
        {
            if (IsConnected || socket != null || !string.IsNullOrWhiteSpace(ConnectedAddress))
                MultiplayerLog.Info("disconnect-request", "server=" + ConnectedAddress);
            IsConnected = false;
            heartbeatAwaiting = false;
            heartbeatDeadline = 0f;
            hasServerClockSample = false;
            serverMinusLocalMilliseconds = 0;
            bestClockRoundTripMilliseconds = double.PositiveInfinity;
#if UNITY_IOS && !UNITY_EDITOR
            iosNativeSocketOpen = false;
            try { ChunithmWebSocketClose(); } catch { }
#endif
            try { cancellation?.Cancel(); } catch { }
            try { socket?.Abort(); } catch { }
            try { socket?.Dispose(); } catch { }
            cancellation = null;
            socket = null;
            ConnectedAddress = "";
        }

#if UNITY_IOS && !UNITY_EDITOR
        // Called by ChunithmWebSocket.mm through UnitySendMessage. These methods
        // execute on Unity's main thread and only enqueue the same transport
        // events used by the managed implementation, keeping room/UI behavior
        // identical across Android and iOS.
        public void OnIosWebSocketConnected(string unused)
        {
            iosNativeSocketOpen = true;
            events.Enqueue(new QueuedEvent { Kind = EventKind.Connected });
            var hello = new MultiplayerHelloMessage {
                playerName = iosPendingPlayerName,
                deviceId = iosPendingDeviceId,
                legacyDeviceId = iosPendingLegacyDeviceId,
                clientVersion = iosPendingClientVersion
            };
            string helloJson = JsonUtility.ToJson(hello);
            MultiplayerLog.Info("network-send", MultiplayerLog.SummarizeJson(helloJson));
            ChunithmWebSocketSend(helloJson);
        }

        public void OnIosWebSocketMessage(string json)
        {
            if (!iosNativeSocketOpen || string.IsNullOrWhiteSpace(json)) return;
            events.Enqueue(new QueuedEvent { Kind = EventKind.Message, Value = json });
        }

        public void OnIosWebSocketError(string error)
        {
            if (!iosNativeSocketOpen && string.IsNullOrWhiteSpace(ConnectedAddress)) return;
            iosNativeSocketOpen = false;
            events.Enqueue(new QueuedEvent {
                Kind = EventKind.Error,
                Value = string.IsNullOrWhiteSpace(error) ? "iOS WebSocket 连接失败" : error
            });
        }

        public void OnIosWebSocketClosed(string detail)
        {
            if (!iosNativeSocketOpen && string.IsNullOrWhiteSpace(ConnectedAddress)) return;
            iosNativeSocketOpen = false;
            if (!string.IsNullOrWhiteSpace(detail))
                MultiplayerLog.Warn("transport-close", detail);
            events.Enqueue(new QueuedEvent { Kind = EventKind.Disconnected });
        }

        [DllImport("__Internal")]
        private static extern void ChunithmWebSocketConnect(string url);

        [DllImport("__Internal")]
        private static extern void ChunithmWebSocketSend(string message);

        [DllImport("__Internal")]
        private static extern void ChunithmWebSocketClose();
#endif

        private static bool TryReadPong(string json, out MultiplayerPongMessage pong)
        {
            pong = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                pong = JsonUtility.FromJson<MultiplayerPongMessage>(json);
                return pong != null && pong.type == "pong";
            }
            catch { pong = null; return false; }
        }

        public void LeaveLobby() => Send("{\"type\":\"leaveLobby\"}");

        // WebSocket 的后台生命周期集中在此方法。异常必须转成队列事件，不能从 async void 入口直接抛出，
        // 否则联机加载页可能收不到失败通知而永久等待。
        private async Task ConnectAndReceive(Uri uri, string playerName, string deviceId, string legacyDeviceId, string clientVersion,
            CancellationToken token)
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
                    clientVersion = clientVersion
                };
                string helloJson = JsonUtility.ToJson(hello);
                MultiplayerLog.Info("network-send", MultiplayerLog.SummarizeJson(helloJson));
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
                    string description = DescribeTransportException(exception);
                    MultiplayerLog.Error("transport-exception", description);
                    events.Enqueue(new QueuedEvent { Kind = EventKind.Error, Value = description });
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

        private static string DescribeTransportException(Exception exception)
        {
            if (exception == null) return "未知网络错误";
            var builder = new StringBuilder();
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (builder.Length > 0) builder.Append(" <- ");
                builder.Append(current.GetType().Name).Append(": ").Append(current.Message);
            }
            return builder.ToString();
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
            {
                // A bare public domain uses secure WebSocket by default, so a
                // player only needs to enter e.g. hmqyhm.net. Explicit ports,
                // localhost and numeric IP addresses remain suitable for LAN
                // development and use plain WS unless the scheme says otherwise.
                string authority = value.Split('/')[0];
                bool hasExplicitPort = authority.LastIndexOf(':') > 0;
                string host = hasExplicitPort ? authority.Substring(0, authority.LastIndexOf(':')) : authority;
                bool localHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                                 host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                                 host.Equals("::1", StringComparison.OrdinalIgnoreCase);
                bool numericIp = Uri.CheckHostName(host) == UriHostNameType.IPv4 ||
                                 Uri.CheckHostName(host) == UriHostNameType.IPv6;
                value = (!hasExplicitPort && !localHost && !numericIp ? "wss://" : "ws://") + value;
            }
            if (value.IndexOf("/ws", StringComparison.OrdinalIgnoreCase) < 0) value = value.TrimEnd('/') + "/ws";
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || (uri.Scheme != "ws" && uri.Scheme != "wss"))
            {
                uri = null;
                error = "服务器地址格式无效，请填写域名、IP:端口或完整的 ws:// / wss:// 地址。";
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
