using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using ChunithmMobile.Judgement;
using ChunithmMobile.Platform;

namespace ChunithmMobile.Runtime
{
    // 联机 UI 编排层：连接登录、大厅、房间、歌曲同步、准备、游玩和多人结算页面。
    // 网络回调可能晚于当前页面，所有异步回调都必须检查房间/回合身份后再更新 UI。
    public sealed partial class AppUiShell
    {
        private MultiplayerSongTransfer multiplayerSongTransfer;
        private Button songSelectBackButton;
        private Text songSelectTitle;
        private Button songSelectModsButton;
        private Button songSelectDeleteButton;
        private Button songSelectPrimaryButton;
        private Button gameplayRestartButton;
        private Button gameplayExitButton;
        private Button resultReplayButton;
        private Button resultContinueButton;

        private InputField multiplayerBrowserSearchInput;
        private InputField multiplayerBrowserPasswordInput;
        private RectTransform multiplayerBrowserRoomContent;
        private Text multiplayerBrowserDetails;
        private Text multiplayerBrowserStatus;
        private Text multiplayerBrowserQuota;
        private Button multiplayerBrowserCreateButton;
        private Button multiplayerBrowserJoinButton;
        private Button multiplayerBrowserFilterButton;
        private sealed class MultiplayerRoomCardVisual
        {
            public RectTransform Card;
            public Button Button;
            public Text Label;
        }
        private readonly Dictionary<string, MultiplayerRoomCardVisual> multiplayerRoomCards =
            new Dictionary<string, MultiplayerRoomCardVisual>(StringComparer.OrdinalIgnoreCase);
        private readonly Stack<MultiplayerRoomCardVisual> multiplayerRoomCardPool =
            new Stack<MultiplayerRoomCardVisual>();
        private string multiplayerRoomListSignature = "";
        private RectTransform multiplayerBrowserFilterLayer;
        private Button multiplayerFilterIdleButton;
        private Button multiplayerFilterNotFullButton;
        private Button multiplayerFilterUnencryptedButton;
        private Button multiplayerFilterModeButton;
        private Button multiplayerFilterRoomTypeButton;
        private bool multiplayerFilterIdleOnly;
        private bool multiplayerFilterNotFullOnly;
        private bool multiplayerFilterUnencryptedOnly;
        private string multiplayerFilterMode = "";
        private string multiplayerFilterRoomType = "";

        private RectTransform multiplayerCreateLayer;
        private InputField multiplayerCreateRoomNameInput;
        private InputField multiplayerCreatePasswordInput;
        private Text multiplayerCreateRoomIdLabel;
        private Text multiplayerCreateModeLabel;
        private Text multiplayerCreateRoomTypeLabel;
        private Text multiplayerCreateStatus;
        private Text multiplayerCreatePlayersLabel;
        private Text multiplayerCreateSelectionLabel;
        private Text multiplayerCreateModsLabel;
        private Text multiplayerCreateListedLabel;
        private Button multiplayerCreatePlayersMinusButton;
        private Button multiplayerCreatePlayersPlusButton;
        private int multiplayerCreateMaxPlayers = 2;
        private string multiplayerCreateMode = "normal";
        private string multiplayerCreateRoomType = "personal";
        private string multiplayerCreateSelection = "host";
        private bool multiplayerCreateListed = true;
        private string multiplayerReservedRoomId;
        private MultiplayerModsMessage multiplayerCreateMods = new MultiplayerModsMessage();
        private RectTransform multiplayerModPickerLayer;
        private Text multiplayerModPickerTitle;
        private Button multiplayerModPickerApplyButton;
        private readonly List<Button> multiplayerModPickerButtons = new List<Button>();
        private readonly List<string> multiplayerModPickerKeys = new List<string>();
        private MultiplayerModsMessage multiplayerModPickerDraft;
        private bool multiplayerModPickerForRoom;

        private Text multiplayerLoginStatus;
        private Text multiplayerLoginDeviceLabel;
        private Button multiplayerLoginButton;
        private Text multiplayerRoomHeader;
        private Text multiplayerRoomSongLabel;
        private Text multiplayerRoomStatus;
        private Text multiplayerRoomSettingsSummary;
        private Image multiplayerRoomSongProgressFill;
        private RectTransform multiplayerRoomMemberContent;
        private Button multiplayerRoomSongButton;
        private Button multiplayerRoomModeButton;
        private Button multiplayerRoomModsButton;
        private Button multiplayerRoomSelectionButton;
        private Button multiplayerRoomVoteButton;
        private Button multiplayerRoomReadyButton;
        private Button multiplayerRoomLeaveButton;
        private Button multiplayerRoomExitRoundButton;

        private RectTransform multiplayerGroupContent;
        private Text multiplayerGroupHeader;
        private Text multiplayerGroupSong;
        private Image multiplayerGroupCover;
        private Text multiplayerGroupStatus;
        private Button multiplayerGroupContinueButton;

        private bool multiplayerSelectingSong;
        private bool multiplayerInRound;
        private bool multiplayerRoundStarted;
        private bool multiplayerAuthenticatedV2;
        private bool multiplayerUploading;
        private bool multiplayerDownloading;
        private Coroutine multiplayerSongPreparationCoroutine;
        private string multiplayerSongPreparationKey;
        private Coroutine multiplayerRoundPreparationCoroutine;
        // The preparation and reveal coroutines have different jobs: the former
        // binds local resources, while the latter only reveals the already-bound
        // playfield at the server's common time. Keeping this handle prevents a
        // stale reveal coroutine from surviving a cancelled round.
        private Coroutine multiplayerRoundRevealCoroutine;
        private int multiplayerRoundPreparationToken;
        private double multiplayerPendingRoundStartDsp;
        private bool multiplayerRoundStartPending;
        private bool multiplayerRoundPlaybackPrepared;
        private bool multiplayerRoundRevealStarted;
        private string multiplayerPendingTransferHash;
        private long multiplayerPendingTransferSize;
        private string multiplayerPendingTransferPath;
        private bool multiplayerPersonalResultPending;
        private bool multiplayerGroupResultPending;
        private bool multiplayerLeavingLobby;
        private float multiplayerPersonalResultDeadline;
        private float multiplayerGroupResultDeadline;
        private long multiplayerBackgroundAtMs;
        private long multiplayerLobbyReentryUntilMs;
        private long multiplayerLastQuotaServerTime;
        private int multiplayerLastReentrySeconds = -1;
        private Coroutine multiplayerReconnectCoroutine;
        private bool multiplayerManualDisconnect;
        private bool multiplayerRetryInProgress;
        private string multiplayerLastConnectionError = "";
        private int multiplayerReconnectAttempt;
        private string multiplayerRoundId;
        private string multiplayerSessionToken;
        private float multiplayerLastTransferReportAt = -10f;
        private string multiplayerLastTransferReportKey;
        private MultiplayerPreparedPackage multiplayerPreparedPackage;
        private MultiplayerSongDto multiplayerSelectedSong;
        private MultiplayerLeaderboardEntryDto[] multiplayerLeaderboard = Array.Empty<MultiplayerLeaderboardEntryDto>();

        private void BuildMultiplayerV2()
        {
            RectTransform login = Page(AppPage.Multiplayer, ink);
            AddMeteorBackground(login, MeteorBackgroundProfile.Quiet);
            ButtonText("‹", login, new Vector2(.02f, .90f), new Vector2(.08f, .98f), surfaceRaised,
                LeaveMultiplayerModeV2);
            Label("在线登录", login, 38, TextAnchor.MiddleLeft, new Vector2(.10f, .88f), new Vector2(.42f, .98f), Color.white);
            RectTransform card = Panel("Server login", login, surface, new Vector2(.18f, .18f), new Vector2(.82f, .84f));
            Label("连接服务器", card, 30, TextAnchor.MiddleLeft, new Vector2(.09f, .84f), new Vector2(.91f, .95f), cyan);
            Label("客户端版本必须与房间一致", card, 17, TextAnchor.MiddleLeft, new Vector2(.09f, .76f), new Vector2(.91f, .84f), muted);
            Label("服务器地址", card, 18, TextAnchor.MiddleLeft, new Vector2(.09f, .64f), new Vector2(.40f, .71f), muted);
            multiplayerServerInput = Input("域名、IP:端口或完整地址", card, new Vector2(.09f, .53f), new Vector2(.91f, .63f));
            RestoreMultiplayerServerV2();
            multiplayerServerInput.onValueChanged.AddListener(_ => CacheMultiplayerDraftV2());
            multiplayerServerInput.onEndEdit.AddListener(_ => SaveMultiplayerDraftV2());
            Label("玩家名称", card, 18, TextAnchor.MiddleLeft, new Vector2(.09f, .44f), new Vector2(.40f, .51f), muted);
            multiplayerPlayerNameInput = Input("1 至 6 个中文或英文字符", card, new Vector2(.09f, .33f), new Vector2(.91f, .43f));
            multiplayerPlayerNameInput.characterLimit = 6;
            multiplayerPlayerNameInput.SetTextWithoutNotify(PlayerPrefs.GetString("chunithm_mobile_multiplayer_player", ""));
            multiplayerPlayerNameInput.onValueChanged.AddListener(value => LimitMultiplayerName(multiplayerPlayerNameInput, value));
            multiplayerPlayerNameInput.onValueChanged.AddListener(_ => CacheMultiplayerDraftV2());
            multiplayerPlayerNameInput.onEndEdit.AddListener(_ => SaveMultiplayerDraftV2());
            multiplayerLoginDeviceLabel = Label("设备 ID：读取中", card, 16, TextAnchor.MiddleLeft,
                new Vector2(.09f, .24f), new Vector2(.91f, .30f), muted);
            multiplayerLoginDeviceLabel.text = "设备 ID：" + MultiplayerClient.GetStableDeviceId();
            multiplayerLoginButton = ButtonText("进入在线大厅", card, new Vector2(.09f, .12f), new Vector2(.91f, .21f), cyan,
                ConnectMultiplayerV2);
            multiplayerLoginStatus = Label("输入地址和名称后进入服务器。", card, 17, TextAnchor.MiddleCenter,
                new Vector2(.09f, .03f), new Vector2(.91f, .11f), muted);

            RectTransform browser = Page(AppPage.MultiplayerBrowser, ink);
            AddMeteorBackground(browser, MeteorBackgroundProfile.Quiet);
            ButtonText("‹", browser, new Vector2(.02f, .91f), new Vector2(.07f, .985f), surfaceRaised,
                BackToMultiplayerLoginV2);
            Label("大厅", browser, 32, TextAnchor.MiddleLeft, new Vector2(.09f, .91f), new Vector2(.24f, .985f), Color.white);
            multiplayerBrowserSearchInput = Input("搜索房间名、房主或六位房间号", browser,
                new Vector2(.25f, .91f), new Vector2(.48f, .985f));
            multiplayerBrowserSearchInput.onEndEdit.AddListener(_ => RequestMultiplayerRoomsV2());
            multiplayerBrowserFilterButton = ButtonText("筛选", browser, new Vector2(.49f, .91f), new Vector2(.57f, .985f), surfaceRaised,
                ToggleMultiplayerFilterPickerV2);
            multiplayerBrowserCreateButton = ButtonText("创建房间\n剩余 --", browser, new Vector2(.59f, .91f), new Vector2(.72f, .985f), cyan,
                BeginCreateMultiplayerRoomV2);
            ButtonText("刷新", browser, new Vector2(.73f, .91f), new Vector2(.80f, .985f), surfaceRaised,
                RequestMultiplayerRoomsV2);
            multiplayerBrowserQuota = Label("创建剩余 --    加入剩余 --", browser, 16, TextAnchor.MiddleRight,
                new Vector2(.81f, .91f), new Vector2(.98f, .985f), muted);
            RectTransform browserList = Panel("Room list", browser, surface, new Vector2(.025f, .08f), new Vector2(.43f, .88f));
            RectTransform browserViewport = Panel("Room viewport", browserList, Color.clear, new Vector2(.015f, .015f), new Vector2(.985f, .985f));
            browserViewport.gameObject.AddComponent<RectMask2D>();
            multiplayerBrowserRoomContent = RectObject("Room cards", browserViewport, new Vector2(0, 1), new Vector2(1, 1));
            multiplayerBrowserRoomContent.pivot = new Vector2(.5f, 1f);
            ScrollRect browserScroll = browserList.gameObject.AddComponent<ScrollRect>();
            browserScroll.content = multiplayerBrowserRoomContent;
            browserScroll.viewport = browserViewport;
            browserScroll.horizontal = false;
            browserScroll.vertical = true;
            browserScroll.movementType = ScrollRect.MovementType.Elastic;
            RectTransform detail = Panel("Room detail", browser, surface, new Vector2(.45f, .08f), new Vector2(.975f, .88f));
            multiplayerBrowserDetails = Label("选择一个房间查看详情。", detail, 20, TextAnchor.UpperLeft,
                new Vector2(.07f, .48f), new Vector2(.93f, .90f), Color.white);
            multiplayerBrowserPasswordInput = Input("需要密码时填写", detail, new Vector2(.07f, .34f), new Vector2(.56f, .44f));
            multiplayerBrowserPasswordInput.contentType = InputField.ContentType.Password;
            multiplayerBrowserJoinButton = ButtonText("加入选中的房间", detail, new Vector2(.60f, .34f), new Vector2(.93f, .44f), cyan,
                JoinSelectedMultiplayerRoomV2);
            multiplayerBrowserStatus = Label("等待服务器列表…", detail, 17, TextAnchor.UpperLeft,
                new Vector2(.07f, .08f), new Vector2(.93f, .29f), muted);

            BuildMultiplayerCreateOverlayV2(browser);
            BuildMultiplayerFilterOverlayV2(browser);

            RectTransform room = Page(AppPage.MultiplayerRoom, ink);
            AddMeteorBackground(room, MeteorBackgroundProfile.Quiet);
            ButtonText("‹", room, new Vector2(.02f, .91f), new Vector2(.07f, .985f), surfaceRaised,
                LeaveMultiplayerRoomV2);
            multiplayerRoomHeader = Label("准备房间", room, 31, TextAnchor.MiddleLeft,
                new Vector2(.09f, .91f), new Vector2(.65f, .985f), Color.white);
            multiplayerRoomStatus = Label("等待房间状态…", room, 16, TextAnchor.MiddleRight,
                new Vector2(.65f, .91f), new Vector2(.98f, .985f), muted);
            RectTransform membersPanel = Panel("Members", room, surface, new Vector2(.025f, .42f), new Vector2(.58f, .88f));
            Label("成员状态", membersPanel, 22, TextAnchor.MiddleLeft, new Vector2(.05f, .88f), new Vector2(.45f, .98f), cyan);
            RectTransform memberViewport = Panel("Member viewport", membersPanel, Color.clear, new Vector2(.04f, .07f), new Vector2(.96f, .84f));
            memberViewport.gameObject.AddComponent<RectMask2D>();
            multiplayerRoomMemberContent = RectObject("Member rows", memberViewport, new Vector2(0, 1), new Vector2(1, 1));
            multiplayerRoomMemberContent.pivot = new Vector2(.5f, 1f);
            ScrollRect memberScrollHost = membersPanel.gameObject.AddComponent<ScrollRect>();
            memberScrollHost.content = multiplayerRoomMemberContent;
            memberScrollHost.viewport = memberViewport;
            memberScrollHost.horizontal = false;
            memberScrollHost.vertical = true;
            RectTransform songPanel = Panel("Selected song", room, surface, new Vector2(.025f, .08f), new Vector2(.58f, .38f));
            Label("当前歌曲", songPanel, 22, TextAnchor.MiddleLeft, new Vector2(.06f, .78f), new Vector2(.40f, .94f), cyan);
            multiplayerRoomSongLabel = Label("房主尚未选择歌曲", songPanel, 21, TextAnchor.UpperLeft,
                new Vector2(.06f, .34f), new Vector2(.94f, .76f), Color.white);
            RectTransform songProgressTrack = Panel("Song transfer progress", songPanel,
                new Color(.06f, .12f, .16f, .96f), new Vector2(.06f, .025f), new Vector2(.94f, .07f));
            multiplayerRoomSongProgressFill = Panel("Song transfer progress fill", songProgressTrack, cyan,
                Vector2.zero, new Vector2(0, 1)).GetComponent<Image>();
            multiplayerRoomSongButton = ButtonText("选择歌曲", songPanel, new Vector2(.06f, .08f), new Vector2(.94f, .28f), cyan,
                OpenOrSyncMultiplayerSongV2);
            RectTransform actions = Panel("Room actions", room, surface, new Vector2(.61f, .08f), new Vector2(.975f, .88f));
            Label("房间设置", actions, 24, TextAnchor.MiddleLeft, new Vector2(.08f, .90f), new Vector2(.92f, .98f), cyan);
            multiplayerRoomModeButton = ButtonText("模式", actions, new Vector2(.08f, .73f), new Vector2(.92f, .84f), surfaceRaised,
                ToggleMultiplayerRoomModeV2);
            multiplayerRoomModsButton = ButtonText("MOD", actions, new Vector2(.08f, .60f), new Vector2(.92f, .71f), surfaceRaised,
                ToggleMultiplayerRoomModsV2);
            multiplayerRoomSelectionButton = ButtonText("选歌方式", actions, new Vector2(.08f, .47f), new Vector2(.92f, .58f), surfaceRaised,
                ToggleMultiplayerRoomSelectionV2);
            multiplayerRoomVoteButton = ButtonText("投票换歌", actions, new Vector2(.08f, .34f), new Vector2(.92f, .45f), surfaceRaised,
                ToggleMultiplayerSongChangeVoteV2);
            multiplayerRoomReadyButton = ButtonText("准备", actions, new Vector2(.08f, .21f), new Vector2(.92f, .32f), new Color(.12f, .58f, .32f),
                ToggleMultiplayerReadyV2);
            multiplayerRoomLeaveButton = ButtonText("退出房间", actions, new Vector2(.08f, .06f), new Vector2(.48f, .18f), surfaceRaised,
                LeaveMultiplayerRoomV2);
            multiplayerRoomExitRoundButton = ButtonText("退出本局", actions, new Vector2(.52f, .06f), new Vector2(.92f, .18f), new Color(.55f, .18f, .20f),
                ExitMultiplayerRoundV2);

            RectTransform group = Page(AppPage.MultiplayerGroupResults, ink);
            AddMeteorBackground(group, MeteorBackgroundProfile.Quiet);
            multiplayerGroupHeader = Label("联机成绩排名", group, 34, TextAnchor.MiddleLeft,
                new Vector2(.05f, .90f), new Vector2(.60f, .98f), Color.white);
            multiplayerGroupSong = Label("歌曲", group, 23, TextAnchor.UpperLeft,
                new Vector2(.06f, .14f), new Vector2(.36f, .35f), Color.white);
            RectTransform groupCoverFrame = Panel("Group cover", group, surfaceRaised, new Vector2(.07f, .39f), new Vector2(.33f, .84f));
            multiplayerGroupCover = Panel("Group cover image", groupCoverFrame, Color.clear, new Vector2(.03f, .03f), new Vector2(.97f, .97f)).GetComponent<Image>();
            RectTransform groupList = Panel("Group leaderboard", group, surface, new Vector2(.39f, .10f), new Vector2(.96f, .88f));
            RectTransform groupViewport = Panel("Group results viewport", groupList, Color.clear, new Vector2(.025f, .07f), new Vector2(.975f, .96f));
            groupViewport.gameObject.AddComponent<RectMask2D>();
            multiplayerGroupContent = RectObject("Group result rows", groupViewport, new Vector2(0, 1), new Vector2(1, 1));
            multiplayerGroupContent.pivot = new Vector2(.5f, 1f);
            ScrollRect groupScroll = groupList.gameObject.AddComponent<ScrollRect>();
            groupScroll.content = multiplayerGroupContent;
            groupScroll.viewport = groupViewport;
            groupScroll.horizontal = false;
            groupScroll.vertical = true;
            multiplayerGroupStatus = Label("10 秒后自动返回准备房间", group, 18, TextAnchor.MiddleLeft,
                new Vector2(.39f, .035f), new Vector2(.70f, .09f), muted);
            multiplayerGroupContinueButton = ButtonText("继续", group, new Vector2(.74f, .025f), new Vector2(.96f, .095f), cyan,
                ConfirmMultiplayerGroupResultsV2);
            BuildMultiplayerModPickerV2();
        }

        private void BuildMultiplayerCreateOverlayV2(RectTransform browser)
        {
            multiplayerCreateLayer = Panel("Create room overlay", browser, new Color(.008f, .015f, .03f, .97f),
                new Vector2(.18f, .10f), new Vector2(.82f, .90f));
            Label("创建房间", multiplayerCreateLayer, 30, TextAnchor.MiddleLeft,
                new Vector2(.08f, .88f), new Vector2(.92f, .97f), Color.white);
            multiplayerCreateRoomIdLabel = Label("房间号：等待服务端分配", multiplayerCreateLayer, 20, TextAnchor.MiddleLeft,
                new Vector2(.08f, .80f), new Vector2(.92f, .87f), cyan);
            multiplayerCreateRoomNameInput = Input("房间名称（最多 6 个字）", multiplayerCreateLayer,
                new Vector2(.08f, .69f), new Vector2(.92f, .78f));
            multiplayerCreateRoomNameInput.characterLimit = 6;
            multiplayerCreateRoomNameInput.onValueChanged.AddListener(value => LimitMultiplayerName(multiplayerCreateRoomNameInput, value));
            multiplayerCreatePasswordInput = Input("密码（可选，至少 4 位）", multiplayerCreateLayer,
                new Vector2(.08f, .58f), new Vector2(.92f, .67f));
            multiplayerCreatePasswordInput.contentType = InputField.ContentType.Password;
            multiplayerCreatePlayersMinusButton = ButtonText("−", multiplayerCreateLayer, new Vector2(.08f, .47f), new Vector2(.17f, .56f), surfaceRaised,
                () => ChangeMultiplayerCreatePlayersV2(-1));
            multiplayerCreatePlayersLabel = Label("人数：2", multiplayerCreateLayer, 17, TextAnchor.MiddleCenter,
                new Vector2(.17f, .47f), new Vector2(.34f, .56f), Color.white);
            multiplayerCreatePlayersPlusButton = ButtonText("+", multiplayerCreateLayer, new Vector2(.34f, .47f), new Vector2(.43f, .56f), surfaceRaised,
                () => ChangeMultiplayerCreatePlayersV2(1));
            multiplayerCreateModeLabel = ButtonText("模式：普通", multiplayerCreateLayer, new Vector2(.47f, .47f), new Vector2(.92f, .56f), surfaceRaised,
                CycleMultiplayerCreateModeV2).GetComponentInChildren<Text>();
            multiplayerCreateRoomTypeLabel = ButtonText("房间：个人（常驻房间由服务端后台创建）", multiplayerCreateLayer, new Vector2(.08f, .36f), new Vector2(.50f, .45f), surfaceRaised,
                null).GetComponentInChildren<Text>();
            multiplayerCreateSelectionLabel = ButtonText("选歌：房主专选", multiplayerCreateLayer, new Vector2(.54f, .36f), new Vector2(.92f, .45f), surfaceRaised,
                CycleMultiplayerCreateSelectionV2).GetComponentInChildren<Text>();
            multiplayerCreateListedLabel = ButtonText("显示在大厅：是", multiplayerCreateLayer, new Vector2(.08f, .25f), new Vector2(.50f, .34f), surfaceRaised,
                ToggleMultiplayerCreateListedV2).GetComponentInChildren<Text>();
            multiplayerCreateModsLabel = ButtonText("MOD：使用当前设置", multiplayerCreateLayer, new Vector2(.54f, .25f), new Vector2(.92f, .34f), surfaceRaised,
                ToggleMultiplayerCreateModsV2).GetComponentInChildren<Text>();
            ButtonText("取消", multiplayerCreateLayer, new Vector2(.08f, .08f), new Vector2(.45f, .19f), surfaceRaised,
                CancelCreateMultiplayerRoomV2);
            ButtonText("创建房间", multiplayerCreateLayer, new Vector2(.55f, .08f), new Vector2(.92f, .19f), cyan,
                SubmitCreateMultiplayerRoomV2);
            multiplayerCreateStatus = Label("", multiplayerCreateLayer, 14, TextAnchor.MiddleCenter,
                new Vector2(.08f, .01f), new Vector2(.92f, .07f), muted);
            multiplayerCreateLayer.gameObject.SetActive(false);
        }

        private void BuildMultiplayerFilterOverlayV2(RectTransform browser)
        {
            multiplayerBrowserFilterLayer = Panel("Room search filters", browser, new Color(.008f, .015f, .03f, .98f),
                new Vector2(.22f, .13f), new Vector2(.78f, .86f));
            Label("搜索限制", multiplayerBrowserFilterLayer, 28, TextAnchor.MiddleLeft,
                new Vector2(.08f, .84f), new Vector2(.70f, .96f), Color.white);
            multiplayerFilterIdleButton = ButtonText("", multiplayerBrowserFilterLayer,
                new Vector2(.08f, .64f), new Vector2(.44f, .76f), surfaceRaised, ToggleMultiplayerFilterIdleV2);
            multiplayerFilterNotFullButton = ButtonText("", multiplayerBrowserFilterLayer,
                new Vector2(.56f, .64f), new Vector2(.92f, .76f), surfaceRaised, ToggleMultiplayerFilterNotFullV2);
            multiplayerFilterUnencryptedButton = ButtonText("", multiplayerBrowserFilterLayer,
                new Vector2(.08f, .47f), new Vector2(.44f, .59f), surfaceRaised, ToggleMultiplayerFilterUnencryptedV2);
            multiplayerFilterModeButton = ButtonText("", multiplayerBrowserFilterLayer,
                new Vector2(.56f, .47f), new Vector2(.92f, .59f), surfaceRaised, CycleMultiplayerFilterModeV2);
            multiplayerFilterRoomTypeButton = ButtonText("", multiplayerBrowserFilterLayer,
                new Vector2(.08f, .32f), new Vector2(.44f, .44f), surfaceRaised, CycleMultiplayerFilterRoomTypeV2);
            ButtonText("清除限制", multiplayerBrowserFilterLayer, new Vector2(.08f, .18f), new Vector2(.44f, .30f), surfaceRaised,
                ClearMultiplayerFiltersV2);
            ButtonText("应用", multiplayerBrowserFilterLayer, new Vector2(.56f, .18f), new Vector2(.92f, .30f), cyan,
                ApplyMultiplayerFiltersV2);
            Label("限制只影响大厅显示；正在游玩的房间仍由服务器禁止加入。", multiplayerBrowserFilterLayer, 15,
                TextAnchor.UpperLeft, new Vector2(.08f, .06f), new Vector2(.92f, .16f), muted);
            multiplayerBrowserFilterLayer.gameObject.SetActive(false);
            RefreshMultiplayerFiltersV2();
        }

        private void BuildMultiplayerModPickerV2()
        {
            RectTransform card = BuildModDialog("Multiplayer MOD picker", out multiplayerModPickerLayer,
                out multiplayerModPickerTitle, "选择 MOD", HideMultiplayerModPickerV2);
            string[] keys = { "fullscreenTouchpad", "autoPlay", "airAreaAuto", "directionalAirAuto", "mineImmunity", "airLongNoMotion", "nonMissJusticeCritical", "mirrorChart", "flickAllTap", "uniformSpeed" };
            multiplayerModPickerButtons.Clear();
            multiplayerModPickerKeys.Clear();
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                bool scoring = key == "airLongNoMotion" || key == "mirrorChart" || key == "uniformSpeed";
                int index = i;
                Button button = ModOption(card, index % 2, index / 2, scoring,
                    () => ToggleMultiplayerModPickerKeyV2(key));
                multiplayerModPickerButtons.Add(button);
                multiplayerModPickerKeys.Add(key);
            }
            float[] rates = { .8f, 1.2f, 1.5f };
            for (int i = 0; i < rates.Length; i++)
            {
                string key = "musicRate:" + rates[i].ToString("0.0", CultureInfo.InvariantCulture);
                Button button = ModOption(card, i, 0, false,
                    () => ToggleMultiplayerModPickerKeyV2(key), true);
                multiplayerModPickerButtons.Add(button);
                multiplayerModPickerKeys.Add(key);
            }
            RelayoutModSections(card);
            ButtonText("恢复默认", card, new Vector2(.05f, .045f), new Vector2(.27f, .135f), surface,
                () => { multiplayerModPickerDraft = new MultiplayerModsMessage(); RefreshMultiplayerModPickerV2(); });
            ButtonText("取消", card, new Vector2(.49f, .045f), new Vector2(.71f, .135f), surface, HideMultiplayerModPickerV2);
            multiplayerModPickerApplyButton = ButtonText("应用", card, new Vector2(.73f, .045f), new Vector2(.95f, .135f), cyan,
                ApplyMultiplayerModPickerV2);
            multiplayerModPickerLayer.gameObject.SetActive(false);
        }

        private static MultiplayerModsMessage CopyMultiplayerModsV2(MultiplayerModsMessage source)
        {
            MultiplayerModsMessage value = source ?? new MultiplayerModsMessage();
            return new MultiplayerModsMessage {
                autoPlay = false,
                airAreaAuto = value.airAreaAuto,
                directionalAirAuto = value.directionalAirAuto,
                airLongNoMotion = value.airLongNoMotion,
                mineImmunity = value.mineImmunity,
                mirrorChart = value.mirrorChart,
                flickAllTap = value.flickAllTap,
                fullscreenTouchpad = value.fullscreenTouchpad,
                nonMissJusticeCritical = value.nonMissJusticeCritical,
                uniformSpeed = value.uniformSpeed,
                musicRate = value.musicRate
            };
        }

        private bool MultiplayerModPickerScoringOnlyV2()
        {
            return multiplayerModPickerForRoom
                ? activeMultiplayerRoom != null && activeMultiplayerRoom.mode == "normal"
                : multiplayerCreateMode == "normal";
        }

        private static bool IsNonScoringMultiplayerModKeyV2(string key)
        {
            return key == "autoPlay" || key == "airAreaAuto" || key == "directionalAirAuto" || key == "mineImmunity" || key == "flickAllTap" ||
                key == "fullscreenTouchpad" || key == "nonMissJusticeCritical" || key.StartsWith("musicRate:", StringComparison.Ordinal);
        }

        private static bool GetMultiplayerModValueV2(MultiplayerModsMessage value, string key)
        {
            switch (key)
            {
                case "autoPlay": return value.autoPlay;
                case "airAreaAuto": return value.airAreaAuto;
                case "directionalAirAuto": return value.directionalAirAuto;
                case "airLongNoMotion": return value.airLongNoMotion;
                case "mineImmunity": return value.mineImmunity;
                case "mirrorChart": return value.mirrorChart;
                case "flickAllTap": return value.flickAllTap;
                case "fullscreenTouchpad": return value.fullscreenTouchpad;
                case "nonMissJusticeCritical": return value.nonMissJusticeCritical;
                case "uniformSpeed": return value.uniformSpeed;
                default:
                    if (key.StartsWith("musicRate:", StringComparison.Ordinal) && float.TryParse(key.Substring(10), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float rate)) return Math.Abs(value.musicRate - rate) < .01f;
                    return false;
            }
        }

        private void SetMultiplayerModPickerButtonV2(Button button, string key, MultiplayerModsMessage value, bool scoringOnly)
        {
            bool disabled = scoringOnly && IsNonScoringMultiplayerModKeyV2(key);
            bool selected = !disabled && GetMultiplayerModValueV2(value, key);
            string name = key == "autoPlay" ? "全谱面 AUTO" : key == "flickAllTap" ? "Flick 可点击判定" : key == "airAreaAuto" ? "AIR 区域 AUTO" :
                key == "directionalAirAuto" ? "上下 AIR AUTO" :
                key == "airLongNoMotion" ? "air长条简单模式" :
                key == "mineImmunity" ? "免疫地雷音符" :
                key == "nonMissJusticeCritical" ? "简单判定" :
                key == "mirrorChart" ? "谱面镜像" : key == "fullscreenTouchpad" ? "全屏触控板" :
                key == "uniformSpeed" ? "匀速模式" : key.Substring(10) + "x";
            UpdateModOption(button, name, selected, disabled);
        }

        private void RefreshMultiplayerModPickerV2()
        {
            if (multiplayerModPickerLayer == null || multiplayerModPickerDraft == null) return;
            bool scoringOnly = MultiplayerModPickerScoringOnlyV2();
            if (multiplayerModPickerTitle != null)
                multiplayerModPickerTitle.text = UiLocalization.Translate(scoringOnly ? "选择 MOD · 普通模式" : "选择 MOD · 休闲模式");
            for (int i = 0; i < multiplayerModPickerButtons.Count && i < multiplayerModPickerKeys.Count; i++)
                SetMultiplayerModPickerButtonV2(multiplayerModPickerButtons[i], multiplayerModPickerKeys[i], multiplayerModPickerDraft, scoringOnly);
        }

        private void OpenMultiplayerModPickerV2(bool forRoom)
        {
            if (forRoom && !IsLocalMultiplayerHostV2()) return;
            multiplayerModPickerForRoom = forRoom;
            multiplayerModPickerDraft = CopyMultiplayerModsV2(forRoom
                ? activeMultiplayerRoom?.mods
                : multiplayerCreateMods);
            RefreshMultiplayerModPickerV2();
            multiplayerModPickerLayer.gameObject.SetActive(true);
            multiplayerModPickerLayer.SetAsLastSibling();
            CanvasGroup group = multiplayerModPickerLayer.GetComponent<CanvasGroup>() ??
                multiplayerModPickerLayer.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0;
            StartCoroutine(Fade(group, 0, 1, .16f));
        }

        private void HideMultiplayerModPickerV2()
        {
            if (multiplayerModPickerLayer != null) multiplayerModPickerLayer.gameObject.SetActive(false);
        }

        private void ToggleMultiplayerModPickerKeyV2(string key)
        {
            if (multiplayerModPickerDraft == null || (MultiplayerModPickerScoringOnlyV2() && IsNonScoringMultiplayerModKeyV2(key))) return;
            switch (key)
            {
                case "autoPlay": multiplayerModPickerDraft.autoPlay = !multiplayerModPickerDraft.autoPlay; break;
                case "airAreaAuto": multiplayerModPickerDraft.airAreaAuto = !multiplayerModPickerDraft.airAreaAuto; break;
                case "directionalAirAuto": multiplayerModPickerDraft.directionalAirAuto = !multiplayerModPickerDraft.directionalAirAuto; break;
                case "airLongNoMotion": multiplayerModPickerDraft.airLongNoMotion = !multiplayerModPickerDraft.airLongNoMotion; break;
                case "mineImmunity": multiplayerModPickerDraft.mineImmunity = !multiplayerModPickerDraft.mineImmunity; break;
                case "mirrorChart": multiplayerModPickerDraft.mirrorChart = !multiplayerModPickerDraft.mirrorChart; break;
                case "flickAllTap": multiplayerModPickerDraft.flickAllTap = !multiplayerModPickerDraft.flickAllTap; break;
                case "fullscreenTouchpad": multiplayerModPickerDraft.fullscreenTouchpad = !multiplayerModPickerDraft.fullscreenTouchpad; break;
                case "nonMissJusticeCritical": multiplayerModPickerDraft.nonMissJusticeCritical = !multiplayerModPickerDraft.nonMissJusticeCritical; break;
                case "uniformSpeed": multiplayerModPickerDraft.uniformSpeed = !multiplayerModPickerDraft.uniformSpeed; break;
                default:
                    if (key.StartsWith("musicRate:", StringComparison.Ordinal) && float.TryParse(key.Substring(10), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float rate))
                        multiplayerModPickerDraft.musicRate = Math.Abs(multiplayerModPickerDraft.musicRate - rate) < .01f ? 1f : rate;
                    break;
            }
            RefreshMultiplayerModPickerV2();
        }

        private void ApplyMultiplayerModPickerV2()
        {
            if (multiplayerModPickerDraft == null) { HideMultiplayerModPickerV2(); return; }
            if (multiplayerModPickerForRoom && activeMultiplayerRoom != null && IsLocalMultiplayerHostV2())
                SendMultiplayerRoomSettingsV2(activeMultiplayerRoom.mode, activeMultiplayerRoom.selectionMode,
                    CopyMultiplayerModsV2(multiplayerModPickerDraft));
            else if (!multiplayerModPickerForRoom)
                multiplayerCreateMods = CopyMultiplayerModsV2(multiplayerModPickerDraft);
            HideMultiplayerModPickerV2();
            RefreshCreateMultiplayerOverlayV2();
        }

        private void ToggleMultiplayerFilterPickerV2()
        {
            if (multiplayerBrowserFilterLayer == null) return;
            multiplayerBrowserFilterLayer.gameObject.SetActive(!multiplayerBrowserFilterLayer.gameObject.activeSelf);
            if (multiplayerBrowserFilterLayer.gameObject.activeSelf)
            {
                multiplayerBrowserFilterLayer.SetAsLastSibling();
                RefreshMultiplayerFiltersV2();
            }
        }

        private void RefreshMultiplayerFiltersV2()
        {
            SetButtonLabel(multiplayerFilterIdleButton, multiplayerFilterIdleOnly ? "空闲：是" : "空闲：不限");
            SetButtonLabel(multiplayerFilterNotFullButton, multiplayerFilterNotFullOnly ? "未满人：是" : "未满人：不限");
            SetButtonLabel(multiplayerFilterUnencryptedButton, multiplayerFilterUnencryptedOnly ? "未加密：是" : "未加密：不限");
            SetButtonLabel(multiplayerFilterModeButton, multiplayerFilterMode == "casual" ? "模式：休闲" : multiplayerFilterMode == "normal" ? "模式：普通" : "模式：不限");
            SetButtonLabel(multiplayerFilterRoomTypeButton, multiplayerFilterRoomType == "personal" ? "房间：个人" : multiplayerFilterRoomType == "persistent" ? "房间：常驻" : "房间：全部");
            SetFilterButtonColorV2(multiplayerFilterIdleButton, multiplayerFilterIdleOnly);
            SetFilterButtonColorV2(multiplayerFilterNotFullButton, multiplayerFilterNotFullOnly);
            SetFilterButtonColorV2(multiplayerFilterUnencryptedButton, multiplayerFilterUnencryptedOnly);
            SetFilterButtonColorV2(multiplayerFilterModeButton, !string.IsNullOrEmpty(multiplayerFilterMode));
            SetFilterButtonColorV2(multiplayerFilterRoomTypeButton, !string.IsNullOrEmpty(multiplayerFilterRoomType));
        }

        private void SetFilterButtonColorV2(Button button, bool selected)
        {
            if (button == null) return;
            Image image = button.GetComponent<Image>();
            if (image != null) image.color = selected
                ? (HighContrastUi ? UiThemeContrast.Yellow : new Color(.10f, .56f, .32f, 1f))
                : surfaceRaised;
        }

        private void ToggleMultiplayerFilterIdleV2()
        {
            multiplayerFilterIdleOnly = !multiplayerFilterIdleOnly;
            MultiplayerLog.Info("room-filter-changed", "idleOnly=" + multiplayerFilterIdleOnly);
            RefreshMultiplayerFiltersV2();
        }
        private void ToggleMultiplayerFilterNotFullV2()
        {
            multiplayerFilterNotFullOnly = !multiplayerFilterNotFullOnly;
            MultiplayerLog.Info("room-filter-changed", "notFullOnly=" + multiplayerFilterNotFullOnly);
            RefreshMultiplayerFiltersV2();
        }
        private void ToggleMultiplayerFilterUnencryptedV2()
        {
            multiplayerFilterUnencryptedOnly = !multiplayerFilterUnencryptedOnly;
            MultiplayerLog.Info("room-filter-changed", "unencryptedOnly=" + multiplayerFilterUnencryptedOnly);
            RefreshMultiplayerFiltersV2();
        }
        private void CycleMultiplayerFilterModeV2()
        {
            multiplayerFilterMode = multiplayerFilterMode == "" ? "casual" : multiplayerFilterMode == "casual" ? "normal" : "";
            MultiplayerLog.Info("room-filter-changed", "mode=" + (string.IsNullOrEmpty(multiplayerFilterMode) ? "any" : multiplayerFilterMode));
            RefreshMultiplayerFiltersV2();
        }
        private void CycleMultiplayerFilterRoomTypeV2()
        {
            multiplayerFilterRoomType = multiplayerFilterRoomType == "" ? "personal" : multiplayerFilterRoomType == "personal" ? "persistent" : "";
            RefreshMultiplayerFiltersV2();
        }
        private void ClearMultiplayerFiltersV2()
        {
            multiplayerFilterIdleOnly = false;
            multiplayerFilterNotFullOnly = false;
            multiplayerFilterUnencryptedOnly = false;
            multiplayerFilterMode = "";
            multiplayerFilterRoomType = "";
            MultiplayerLog.Info("room-filter-cleared");
            RefreshMultiplayerFiltersV2();
        }
        private void ApplyMultiplayerFiltersV2()
        {
            if (multiplayerBrowserFilterLayer != null) multiplayerBrowserFilterLayer.gameObject.SetActive(false);
            MultiplayerLog.Info("room-filter-applied");
            RequestMultiplayerRoomsV2();
        }

        private void BuildRoomListContentV2(MultiplayerRoomDto[] rooms)
        {
            if (multiplayerBrowserRoomContent == null) return;
            MultiplayerRoomDto[] source = (rooms ?? Array.Empty<MultiplayerRoomDto>())
                .Where(room => room != null && !string.IsNullOrWhiteSpace(room.roomId))
                .ToArray();
            string signature = BuildMultiplayerRoomListSignatureV2(source);
            if (string.Equals(signature, multiplayerRoomListSignature, StringComparison.Ordinal)) return;
            multiplayerRoomListSignature = signature;

            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            foreach (MultiplayerRoomDto room in source)
            {
                used.Add(room.roomId);
                string state = MultiplayerStateLabelV2(room.state);
                Color color = room.clientVersion == Application.version
                    ? surfaceRaised
                    : (HighContrastUi ? UiThemeContrast.Danger : new Color(.19f, .11f, .12f, .98f));
                string song = room.song == null ? "未选歌" : (room.song.songName ?? room.song.songId) + " · " + room.song.difficulty;
                if (!multiplayerRoomCards.TryGetValue(room.roomId, out MultiplayerRoomCardVisual visual) ||
                    visual == null || visual.Card == null)
                {
                    if (visual != null && visual.Card == null) multiplayerRoomCards.Remove(room.roomId);
                    visual = null;
                    while (multiplayerRoomCardPool.Count > 0 && visual == null)
                    {
                        MultiplayerRoomCardVisual pooled = multiplayerRoomCardPool.Pop();
                        if (pooled != null && pooled.Card != null) visual = pooled;
                    }
                    if (visual == null)
                    {
                        Button newButton = ButtonText("", multiplayerBrowserRoomContent, Vector2.zero, Vector2.one,
                            surfaceRaised, null);
                        visual = new MultiplayerRoomCardVisual {
                            Card = (RectTransform)newButton.transform,
                            Button = newButton,
                            Label = newButton.GetComponentInChildren<Text>()
                        };
                    }
                    multiplayerRoomCards[room.roomId] = visual;
                }
                visual.Card.gameObject.SetActive(true);
                visual.Button.onClick.RemoveAllListeners();
                string roomId = room.roomId;
                visual.Button.onClick.AddListener(() => {
                    MultiplayerRoomDto latest = (multiplayerRooms ?? Array.Empty<MultiplayerRoomDto>())
                        .FirstOrDefault(value => value != null &&
                            string.Equals(value.roomId, roomId, StringComparison.OrdinalIgnoreCase));
                    SelectMultiplayerRoomV2(latest ?? room);
                });
                visual.Label.text = room.roomName + "  " + room.roomId + "\n" + room.memberCount + "/" + room.maxPlayers +
                    "  " + state + "  " + (room.hasPassword ? "🔒" : "") + "\n" + song + " · " +
                    (room.mode == "normal" ? "普通" : "休闲") + " · " + (room.persistent ? "常驻" : "个人");
                visual.Label.fontSize = 15;
                visual.Label.alignment = TextAnchor.MiddleLeft;
                visual.Label.rectTransform.offsetMin = new Vector2(18, 0);
                Image image = visual.Card.GetComponent<Image>();
                image.color = color;
                ColorBlock colors = visual.Button.colors;
                colors.normalColor = color;
                colors.highlightedColor = Color.Lerp(color, Color.white, .12f);
                colors.pressedColor = Color.Lerp(color, Color.black, .25f);
                visual.Button.colors = colors;
                RectTransform rect = visual.Card;
                rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(.5f, 1);
                float top = count * 84f + 6f;
                rect.offsetMin = new Vector2(6, -top - 78f); rect.offsetMax = new Vector2(-6, -top);
                count++;
            }
            foreach (string roomId in multiplayerRoomCards.Keys.Where(key => !used.Contains(key)).ToArray())
            {
                MultiplayerRoomCardVisual stale = multiplayerRoomCards[roomId];
                if (stale != null && stale.Card != null)
                {
                    stale.Button.onClick.RemoveAllListeners();
                    stale.Card.gameObject.SetActive(false);
                    multiplayerRoomCardPool.Push(stale);
                }
                multiplayerRoomCards.Remove(roomId);
            }
            multiplayerBrowserRoomContent.sizeDelta = new Vector2(0, count * 84f + 12f);
            if (multiplayerBrowserStatus != null) multiplayerBrowserStatus.text = "当前显示 " + count + " 个房间。";
        }

        private static string BuildMultiplayerRoomListSignatureV2(IEnumerable<MultiplayerRoomDto> rooms)
        {
            StringBuilder builder = new StringBuilder();
            foreach (MultiplayerRoomDto room in rooms)
            {
                builder.Append(room.roomId).Append('|')
                    .Append(room.roomName).Append('|')
                    .Append(room.memberCount).Append('|').Append(room.maxPlayers).Append('|')
                    .Append(room.state).Append('|').Append(room.hasPassword).Append('|')
                    .Append(room.mode).Append('|').Append(room.persistent).Append('|').Append(room.clientVersion).Append('|');
                if (room.song != null)
                    builder.Append(room.song.songId).Append('|').Append(room.song.songName).Append('|')
                        .Append(room.song.difficulty).Append('|');
                builder.Append('\n');
            }
            return builder.ToString();
        }

        private void ResetMultiplayerRoomCardCacheV2()
        {
            multiplayerRoomCards.Clear();
            multiplayerRoomCardPool.Clear();
            multiplayerRoomListSignature = "";
        }

        private void SelectMultiplayerRoomV2(MultiplayerRoomDto room)
        {
            selectedMultiplayerRoom = room;
            if (room == null || multiplayerBrowserDetails == null) return;
            MultiplayerLog.Info("room-selected", "roomId=" + room.roomId + " name=" + room.roomName);
            string version = room.clientVersion == Application.version ? "版本一致，可加入" : "版本不一致，无法加入";
            string song = room.song == null ? "尚未选择" : (room.song.songName ?? room.song.songId) + "  " + room.song.difficulty + "  Lv." + room.song.level;
            string mods = CurrentMultiplayerModSummaryV2(room.mods);
            bool active = room.state == "loading" || room.state == "countdown" || room.state == "playing" ||
                          room.state == "personalResults" || room.state == "groupResults";
            multiplayerBrowserDetails.text = "房间：" + room.roomName + "  " + room.roomId +
                "\n创建者：" + room.ownerName + "\n当前房主：" + room.hostName +
                "\n人数：" + room.memberCount + "/" + room.maxPlayers + "\n模式：" + (room.mode == "normal" ? "普通" : "休闲") +
                "\n类型：" + (room.persistent ? "常驻房间" : "个人房间") +
                "\n选歌：" + (room.selectionMode == "loop" ? "循环轮流" : "房主专选") +
                "\nMOD：" + mods + "\n歌曲：" + song + "\n状态：" + MultiplayerStateLabelV2(room.state) +
                (active ? "\n正在游玩或结算，暂时禁止加入" : "") + "\n" + version;
            multiplayerBrowserJoinButton.interactable = room.clientVersion == Application.version &&
                !active && room.memberCount < room.maxPlayers;
        }

        private void RestoreMultiplayerViewV2()
        {
            if (activeMultiplayerRoom != null)
            {
                RenderMultiplayerRoomV2();
                return;
            }
            if (multiplayerAuthenticatedV2)
            {
                RenderMultiplayerBrowserV2();
                return;
            }
            if (multiplayerLoginDeviceLabel != null)
                multiplayerLoginDeviceLabel.text = "设备 ID：" + MultiplayerClient.GetStableDeviceId();
        }

        private void OpenMultiplayerV2()
        {
            MultiplayerLog.Info("ui-open-multiplayer", "page=" + currentPage);
            ConfigureSongSelectContextV2(false);
            if (multiplayerClient != null && multiplayerClient.IsConnected && multiplayerAuthenticatedV2)
            {
                SwitchTo(AppPage.MultiplayerBrowser, RequestMultiplayerRoomsV2);
            }
            else SwitchTo(AppPage.Multiplayer, () => {
                RestoreMultiplayerServerV2();
                UpdateMultiplayerReentryUiV2();
            });
        }

        private void ConfigureSongSelectContextV2(bool multiplayer)
        {
            multiplayerSelectingSong = multiplayer;
            MultiplayerLog.Info("song-select-context", multiplayer ? "multiplayer" : "singleplayer");
            if (songSelectTitle != null) songSelectTitle.text = UiLocalization.Translate(multiplayer ? "在线选曲" : "SONG SELECT");
            if (songSelectPrimaryButton != null)
            {
                SetButtonLabel(songSelectPrimaryButton, multiplayer ? "选择歌曲" : "开始！");
                songSelectPrimaryButton.onClick.RemoveAllListeners();
                songSelectPrimaryButton.onClick.AddListener(multiplayer ? (UnityEngine.Events.UnityAction)SendMultiplayerSongSelectionV2 : PrepareGameplay);
            }
            if (songSelectBackButton != null)
            {
                songSelectBackButton.onClick.RemoveAllListeners();
                songSelectBackButton.onClick.AddListener(multiplayer ? (UnityEngine.Events.UnityAction)ReturnToMultiplayerRoomV2 :
                    () => { preview?.StopPreview(); SwitchTo(AppPage.Home); });
            }
            if (songSelectModsButton != null) songSelectModsButton.gameObject.SetActive(!multiplayer);
            if (songSelectDeleteButton != null) songSelectDeleteButton.gameObject.SetActive(!multiplayer);
        }

        private void ConnectMultiplayerV2()
        {
            int reentrySeconds = MultiplayerReentryRemainingSecondsV2();
            if (reentrySeconds > 0)
            {
                MultiplayerLog.Warn("login-blocked", "reentryCooldownSeconds=" + reentrySeconds);
                SetMultiplayerLoginStatusV2("退出在线大厅后请等待 " + reentrySeconds + " 秒。", true);
                return;
            }
            if (multiplayerServerInput == null || string.IsNullOrWhiteSpace(multiplayerServerInput.text))
            {
                MultiplayerLog.Warn("login-validation-failed", "missing=server");
                SetMultiplayerLoginStatusV2("请填写服务器地址。", true);
                return;
            }
            if (multiplayerPlayerNameInput == null || string.IsNullOrWhiteSpace(multiplayerPlayerNameInput.text))
            {
                MultiplayerLog.Warn("login-validation-failed", "missing=playerName");
                SetMultiplayerLoginStatusV2("请填写玩家名称。", true);
                return;
            }
            multiplayerAuthenticatedV2 = false;
            multiplayerSessionToken = "";
            multiplayerManualDisconnect = false;
            multiplayerRetryInProgress = false;
            multiplayerReconnectAttempt = 0;
            multiplayerLastConnectionError = "";
            if (multiplayerReconnectCoroutine != null)
            {
                StopCoroutine(multiplayerReconnectCoroutine);
                multiplayerReconnectCoroutine = null;
            }
            multiplayerConnectedServer = multiplayerServerInput.text.Trim();
            multiplayerConnectedPlayerName = multiplayerPlayerNameInput.text.Trim();
            SaveMultiplayerDraftV2();
            MultiplayerLog.Info("login-input", "server=" + multiplayerConnectedServer + " player=" + multiplayerConnectedPlayerName);
            SetMultiplayerLoginStatusV2("正在连接服务器…", false);
            multiplayerClient.Connect(multiplayerConnectedServer, multiplayerConnectedPlayerName,
                MultiplayerClient.GetStableDeviceId(), Application.version);
        }

        private void OnMultiplayerMessageV2(string json)
        {
            MultiplayerEnvelope message;
            try { message = JsonUtility.FromJson<MultiplayerEnvelope>(json); }
            catch (Exception exception)
            {
                MultiplayerLog.Error("message-parse-failed", exception.Message);
                SetMultiplayerLoginStatusV2("联机数据解析失败：" + exception.Message, true);
                return;
            }
            if (message == null)
            {
                MultiplayerLog.Warn("message-ignored", "null message");
                return;
            }
            MultiplayerLog.Info("multiplayer-event", "page=" + currentPage + " " + MultiplayerLog.SummarizeJson(json));
            ApplyMultiplayerQuotaV2(message.quota, message.serverTime, "message");
            ApplyMultiplayerQuotaV2(message.details?.quota, message.serverTime, "error details");
            switch (message.type)
            {
                case "helloAccepted":
                    multiplayerAuthenticatedV2 = true;
                    multiplayerAuthenticated = true;
                    multiplayerLeavingLobby = false;
                    multiplayerRetryInProgress = false;
                    multiplayerReconnectAttempt = 0;
                    if (multiplayerReconnectCoroutine != null)
                    {
                        StopCoroutine(multiplayerReconnectCoroutine);
                        multiplayerReconnectCoroutine = null;
                    }
                    multiplayerGameId = message.gameId ?? "";
                    multiplayerSessionToken = message.sessionToken ?? "";
                    SaveMultiplayerDraft();
                    multiplayerLobbyReentryUntilMs = 0;
                    PlayerPrefs.DeleteKey("chunithm_mobile_multiplayer_reentry_until");
                    PlayerPrefs.SetString("chunithm_mobile_multiplayer_game_id", multiplayerGameId);
                    PlayerPrefs.Save();
                    MultiplayerLog.Info("login-accepted", "server=" + multiplayerConnectedServer + " player=" +
                        multiplayerConnectedPlayerName + " gameId=" + MultiplayerLog.ShortId(multiplayerGameId));
                    SwitchTo(AppPage.MultiplayerBrowser, RequestMultiplayerRoomsV2);
                    break;
                case "roomList":
                    multiplayerRooms = message.rooms ?? Array.Empty<MultiplayerRoomDto>();
                    MultiplayerLog.Info("room-list-updated", "count=" + multiplayerRooms.Length);
                    RenderMultiplayerBrowserV2();
                    break;
                case "roomIdReserved":
                    multiplayerReservedRoomId = message.roomId;
                    ShowCreateMultiplayerOverlayV2(message.roomId);
                    break;
                case "roomIdReleased":
                    multiplayerReservedRoomId = null;
                    HideCreateMultiplayerOverlayV2();
                    break;
                case "roomCreated":
                case "roomJoined":
                    activeMultiplayerRoom = message.room;
                    MultiplayerLog.Info(message.type == "roomCreated" ? "room-created" : "room-joined",
                        "roomId=" + (activeMultiplayerRoom?.roomId ?? message.roomId ?? "-") +
                        " name=" + (activeMultiplayerRoom?.roomName ?? "-"));
                    multiplayerSelectingSong = false;
                    HideCreateMultiplayerOverlayV2();
                    SwitchTo(AppPage.MultiplayerRoom, RenderMultiplayerRoomV2);
                    ApplyPendingMultiplayerTransferV2(activeMultiplayerRoom?.song);
                    StartPreparingMultiplayerSongV2(activeMultiplayerRoom?.song);
                    break;
                case "roomUpdated":
                case "roomSettingsUpdated":
                    activeMultiplayerRoom = message.room;
                    ApplyPendingMultiplayerTransferV2(activeMultiplayerRoom?.song);
                    if (currentPage == AppPage.MultiplayerRoom) RenderMultiplayerRoomV2();
                    // transferReady and the room snapshot are sent as separate messages.
                    // A member may have already stopped at "waiting for upload" when
                    // this snapshot is the first message that says the package is ready.
                    StartPreparingMultiplayerSongV2(activeMultiplayerRoom?.song);
                    break;
                case "songSelected":
                    MultiplayerLog.Info("song-selected-by-server", "song=" + (message.song?.songId ?? "-") +
                        " difficulty=" + (message.song?.difficulty ?? "-"));
                    if (activeMultiplayerRoom != null) activeMultiplayerRoom.song = message.song;
                    multiplayerSelectedSong = message.song;
                    ApplyPendingMultiplayerTransferV2(message.song);
                    RenderMultiplayerRoomV2();
                    StartPreparingMultiplayerSongV2(message.song);
                    break;
                case "songChangeVoteUpdated":
                    if (activeMultiplayerRoom != null)
                    {
                        activeMultiplayerRoom.songChangeVote = new MultiplayerSongChangeVoteDto {
                            active = message.active,
                            requiredRatio = message.requiredRatio,
                            threshold = message.threshold,
                            count = message.voteCount,
                            eligible = message.eligibleCount
                        };
                        if (currentPage == AppPage.MultiplayerRoom) RenderMultiplayerRoomV2();
                    }
                    break;
                case "songChangeVotePassed":
                    if (activeMultiplayerRoom != null && activeMultiplayerRoom.songChangeVote != null)
                    {
                        activeMultiplayerRoom.songChangeVote.active = false;
                        activeMultiplayerRoom.songChangeVote.count = 0;
                    }
                    SetMultiplayerRoomStatusV2("投票已通过，房间已返回选曲。", false);
                    break;
                case "transferReady":
                    MultiplayerLog.Info("transfer-ready", "packageHash=" + (message.packageHash ?? "-") +
                        " bytes=" + message.packageSize);
                    if (activeMultiplayerRoom?.song != null && message.packageHash == activeMultiplayerRoom.song.packageHash)
                    {
                        // The server intentionally keeps this notification small. Keep the
                        // local room snapshot in sync before starting the download; otherwise
                        // PrepareSelectedMultiplayerSongV2 sees transferReady == false and exits.
                        activeMultiplayerRoom.song.transferReady = true;
                        activeMultiplayerRoom.song.transferProgress = 1f;
                        activeMultiplayerRoom.song.transferDirection = "upload";
                        activeMultiplayerRoom.song.transferState = "completed";
                        if (message.packageSize > 0) activeMultiplayerRoom.song.packageSize = message.packageSize;
                        if (!string.IsNullOrWhiteSpace(message.downloadPath))
                            activeMultiplayerRoom.song.downloadPath = message.downloadPath;
                        if (currentPage == AppPage.MultiplayerRoom) RenderMultiplayerRoomV2();
                        StartPreparingMultiplayerSongV2(activeMultiplayerRoom.song);
                    }
                    else if (activeMultiplayerRoom?.song == null && !string.IsNullOrWhiteSpace(message.packageHash))
                    {
                        // Keep the notification until roomJoined/songSelected supplies the
                        // song metadata. This also makes reconnect/join-after-selection work.
                        multiplayerPendingTransferHash = message.packageHash;
                        multiplayerPendingTransferSize = message.packageSize;
                        multiplayerPendingTransferPath = message.downloadPath;
                    }
                    break;
                case "transferProgress":
                    MultiplayerLog.Info("transfer-progress", "packageHash=" + (message.packageHash ?? "-") +
                        " direction=" + (message.direction ?? "-") + " state=" + (message.transferState ?? "-") +
                        " progress=" + (Mathf.Clamp01(message.progress) * 100f).ToString("0.0") + "%");
                    if (activeMultiplayerRoom?.song != null &&
                        string.Equals(message.packageHash, activeMultiplayerRoom.song.packageHash, StringComparison.OrdinalIgnoreCase))
                    {
                        activeMultiplayerRoom.song.transferProgress = Mathf.Clamp01(message.progress);
                        activeMultiplayerRoom.song.transferDirection = message.direction ?? "";
                        activeMultiplayerRoom.song.transferState = message.transferState ?? "";
                        activeMultiplayerRoom.song.transferPlayerId = message.playerId ?? "";
                        MultiplayerRoomMemberDto member = (activeMultiplayerRoom.members ?? Array.Empty<MultiplayerRoomMemberDto>())
                            .FirstOrDefault(value => value != null && value.playerId == message.playerId);
                        if (member != null && message.direction == "download")
                        {
                            member.syncProgress = Mathf.Clamp01(message.progress);
                            member.status = message.transferState == "failed" ? "syncFailed" : "syncing";
                        }
                        if (currentPage == AppPage.MultiplayerRoom) RenderMultiplayerRoomV2();
                    }
                    break;
                case "transferExpired":
                    SetMultiplayerRoomStatusV2("歌曲包已过期，请房主重新选择歌曲。", true);
                    break;
                case "roundPreparing":
                    bool sameRound = string.Equals(multiplayerRoundId, message.roundId, StringComparison.Ordinal);
                    if (sameRound && (multiplayerRoundPreparationCoroutine != null || multiplayerInRound))
                        break;
                    if (!sameRound || !multiplayerInRound)
                    {
                        gameplaySession?.StopPlayback();
                        ClearGameplayVisuals();
                        SetGameplayScene(false);
                        multiplayerInRound = false;
                    }
                    if (multiplayerRoundPreparationCoroutine != null)
                    {
                        StopCoroutine(multiplayerRoundPreparationCoroutine);
                        multiplayerRoundPreparationCoroutine = null;
                    }
                    multiplayerRoundPreparationToken++;
                    multiplayerRoundId = message.roundId;
                    multiplayerRoundPlaybackPrepared = false;
                    if (!sameRound) {
                        multiplayerRoundStartPending = false;
                        multiplayerRoundRevealStarted = false;
                        multiplayerRoundStarted = false;
                    }
                    MultiplayerLog.Info("gameplay-start", "stage=receive-start-message room=" +
                        (activeMultiplayerRoom?.roomId ?? "-") + " round=" + (message.roundId ?? "-"));
                    multiplayerRoundPreparationCoroutine = StartCoroutine(
                        PrepareMultiplayerRoundV2(message, multiplayerRoundPreparationToken));
                    break;
                case "roundStarting":
                    multiplayerRoundId = message.roundId;
                    multiplayerRoundStarted = true;
                    long receivedLocalUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    double remainingMs = multiplayerClient.ServerDeadlineDelayMilliseconds(
                        message.startAt, message.serverTime, receivedLocalUnix);
                    multiplayerPendingRoundStartDsp = PlatformServices.Current.AudioTime.Seconds + remainingMs / 1000.0;
                    multiplayerRoundStartPending = true;
                    MultiplayerLog.Info("gameplay-start", "stage=round-starting room=" +
                        (activeMultiplayerRoom?.roomId ?? "-") + " round=" + (message.roundId ?? "-") +
                        " serverStartAt=" + message.startAt + " localStartDsp=" + multiplayerPendingRoundStartDsp.ToString("F3", CultureInfo.InvariantCulture) +
                        " clockOffset=" + multiplayerClient.ServerClockOffsetMilliseconds.ToString("F2", CultureInfo.InvariantCulture) +
                        "ms calibrated=" + multiplayerClient.HasServerClockSample);
                    // The start message may arrive before or after local decoding.
                    // Both paths now use the same idempotent gate, so neither order
                    // can start audio without a prepared renderer/judge/input chain.
                    TryStartMultiplayerRoundPlaybackV2(message.roundId);
                    TryBeginMultiplayerRoundRevealV2(message.roundId);
                    break;
                case "roundStarted":
                    SetMultiplayerRoomStatusV2("已开始游玩。", false);
                    break;
                case "roundCancelled":
                    AbortMultiplayerRoundV2(message.message ?? "本局已取消。", true);
                    break;
                case "roundExited":
                    AbortMultiplayerRoundV2("已退出本局，等待其他成员。", false);
                    break;
                case "resultAccepted":
                    if (message.result != null) SetMultiplayerRoomStatusV2("个人成绩已提交，等待其他成员。", false);
                    break;
                case "allPlayersFinished":
                    SetMultiplayerRoomStatusV2("所有成员已完成，正在准备排名。", false);
                    break;
                case "groupResultsAvailable":
                    multiplayerLeaderboard = message.leaderboard ?? Array.Empty<MultiplayerLeaderboardEntryDto>();
                    multiplayerGroupResultPending = true;
                    multiplayerGroupResultDeadline = Time.unscaledTime + 10f;
                    RenderMultiplayerGroupResultsV2(message.song, multiplayerLeaderboard);
                    SwitchTo(AppPage.MultiplayerGroupResults);
                    break;
                case "returnedToLobby":
                    CancelMultiplayerRoundPreparationV2();
                    activeMultiplayerRoom = message.room ?? activeMultiplayerRoom;
                    multiplayerInRound = false;
                    multiplayerRoundStarted = false;
                    multiplayerPersonalResultPending = false;
                    multiplayerGroupResultPending = false;
                    ConfigureSongSelectContextV2(false);
                    SwitchTo(AppPage.MultiplayerRoom, RenderMultiplayerRoomV2);
                    break;
                case "hostChanged":
                    if (activeMultiplayerRoom != null)
                    {
                        activeMultiplayerRoom.hostPlayerId = message.hostPlayerId;
                        activeMultiplayerRoom.hostName = message.hostName;
                        foreach (MultiplayerRoomMemberDto member in activeMultiplayerRoom.members ?? Array.Empty<MultiplayerRoomMemberDto>())
                            if (member != null) member.isHost = member.playerId == message.hostPlayerId;
                    }
                    SetMultiplayerRoomStatusV2("当前选歌房主：" + (message.hostName ?? "-"), false);
                    if (currentPage == AppPage.MultiplayerRoom) RenderMultiplayerRoomV2();
                    break;
                case "roomLeft":
                    activeMultiplayerRoom = null;
                    preview?.StopPreview();
                    if (multiplayerLeavingLobby) break;
                    interfaceAudio?.RestartBackgroundMusic(AppPage.MultiplayerBrowser);
                    SwitchTo(AppPage.MultiplayerBrowser, RequestMultiplayerRoomsV2);
                    break;
                case "kickedFromRoom":
                    activeMultiplayerRoom = null;
                    preview?.StopPreview();
                    if (multiplayerLeavingLobby) break;
                    interfaceAudio?.RestartBackgroundMusic(AppPage.MultiplayerBrowser);
                    SwitchTo(AppPage.MultiplayerBrowser, () => SetMultiplayerBrowserStatusV2("你已被房主移出房间。", true));
                    break;
                case "lobbyLeft":
                    if (message.cooldownUntil > 0)
                    {
                        multiplayerLobbyReentryUntilMs = message.cooldownUntil;
                        PlayerPrefs.SetString("chunithm_mobile_multiplayer_reentry_until",
                            multiplayerLobbyReentryUntilMs.ToString(CultureInfo.InvariantCulture));
                        PlayerPrefs.Save();
                    }
                    if (multiplayerLeavingLobby)
                    {
                        StartCoroutine(DisconnectMultiplayerAfterLobbyLeaveV2());
                    }
                    break;
                case "backgroundTimeout":
                    HandleMultiplayerTimeoutV2();
                    break;
                case "idleWarning":
                    SetMultiplayerLoginStatusV2("连接将在 " + message.remainingSeconds + " 秒后因无操作断开。", true);
                    break;
                case "error":
                    MultiplayerLog.Error("server-rejected-action", "code=" + (message.code ?? "-") +
                        " message=" + (message.message ?? "服务器拒绝了操作。"));
                    string detail = message.code == "CLIENT_VERSION_MISMATCH" ? "\n房间版本：" + message.expectedVersion + "\n当前版本：" + message.actualVersion : "";
                    if (message.code == "LOBBY_REENTRY_COOLDOWN")
                    {
                        int seconds = message.details != null && message.details.remainingSeconds > 0
                            ? message.details.remainingSeconds : Mathf.Max(1, message.remainingSeconds);
                        multiplayerLobbyReentryUntilMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + seconds * 1000L;
                        PlayerPrefs.SetString("chunithm_mobile_multiplayer_reentry_until", multiplayerLobbyReentryUntilMs.ToString(CultureInfo.InvariantCulture));
                        PlayerPrefs.Save();
                        detail = "\n请等待 " + seconds + " 秒后重新进入。";
                    }
                    string errorText = (message.message ?? "服务器拒绝了操作。") + detail +
                        (string.IsNullOrWhiteSpace(message.code) ? "" : "\n错误代码：" + message.code);
                    if (currentPage == AppPage.MultiplayerRoom || currentPage == AppPage.MultiplayerGroupResults)
                        SetMultiplayerRoomStatusV2(errorText, true);
                    else if (currentPage == AppPage.MultiplayerBrowser)
                        SetMultiplayerBrowserStatusV2(errorText, true);
                    else SetMultiplayerLoginStatusV2(errorText, true);
                    break;
            }
        }

        private void RenderMultiplayerBrowserV2()
        {
            if (currentPage != AppPage.MultiplayerBrowser && multiplayerAuthenticatedV2) return;
            BuildRoomListContentV2(multiplayerRooms);
            if (selectedMultiplayerRoom != null)
            {
                string selectedRoomId = selectedMultiplayerRoom.roomId;
                MultiplayerRoomDto refreshed = (multiplayerRooms ?? Array.Empty<MultiplayerRoomDto>())
                    .FirstOrDefault(room => room != null && room.roomId == selectedRoomId);
                if (refreshed != null) SelectMultiplayerRoomV2(refreshed);
                else
                {
                    selectedMultiplayerRoom = null;
                    if (multiplayerBrowserDetails != null) multiplayerBrowserDetails.text = "选择一个房间查看详情。";
                    if (multiplayerBrowserJoinButton != null) multiplayerBrowserJoinButton.interactable = false;
                }
            }
            RefreshMultiplayerQuotaV2();
        }

        private void RenderMultiplayerRoomV2()
        {
            MultiplayerRoomDto room = activeMultiplayerRoom;
            if (room == null || multiplayerRoomMemberContent == null) return;
            if (currentPage != AppPage.MultiplayerRoom && currentPage != AppPage.MultiplayerGroupResults) return;
            string mode = room.mode == "normal" ? "普通模式" : "休闲模式";
            string selection = room.selectionMode == "loop" ? "循环轮流选歌" : "房主专选";
            multiplayerRoomHeader.text = room.roomName + "  #" + room.roomId + "  " + room.memberCount + "/" + room.maxPlayers;
            multiplayerRoomStatus.text = MultiplayerStateLabelV2(room.state);
            SetButtonLabel(multiplayerRoomModeButton, "模式：" + mode);
            SetButtonLabel(multiplayerRoomModsButton, "MOD：" + CurrentMultiplayerModSummaryV2(room.mods));
            SetButtonLabel(multiplayerRoomSelectionButton, "选歌方式：" + selection);
            MultiplayerSongChangeVoteDto vote = room.songChangeVote;
            if (multiplayerRoomVoteButton != null)
            {
                if (vote != null && vote.active)
                {
                    int percent = Mathf.RoundToInt(Mathf.Clamp01(vote.requiredRatio) * 100f);
                    SetButtonLabel(multiplayerRoomVoteButton, "投票换歌 " + vote.count + "/" + vote.threshold + "（" + percent + "%）");
                }
                else SetButtonLabel(multiplayerRoomVoteButton, "投票换歌（" + (vote == null ? 50 : Mathf.RoundToInt(vote.requiredRatio * 100f)) + "%）");
            }
            multiplayerRoomSongLabel.text = room.song == null ? "房主尚未选择歌曲" :
                (room.song.songName ?? room.song.songId) + "\n" + room.song.difficulty + "  Lv." + room.song.level +
                "\n" + MultiplayerSongTransferStatusV2(room.song);
            RefreshMultiplayerSongProgressV2(room.song);
            PlayMultiplayerRoomAudioV2(room);
            foreach (Transform child in multiplayerRoomMemberContent) Destroy(child.gameObject);
            MultiplayerRoomMemberDto[] members = room.members ?? Array.Empty<MultiplayerRoomMemberDto>();
            int count = 0;
            foreach (MultiplayerRoomMemberDto member in members)
            {
                if (member == null) continue;
                bool canManage = IsLocalMultiplayerHostV2() && !member.isHost && room.state == "lobby";
                string status = MultiplayerMemberStatusV2(member);
                string score = member.bestScore > 0 ? "  " + member.bestScore.ToString("N0") + "  " + member.bestRank : "";
                RectTransform rect = Panel("Member " + member.playerId, multiplayerRoomMemberContent,
                    member.ready
                        ? (HighContrastUi ? UiThemeContrast.Success : new Color(.10f, .34f, .20f, .98f))
                        : surfaceRaised, Vector2.zero, Vector2.one);
                rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(.5f, 1);
                float top = count * 62f + 4f;
                rect.offsetMin = new Vector2(6, -top - 56f); rect.offsetMax = new Vector2(-6, -top);
                float textRight = canManage ? .55f : .95f;
                Label(member.name + (member.isHost ? "  房主" : "") + score, rect, 16, TextAnchor.MiddleLeft,
                    new Vector2(.035f, .47f), new Vector2(textRight, .94f), Color.white);
                Label(status, rect, 13, TextAnchor.MiddleLeft,
                    new Vector2(.035f, .14f), new Vector2(textRight, .46f), muted);
                if (member.status == "syncing")
                {
                    RectTransform track = Panel("Member sync track", rect, new Color(.05f, .10f, .14f, .96f),
                        new Vector2(.035f, .06f), new Vector2(textRight, .12f));
                    float progress = Mathf.Clamp01(member.syncProgress);
                    Panel("Member sync fill", track, cyan, Vector2.zero, new Vector2(progress, 1));
                }
                if (canManage)
                {
                    Button transfer = ButtonText("转让\n房主", rect, new Vector2(.58f, .20f), new Vector2(.77f, .80f), surfaceRaised,
                        () => ConfirmTransferMultiplayerHostV2(member.playerId, member.name));
                    Button kick = ButtonText("踢出", rect, new Vector2(.79f, .20f), new Vector2(.97f, .80f),
                        HighContrastUi ? UiThemeContrast.Danger : new Color(.45f, .12f, .15f),
                        () => ConfirmKickMultiplayerMemberV2(member.playerId, member.name));
                    SetButtonFontSizeV2(transfer, 13);
                    SetButtonFontSizeV2(kick, 14);
                }
                count++;
            }
            multiplayerRoomMemberContent.sizeDelta = new Vector2(0, count * 62f + 8f);
            bool host = IsLocalMultiplayerHostV2();
            bool active = room.state == "loading" || room.state == "countdown" || room.state == "playing" ||
                          room.state == "personalResults" || room.state == "groupResults";
            MultiplayerRoomMemberDto self = members.FirstOrDefault(value => value != null && value.playerId == multiplayerGameId);
            bool hasSong = room.song != null;
            if (host) SetButtonLabel(multiplayerRoomSongButton, hasSong ? "切换歌曲" : "选择歌曲");
            else if (!hasSong) SetButtonLabel(multiplayerRoomSongButton, "等待房主选歌");
            else if (self != null && self.songReady) SetButtonLabel(multiplayerRoomSongButton, "同步成功");
            else if (multiplayerDownloading || multiplayerSongPreparationCoroutine != null) SetButtonLabel(multiplayerRoomSongButton,
                "同步中 " + Mathf.RoundToInt((self == null ? 0f : self.syncProgress) * 100f) + "%");
            else if (!room.song.transferReady) SetButtonLabel(multiplayerRoomSongButton, "等待房主上传");
            else SetButtonLabel(multiplayerRoomSongButton, "同步当前歌曲");
            multiplayerRoomSongButton.interactable = !active && hasSong && (host || self == null || !self.songReady);
            multiplayerRoomModeButton.interactable = !active && host;
            multiplayerRoomModsButton.interactable = !active && host;
            multiplayerRoomSelectionButton.interactable = !active && host;
            multiplayerRoomVoteButton.interactable = !active && hasSong && self != null;
            multiplayerRoomReadyButton.interactable = !active && room.song != null && self != null && self.songReady;
            SetButtonLabel(multiplayerRoomReadyButton, self != null && self.ready ? "取消准备" : "准备");
            multiplayerRoomExitRoundButton.gameObject.SetActive(active);
            multiplayerRoomLeaveButton.gameObject.SetActive(!active);
            if (room.state == "syncing") SetMultiplayerRoomStatusV2("正在等待所有成员下载并校验歌曲。", false);
        }

        private void PlayMultiplayerRoomAudioV2(MultiplayerRoomDto room)
        {
            if (room == null || room.song == null)
            {
                preview?.StopPreview();
                interfaceAudio?.RestartBackgroundMusic(AppPage.MultiplayerRoom);
                return;
            }
            SongEntry localSong = catalog.FirstOrDefault(value => value != null && value.Id == room.song.songId);
            if (localSong == null || string.IsNullOrWhiteSpace(SongAudioFiles.Resolve(localSong.Root).SelectionPreview))
            {
                preview?.StopPreview();
                interfaceAudio?.RestartBackgroundMusic(AppPage.MultiplayerRoom);
                return;
            }
            interfaceAudio?.StopBackgroundMusicForSongPreview();
            if (!string.Equals(preview?.SelectedSongRoot, localSong.Root, StringComparison.OrdinalIgnoreCase) ||
                (preview != null && !preview.IsPlaying && !preview.IsLoading))
                preview?.SelectSong(localSong.Root);
        }

        private static void SetButtonFontSizeV2(Button button, int size)
        {
            if (button == null) return;
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.fontSize = size;
        }

        private static string MultiplayerSongTransferStatusV2(MultiplayerSongDto song)
        {
            if (song == null) return "";
            if (song.transferReady) return "歌曲包已就绪";
            int percent = Mathf.RoundToInt(Mathf.Clamp01(song.transferProgress) * 100f);
            if (song.transferDirection == "upload") return "房主正在上传 " + percent + "%";
            if (song.transferDirection == "download") return "成员正在同步 " + percent + "%";
            return "等待房主上传";
        }

        private void RefreshMultiplayerSongProgressV2(MultiplayerSongDto song)
        {
            if (multiplayerRoomSongProgressFill == null) return;
            bool visible = song != null && !song.transferReady && !string.IsNullOrWhiteSpace(song.transferState);
            Transform track = multiplayerRoomSongProgressFill.transform.parent;
            if (track != null) track.gameObject.SetActive(visible);
            float progress = song == null ? 0f : Mathf.Clamp01(song.transferProgress);
            RectTransform fill = multiplayerRoomSongProgressFill.rectTransform;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(progress, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
        }

        private void OpenOrSyncMultiplayerSongV2()
        {
            if (activeMultiplayerRoom == null) return;
            if (IsLocalMultiplayerHostV2())
            {
                OpenMultiplayerSongSelectV2();
                return;
            }
            if (activeMultiplayerRoom.song == null)
            {
                SetMultiplayerRoomStatusV2("等待房主选择歌曲。", false);
                return;
            }
            MultiplayerRoomMemberDto self = (activeMultiplayerRoom.members ?? Array.Empty<MultiplayerRoomMemberDto>())
                .FirstOrDefault(value => value != null && value.playerId == multiplayerGameId);
            if (self != null && self.songReady)
            {
                SetMultiplayerRoomStatusV2("当前歌曲已同步成功。", false);
                return;
            }
            StartPreparingMultiplayerSongV2(activeMultiplayerRoom.song);
            RenderMultiplayerRoomV2();
            SetMultiplayerRoomStatusV2(activeMultiplayerRoom.song.transferReady ? "正在同步当前歌曲。" : "等待房主上传歌曲包。", false);
        }

        private void RefreshMultiplayerQuotaV2()
        {
            if (multiplayerQuota == null) return;
            string create = multiplayerQuota.createRoomRemaining < 0 ? "不限" : multiplayerQuota.createRoomRemaining.ToString(CultureInfo.InvariantCulture);
            string join = multiplayerQuota.joinRoomRemaining < 0 ? "不限" : multiplayerQuota.joinRoomRemaining.ToString(CultureInfo.InvariantCulture);
            if (multiplayerBrowserQuota != null) multiplayerBrowserQuota.text = "创建剩余 " + create + "    加入剩余 " + join;
            if (multiplayerBrowserCreateButton != null)
            {
                multiplayerBrowserCreateButton.interactable = multiplayerQuota.createRoomRemaining != 0;
                SetButtonLabel(multiplayerBrowserCreateButton, "创建房间\n剩余 " + create);
            }
        }

        private void ApplyMultiplayerQuotaV2(MultiplayerQuotaDto value, long serverTime, string source)
        {
            if (!IsValidMultiplayerQuotaV2(value))
            {
                if (value != null) Debug.LogWarning("Ignored malformed multiplayer quota from " + source + ".");
                return;
            }
            if (serverTime > 0 && multiplayerLastQuotaServerTime > 0 && serverTime < multiplayerLastQuotaServerTime)
                return;
            multiplayerLastQuotaServerTime = Math.Max(multiplayerLastQuotaServerTime, serverTime);
            multiplayerQuota = value;
            RefreshMultiplayerQuotaV2();
        }

        private static bool IsValidMultiplayerQuotaV2(MultiplayerQuotaDto value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.day)) return false;
            if (!IsValidQuotaPairV2(value.createRoomLimit, value.createRoomUsed, value.createRoomRemaining)) return false;
            return IsValidQuotaPairV2(value.joinRoomLimit, value.joinRoomUsed, value.joinRoomRemaining);
        }

        private static bool IsValidQuotaPairV2(int limit, int used, int remaining)
        {
            if (limit < -1 || used < 0 || remaining < -1) return false;
            return limit < 0 ? remaining == -1 : used <= limit && remaining == limit - used;
        }

        private void RequestMultiplayerRoomsV2()
        {
            if (multiplayerClient == null || !multiplayerAuthenticatedV2) return;
            string search = multiplayerBrowserSearchInput == null ? "" : multiplayerBrowserSearchInput.text.Trim();
            string exactId = RegexLikeRoomId(search) ? search : "";
            multiplayerClient.RequestRoomList(exactId.Length == 0 ? search : "", exactId,
                multiplayerFilterIdleOnly, multiplayerFilterNotFullOnly, multiplayerFilterUnencryptedOnly, multiplayerFilterMode, multiplayerFilterRoomType);
            SetMultiplayerBrowserStatusV2("正在刷新房间列表…", false);
        }

        private void BeginCreateMultiplayerRoomV2()
        {
            if (multiplayerClient == null || !multiplayerClient.IsConnected)
            {
                SetMultiplayerBrowserStatusV2("尚未连接服务器，请重新进入在线大厅。", true);
                return;
            }
            if (!multiplayerAuthenticatedV2)
            {
                SetMultiplayerBrowserStatusV2("登录状态已失效，请返回后重新进入在线大厅。", true);
                return;
            }
            if (multiplayerQuota != null && multiplayerQuota.createRoomRemaining == 0)
            {
                SetMultiplayerBrowserStatusV2("今日创建房间次数已用完。", true);
                return;
            }
            // The server can allocate the room number atomically during
            // createRoom. Avoid making the UI depend on a separate reservation
            // event, which could be lost during a reconnect and leave the user
            // stuck on the browser with no dialog.
            multiplayerReservedRoomId = null;
            ShowCreateMultiplayerOverlayV2(null);
            SetMultiplayerBrowserStatusV2("请填写房间设置，房间号将由服务器分配。", false);
        }

        private void ShowCreateMultiplayerOverlayV2(string roomId)
        {
            multiplayerCreateRoomIdLabel.text = string.IsNullOrWhiteSpace(roomId)
                ? "房间号：由服务器自动分配"
                : "房间号：" + roomId;
            multiplayerCreateRoomNameInput.SetTextWithoutNotify(PlayerPrefs.GetString("chunithm_mobile_multiplayer_room", "联机房"));
            multiplayerCreatePasswordInput.SetTextWithoutNotify("");
            multiplayerCreateMods = BuildMultiplayerModsV2(multiplayerCreateMode == "normal");
            RefreshCreateMultiplayerOverlayV2();
            multiplayerCreateLayer.gameObject.SetActive(true);
        }

        private void HideCreateMultiplayerOverlayV2()
        {
            if (multiplayerCreateLayer != null) multiplayerCreateLayer.gameObject.SetActive(false);
        }

        private void CancelCreateMultiplayerRoomV2()
        {
            if (multiplayerClient != null && !string.IsNullOrWhiteSpace(multiplayerReservedRoomId)) multiplayerClient.ReleaseRoomId();
            else HideCreateMultiplayerOverlayV2();
        }

        private void SubmitCreateMultiplayerRoomV2()
        {
            string name = multiplayerCreateRoomNameInput == null ? "" : multiplayerCreateRoomNameInput.text.Trim();
            if (string.IsNullOrWhiteSpace(name)) { SetMultiplayerBrowserStatusV2("请填写房间名称。", true); return; }
            if (multiplayerCreatePasswordInput != null && multiplayerCreatePasswordInput.text.Length > 0 && multiplayerCreatePasswordInput.text.Length < 4)
            { SetMultiplayerBrowserStatusV2("密码至少需要 4 位。", true); return; }
            MultiplayerCreateRoomMessage request = new MultiplayerCreateRoomMessage {
                reservedRoomId = multiplayerReservedRoomId,
                roomName = name,
                roomMode = multiplayerCreateMode,
                roomType = multiplayerCreateRoomType,
                maxPlayers = multiplayerCreateMaxPlayers,
                selectionMode = multiplayerCreateSelection,
                listed = multiplayerCreateListed,
                password = multiplayerCreatePasswordInput == null ? "" : multiplayerCreatePasswordInput.text,
                mods = CopyMultiplayerModsV2(multiplayerCreateMods)
            };
            multiplayerClient.Send(JsonUtility.ToJson(request));
            SetMultiplayerBrowserStatusV2("正在创建房间…", false);
        }

        private void ChangeMultiplayerCreatePlayersV2(int direction)
        {
            multiplayerCreateMaxPlayers = Mathf.Clamp(multiplayerCreateMaxPlayers + Math.Sign(direction), 2, 8);
            RefreshCreateMultiplayerOverlayV2();
        }

        private void CycleMultiplayerCreateModeV2()
        {
            multiplayerCreateMode = multiplayerCreateMode == "normal" ? "casual" : "normal";
            if (multiplayerCreateMode == "normal")
            {
                multiplayerCreateMods.airAreaAuto = false;
                multiplayerCreateMods.directionalAirAuto = false;
                multiplayerCreateMods.mineImmunity = false;
                multiplayerCreateMods.musicRate = 1f;
            }
            RefreshCreateMultiplayerOverlayV2();
        }

        private void CycleMultiplayerCreateSelectionV2()
        {
            multiplayerCreateSelection = multiplayerCreateSelection == "host" ? "loop" : "host";
            RefreshCreateMultiplayerOverlayV2();
        }

        private void ToggleMultiplayerCreateListedV2()
        {
            multiplayerCreateListed = !multiplayerCreateListed;
            RefreshCreateMultiplayerOverlayV2();
        }

        private void ToggleMultiplayerCreateModsV2() => OpenMultiplayerModPickerV2(false);

        private void RefreshCreateMultiplayerOverlayV2()
        {
            if (multiplayerCreatePlayersLabel != null) multiplayerCreatePlayersLabel.text = "人数：" + multiplayerCreateMaxPlayers;
            if (multiplayerCreateModeLabel != null) multiplayerCreateModeLabel.text = "模式：" + (multiplayerCreateMode == "normal" ? "普通" : "休闲");
            if (multiplayerCreateRoomTypeLabel != null) multiplayerCreateRoomTypeLabel.text = "房间：个人（常驻房间由服务端后台创建）";
            if (multiplayerCreateSelectionLabel != null) multiplayerCreateSelectionLabel.text = "选歌：" + (multiplayerCreateSelection == "host" ? "房主专选" : "循环轮流");
            if (multiplayerCreateListedLabel != null) multiplayerCreateListedLabel.text = "显示在大厅：" + (multiplayerCreateListed ? "是" : "否");
            if (multiplayerCreateModsLabel != null) multiplayerCreateModsLabel.text = "MOD：" + CurrentMultiplayerModSummaryV2(multiplayerCreateMods);
        }

        private void JoinSelectedMultiplayerRoomV2()
        {
            if (selectedMultiplayerRoom == null || multiplayerClient == null) return;
            multiplayerClient.Send(JsonUtility.ToJson(new MultiplayerJoinRoomMessage {
                roomId = selectedMultiplayerRoom.roomId,
                password = multiplayerBrowserPasswordInput == null ? "" : multiplayerBrowserPasswordInput.text
            }));
            SetMultiplayerBrowserStatusV2("正在加入房间…", false);
        }

        private void OpenMultiplayerSongSelectV2()
        {
            if (activeMultiplayerRoom == null || !IsLocalMultiplayerHostV2()) return;
            EnsurePageBuilt(AppPage.SongSelect);
            ConfigureSongSelectContextV2(true);
            if (activeMultiplayerRoom.song != null)
            {
                SongEntry existing = catalog.FirstOrDefault(song => song.Id == activeMultiplayerRoom.song.songId);
                if (existing != null)
                {
                    selectedSong = existing;
                    selectedChart = existing.Charts.FirstOrDefault(chart => chart.Difficulty == activeMultiplayerRoom.song.difficulty) ?? existing.Charts.FirstOrDefault();
                }
            }
            RebuildSongGrid(true);
            SwitchTo(AppPage.SongSelect, () => {
                if (selectedSong != null) preview?.SelectSong(selectedSong.Root);
            });
        }

        private void ReturnToMultiplayerRoomV2()
        {
            ConfigureSongSelectContextV2(false);
            SwitchTo(AppPage.MultiplayerRoom, RenderMultiplayerRoomV2);
        }

        private void SendMultiplayerSongSelectionV2()
        {
            if (activeMultiplayerRoom == null || multiplayerClient == null || !IsLocalMultiplayerHostV2()) return;
            if (selectedSong == null || selectedChart == null) { SetMultiplayerRoomStatusV2("请先选择歌曲和难度。", true); return; }
            SwitchTo(AppPage.MultiplayerRoom, () => StartCoroutine(BuildAndSendMultiplayerSongV2()));
        }

        private IEnumerator BuildAndSendMultiplayerSongV2()
        {
            preview?.SelectSong(selectedSong.Root);
            Task<MultiplayerPreparedPackage> task = multiplayerSongTransfer.PrepareHostPackageAsync(selectedSong.Root, selectedChart.Path,
                (fraction, status) => SetMultiplayerRoomStatusV2(status + "  " + Mathf.RoundToInt(fraction * 100) + "%", false));
            while (!task.IsCompleted)
            {
                SetMultiplayerRoomStatusV2("正在读取并打包歌曲，请稍候…", false);
                yield return null;
            }
            if (task.IsFaulted)
            {
                SetMultiplayerRoomStatusV2("歌曲打包失败：" + task.Exception?.GetBaseException().Message, true);
                yield break;
            }
            multiplayerPreparedPackage = task.Result;
            string coverPath = "";
            string coverHash = "";
            long coverSize = 0;
            if (selectedSong != null && !string.IsNullOrWhiteSpace(selectedSong.JacketPath) &&
                File.Exists(selectedSong.JacketPath) && !string.IsNullOrWhiteSpace(selectedSong.Root))
            {
                string root = selectedSong.Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (selectedSong.JacketPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    coverPath = selectedSong.JacketPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(coverPath))
                {
                    coverSize = new FileInfo(selectedSong.JacketPath).Length;
                    Task<string> coverHashTask = MultiplayerSongTransfer.ComputeSha256Async(selectedSong.JacketPath);
                    while (!coverHashTask.IsCompleted) yield return null;
                    if (!coverHashTask.IsFaulted) coverHash = coverHashTask.Result;
                }
            }
            multiplayerClient.SelectSong(new MultiplayerSelectSongMessage {
                songId = selectedSong.Id, songName = selectedSong.Name, artist = selectedSong.Artist,
                chartPath = Path.GetFileName(selectedChart.Path).Replace('\\', '/'), difficulty = selectedChart.Difficulty,
                level = selectedChart.Level, designer = selectedChart.Designer, bpm = selectedSong.Bpm,
                durationText = selectedSong.DurationText, chartHash = multiplayerPreparedPackage.ChartHash,
                packageHash = multiplayerPreparedPackage.PackageHash, packageSize = multiplayerPreparedPackage.Size,
                coverPath = coverPath, coverHash = coverHash, coverSize = coverSize
            });
            SetMultiplayerRoomStatusV2("选曲已提交，正在通知成员下载。", false);
        }

        private void StartPreparingMultiplayerSongV2(MultiplayerSongDto song)
        {
            string key = song == null ? "" : ((song.packageHash ?? "") + "|" + (song.songId ?? "") + "|" +
                (song.difficulty ?? ""));
            if (multiplayerSongPreparationCoroutine != null)
            {
                if (string.Equals(multiplayerSongPreparationKey, key, StringComparison.OrdinalIgnoreCase)) return;
                StopCoroutine(multiplayerSongPreparationCoroutine);
                multiplayerSongPreparationCoroutine = null;
                multiplayerSongPreparationKey = null;
            }
            if (song == null) return;
            multiplayerSongPreparationKey = key;
            multiplayerSongPreparationCoroutine = StartCoroutine(RunMultiplayerSongPreparationV2(song));
        }

        private IEnumerator RunMultiplayerSongPreparationV2(MultiplayerSongDto song)
        {
            yield return PrepareSelectedMultiplayerSongV2(song);
            multiplayerSongPreparationCoroutine = null;
            multiplayerSongPreparationKey = null;
        }

        private void ApplyPendingMultiplayerTransferV2(MultiplayerSongDto song)
        {
            if (song == null || string.IsNullOrWhiteSpace(multiplayerPendingTransferHash) ||
                !string.Equals(multiplayerPendingTransferHash, song.packageHash, StringComparison.OrdinalIgnoreCase)) return;
            song.transferReady = true;
            if (multiplayerPendingTransferSize > 0) song.packageSize = multiplayerPendingTransferSize;
            if (!string.IsNullOrWhiteSpace(multiplayerPendingTransferPath)) song.downloadPath = multiplayerPendingTransferPath;
            multiplayerPendingTransferHash = null;
            multiplayerPendingTransferSize = 0;
            multiplayerPendingTransferPath = null;
        }

        private void ReportMultiplayerTransferProgressV2(string packageHash, string direction, string state,
            float progress, bool force = false)
        {
            if (multiplayerClient == null || string.IsNullOrWhiteSpace(packageHash)) return;
            string key = packageHash + "|" + direction + "|" + state + "|" + Mathf.RoundToInt(progress * 1000f);
            if (!force && string.Equals(key, multiplayerLastTransferReportKey, StringComparison.Ordinal) &&
                Time.unscaledTime - multiplayerLastTransferReportAt < .10f) return;
            if (!force && Time.unscaledTime - multiplayerLastTransferReportAt < .10f) return;
            multiplayerLastTransferReportKey = key;
            multiplayerLastTransferReportAt = Time.unscaledTime;
            multiplayerClient.ReportTransferProgress(packageHash, direction, state, progress);
        }

        private IEnumerator PrepareSelectedMultiplayerSongV2(MultiplayerSongDto song)
        {
            if (song == null || activeMultiplayerRoom == null || multiplayerClient == null) yield break;
            if (multiplayerUploading || multiplayerDownloading) yield break;
            if (song.selectedBy == multiplayerGameId)
            {
                if (song.transferReady) yield break;
                if (multiplayerPreparedPackage == null || multiplayerPreparedPackage.PackageHash != song.packageHash)
                { SetMultiplayerRoomStatusV2("房主歌曲包不存在，请重新选择歌曲。", true); yield break; }
                multiplayerUploading = true;
                ReportMultiplayerTransferProgressV2(song.packageHash, "upload", "started", 0f, true);
                yield return multiplayerSongTransfer.Upload(multiplayerConnectedServer, activeMultiplayerRoom.roomId, multiplayerSessionToken,
                    multiplayerPreparedPackage, (fraction, status) => {
                        ReportMultiplayerTransferProgressV2(song.packageHash, "upload", fraction >= .999f ? "completed" : "running", fraction);
                        SetMultiplayerRoomStatusV2(status + "  " + Mathf.RoundToInt(fraction * 100) + "%", false);
                    },
                    (success, error) => {
                        multiplayerUploading = false;
                        if (!success)
                        {
                            ReportMultiplayerTransferProgressV2(song.packageHash, "upload", "failed", 0f, true);
                            SetMultiplayerRoomStatusV2(error, true);
                        }
                    });
                yield break;
            }
            Task<SongChartEntry> localTask = FindLocalMultiplayerChartV2(song);
            while (!localTask.IsCompleted) yield return null;
            if (localTask.Status == TaskStatus.RanToCompletion && localTask.Result != null)
            {
                selectedSong = catalog.FirstOrDefault(value => value.Id == song.songId);
                selectedChart = localTask.Result;
                multiplayerClient.SetSongSyncStatus(song.packageHash, true, 1f);
                PlayMultiplayerRoomAudioV2(activeMultiplayerRoom);
                SetMultiplayerRoomStatusV2("本地歌曲已校验完成。", false);
                yield break;
            }
            if (!song.transferReady)
            {
                SetMultiplayerRoomStatusV2("等待房主上传歌曲包。", false);
                yield break;
            }
            multiplayerDownloading = true;
            ReportMultiplayerTransferProgressV2(song.packageHash, "download", "started", 0f, true);
            bool downloadSucceeded = false;
            SongImportResult downloadedImport = null;
            yield return multiplayerSongTransfer.DownloadAndImport(multiplayerConnectedServer, activeMultiplayerRoom.roomId,
                multiplayerSessionToken, song, PlatformStoragePaths.PreferredMultiplayerSongRoot(),
                (fraction, status) => {
                    ReportMultiplayerTransferProgressV2(song.packageHash, "download", "running", fraction);
                    SetMultiplayerRoomStatusV2(status + "  " + Mathf.RoundToInt(fraction * 100) + "%", false);
                },
                (success, error, imported) => {
                    downloadSucceeded = success;
                    downloadedImport = imported;
                    multiplayerDownloading = false;
                    if (!success)
                    {
                        ReportMultiplayerTransferProgressV2(song.packageHash, "download", "failed", 0f, true);
                        multiplayerClient.SetSongSyncStatus(song.packageHash, false, 0f, true);
                        SetMultiplayerRoomStatusV2(error, true);
                    }
                });
            if (downloadSucceeded)
            {
                RefreshCatalogAfterImport(downloadedImport);
                Task<SongChartEntry> imported = FindLocalMultiplayerChartV2(song);
                while (!imported.IsCompleted) yield return null;
                if (imported.Status == TaskStatus.RanToCompletion && imported.Result != null)
                {
                    selectedSong = catalog.FirstOrDefault(value => value.Id == song.songId);
                    selectedChart = imported.Result;
                    ReportMultiplayerTransferProgressV2(song.packageHash, "download", "completed", 1f, true);
                    multiplayerClient.SetSongSyncStatus(song.packageHash, true, 1f);
                    PlayMultiplayerRoomAudioV2(activeMultiplayerRoom);
                }
                else
                {
                    multiplayerClient.SetSongSyncStatus(song.packageHash, false, 0f, true);
                    SetMultiplayerRoomStatusV2("歌曲已下载，但没有在联机歌曲库中找到对应谱面。", true);
                }
            }
        }

        private Task<SongChartEntry> FindLocalMultiplayerChartV2(MultiplayerSongDto song)
        {
            SongEntry local = catalog.FirstOrDefault(value => value.Id == song.songId);
            SongChartEntry chart = local?.Charts.FirstOrDefault(value => value.Difficulty == song.difficulty);
            if (chart == null || string.IsNullOrWhiteSpace(song.chartHash)) return Task.FromResult<SongChartEntry>(null);
            return CheckChartHashV2(chart, song.chartHash);
        }

        private async Task<SongChartEntry> CheckChartHashV2(SongChartEntry chart, string expectedHash)
        {
            try
            {
                string actual = await MultiplayerSongTransfer.ComputeSha256Async(chart.Path);
                return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase) ? chart : null;
            }
            catch { return null; }
        }

        // 联机回合准备状态机：先确认回合令牌和本地文件，再解析/绑定游玩组件，
        // 最后等待统一起点。任何阶段失败都必须让当前 token 失效，防止旧回合回调重新打开加载页。
        private IEnumerator PrepareMultiplayerRoundV2(MultiplayerEnvelope message, int preparationToken)
        {
            if (activeMultiplayerRoom == null || activeMultiplayerRoom.song == null) yield break;
            MultiplayerSongDto song = activeMultiplayerRoom.song;
            MultiplayerLog.Info("gameplay-start", "stage=local-song-path room=" + activeMultiplayerRoom.roomId +
                " round=" + (message.roundId ?? "-") + " song=" + (song.songId ?? "-"));
            Task<SongChartEntry> chartTask = FindLocalMultiplayerChartV2(song);
            while (!chartTask.IsCompleted) yield return null;
            if (preparationToken != multiplayerRoundPreparationToken || multiplayerRoundId != message.roundId)
                yield break;
            if (chartTask.Status != TaskStatus.RanToCompletion || chartTask.Result == null)
            {
                MultiplayerLog.Error("gameplay-start", "stage=local-chart-missing round=" + (message.roundId ?? "-"));
                multiplayerClient.RoundLoadFailed(message.roundId);
                yield break;
            }
            selectedSong = catalog.FirstOrDefault(value => value.Id == song.songId);
            selectedChart = chartTask.Result;
            MultiplayerLog.Info("gameplay-start", "stage=local-chart-path round=" + (message.roundId ?? "-") +
                " path=" + selectedChart.Path);
            multiplayerRoundId = message.roundId;
            multiplayerInRound = true;
            ApplyRuntimeSettings();
            PrepareSelectedGameplayScene();
            MultiplayerLog.Info("gameplay-start", "stage=gameplay-page-switch-begin room=" + activeMultiplayerRoom.roomId +
                " round=" + (message.roundId ?? "-"));
            SwitchTo(AppPage.Gameplay, () => {
                if (preparationToken != multiplayerRoundPreparationToken || multiplayerRoundId != message.roundId ||
                    !multiplayerInRound) return;
                MultiplayerLog.Info("gameplay-start", "stage=gameplay-page-switch-complete room=" + activeMultiplayerRoom.roomId +
                    " round=" + (message.roundId ?? "-"));
                MultiplayerLog.Info("gameplay-start", "stage=playback-session-created round=" + (message.roundId ?? "-"));
                gameplaySession.PlaySong(playedSong, playedChart, activeMultiplayerRoom.mods?.musicRate ?? 1f,
                activeMultiplayerRoom.mods?.autoPlay ?? false,
                (activeMultiplayerRoom.mods?.airAreaAuto ?? false) || (activeMultiplayerRoom.mods?.fullscreenTouchpad ?? false),
                (activeMultiplayerRoom.mods?.directionalAirAuto ?? false) || (activeMultiplayerRoom.mods?.fullscreenTouchpad ?? false),
                activeMultiplayerRoom.mods?.mineImmunity ?? false,
                activeMultiplayerRoom.mods?.airLongNoMotion ?? false,
                playedMusicOffsetMs, playedJudgeOffsetMs,
                onReady: () => {
                    if (preparationToken != multiplayerRoundPreparationToken || multiplayerRoundId != message.roundId ||
                        !multiplayerInRound) return;
                    MultiplayerLog.Info("gameplay-start", "stage=renderer-judge-input-bound round=" +
                        (message.roundId ?? "-"));
                    MultiplayerLog.Info("gameplay-start", "stage=first-gameplay-frame-rendered room=" + activeMultiplayerRoom.roomId +
                        " round=" + (message.roundId ?? "-"));
                    multiplayerRoundPlaybackPrepared = true;
                    multiplayerClient.SetRoundLoaded(message.roundId);
                    TryStartMultiplayerRoundPlaybackV2(message.roundId);
                    TryBeginMultiplayerRoundRevealV2(message.roundId);
                },
                onFailed: error => {
                    if (preparationToken != multiplayerRoundPreparationToken || multiplayerRoundId != message.roundId)
                        return;
                    MultiplayerLog.Error("gameplay-start", "stage=failed round=" + (message.roundId ?? "-") +
                        " reason=" + error);
                    multiplayerClient.RoundLoadFailed(message.roundId);
                    multiplayerInRound = false;
                    multiplayerRoundStarted = false;
                    CancelMultiplayerRoundPreparationV2();
                    gameplaySession?.StopPlayback();
                    FindObjectOfType<RuntimeLoadingOverlay>()?.Hide();
                    ClearGameplayVisuals();
                    SetGameplayScene(false);
                    SetMultiplayerRoomStatusV2("本局加载失败，请重新开始。", true);
                },
                keepLoadingVisible: true,
                startDelaySeconds: .2f,
                deferStart: true,
                mirrorChart: activeMultiplayerRoom.mods?.mirrorChart ?? false,
                nonMissJusticeCritical: activeMultiplayerRoom.mods?.nonMissJusticeCritical ?? false,
                flickAllTap: activeMultiplayerRoom.mods?.flickAllTap ?? false,
                uniformSpeed: activeMultiplayerRoom.mods?.uniformSpeed ?? false);
            });
            multiplayerRoundPreparationCoroutine = null;
        }

        // There are two independent network events: local preparation completion
        // and the server's shared start time. This gate is intentionally the only
        // place that schedules the gameplay AudioSource and DSP chart clock.
        private void TryStartMultiplayerRoundPlaybackV2(string roundId)
        {
            if (!multiplayerInRound || !multiplayerRoundStarted || !multiplayerRoundStartPending ||
                !multiplayerRoundPlaybackPrepared || gameplaySession == null ||
                !string.Equals(multiplayerRoundId, roundId, StringComparison.Ordinal)) return;
            if (!gameplaySession.StartPreparedAtDspTime(multiplayerPendingRoundStartDsp)) {
                MultiplayerLog.Error("gameplay-start", "stage=prepared-session-missing round=" + (roundId ?? "-"));
                AbortMultiplayerRoundV2("本地游玩资源准备失败。", true);
                return;
            }
            multiplayerRoundStartPending = false;
            MultiplayerLog.Info("gameplay-start", "stage=audio-and-timeline-scheduled round=" +
                (roundId ?? "-") + " startDsp=" + multiplayerPendingRoundStartDsp.ToString("F3", CultureInfo.InvariantCulture));
        }

        // Start the reveal coroutine at most once per round. The coroutine is
        // allowed to wait for IsPrepared because server countdown and local
        // decoding can complete in either order.
        private void TryBeginMultiplayerRoundRevealV2(string roundId)
        {
            if (!multiplayerInRound || !multiplayerRoundStarted || multiplayerRoundRevealStarted ||
                !string.Equals(multiplayerRoundId, roundId, StringComparison.Ordinal)) return;
            RuntimeLoadingOverlay overlay = FindObjectOfType<RuntimeLoadingOverlay>();
            if (overlay == null) {
                MultiplayerLog.Warn("gameplay-start", "stage=loading-overlay-missing round=" + (roundId ?? "-"));
                return;
            }
            multiplayerRoundRevealStarted = true;
            multiplayerRoundRevealCoroutine = StartCoroutine(
                RevealMultiplayerPreparationV2(overlay, multiplayerPendingRoundStartDsp, roundId));
        }

        // 在服务器指定的 Unix 时间到达前保持预备态；这里只更新提示，不提前推进谱面或播放音乐。
        // 起点到达后由播放会话同时启动音频、谱面时钟和判定输入，避免联机成员各自漂移。
        private IEnumerator RevealMultiplayerPreparationV2(RuntimeLoadingOverlay overlay, double startAtDsp, string roundId)
        {
            // The server already reserves a common start time. Reveal the
            // playfield two seconds before it, without shifting any player's
            // audio clock by a locally measured wait.
            const float revealSeconds = .18f;
            double revealAtDsp = startAtDsp - LocalChartPlaybackSession.PreparationSeconds - revealSeconds;
            while (PlatformServices.Current.AudioTime.Seconds < revealAtDsp) {
                if (!multiplayerInRound || multiplayerRoundId != roundId) yield break;
                yield return null;
            }
            // Never reveal a stale or unprepared playfield. If local decoding is
            // slower than the server's reveal point, keep the loading surface up
            // until the ready callback has bound the renderer and judge.
            float waitStarted = Time.unscaledTime;
            while (multiplayerInRound && multiplayerRoundId == roundId &&
                   (gameplaySession == null || !gameplaySession.IsPrepared)) {
                if (Time.unscaledTime - waitStarted > 35f) {
                    MultiplayerLog.Error("gameplay-start", "stage=prepared-session-timeout round=" + (roundId ?? "-"));
                    AbortMultiplayerRoundV2("歌曲准备超时，请重新尝试。", true);
                    yield break;
                }
                yield return null;
            }
            if (multiplayerInRound && multiplayerRoundId == roundId) {
                yield return overlay.HideAnimated(revealSeconds);
                MultiplayerLog.Info("gameplay-start", "stage=loading-overlay-closed round=" + (roundId ?? "-"));
            }
            multiplayerRoundRevealCoroutine = null;
        }

        private void CancelMultiplayerRoundPreparationV2()
        {
            multiplayerRoundPreparationToken++;
            multiplayerRoundStartPending = false;
            multiplayerRoundPlaybackPrepared = false;
            multiplayerRoundRevealStarted = false;
            multiplayerPendingRoundStartDsp = 0;
            if (multiplayerRoundPreparationCoroutine != null)
            {
                StopCoroutine(multiplayerRoundPreparationCoroutine);
                multiplayerRoundPreparationCoroutine = null;
            }
            if (multiplayerRoundRevealCoroutine != null)
            {
                StopCoroutine(multiplayerRoundRevealCoroutine);
                multiplayerRoundRevealCoroutine = null;
            }
        }

        private void FinishMultiplayerGameplayV2()
        {
            if (gameplaySession == null || activeMultiplayerRoom == null) return;
            GameplayScoreState score = gameplaySession.ScoreState;
            int total = gameplaySession.JudgeTotal;
            int scoreValue = score?.Score(total) ?? 0;
            MultiplayerModsMessage roomMods = activeMultiplayerRoom.mods;
            bool recordsScore = activeMultiplayerRoom.mode == "normal";
            playedHasAnyMod = !string.Equals(CurrentMultiplayerModSummaryV2(roomMods), "NORMAL", StringComparison.Ordinal);
            playedModLabel = CurrentMultiplayerModSummaryV2(roomMods);
            var model = new ResultViewModel {
                SongName = playedSong.Name, Artist = playedSong.Artist, Difficulty = playedChart.Difficulty, Level = playedChart.Level,
                Score = scoreValue, JusticeCritical = score?.JusticeCritical ?? 0, Justice = score?.Justice ?? 0,
                Attack = score?.Attack ?? 0, Miss = score?.Miss ?? 0,
                FastAverageMs = playedDeltas.Where(value => value < 0).DefaultIfEmpty(0).Average(),
                LateAverageMs = playedDeltas.Where(value => value > 0).DefaultIfEmpty(0).Average(),
                TotalAverageMs = playedDeltas.Count == 0 ? 0 : playedDeltas.Average(), ModLabel = CurrentMultiplayerModSummaryV2(roomMods)
            };
            multiplayerClient.SubmitResult(multiplayerRoundId, model.Score, model.JusticeCritical, model.Justice, model.Attack, model.Miss,
                score?.MaxCombo ?? 0);
            CancelMultiplayerRoundPreparationV2();
            gameplaySession.StopPlayback();
            ClearGameplayVisuals();
            SetGameplayScene(false);
            multiplayerPersonalResultPending = true;
            multiplayerPersonalResultDeadline = Time.unscaledTime + 10f;
            multiplayerInRound = false;
            ShowResults(model, playedSong, playedChart, recordsScore);
            ConfigureMultiplayerPersonalResultV2();
        }

        private void ConfigureMultiplayerPersonalResultV2()
        {
            if (resultReplayButton != null) resultReplayButton.gameObject.SetActive(false);
            if (resultContinueButton == null) return;
            resultContinueButton.gameObject.SetActive(true);
            resultContinueButton.onClick.RemoveAllListeners();
            resultContinueButton.onClick.AddListener(ConfirmMultiplayerPersonalResultV2);
            SetButtonLabel(resultContinueButton, "继续");
        }

        private void ConfirmMultiplayerPersonalResultV2()
        {
            if (!multiplayerPersonalResultPending) return;
            multiplayerPersonalResultPending = false;
            multiplayerClient.ConfirmPersonalResult();
            SetButtonLabel(resultContinueButton, "等待其他成员");
            resultContinueButton.interactable = false;
        }

        private void RenderMultiplayerGroupResultsV2(MultiplayerSongDto song, MultiplayerLeaderboardEntryDto[] leaderboard)
        {
            if (song == null) return;
            multiplayerGroupSong.text = (song.songName ?? song.songId) + "\n" + song.difficulty + "  Lv." + song.level;
            LocalCoverLoader.Reset(multiplayerGroupCover);
            if (playedSong != null && !string.IsNullOrWhiteSpace(playedSong.JacketPath)) LocalCoverLoader.Load(this, playedSong.JacketPath, multiplayerGroupCover, false);
            foreach (Transform child in multiplayerGroupContent) Destroy(child.gameObject);
            int count = 0;
            foreach (MultiplayerLeaderboardEntryDto entry in leaderboard ?? Array.Empty<MultiplayerLeaderboardEntryDto>())
            {
                if (entry == null) continue;
                Color color = entry.placement == 1 ? new Color(.76f, .56f, .12f, .98f) :
                    entry.placement == 2 ? new Color(.48f, .55f, .62f, .98f) :
                    entry.placement == 3 ? new Color(.56f, .31f, .16f, .98f) : surfaceRaised;
                string label = (entry.placement == 0 ? "退出" : "#" + entry.placement) + "  " + entry.name +
                    "\n" + entry.score.ToString("N0") + "  " + entry.rank + "   JC " + entry.justiceCritical + "  J " + entry.justice +
                    "  AT " + entry.attack + "  MISS " + entry.miss;
                Button row = ButtonText(label, multiplayerGroupContent, Vector2.zero, Vector2.one, color, null);
                RectTransform rect = (RectTransform)row.transform;
                rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(.5f, 1);
                float top = count * 78f + 4f;
                rect.offsetMin = new Vector2(6, -top - 72f); rect.offsetMax = new Vector2(-6, -top);
                Text text = row.GetComponentInChildren<Text>();
                if (text != null) { text.fontSize = 17; text.alignment = TextAnchor.MiddleLeft; text.rectTransform.offsetMin = new Vector2(18, 0); }
                count++;
            }
            multiplayerGroupContent.sizeDelta = new Vector2(0, count * 78f + 8f);
        }

        private void ConfirmMultiplayerGroupResultsV2()
        {
            if (!multiplayerGroupResultPending) return;
            multiplayerGroupResultPending = false;
            multiplayerClient.ConfirmGroupResult();
            multiplayerGroupContinueButton.interactable = false;
            multiplayerGroupStatus.text = "等待其他成员确认…";
        }

        private void ConfigureMultiplayerPauseV2()
        {
            if (!multiplayerInRound) return;
            if (gameplayRestartButton != null) gameplayRestartButton.gameObject.SetActive(false);
            if (gameplayExitButton != null)
            {
                SetButtonLabel(gameplayExitButton, "退出本局");
                gameplayExitButton.onClick.RemoveAllListeners();
                gameplayExitButton.onClick.AddListener(ExitMultiplayerRoundV2);
            }
        }

        private void ExitMultiplayerRoundV2()
        {
            if (!multiplayerInRound) return;
            multiplayerClient.ExitRound();
            CancelMultiplayerRoundPreparationV2();
            multiplayerInRound = false;
            multiplayerRoundStarted = false;
            gameplaySession?.StopPlayback();
            FindObjectOfType<RuntimeLoadingOverlay>()?.Hide();
            gameplayPausePanel?.gameObject.SetActive(false);
            ClearGameplayVisuals();
            SetGameplayScene(false);
            SwitchTo(AppPage.MultiplayerRoom, RenderMultiplayerRoomV2);
        }

        private void AbortMultiplayerRoundV2(string message, bool notifyError)
        {
            CancelMultiplayerRoundPreparationV2();
            multiplayerInRound = false;
            multiplayerRoundStarted = false;
            gameplaySession?.StopPlayback();
            FindObjectOfType<RuntimeLoadingOverlay>()?.Hide();
            ClearGameplayVisuals();
            SetGameplayScene(false);
            SwitchTo(AppPage.MultiplayerRoom, () => SetMultiplayerRoomStatusV2(message, notifyError));
        }

        private void ToggleMultiplayerReadyV2()
        {
            if (activeMultiplayerRoom == null) return;
            MultiplayerRoomMemberDto self = (activeMultiplayerRoom.members ?? Array.Empty<MultiplayerRoomMemberDto>()).FirstOrDefault(value => value?.playerId == multiplayerGameId);
            if (self != null) multiplayerClient.SetReady(!self.ready);
        }

        private void ToggleMultiplayerSongChangeVoteV2()
        {
            if (activeMultiplayerRoom == null || multiplayerClient == null) return;
            MultiplayerSongChangeVoteDto vote = activeMultiplayerRoom.songChangeVote;
            if (vote != null && vote.active) multiplayerClient.CastSongChangeVote();
            else multiplayerClient.RequestSongChangeVote();
            SetMultiplayerRoomStatusV2("已提交换歌投票，等待其他成员。", false);
        }

        private void ToggleMultiplayerRoomModeV2()
        {
            if (!IsLocalMultiplayerHostV2() || activeMultiplayerRoom == null) return;
            string mode = activeMultiplayerRoom.mode == "normal" ? "casual" : "normal";
            MultiplayerModsMessage currentMods = activeMultiplayerRoom.mods ?? new MultiplayerModsMessage();
            MultiplayerModsMessage nextMods = new MultiplayerModsMessage {
                autoPlay = currentMods.autoPlay,
                airAreaAuto = currentMods.airAreaAuto,
                directionalAirAuto = currentMods.directionalAirAuto,
                airLongNoMotion = currentMods.airLongNoMotion,
                mineImmunity = currentMods.mineImmunity,
                mirrorChart = currentMods.mirrorChart,
                flickAllTap = currentMods.flickAllTap,
                fullscreenTouchpad = currentMods.fullscreenTouchpad,
                nonMissJusticeCritical = currentMods.nonMissJusticeCritical,
                uniformSpeed = currentMods.uniformSpeed,
                musicRate = currentMods.musicRate
            };
            if (mode == "normal")
            {
                nextMods.autoPlay = false;
                nextMods.airAreaAuto = false;
                nextMods.directionalAirAuto = false;
                nextMods.mineImmunity = false;
                nextMods.fullscreenTouchpad = false;
                nextMods.musicRate = 1f;
            }
            SendMultiplayerRoomSettingsV2(mode, activeMultiplayerRoom.selectionMode, nextMods);
        }

        private void ToggleMultiplayerRoomModsV2()
        {
            if (!IsLocalMultiplayerHostV2() || activeMultiplayerRoom == null) return;
            OpenMultiplayerModPickerV2(true);
        }

        private void ToggleMultiplayerRoomSelectionV2()
        {
            if (!IsLocalMultiplayerHostV2() || activeMultiplayerRoom == null) return;
            SendMultiplayerRoomSettingsV2(activeMultiplayerRoom.mode,
                activeMultiplayerRoom.selectionMode == "host" ? "loop" : "host", activeMultiplayerRoom.mods);
        }

        private void SendMultiplayerRoomSettingsV2(string mode, string selection, MultiplayerModsMessage roomMods)
        {
            multiplayerClient.Send(JsonUtility.ToJson(new MultiplayerRoomSettingsRequestV2 {
                roomMode = mode, selectionMode = selection, mods = roomMods
            }));
        }

        private void ConfirmKickMultiplayerMemberV2(string playerId, string playerName)
        {
            ShowModal("移出成员", "确定将“" + playerName + "”移出房间？\n该成员一分钟内不能再次加入本房间。",
                () => multiplayerClient.KickPlayer(playerId), true);
        }

        private void ConfirmTransferMultiplayerHostV2(string playerId, string playerName)
        {
            ShowModal("转让房主", "确定将房主转让给“" + playerName + "”？", () => multiplayerClient.TransferHost(playerId), true);
        }

        private bool IsLocalMultiplayerHostV2() => activeMultiplayerRoom != null && activeMultiplayerRoom.hostPlayerId == multiplayerGameId;
        private bool IsLocalMultiplayerOwnerV2() => activeMultiplayerRoom != null && activeMultiplayerRoom.ownerPlayerId == multiplayerGameId;

        private MultiplayerModsMessage BuildMultiplayerModsV2(bool scoringOnly)
        {
            MultiplayerModsMessage value = new MultiplayerModsMessage {
                autoPlay = false, airAreaAuto = mods.AirAreaAuto, directionalAirAuto = mods.DirectionalAirAuto,
                airLongNoMotion = mods.AirLongNoMotion, mineImmunity = mods.MineImmunity,
                mirrorChart = mods.MirrorChart,
                flickAllTap = mods.FlickAllTap,
                fullscreenTouchpad = mods.FullscreenTouchpad,
                nonMissJusticeCritical = mods.NonMissJusticeCritical,
                uniformSpeed = mods.UniformSpeed,
                musicRate = mods.MusicRate
            };
            if (scoringOnly) { value.autoPlay = false; value.airAreaAuto = false; value.directionalAirAuto = false; value.mineImmunity = false; value.flickAllTap = false; value.fullscreenTouchpad = false; value.nonMissJusticeCritical = false; value.musicRate = 1f; }
            return value;
        }

        private string CurrentMultiplayerModSummaryV2(MultiplayerModsMessage value)
        {
            if (value == null) return "NORMAL";
            List<string> labels = new List<string>();
            if (value.fullscreenTouchpad) labels.Add("全屏触控板");
            if (value.autoPlay) labels.Add("AUTO");
            if (value.airAreaAuto) labels.Add("AIR AUTO");
            if (value.directionalAirAuto) labels.Add("AIR UP/DOWN");
            if (value.mineImmunity) labels.Add("MINE OFF");
            if (value.airLongNoMotion) labels.Add("AIR 长条简单");
            if (value.nonMissJusticeCritical) labels.Add("简单判定");
            if (value.mirrorChart) labels.Add("MIRROR");
            if (value.flickAllTap) labels.Add("FLICK 可点击判定");
            if (value.uniformSpeed) labels.Add("匀速模式");
            if (Math.Abs(value.musicRate - 1f) > .01f) labels.Add(value.musicRate.ToString("0.0") + "x");
            return labels.Count == 0 ? "NORMAL" : string.Join(" + ", labels);
        }

        private static string MultiplayerStateLabelV2(string state)
        {
            switch (state)
            {
                case "syncing": return "同步歌曲";
                case "loading": return "加载中";
                case "countdown": return "即将开始";
                case "playing": return "游玩中";
                case "personalResults": return "个人结算";
                case "groupResults": return "团队排名";
                default: return "准备中";
            }
        }

        private static string MultiplayerMemberStatusV2(MultiplayerRoomMemberDto member)
        {
            if (!member.connected) return "已断开";
            if (!string.IsNullOrWhiteSpace(member.status))
            {
                switch (member.status) {
                    case "syncing": return "正在同步歌曲 " + Mathf.RoundToInt(member.syncProgress * 100) + "%";
                    case "loading": return "正在加载";
                    case "playing": return "正在游玩";
                    case "personalResult": return "正在结算";
                    case "groupResult": return "查看排名";
                    case "waitingLobby": return "等待回到准备房间";
                }
            }
            return member.ready ? "已准备" : (member.songReady ? "未准备" : "等待歌曲");
        }

        private void BackToMultiplayerLoginV2()
        {
            multiplayerManualDisconnect = true;
            multiplayerRetryInProgress = false;
            if (multiplayerReconnectCoroutine != null)
            {
                StopCoroutine(multiplayerReconnectCoroutine);
                multiplayerReconnectCoroutine = null;
            }
            if (activeMultiplayerRoom != null) multiplayerClient.LeaveRoom();
            multiplayerLeavingLobby = true;
            multiplayerClient?.LeaveLobby();
            preview?.StopPreview();
            interfaceAudio?.StopBackgroundMusicForSongPreview();
            activeMultiplayerRoom = null;
            multiplayerAuthenticatedV2 = false;
            multiplayerLobbyReentryUntilMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60 * 1000L;
            PlayerPrefs.SetString("chunithm_mobile_multiplayer_reentry_until", multiplayerLobbyReentryUntilMs.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
            SwitchTo(AppPage.Multiplayer, () => {
                RestoreMultiplayerServerV2();
                UpdateMultiplayerReentryUiV2();
            });
        }

        private void LeaveMultiplayerModeV2()
        {
            CancelMultiplayerRoundPreparationV2();
            SaveMultiplayerDraftV2();
            multiplayerManualDisconnect = true;
            multiplayerRetryInProgress = false;
            if (multiplayerReconnectCoroutine != null)
            {
                StopCoroutine(multiplayerReconnectCoroutine);
                multiplayerReconnectCoroutine = null;
            }
            multiplayerClient?.Disconnect();
            activeMultiplayerRoom = null;
            multiplayerAuthenticatedV2 = false;
            multiplayerAuthenticated = false;
            multiplayerSessionToken = "";
            multiplayerInRound = false;
            multiplayerRoundStarted = false;
            preview?.StopPreview();
            interfaceAudio?.StopBackgroundMusicForSongPreview();
            SwitchTo(AppPage.Home);
        }

        private IEnumerator DisconnectMultiplayerAfterLobbyLeaveV2()
        {
            yield return new WaitForSecondsRealtime(.5f);
            if (!multiplayerLeavingLobby) yield break;
            multiplayerLeavingLobby = false;
            multiplayerClient?.Disconnect();
        }

        private void LeaveMultiplayerRoomV2()
        {
            if (activeMultiplayerRoom == null) return;
            multiplayerClient.LeaveRoom();
        }

        private void SetMultiplayerLoginStatusV2(string value, bool error)
        {
            if (multiplayerLoginStatus != null) { multiplayerLoginStatus.text = UiLocalization.Translate(value); multiplayerLoginStatus.color = error ? (HighContrastUi ? UiThemeContrast.Danger : new Color(1f, .32f, .35f)) : muted; }
            SetMultiplayerBrowserStatusV2(value, error);
        }

        private void SetMultiplayerBrowserStatusV2(string value, bool error)
        {
            if (multiplayerBrowserStatus != null) { multiplayerBrowserStatus.text = UiLocalization.Translate(value); multiplayerBrowserStatus.color = error ? (HighContrastUi ? UiThemeContrast.Danger : new Color(1f, .32f, .35f)) : muted; }
            if (multiplayerCreateStatus != null && multiplayerCreateLayer != null && multiplayerCreateLayer.gameObject.activeSelf)
            {
                multiplayerCreateStatus.text = UiLocalization.Translate(value);
                multiplayerCreateStatus.color = error ? (HighContrastUi ? UiThemeContrast.Danger : new Color(1f, .32f, .35f)) : muted;
            }
        }

        private void SetMultiplayerRoomStatusV2(string value, bool error)
        {
            if (multiplayerRoomStatus != null) { multiplayerRoomStatus.text = UiLocalization.Translate(value); multiplayerRoomStatus.color = error ? (HighContrastUi ? UiThemeContrast.Danger : new Color(1f, .32f, .35f)) : muted; }
        }

        private void SetMultiplayerRetryStatusV2(string value)
        {
            if (currentPage == AppPage.MultiplayerRoom || currentPage == AppPage.MultiplayerGroupResults)
                SetMultiplayerRoomStatusV2(value, true);
            else SetMultiplayerLoginStatusV2(value, true);
        }

        private void HandleMultiplayerTimeoutV2()
        {
            CancelMultiplayerRoundPreparationV2();
            multiplayerManualDisconnect = true;
            multiplayerRetryInProgress = false;
            if (multiplayerReconnectCoroutine != null)
            {
                StopCoroutine(multiplayerReconnectCoroutine);
                multiplayerReconnectCoroutine = null;
            }
            multiplayerLeavingLobby = false;
            multiplayerClient?.Disconnect();
            multiplayerAuthenticatedV2 = false;
            multiplayerAuthenticated = false;
            activeMultiplayerRoom = null;
            multiplayerInRound = false;
            SwitchTo(AppPage.Multiplayer, () => {
                RestoreMultiplayerServerV2();
                SetMultiplayerLoginStatusV2("等待超时", true);
            });
        }

        private void OnMultiplayerConnectionErrorV2(string error)
        {
            multiplayerAuthenticatedV2 = false;
            if (multiplayerManualDisconnect) return;
            multiplayerLastConnectionError = string.IsNullOrWhiteSpace(error)
                ? "服务器没有返回具体错误" : error.Trim();
            SetMultiplayerRetryStatusV2("连接失败：" + multiplayerLastConnectionError);
            BeginMultiplayerReconnectV2();
        }

        private void OnMultiplayerDisconnectedV2()
        {
            multiplayerAuthenticatedV2 = false;
            multiplayerAuthenticated = false;
            if (multiplayerLeavingLobby) return;
            if (multiplayerManualDisconnect) return;
            BeginMultiplayerReconnectV2();
        }

        private void OnMultiplayerHeartbeatTimeoutV2()
        {
            if (multiplayerManualDisconnect) return;
            multiplayerAuthenticatedV2 = false;
            multiplayerAuthenticated = false;
            multiplayerClient?.Disconnect();
            BeginMultiplayerReconnectV2();
        }

        private void BeginMultiplayerReconnectV2()
        {
            if (multiplayerManualDisconnect || multiplayerRetryInProgress || multiplayerClient == null ||
                string.IsNullOrWhiteSpace(multiplayerConnectedServer) || string.IsNullOrWhiteSpace(multiplayerConnectedPlayerName)) return;
            multiplayerRetryInProgress = true;
            multiplayerReconnectAttempt = 0;
            multiplayerReconnectCoroutine = StartCoroutine(RetryMultiplayerConnectionV2());
        }

        private IEnumerator RetryMultiplayerConnectionV2()
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (multiplayerManualDisconnect) yield break;
                multiplayerReconnectAttempt = attempt;
                SetMultiplayerRetryStatusV2("网络错误，正在重试 " + attempt + "/3");
                multiplayerClient.Disconnect();
                yield return new WaitForSecondsRealtime(attempt == 1 ? .5f : 5f);
                if (multiplayerManualDisconnect) yield break;
                multiplayerClient.Connect(multiplayerConnectedServer, multiplayerConnectedPlayerName,
                    MultiplayerClient.GetStableDeviceId(), Application.version);
                float deadline = Time.unscaledTime + 5f;
                while (!multiplayerManualDisconnect && !multiplayerAuthenticatedV2 && Time.unscaledTime < deadline)
                    yield return null;
                if (multiplayerAuthenticatedV2)
                {
                    multiplayerRetryInProgress = false;
                    multiplayerReconnectAttempt = 0;
                    multiplayerReconnectCoroutine = null;
                    yield break;
                }
            }

            multiplayerRetryInProgress = false;
            multiplayerReconnectAttempt = 0;
            multiplayerReconnectCoroutine = null;
            ExitMultiplayerAfterReconnectFailureV2();
        }

        private void ExitMultiplayerAfterReconnectFailureV2()
        {
            CancelMultiplayerRoundPreparationV2();
            multiplayerManualDisconnect = true;
            multiplayerClient?.Disconnect();
            multiplayerAuthenticatedV2 = false;
            multiplayerAuthenticated = false;
            multiplayerSessionToken = "";
            activeMultiplayerRoom = null;
            multiplayerInRound = false;
            multiplayerRoundStarted = false;
            multiplayerPersonalResultPending = false;
            multiplayerGroupResultPending = false;
            preview?.StopPreview();
            interfaceAudio?.StopBackgroundMusicForSongPreview();
            gameplaySession?.StopPlayback();
            ClearGameplayVisuals();
            SetGameplayScene(false);
            string detail = string.IsNullOrWhiteSpace(multiplayerLastConnectionError)
                ? "服务器没有返回具体错误。"
                : multiplayerLastConnectionError;
            SwitchTo(AppPage.Home, () => ShowModal("网络错误",
                "网络错误，已重试 3 次，已退出在线模式。\n\n最后一次错误：" + detail,
                null, false));
        }

        private void UpdateMultiplayerV2()
        {
            UpdateMultiplayerReentryUiV2();
            if (multiplayerPersonalResultPending && currentPage == AppPage.Results)
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt(multiplayerPersonalResultDeadline - Time.unscaledTime));
                if (seconds <= 0) ConfirmMultiplayerPersonalResultV2();
            }
            if (multiplayerGroupResultPending && currentPage == AppPage.MultiplayerGroupResults)
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt(multiplayerGroupResultDeadline - Time.unscaledTime));
                multiplayerGroupStatus.text = "" + seconds + " 秒后自动返回准备房间";
                if (seconds <= 0) ConfirmMultiplayerGroupResultsV2();
            }
            if (multiplayerBackgroundAtMs > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - multiplayerBackgroundAtMs >= 10 * 60 * 1000)
                HandleMultiplayerTimeoutV2();
        }

        private int MultiplayerReentryRemainingSecondsV2()
        {
            if (multiplayerLobbyReentryUntilMs <= 0)
            {
                string saved = PlayerPrefs.GetString("chunithm_mobile_multiplayer_reentry_until", "");
                if (!long.TryParse(saved, NumberStyles.Integer, CultureInfo.InvariantCulture, out multiplayerLobbyReentryUntilMs))
                    multiplayerLobbyReentryUntilMs = 0;
            }
            long remaining = multiplayerLobbyReentryUntilMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (remaining <= 0)
            {
                multiplayerLobbyReentryUntilMs = 0;
                PlayerPrefs.DeleteKey("chunithm_mobile_multiplayer_reentry_until");
                return 0;
            }
            return Mathf.Max(1, Mathf.CeilToInt(remaining / 1000f));
        }

        private void UpdateMultiplayerReentryUiV2()
        {
            if (currentPage != AppPage.Multiplayer || multiplayerAuthenticatedV2) return;
            int seconds = MultiplayerReentryRemainingSecondsV2();
            if (multiplayerLoginButton != null) multiplayerLoginButton.interactable = seconds <= 0;
            if (seconds == multiplayerLastReentrySeconds) return;
            multiplayerLastReentrySeconds = seconds;
                SetMultiplayerLoginStatusV2(seconds > 0
                ? "退出在线大厅后请等待 " + seconds + " 秒后重新进入。"
                : "输入地址和名称后进入服务器。", seconds > 0);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) {
                PauseGameplay();
                SaveMultiplayerDraftV2();
                settings?.Save();
                uiSession?.Save();
            }
            if (multiplayerClient == null || !multiplayerAuthenticatedV2) return;
            if (paused)
            {
                multiplayerBackgroundAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                multiplayerClient.SendAppState(true);
            }
            else
            {
                multiplayerBackgroundAtMs = 0;
                multiplayerClient.SendAppState(false);
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) {
                PauseGameplay();
                SaveMultiplayerDraftV2();
            }
            if (!focused || multiplayerClient == null || !multiplayerAuthenticatedV2) return;
            multiplayerBackgroundAtMs = 0;
            multiplayerClient.SendAppState(false);
        }

        private bool HandleMultiplayerBackV2()
        {
            if (multiplayerCreateLayer != null && multiplayerCreateLayer.gameObject.activeSelf)
            {
                CancelCreateMultiplayerRoomV2();
                return true;
            }
            if (currentPage == AppPage.SongSelect && multiplayerSelectingSong)
            {
                ReturnToMultiplayerRoomV2();
                return true;
            }
            if (currentPage == AppPage.MultiplayerGroupResults)
            {
                ConfirmMultiplayerGroupResultsV2();
                return true;
            }
            if (currentPage == AppPage.MultiplayerRoom)
            {
                LeaveMultiplayerRoomV2();
                return true;
            }
            if (currentPage == AppPage.MultiplayerBrowser)
            {
                BackToMultiplayerLoginV2();
                return true;
            }
            if (currentPage == AppPage.Multiplayer) SaveMultiplayerDraftV2();
            return false;
        }

        private void RestoreMultiplayerServerV2()
        {
            if (multiplayerServerInput != null)
                multiplayerServerInput.SetTextWithoutNotify(PlayerPrefs.GetString("chunithm_mobile_multiplayer_server", ""));
        }

        private void CacheMultiplayerDraftV2()
        {
            if (multiplayerServerInput != null) PlayerPrefs.SetString("chunithm_mobile_multiplayer_server", multiplayerServerInput.text ?? "");
            if (multiplayerPlayerNameInput != null) PlayerPrefs.SetString("chunithm_mobile_multiplayer_player", multiplayerPlayerNameInput.text ?? "");
        }

        private void SaveMultiplayerDraftV2()
        {
            CacheMultiplayerDraftV2();
            PlayerPrefs.Save();
        }

        [Serializable]
        private sealed class MultiplayerRoomSettingsRequestV2
        {
            public string type = "updateRoomSettings";
            public string roomMode;
            public string selectionMode;
            public MultiplayerModsMessage mods;
        }
    }
}
