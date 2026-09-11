using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ChunithmMobile.Chart;
using ChunithmMobile.Input;
using ChunithmMobile.Judgement;
using ChunithmMobile.Platform;
using ChunithmMobile.Rendering;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// Responsive non-gameplay application shell: loading, home, song select,
    /// settings, mods, confirmation dialogs and result presentation.
    /// </summary>
    public sealed partial class AppUiShell : MonoBehaviour
    {
        private const float LayoutEditorSafeInset = .08f;
        public bool AnswerSoundEnabled => settings != null && settings.AnswerSoundEnabled;
        public float AnswerSoundVolume => settings == null ? 0f : settings.AnswerSoundVolume / 100f;
        public float GameplayMusicVolume => settings == null ? 1f : settings.MusicVolume / 100f;

        private bool HighContrastUi => settings != null && settings.HighContrastUi;
        private static readonly Color GameplayAccent = new Color(.10f, .82f, .96f, 1f);
        private Color ink => HighContrastUi ? UiThemeContrast.PageLight : new Color(.008f, .015f, .030f, 1f);
        private Color surface => HighContrastUi ? UiThemeContrast.PanelLight : new Color(.025f, .052f, .085f, .97f);
        private Color surfaceRaised => HighContrastUi ? Color.white : new Color(.045f, .09f, .14f, .98f);
        private Color cyan => HighContrastUi ? new Color(1f, .91f, .08f) : new Color(.10f, .82f, .96f, 1f);
        private Color violet => HighContrastUi ? new Color(1f, .12f, .59f) : new Color(.58f, .10f, .92f, 1f);
        private readonly Color muted = new Color(.58f, .68f, .78f, 1f);
        private readonly Dictionary<AppPage, RectTransform> pages = new Dictionary<AppPage, RectTransform>();
        private readonly List<MeteorKeyBackground> meteorBackgrounds = new List<MeteorKeyBackground>();
        private readonly ModSettings mods = new ModSettings();
        private AppSettings settings;
        private UiSessionState uiSession;
        private Font font;
        private Font titleFont;
        private const int UiCornerRadius = 12;
        private Sprite rounded;
        private Canvas canvas;
        private AppPage currentPage = AppPage.Loading;
        private bool transitioning;
        private AppPage? queuedPage;
        private Action queuedPageCompletion;
        private float lastHomeBack = -10;
        private IReadOnlyList<SongEntry> catalog = Array.Empty<SongEntry>();
        private sealed class SongListItem
        {
            public SongEntry Song;
            public SongChartEntry Chart;
        }

        // Visible song cards are reused while scrolling and while changing the
        // active bucket. This keeps the list responsive when the catalog is large.
        private sealed class SongCardVisual
        {
            public RectTransform Card;
            public Button Button;
            public Image Border;
            public Image Art;
            public Text Placeholder;
            public Text Title;
            public Text Artist;
            public Text Level;
            public Text Score;
            public Text Rank;
            public Text Bpm;
            public Text Duration;
            public string Root;
        }

        private List<SongListItem> visibleSongs = new List<SongListItem>();
        private SongEntry selectedSong;
        private SongChartEntry selectedChart;
        private SongSortMode sortMode = SongSortMode.Category;
        private int categoryBucketIndex;
        private int levelBucketIndex;
        private string searchText = "";
        private List<string> categories = new List<string>();
        private List<string> levels = new List<string>();
        private Transform songGrid;
        private CanvasGroup songGridGroup;
        private Coroutine songGridTransition;
        private Coroutine songSelectionPulse;
        private readonly Dictionary<string, List<Image>> songCardBorders = new Dictionary<string, List<Image>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, RectTransform> virtualSongCards = new Dictionary<int, RectTransform>();
        private readonly Dictionary<RectTransform, SongCardVisual> songCardVisuals = new Dictionary<RectTransform, SongCardVisual>();
        private readonly Stack<SongCardVisual> songCardPool = new Stack<SongCardVisual>();
        private RectTransform songListPanel;
        private RectTransform songDetailPanel;
        private RectTransform songViewport;
        private ScrollRect songScroll;
        private int gridColumns = 4;
        private Vector2 songCardSize = new Vector2(230, 292);
        private float songGridGap = 14f;
        private const float SongGridInset = 18f;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private Coroutine gridLayoutRefresh;
        private Transform difficultyRow;
        private Text detailTitle;
        private Text detailMeta;
        private Image detailCover;
        private GameObject detailCoverPlaceholder;
        private Text bucketLabel;
        private Text sortLabel;
        private Text modLabel;
        private Text loadingLabel;
        private Text homeLibraryLabel;
        private InputField multiplayerServerInput;
        private InputField multiplayerPlayerNameInput;
        private InputField multiplayerRoomNameInput;
        private InputField multiplayerRoomIdInput;
        private InputField multiplayerPasswordInput;
        private InputField multiplayerSearchInput;
        private Text multiplayerStatus;
        private Text multiplayerQuotaLabel;
        private Text multiplayerRoomDetails;
        private RectTransform multiplayerRoomListContent;
        private Button multiplayerCreateButton;
        private Button multiplayerJoinButton;
        private Button multiplayerSelectSongButton;
        private Button multiplayerReadyButton;
        private Button multiplayerStartButton;
        private Button multiplayerLeaveButton;
        private MultiplayerClient multiplayerClient;
        private MultiplayerQuotaDto multiplayerQuota;
        private MultiplayerRoomDto selectedMultiplayerRoom;
        private MultiplayerRoomDto activeMultiplayerRoom;
        private MultiplayerRoomDto[] multiplayerRooms = Array.Empty<MultiplayerRoomDto>();
        private string multiplayerGameId = "";
        private string multiplayerConnectedServer = "";
        private string multiplayerConnectedPlayerName = "";
        private bool multiplayerAuthenticated;
        private string multiplayerPendingAction = "";
        private Text managementSummary;
        private RectTransform managementContent;
        private ScrollRect managementScroll;
        private string managementSearchText = "";
        private bool managementSkinMode;
        private string managedSkinId = "";
        private int managedSkinPreviewPage;
        private AppPage managementReturnPage = AppPage.Home;
        private RectTransform managementPage;
        private InputField managementSearch;
        private Button managementSongTab;
        private Button managementSkinTab;
        private Button managementImportButton;
        private Button managementRefreshButton;
        private Button managementDeleteButton;
        private Button managementSelectAllButton;
        private Button managementBatchButton;
        private Button managementEnterSongButton;
        private Text managementHint;
        private readonly HashSet<string> managedSongRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private RectTransform metadataLayer;
        private Text metadataSelectionLabel;
        private Text metadataHint;
        private InputField metadataNameInput;
        private InputField metadataArtistInput;
        private InputField metadataCategoryInput;
        private RectTransform importLayer;
        private Text importStatus;
        private Text importTitle;
        private Text importEta;
        private Text importCurrent;
        private Text importDetails;
        private RectTransform importDetailsViewport;
        private Image importProgressFill;
        private Button importClose;
        private bool importRunning;
        private bool gameplaySkinImportPending;
        private bool importHadCopyPhase;
        private bool importProgressIndeterminate;
        private bool backgroundImageImport;
        private int backgroundImageTotal;
        private string backgroundImageStaging;
        private float importStartedRealtime;
        private float importCopyStartedRealtime;
        private float importDataStartedRealtime;
        private long importDataTotalBytes = -1;
        private string importDataStage = "";
        private readonly ConcurrentQueue<SongImportProgress> pendingImportProgress = new ConcurrentQueue<SongImportProgress>();
        private readonly List<string> importDetailLines = new List<string>();
        private Image loadingFill;
        private RectTransform modalLayer;
        private Text modalTitle;
        private Text modalBody;
        private Button modalConfirm;
        private Button modalOptionA;
        private Button modalOptionB;
        private UnityEngine.Events.UnityAction modalAction;
        private RectTransform updateNoticeLayer;
        private Button updateNoticeConfirm;
        private Button updateNoticeLogsButton;
        private bool updateLogsExpanded;
        private int updateLogIndex;
        private RectTransform modsLayer;
        private Button autoPlayButton;
        private Button airAreaAutoButton;
        private Button directionalAirAutoButton;
        private Button airLongNoMotionButton;
        private Button nonMissJusticeCriticalButton;
        private Button mirrorChartButton;
        private Button mineImmunityButton;
        private readonly List<Button> rateButtons = new List<Button>();
        private ResultViewModel result = new ResultViewModel();
        private Image resultCover;
        private GameObject resultCoverPlaceholder;
        private Text resultSong;
        private Text resultScore;
        private Text resultRank;
        private Text resultJudgements;
        private Text resultPreviousScore;
        private Text resultNewBadge;
        private Coroutine resultNewPulse;
        private RectTransform settingsBody;
        private ScrollRect settingsScroll;
        private CanvasGroup settingsContentGroup;
        private Coroutine settingsTransition;
        private float settingsCursor;
        private AudioSource settingsPreviewSource;
        private string settingsCategory = "延迟";
        private static readonly string[] SettingsCategories = { "延迟", "画面", "皮肤", "音频", "更多" };
        private readonly Dictionary<string, Button> settingsCategoryButtons = new Dictionary<string, Button>();
        private static readonly string[] UpdateLogVersions = { "0.2.8", "0.2.7", "0.2.6", "0.2.5", "0.2.4", "0.2.3", "0.2.2" };
        private static readonly string[][] UpdateLogLineKeys = {
            new[] {
                "0.2.8 更新日志",
                "0.2.8 1. 优化折线判定",
                "0.2.8 2. 优化皮肤加载",
                "0.2.8 3. 修复地雷音符bug",
                "0.2.8 4. 修复等级分级不正确bug",
                "0.2.8 5. 修复谱面开始音符消失bug",
                "0.2.8 6. 画面设置增加限制帧率选项"
            },
            new[] {
                "0.2.7 更新日志",
                "0.2.7 1. 优化 AIR / FLICK 手感",
                "0.2.7 2. 新增 UI 配色",
                "0.2.7 3. 优化部分 UI 布局",
                "0.2.7 4. 新增游戏图标",
                "0.2.7 5. 修复游玩界面的音符 bug",
                "0.2.7 6. 优化帧率"
            },
            new[] {
                "0.2.6 更新日志",
                "0.2.6 1. 更新轨道和谱面音符皮肤",
                "0.2.6 2. 开放皮肤系统",
                "0.2.6 3. 数据管理新增皮肤数据管理",
                "0.2.6 4. 优化设置界面"
            },
            new[] {
                "0.2.5 更新日志",
                "0.2.5 1. 性能优化",
                "0.2.5 2. 优化游玩手感与谱面播放",
                "0.2.5 3. 增加联机模式",
                "0.2.5 4. 新增镜像 mod",
                "0.2.5 5. 优化 flick 简单模式 mod",
                "0.2.5 6. 增加制作与感谢名单"
            },
            new[] {
                "0.2.4 更新日志",
                "0.2.4 更新触控范围选项",
                "0.2.4 新增触控板透明度设置",
                "0.2.4 更新可视化界面设置",
                "0.2.4 新增 flick 简单模式记录分数 mod",
                "0.2.4 优化音乐列表显示",
                "0.2.4 更新结算界面"
            },
            new[] {
                "0.2.3 更新日志",
                "0.2.3 AIR 拖尾与谱面装饰线分离渲染",
                "0.2.3 1. 优化谱面音符显示",
                "0.2.3 2. 优化多色谱面装饰线显示",
                "0.2.3 3. 修复谱面装饰线显示异常",
                "0.2.3 4. 增加装饰线采样精度，改善复杂谱面的显示效果",
                "0.2.3 5. 增加彩虹拖尾设置与「皮肤」设置",
                "0.2.3 6. 优化游玩体验",
                "0.2.3 7. 修改延迟设置，调整为音乐延迟与判定延迟",
                "0.2.3 8. 优化延迟自测",
                "0.2.3 9. 增加背景自定义设置"
            },
            new[] {
                "0.2.2 更新日志",
                "0.2.2 增加多语言",
                "0.2.2 优化判定",
                "0.2.2 增加 air 长条简单模式（可以不用上下动即可判定）",
                "0.2.2 MODS 增加保留成绩 MOD",
                "0.2.2 可视化界面设置更新",
                "0.2.2 修复一些 bug"
            }
        };
        private RectTransform layoutEditorLayer;
        private RectTransform layoutEditorPreview;
        private RectTransform editorPauseHandle;
        private RectTransform editorHudHandle;
        private RectTransform editorComboHandle;
        private RectTransform editorJudgementHandle;
        private RectTransform editorSongInfoHandle;
        private RectTransform editorTouchpadHandle;
        private RectTransform editorTouchpadWidthHandle;
        private RectTransform editorAirTopHandle;
        private RectTransform editorTrackHandle;
        private RectTransform editorTrackWidthHandle;
        private RectTransform editorTrackTopWidthHandle;
        private RectTransform editorTrackMaskHandle;
        private RectTransform layoutEditorFloatingButton;
        private RectTransform layoutEditorMenu;
        private Slider editorBackgroundBrightnessSlider;
        private Slider editorTouchpadOpacitySlider;
        private Slider editorTouchRangeSlider;
        private Slider editorTouchpadTiltSlider;
        private Slider editorNoteSpeedSlider;
        private Text editorBackgroundBrightnessValue;
        private Text editorTouchpadOpacityValue;
        private Text editorTouchRangeValue;
        private Text editorTouchpadTiltValue;
        private Text editorNoteSpeedValue;
        private AppSettings draftSettings;
        private SongSelectionPreviewPlayer preview;
        private InterfaceAudioController interfaceAudio;
        private LocalChartPlaybackSession gameplaySession;
        private PlayfieldRenderer gameplayPlayfield;
        private ChartPlaybackRenderer gameplayChartRenderer;
        private GameplayJudgeController gameplayJudge;
        private GameObject gameplayCamera;
        private GameplayBackdropRenderer gameplayBackdrop;
        private ArcadeInputRig inputRig;
        private RectTransform gameplayHud;
        private RectTransform gameplaySongInfoBlock;
        private Text gameplaySongInfo;
        private Image gameplaySongInfoCover;
        private RectTransform gameplaySongInfoCoverMask;
        private RectTransform gameplayTouchpad;
        private RectTransform gameplayAirZone;
        private Image gameplayProgressTrack;
        private Image gameplayProgressFill;
        private Text gameplayFpsLabel;
        private float gameplayFpsTimer;
        private int gameplayFpsFrames;
        private Image gameplayAirGuide;
        private Image gameplayAirBoundaryBottom;
        private Image gameplayAirBoundaryTop;
        private GameplayJudgementEffects gameplayEffects;
        private TouchContactOverlay gameplayTouchContacts;
        private TrackMaskOverlay gameplayTrackMask;
        private readonly List<Image> gameplayLaneHighlights = new List<Image>();
        private Text gameplayScore;
        private Text gameplayCombo;
        private Text gameplayJudgements;
        private Text gameplayTiming;
        private RectTransform gameplayJudgementBlock;
        private RawImage gameplayJudgementImage;
        private readonly Texture2D[] gameplayJudgementTextures = new Texture2D[4];
        private float gameplayTimingHideAt;
        private Text gameplayCountdown;
        private RectTransform gameplayPausePanel;
        private Button gameplayPauseButton;
        private float pauseTapPendingUntil;
        private Button delayCalibrationExitButton;
        private Text delayCalibrationLabel;
        private Text delayCalibrationLive;
        private Text delayCalibrationResultSummary;
        private Text delayCalibrationResultStats;
        private bool delayCalibrationActive;
        private SongEntry playedSong;
        private SongChartEntry playedChart;
        private bool playedRecordsScore;
        private bool playedHasAnyMod;
        private string playedModLabel = "NORMAL";
        private int playedMusicOffsetMs;
        private int playedJudgeOffsetMs;
        private int playedDspBufferSize;
        private readonly List<double> playedDeltas = new List<double>();

        private void Start()
        {
            MultiplayerLog.Initialize();
            MultiplayerLog.Info("app-start", "version=" + Application.version + " platform=" + Application.platform);
            settings = AppSettings.Load();
            GameplaySkinManager.Initialize(settings.GameplaySkinId);
            BackgroundImageStore.EnsureDirectories();
            LowLatencyAudioController.Apply(settings.DspBufferSize);
            UiLocalization.SetLanguage(settings.EffectiveLanguage);
            uiSession = UiSessionState.Load();
            sortMode = Enum.IsDefined(typeof(SongSortMode), uiSession.SortMode)
                ? (SongSortMode)uiSession.SortMode
                : SongSortMode.Category;
            interfaceAudio = GetComponent<InterfaceAudioController>() ?? gameObject.AddComponent<InterfaceAudioController>();
            multiplayerClient = GetComponent<MultiplayerClient>() ?? gameObject.AddComponent<MultiplayerClient>();
            multiplayerSongTransfer = GetComponent<MultiplayerSongTransfer>() ?? gameObject.AddComponent<MultiplayerSongTransfer>();
            multiplayerClient.MessageReceived += OnMultiplayerMessageV2;
            multiplayerClient.ConnectionError += OnMultiplayerConnectionErrorV2;
            multiplayerClient.Disconnected += OnMultiplayerDisconnectedV2;
            multiplayerClient.HeartbeatTimeout += OnMultiplayerHeartbeatTimeoutV2;
            preview = FindObjectOfType<SongSelectionPreviewPlayer>();
            gameplaySession = GetComponentInChildren<LocalChartPlaybackSession>(true);
            gameplayPlayfield = gameplaySession != null ? gameplaySession.GetComponent<PlayfieldRenderer>() : null;
            gameplayChartRenderer = gameplaySession != null ? gameplaySession.GetComponent<ChartPlaybackRenderer>() : null;
            gameplayJudge = gameplaySession != null ? gameplaySession.GetComponent<GameplayJudgeController>() : null;
            gameplayCamera = transform.Find("Gameplay Camera")?.gameObject;
            gameplayBackdrop = GetComponentInChildren<GameplayBackdropRenderer>(true);
            inputRig = GetComponent<ArcadeInputRig>();
            if (gameplaySession != null) gameplaySession.PlaybackCompleted += OnGameplayCompleted;
            if (gameplayJudge != null) gameplayJudge.Judged += OnGameplayJudged;
            font = Resources.Load<Font>("Fonts/HarmonyOS_Sans_SC_Regular") ?? Font.CreateDynamicFontFromOSFont(new[] { "Noto Sans CJK SC", "sans-serif", "Arial" }, 32);
            titleFont = Resources.Load<Font>("Fonts/HarmonyOS_Sans_SC_Bold") ?? font;
            rounded = CreateRoundedSprite(UiCornerRadius);
            EnsureEventSystem();
            BuildCanvas();
            BuildLoading(); BuildHome(); BuildGameplay(); BuildResults(); BuildDelayCalibrationResults(); BuildLayoutEditor(); BuildModal(); BuildUpdateNotice(); BuildMods();
            BuildMetadataEditor(); BuildImportOverlay(); BuildCredits();
            ApplyRuntimeSettings();
            RefreshMods();
            foreach (var pair in pages) SetPageImmediate(pair.Value, pair.Key == AppPage.Loading);
            FindObjectOfType<RuntimeLoadingOverlay>()?.Hide();
            StartCoroutine(LoadLibrary());
        }

        private void RebuildUiForLanguage()
        {
            AppPage pageToRestore = currentPage;
            if (settingsTransition != null) StopCoroutine(settingsTransition);
            settingsTransition = null;
            queuedPage = null;
            queuedPageCompletion = null;
            if (songGridTransition != null) StopCoroutine(songGridTransition);
            songGridTransition = null;
            if (gridLayoutRefresh != null) StopCoroutine(gridLayoutRefresh);
            gridLayoutRefresh = null;
            songCardPool.Clear();
            songCardVisuals.Clear();
            virtualSongCards.Clear();
            songCardBorders.Clear();
            StopManagementBuild();
            managementRows.Clear();
            managementSongs = Array.Empty<SongEntry>();
            managementContent = null;
            managementSummary = null;
            songGrid = null;
            songViewport = null;
            difficultyRow = null;
            modLabel = null;
            settingsBody = null;
            settingsScroll = null;
            ResetMultiplayerRoomCardCacheV2();
            if (canvas != null) { canvas.gameObject.SetActive(false); Destroy(canvas.gameObject); }

            pages.Clear();
            meteorBackgrounds.Clear();
            settingsCategoryButtons.Clear();
            rateButtons.Clear();
            gameplayLaneHighlights.Clear();

            BuildCanvas();
            BuildLoading(); BuildHome(); BuildGameplay();
            BuildResults(); BuildDelayCalibrationResults(); BuildLayoutEditor(); BuildModal(); BuildUpdateNotice(); BuildMods();
            BuildMetadataEditor(); BuildImportOverlay(); BuildCredits();
            EnsurePageBuilt(pageToRestore);
            homeLibraryLabel.text = $"已加载歌曲：{catalog.Count}\nby: HMQYHM    v{Application.version}";
            ApplyRuntimeSettings();
            foreach (var pair in pages) SetPageImmediate(pair.Value, pair.Key == pageToRestore);
            RebuildSongGrid(false);
            RebuildManagementList();
            RenderSongDetails();
            RefreshMods();
            if (pages.ContainsKey(AppPage.Multiplayer)) RestoreMultiplayerViewV2();
        }

        private void EnsurePageBuilt(AppPage page)
        {
            if (pages.ContainsKey(page)) return;
            switch (page) {
                case AppPage.SongSelect:
                    BuildSongSelect();
                    ConfigureSongSelectContextV2(multiplayerSelectingSong);
                    RenderSongDetails();
                    RefreshMods();
                    break;
                case AppPage.Settings: BuildSettings(); break;
                case AppPage.DataManagement: BuildDataManagement(); break;
                case AppPage.Multiplayer:
                case AppPage.MultiplayerBrowser:
                case AppPage.MultiplayerRoom:
                case AppPage.MultiplayerGroupResults:
                    BuildMultiplayerV2();
                    foreach (AppPage item in new[] { AppPage.Multiplayer, AppPage.MultiplayerBrowser,
                        AppPage.MultiplayerRoom, AppPage.MultiplayerGroupResults })
                        SetPageImmediate(pages[item], false);
                    break;
            }
            if (pages.TryGetValue(page, out RectTransform built)) {
                SetPageImmediate(built, false);
                // Lazily created pages must remain below shared modal layers.
                built.SetAsFirstSibling();
            }
        }

        private void Update()
        {
            DrainImportProgress();
            UpdateIndeterminateImportProgress();
            UpdateCreditsAutoScroll();
            if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
            {
                UpdateResponsiveGrid();
            }
            UpdateGameplayHud();
            UpdateMultiplayerV2();
            if (transitioning || (updateNoticeLayer != null && updateNoticeLayer.gameObject.activeSelf)) return;
            if (!UnityEngine.Input.GetKeyDown(KeyCode.Escape)) return;
            if (HandleMultiplayerBackV2()) return;
            if (importLayer != null && importLayer.gameObject.activeSelf)
            {
                if (!importRunning) HideImportOverlay();
                return;
            }
            if (creditsLayer != null && creditsLayer.gameObject.activeSelf)
            {
                HideCredits();
                return;
            }
            if (metadataLayer != null && metadataLayer.gameObject.activeSelf)
            {
                HideMetadataEditor();
                return;
            }
            if (layoutEditorLayer != null && layoutEditorLayer.gameObject.activeSelf &&
                (modalLayer == null || !modalLayer.gameObject.activeSelf))
            {
                CloseLayoutEditor();
                return;
            }
            if (currentPage == AppPage.Gameplay) {
                if (delayCalibrationActive) ExitDelayCalibration();
                else PauseGameplay();
                return;
            }
            if (modsLayer != null && modsLayer.gameObject.activeSelf)
            {
                HideMods();
            }
            else if (modalLayer != null && modalLayer.gameObject.activeSelf)
            {
                if (currentPage == AppPage.Home && modalTitle.text == "再次返回退出" && Time.unscaledTime - lastHomeBack < 1.5f)
                    Application.Quit();
                else
                    HideModal();
                return;
            }
            if (currentPage != AppPage.Home) { preview?.StopPreview(); SwitchTo(AppPage.Home); return; }
            if (Time.unscaledTime - lastHomeBack < 1.5f) Application.Quit();
            else { lastHomeBack = Time.unscaledTime; ShowModal("再次返回退出", "再次按返回键即可退出 CHUNITHM Mobile。", null, false); }
        }

        private void OnApplicationQuit()
        {
            SaveMultiplayerDraftV2();
            settings?.Save();
            uiSession?.Save();
        }

        private IEnumerator LoadLibrary()
        {
            SetLoading(.12f, "正在初始化界面…"); yield return null;
            SetLoading(.38f, "正在检查歌曲数据是否变化…");
            yield return null;
            catalog = SongCatalog.Load(); yield return null;
            SetLoading(.58f, SongCatalog.LastLoadUsedCache ? "数据未变化，正在读取现有索引…" : "检测到变化，歌曲索引已重建…");
            ApplyCatalogState(uiSession.SongRoot, uiSession.Difficulty, uiSession.ChartPath, uiSession.SongId);
            SetLoading(.72f, $"已读取 {catalog.Count} 首歌曲，正在整理列表…");
            RebuildSongGrid(false); yield return null;
            SetLoading(1f, "准备完成"); yield return new WaitForSecondsRealtime(.18f);
            SwitchTo(AppPage.Home, ShowUpdateNoticeIfNeeded);
        }

        private void BuildCanvas()
        {
            var root = new GameObject("CHUNITHM Mobile UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 300;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .45f;
        }

        private void BuildLoading()
        {
            RectTransform page = Page(AppPage.Loading, ink);
            Label("CHUNITHM", page, 68, TextAnchor.MiddleCenter, new Vector2(.25f, .42f), new Vector2(.75f, .62f), Color.white);
            Label("M O B I L E", page, 22, TextAnchor.MiddleCenter, new Vector2(.25f, .36f), new Vector2(.75f, .46f), cyan);
            loadingLabel = Label("正在加载…", page, 25, TextAnchor.MiddleLeft, new Vector2(.08f, .09f), new Vector2(.92f, .15f), Color.white);
            RectTransform track = Panel("Progress", page, new Color(.08f, .14f, .20f), new Vector2(.08f, .065f), new Vector2(.92f, .09f));
            loadingFill = Panel("Fill", track, cyan, Vector2.zero, new Vector2(0, 1)).GetComponent<Image>();
        }

        private void BuildHome()
        {
            RectTransform page = Page(AppPage.Home, ink);
            AddMeteorBackground(page, MeteorBackgroundProfile.Home);
            Panel("Glow", page, new Color(.02f, .18f, .24f, .32f), new Vector2(0, 0), new Vector2(.60f, 1));
            Label("CHUNITHM", page, 72, TextAnchor.MiddleLeft, new Vector2(.07f, .45f), new Vector2(.60f, .63f), Color.white);
            Label("MOBILE", page, 26, TextAnchor.MiddleLeft, new Vector2(.075f, .38f), new Vector2(.55f, .47f), cyan);
            RectTransform menu = Panel("Menu", page, Color.clear, new Vector2(.64f, .14f), new Vector2(.94f, .86f));
            MenuButton("开始游戏", "进入歌曲列表", menu, .68f, cyan, OpenSongSelect);
            MenuButton("联机模式", "远程房间与多人游玩", menu, .46f,
                HighContrastUi ? new Color(.04f, .74f, 1f) : new Color(.18f, .25f, .50f), OpenMultiplayer);
            MenuButton("设置", "音频、画面与判定", menu, .24f, surfaceRaised, () => SwitchTo(AppPage.Settings));
            MenuButton("数据管理", "导入、删除与重建索引", menu, .02f, surfaceRaised, OpenSongDataManagement);
            homeLibraryLabel = Label($"已加载歌曲：扫描中\nby: HMQYHM    v{Application.version}", page, 20, TextAnchor.LowerLeft, new Vector2(.075f, .035f), new Vector2(.55f, .15f), muted);
            BuildHomePaletteToggle(page);
        }

        private void BuildSongSelect()
        {
            RectTransform page = Page(AppPage.SongSelect, ink);
            AddMeteorBackground(page, MeteorBackgroundProfile.Quiet);
            songSelectBackButton = ButtonText("‹", page, new Vector2(.015f, .91f), new Vector2(.06f, .985f), surfaceRaised, () => { preview?.StopPreview(); SwitchTo(AppPage.Home); });
            songSelectTitle = Label("SONG SELECT", page, 30, TextAnchor.MiddleLeft, new Vector2(.07f, .91f), new Vector2(.25f, .985f), Color.white);
            ButtonText("全部歌曲随机", page, new Vector2(.25f, .91f), new Vector2(.345f, .985f),
                surfaceRaised, SelectRandomSongFromLibrary);
            ButtonText("当前列表随机", page, new Vector2(.35f, .91f), new Vector2(.445f, .985f),
                surfaceRaised, SelectRandomSongFromCurrentList);
            InputField search = Input("搜索歌曲、作者或谱师", page, new Vector2(.45f, .91f), new Vector2(.75f, .985f));
            search.onValueChanged.AddListener(value => RequestSongListTransition(() => searchText = value, true, true));
            songSelectModsButton = ButtonText("MODS\nNORMAL", page, new Vector2(.76f, .91f), new Vector2(.85f, .985f), surfaceRaised, ShowMods, true);
            modLabel = songSelectModsButton.GetComponentInChildren<Text>();
            songSelectDeleteButton = ButtonText("删除", page, new Vector2(.86f, .91f), new Vector2(.95f, .985f), new Color(.35f, .07f, .10f), ConfirmDelete);

            RectTransform listFrame = songListPanel = Panel("Library Frame", page, Color.Lerp(surfaceRaised, cyan, .24f), new Vector2(.018f, .035f), new Vector2(.605f, .895f));
            RectTransform listPanel = Panel("Library Body", listFrame, surface, new Vector2(.008f, .008f), new Vector2(.992f, .992f));
            songViewport = Panel("Viewport", listPanel, Color.clear, new Vector2(.018f, .115f), new Vector2(.982f, .982f)); songViewport.gameObject.AddComponent<RectMask2D>();
            var content = new GameObject("Song Grid", typeof(RectTransform));
            songGrid = content.transform; songGrid.SetParent(songViewport, false);
            songGridGroup = content.AddComponent<CanvasGroup>();
            RectTransform contentRect = (RectTransform)songGrid;
            contentRect.anchorMin = new Vector2(0, 1); contentRect.anchorMax = new Vector2(1, 1); contentRect.pivot = new Vector2(.5f, 1);
            contentRect.anchoredPosition = Vector2.zero; contentRect.sizeDelta = new Vector2(-SongGridInset * 2f, 0);
            songScroll = listPanel.gameObject.AddComponent<ScrollRect>(); songScroll.content = contentRect; songScroll.viewport = songViewport; songScroll.horizontal = false; songScroll.movementType = ScrollRect.MovementType.Elastic;
            songScroll.onValueChanged.AddListener(_ => RenderVirtualSongCards());
            sortLabel = ButtonText("排序：" + (sortMode == SongSortMode.Category ? "分类" : "等级"), listPanel, new Vector2(.025f, .018f), new Vector2(.25f, .098f),
                surfaceRaised, CycleSort).GetComponentInChildren<Text>();
            bucketLabel = Label("—", listPanel, 24, TextAnchor.MiddleLeft, new Vector2(.29f, .018f), new Vector2(.68f, .098f), Color.white);
            ArrowButton(listPanel, false, new Vector2(.76f, .018f), new Vector2(.845f, .098f), () => ShiftBucket(-1));
            ArrowButton(listPanel, true, new Vector2(.865f, .018f), new Vector2(.95f, .098f), () => ShiftBucket(1));

            RectTransform detail = songDetailPanel = Panel("Details", page, surface, new Vector2(.615f, .035f), new Vector2(.982f, .895f));
            RectTransform coverBox = Panel("Cover Box", detail, Color.clear, new Vector2(.055f, .60f), new Vector2(.40f, .94f));
            coverBox.GetComponent<Image>().raycastTarget = false;
            RectTransform cover = Panel("Cover", coverBox, new Color(.12f, .06f, .22f), Vector2.zero, Vector2.one);
            var aspect = cover.gameObject.AddComponent<AspectRatioFitter>(); aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent; aspect.aspectRatio = 1;
            detailCover = cover.GetComponent<Image>();
            detailCoverPlaceholder = Label("♪", cover, 96, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, cyan).gameObject;
            detailTitle = MarqueeLabel("没有歌曲", detail, 34, new Vector2(.44f, .79f), new Vector2(.94f, .94f), Color.white);
            detailMeta = Label("请导入歌曲", detail, 21, TextAnchor.UpperLeft, new Vector2(.44f, .57f), new Vector2(.94f, .79f), muted);
            difficultyRow = Panel("Difficulties", detail, Color.clear, new Vector2(.055f, .22f), new Vector2(.945f, .53f));
            songSelectPrimaryButton = ButtonText("开始！", detail, new Vector2(.055f, .045f), new Vector2(.945f, .165f), cyan, PrepareGameplay);
        }

        private void SelectRandomSongFromLibrary()
        {
            var charts = catalog.Where(song => song?.Charts != null)
                .SelectMany(song => song.Charts.Select(chart => new SongListItem { Song = song, Chart = chart }))
                .Where(item => item.Song != null && item.Chart != null && File.Exists(item.Chart.Path))
                .ToList();
            if (charts.Count == 0) return;
            SongListItem item = charts[UnityEngine.Random.Range(0, charts.Count)];
            SelectSong(item.Song, true, item.Chart.Difficulty, true, item.Chart.Path);
        }

        private void SelectRandomSongFromCurrentList()
        {
            List<SongListItem> choices = SongsForCurrentBucket()
                .Where(item => item?.Song != null && item.Chart != null && File.Exists(item.Chart.Path))
                .ToList();
            if (choices.Count == 0) return;
            SongListItem item = choices[UnityEngine.Random.Range(0, choices.Count)];
            SelectSong(item.Song, true, item.Chart.Difficulty, true, item.Chart.Path);
        }

        private void BuildDataManagement()
        {
            RectTransform page = Page(AppPage.DataManagement, ink);
            managementPage = page;
            ButtonText("‹", page, new Vector2(.015f, .91f), new Vector2(.06f, .985f), surfaceRaised, ReturnFromDataManagement);
            Label("数据管理", page, 32, TextAnchor.MiddleLeft, new Vector2(.07f, .91f), new Vector2(.25f, .985f), Color.white);
            managementSongTab = ButtonText("歌曲数据", page, new Vector2(.26f, .91f), new Vector2(.37f, .985f), surfaceRaised,
                () => SetManagementMode(false));
            managementSkinTab = ButtonText("游玩皮肤", page, new Vector2(.38f, .91f), new Vector2(.49f, .985f), surfaceRaised,
                () => SetManagementMode(true));
            managementImportButton = ButtonText("导入", page, new Vector2(.57f, .91f), new Vector2(.67f, .985f), new Color(.04f, .43f, .56f),
                OnManagementImport);
            managementRefreshButton = ButtonText("刷新", page, new Vector2(.68f, .91f), new Vector2(.77f, .985f), new Color(.04f, .34f, .46f),
                OnManagementRefresh);
            managementDeleteButton = ButtonText("删除", page, new Vector2(.79f, .91f), new Vector2(.88f, .985f), new Color(.35f, .07f, .10f),
                OnManagementDelete);
            Label("选择项目后操作", page, 14, TextAnchor.MiddleRight, new Vector2(.89f, .91f), new Vector2(.98f, .985f), muted);

            managementSearch = Input("搜索歌曲、作者、分类或皮肤 ID", page, new Vector2(.02f, .815f), new Vector2(.49f, .89f));
            managementSearch.onValueChanged.AddListener(value => {
                managementSearchText = value;
                if (managementSkinMode) RebuildSkinManagementList(true);
                else RebuildManagementList(true);
            });
            managementSummary = Label("歌曲数据加载中…", page, 20, TextAnchor.MiddleRight, new Vector2(.51f, .815f), new Vector2(.98f, .89f), muted);

            RectTransform frame = Panel("Management list", page, surfaceRaised, new Vector2(.02f, .12f), new Vector2(.98f, .80f));
            RectTransform viewport = Panel("Viewport", frame, surface, new Vector2(.008f, .012f), new Vector2(.992f, .988f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var contentObject = new GameObject("Management content", typeof(RectTransform));
            contentObject.transform.SetParent(viewport, false);
            managementContent = (RectTransform)contentObject.transform;
            managementContent.anchorMin = new Vector2(0, 1);
            managementContent.anchorMax = new Vector2(1, 1);
            managementContent.pivot = new Vector2(.5f, 1);
            managementContent.anchoredPosition = Vector2.zero;
            managementContent.sizeDelta = Vector2.zero;
            managementScroll = frame.gameObject.AddComponent<ScrollRect>();
            managementScroll.content = managementContent;
            managementScroll.viewport = viewport;
            managementScroll.horizontal = false;
            managementScroll.movementType = ScrollRect.MovementType.Elastic;
            managementScroll.onValueChanged.AddListener(_ => RenderManagedSongRows());

            managementSelectAllButton = ButtonText("全选 / 取消", page, new Vector2(.02f, .025f), new Vector2(.17f, .10f), surfaceRaised, ToggleAllManagedSongs);
            managementBatchButton = ButtonText("批量编辑", page, new Vector2(.18f, .025f), new Vector2(.32f, .10f), new Color(.12f, .30f, .42f), OpenMetadataEditor);
            managementHint = Label("选择歌曲后可批量编辑或删除", page, 18, TextAnchor.MiddleLeft, new Vector2(.34f, .025f), new Vector2(.75f, .10f), muted);
            managementEnterSongButton = ButtonText("进入选曲", page, new Vector2(.82f, .025f), new Vector2(.98f, .10f), surfaceRaised, OpenSongSelect);
            SetManagementMode(false);
        }

        private void BuildMultiplayer()
        {
            RectTransform page = Page(AppPage.Multiplayer, ink);
            AddMeteorBackground(page, MeteorBackgroundProfile.Quiet);
            ButtonText("‹", page, new Vector2(.02f, .90f), new Vector2(.07f, .98f), surfaceRaised,
                () => SwitchTo(AppPage.Home));
            Label("联机模式", page, 38, TextAnchor.MiddleLeft, new Vector2(.09f, .90f), new Vector2(.42f, .98f), Color.white);

            RectTransform connection = Panel("Multiplayer connection", page, surface,
                new Vector2(.055f, .13f), new Vector2(.56f, .85f));
            Label("客户端连接", connection, 28, TextAnchor.MiddleLeft,
                new Vector2(.08f, .86f), new Vector2(.92f, .96f), cyan);
            Label("版本一致 · 次数由服务器统一管理", connection, 17, TextAnchor.MiddleLeft,
                new Vector2(.08f, .77f), new Vector2(.92f, .86f), muted);

            Label("服务器地址", connection, 18, TextAnchor.MiddleLeft,
                new Vector2(.08f, .72f), new Vector2(.34f, .78f), muted);
            multiplayerServerInput = Input("例如 192.168.1.10:27960", connection,
                new Vector2(.08f, .63f), new Vector2(.92f, .72f));
            multiplayerServerInput.SetTextWithoutNotify(PlayerPrefs.GetString("chunithm_mobile_multiplayer_server", "43.153.175.142:27960"));
            multiplayerServerInput.onEndEdit.AddListener(_ => SaveMultiplayerDraft());

            Label("玩家名称", connection, 18, TextAnchor.MiddleLeft,
                new Vector2(.08f, .57f), new Vector2(.34f, .63f), muted);
            multiplayerPlayerNameInput = Input("最多 6 个中文或英文字符", connection,
                new Vector2(.08f, .48f), new Vector2(.92f, .57f));
            multiplayerPlayerNameInput.characterLimit = 6;
            multiplayerPlayerNameInput.SetTextWithoutNotify(PlayerPrefs.GetString("chunithm_mobile_multiplayer_player", ""));
            multiplayerPlayerNameInput.onValueChanged.AddListener(value => LimitMultiplayerName(multiplayerPlayerNameInput, value));
            multiplayerPlayerNameInput.onEndEdit.AddListener(_ => SaveMultiplayerDraft());

            Label("房间名称", connection, 18, TextAnchor.MiddleLeft,
                new Vector2(.08f, .42f), new Vector2(.34f, .48f), muted);
            multiplayerRoomNameInput = Input("创建房间时使用，最多 6 个字符", connection,
                new Vector2(.08f, .33f), new Vector2(.92f, .42f));
            multiplayerRoomNameInput.characterLimit = 6;
            multiplayerRoomNameInput.SetTextWithoutNotify(PlayerPrefs.GetString("chunithm_mobile_multiplayer_room", ""));
            multiplayerRoomNameInput.onValueChanged.AddListener(value => LimitMultiplayerName(multiplayerRoomNameInput, value));
            multiplayerRoomNameInput.onEndEdit.AddListener(_ => SaveMultiplayerDraft());

            Label("房间号", connection, 18, TextAnchor.MiddleLeft,
                new Vector2(.08f, .27f), new Vector2(.34f, .33f), muted);
            multiplayerRoomIdInput = Input("加入时填写 6 位房间号", connection,
                new Vector2(.08f, .18f), new Vector2(.50f, .27f));
            multiplayerRoomIdInput.characterLimit = 6;
            multiplayerRoomIdInput.contentType = InputField.ContentType.IntegerNumber;
            multiplayerRoomIdInput.SetTextWithoutNotify(PlayerPrefs.GetString("chunithm_mobile_multiplayer_room_id", ""));
            multiplayerRoomIdInput.onEndEdit.AddListener(_ => SaveMultiplayerDraft());
            multiplayerPasswordInput = Input("密码（可选）", connection,
                new Vector2(.52f, .18f), new Vector2(.92f, .27f));
            multiplayerPasswordInput.contentType = InputField.ContentType.Password;

            multiplayerQuotaLabel = Label("创建剩余 --    加入剩余 --", connection, 16, TextAnchor.MiddleCenter,
                new Vector2(.08f, .125f), new Vector2(.92f, .18f), cyan);
            multiplayerCreateButton = ButtonText("创建房间\n剩余 --", connection,
                new Vector2(.08f, .035f), new Vector2(.47f, .115f), cyan, RequestCreateRoom);
            multiplayerJoinButton = ButtonText("加入房间\n剩余 --", connection,
                new Vector2(.53f, .035f), new Vector2(.92f, .115f), surfaceRaised, RequestJoinRoom);

            RectTransform statusPanel = Panel("Multiplayer status", page, surface,
                new Vector2(.595f, .13f), new Vector2(.945f, .85f));
            Label("联机大厅", statusPanel, 28, TextAnchor.MiddleLeft,
                new Vector2(.09f, .86f), new Vector2(.34f, .96f), Color.white);
            multiplayerSearchInput = Input("搜索房间", statusPanel,
                new Vector2(.35f, .86f), new Vector2(.65f, .955f));
            multiplayerSearchInput.onEndEdit.AddListener(_ => RequestMultiplayerRefresh());
            ButtonText("刷新房间", statusPanel, new Vector2(.67f, .855f), new Vector2(.91f, .955f), surfaceRaised,
                RequestMultiplayerRefresh);
            multiplayerStatus = Label("尚未连接服务器\n\n填写地址后可以刷新房间，或直接创建 / 加入房间。",
                statusPanel, 18, TextAnchor.UpperLeft, new Vector2(.09f, .73f), new Vector2(.62f, .84f), muted);
            multiplayerRoomDetails = Label("选择一个房间查看详情。", statusPanel, 17, TextAnchor.UpperLeft,
                new Vector2(.09f, .47f), new Vector2(.91f, .70f), muted);
            RectTransform roomFrame = Panel("Multiplayer rooms", statusPanel, surfaceRaised,
                new Vector2(.09f, .20f), new Vector2(.91f, .46f));
            RectTransform roomViewport = Panel("Room viewport", roomFrame, surface,
                new Vector2(.015f, .02f), new Vector2(.985f, .98f));
            roomViewport.gameObject.AddComponent<RectMask2D>();
            multiplayerRoomListContent = RectObject("Room list content", roomViewport, new Vector2(0, 1), new Vector2(1, 1));
            multiplayerRoomListContent.pivot = new Vector2(.5f, 1f);
            multiplayerRoomListContent.sizeDelta = new Vector2(0, 0);
            ScrollRect roomScroll = roomFrame.gameObject.AddComponent<ScrollRect>();
            roomScroll.content = multiplayerRoomListContent;
            roomScroll.viewport = roomViewport;
            roomScroll.horizontal = false;
            roomScroll.vertical = true;
            roomScroll.movementType = ScrollRect.MovementType.Elastic;
            roomScroll.scrollSensitivity = 42f;
            Label("最多 8 人 · 房主统一 MOD · 支持准备状态同步", statusPanel, 15,
                TextAnchor.LowerLeft, new Vector2(.09f, .135f), new Vector2(.91f, .195f), cyan);
            multiplayerSelectSongButton = ButtonText("同步当前选曲", statusPanel,
                new Vector2(.09f, .035f), new Vector2(.30f, .125f), surfaceRaised, SendMultiplayerSongSelection);
            multiplayerReadyButton = ButtonText("准备", statusPanel,
                new Vector2(.31f, .035f), new Vector2(.49f, .125f), new Color(.18f, .58f, .34f), ToggleMultiplayerReady);
            multiplayerStartButton = ButtonText("开始", statusPanel,
                new Vector2(.50f, .035f), new Vector2(.70f, .125f), cyan, StartOrFinishMultiplayerRound);
            multiplayerLeaveButton = ButtonText("离开房间", statusPanel,
                new Vector2(.71f, .035f), new Vector2(.91f, .125f), new Color(.55f, .18f, .20f), LeaveMultiplayerRoom);
            SetMultiplayerRoomActionsVisible(false);
            RefreshMultiplayerQuotaLabel();
        }

        private void OpenMultiplayer()
        {
            OpenMultiplayerV2();
        }

        private void SaveMultiplayerDraft()
        {
            if (multiplayerServerInput != null)
                PlayerPrefs.SetString("chunithm_mobile_multiplayer_server", multiplayerServerInput.text ?? "");
            if (multiplayerPlayerNameInput != null)
                PlayerPrefs.SetString("chunithm_mobile_multiplayer_player", multiplayerPlayerNameInput.text ?? "");
            if (multiplayerRoomNameInput != null)
                PlayerPrefs.SetString("chunithm_mobile_multiplayer_room", multiplayerRoomNameInput.text ?? "");
            if (multiplayerRoomIdInput != null)
                PlayerPrefs.SetString("chunithm_mobile_multiplayer_room_id", multiplayerRoomIdInput.text ?? "");
            PlayerPrefs.Save();
        }

        private void LimitMultiplayerName(InputField input, string value)
        {
            if (input == null || value == null) return;
            StringBuilder filtered = new StringBuilder(6);
            foreach (char character in value)
            {
                if (!IsMultiplayerNameCharacterAllowed(character)) continue;
                filtered.Append(character);
                if (filtered.Length >= 6) break;
            }
            string normalized = filtered.ToString();
            if (!string.Equals(normalized, value, StringComparison.Ordinal))
                input.SetTextWithoutNotify(normalized);
        }

        private static bool IsMultiplayerNameCharacterAllowed(char character)
        {
            return (character >= 'A' && character <= 'Z') ||
                   (character >= 'a' && character <= 'z') ||
                   (character >= '\u4E00' && character <= '\u9FFF');
        }

        private void RequestCreateRoom()
        {
            SaveMultiplayerDraft();
            if (!ValidateMultiplayerDraft(true)) return;
            multiplayerPendingAction = "create";
            ConnectMultiplayerIfNeeded();
            if (multiplayerClient != null && multiplayerClient.IsConnected) SendCreateRoom();
        }

        private void RequestJoinRoom()
        {
            SaveMultiplayerDraft();
            if (!ValidateMultiplayerDraft(false)) return;
            multiplayerPendingAction = "join";
            ConnectMultiplayerIfNeeded();
            if (multiplayerClient != null && multiplayerClient.IsConnected) SendJoinRoom();
        }

        private void RequestMultiplayerRefresh()
        {
            SaveMultiplayerDraft();
            if (!ValidateMultiplayerDraft(false, requireRoomId: false)) return;
            multiplayerPendingAction = "refresh";
            ConnectMultiplayerIfNeeded();
            if (multiplayerClient != null && multiplayerClient.IsConnected && multiplayerAuthenticated)
                SendMultiplayerRoomListRequest();
        }

        private bool ValidateMultiplayerDraft(bool creating, bool requireRoomId = true)
        {
            if (multiplayerServerInput == null || string.IsNullOrWhiteSpace(multiplayerServerInput.text))
                return SetMultiplayerStatus("请先填写服务器地址。", true);
            if (multiplayerPlayerNameInput == null || string.IsNullOrWhiteSpace(multiplayerPlayerNameInput.text))
                return SetMultiplayerStatus("请先填写玩家名称。", true);
            if (creating && (multiplayerRoomNameInput == null || string.IsNullOrWhiteSpace(multiplayerRoomNameInput.text)))
                return SetMultiplayerStatus("创建房间需要填写房间名称。", true);
            if (!creating && requireRoomId && (multiplayerRoomIdInput == null || !RegexLikeRoomId(multiplayerRoomIdInput.text)))
                return SetMultiplayerStatus("加入房间需要填写 6 位房间号。", true);
            return true;
        }

        private static bool RegexLikeRoomId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 6) return false;
            return value.All(character => character >= '0' && character <= '9');
        }

        private bool SetMultiplayerStatus(string text, bool error)
        {
            if (multiplayerStatus != null) multiplayerStatus.text = UiLocalization.Translate(text);
            return false;
        }

        private void ConnectMultiplayerIfNeeded()
        {
            if (multiplayerClient == null) return;
            string server = multiplayerServerInput == null ? "" : multiplayerServerInput.text.Trim();
            string playerName = multiplayerPlayerNameInput == null ? "" : multiplayerPlayerNameInput.text.Trim();
            if (multiplayerClient.IsConnected &&
                string.Equals(server, multiplayerConnectedServer, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(playerName, multiplayerConnectedPlayerName, StringComparison.Ordinal)) return;
            if (multiplayerClient.IsConnected)
            {
                multiplayerClient.Disconnect();
                multiplayerAuthenticated = false;
                activeMultiplayerRoom = null;
            }
            multiplayerStatus.text = UiLocalization.Translate("正在连接服务器…");
            multiplayerConnectedServer = server;
            multiplayerConnectedPlayerName = playerName;
            multiplayerClient.Connect(server, playerName,
                MultiplayerClient.GetStableDeviceId(), Application.version);
        }

        private void SendCreateRoom()
        {
            if (multiplayerClient == null || !multiplayerClient.IsConnected || !multiplayerAuthenticated) return;
            MultiplayerCreateRoomMessage request = new MultiplayerCreateRoomMessage {
                roomName = multiplayerRoomNameInput.text.Trim(),
                roomMode = mods.RecordsScore ? "normal" : "casual",
                password = multiplayerPasswordInput == null ? "" : multiplayerPasswordInput.text,
                mods = new MultiplayerModsMessage {
                    autoPlay = mods.AutoPlay, airAreaAuto = mods.AirAreaAuto,
                    directionalAirAuto = mods.DirectionalAirAuto, airLongNoMotion = mods.AirLongNoMotion,
                    mineImmunity = mods.MineImmunity,
                    mirrorChart = mods.MirrorChart,
                    musicRate = mods.MusicRate
                }
            };
            multiplayerClient.Send(JsonUtility.ToJson(request));
            multiplayerPendingAction = "";
            SetMultiplayerStatus("正在创建房间…", false);
        }

        private void SendJoinRoom()
        {
            if (multiplayerClient == null || !multiplayerClient.IsConnected || !multiplayerAuthenticated) return;
            MultiplayerJoinRoomMessage request = new MultiplayerJoinRoomMessage {
                roomId = multiplayerRoomIdInput.text.Trim(),
                password = multiplayerPasswordInput == null ? "" : multiplayerPasswordInput.text
            };
            multiplayerClient.Send(JsonUtility.ToJson(request));
            multiplayerPendingAction = "";
            SetMultiplayerStatus("正在加入房间…", false);
        }

        private void OnMultiplayerMessage(string json)
        {
            MultiplayerEnvelope message;
            try { message = JsonUtility.FromJson<MultiplayerEnvelope>(json); }
            catch (Exception exception) { SetMultiplayerStatus("联机数据解析失败：" + exception.Message, true); return; }
            if (message == null) return;
            if (message.quota != null) { multiplayerQuota = message.quota; RefreshMultiplayerQuotaLabel(); }
            if (message.details != null && message.details.quota != null)
            {
                multiplayerQuota = message.details.quota;
                RefreshMultiplayerQuotaLabel();
            }
            switch (message.type)
            {
                case "helloAccepted":
                    multiplayerAuthenticated = true;
                    multiplayerGameId = message.gameId ?? "";
                    PlayerPrefs.SetString("chunithm_mobile_multiplayer_game_id", multiplayerGameId);
                    PlayerPrefs.Save();
                    SetMultiplayerStatus("已连接 · 游戏 ID " + multiplayerGameId + "\n客户端版本 " + Application.version + "\n请选择房间操作。", false);
                    if (multiplayerPendingAction == "create") SendCreateRoom();
                    else if (multiplayerPendingAction == "join") SendJoinRoom();
                    else if (multiplayerPendingAction == "refresh") SendMultiplayerRoomListRequest();
                    multiplayerPendingAction = "";
                    break;
                case "roomList":
                    multiplayerRooms = message.rooms ?? Array.Empty<MultiplayerRoomDto>();
                    if (activeMultiplayerRoom == null) RenderMultiplayerRoomList(multiplayerRooms);
                    break;
                case "roomCreated":
                    EnterOrUpdateMultiplayerRoom(message.room, "房间创建成功");
                    break;
                case "roomJoined":
                    EnterOrUpdateMultiplayerRoom(message.room, "加入房间成功");
                    break;
                case "roomUpdated":
                case "roomSettingsUpdated":
                    EnterOrUpdateMultiplayerRoom(message.room, null);
                    break;
                case "memberJoined":
                    SetMultiplayerStatus((message.playerName ?? "新玩家") + " 已加入房间。", false);
                    break;
                case "memberLeft":
                    SetMultiplayerStatus("有玩家已离开房间。", false);
                    break;
                case "hostChanged":
                    SetMultiplayerStatus("房主已变更为 " + (message.hostName ?? "-") + "。", false);
                    break;
                case "roomLeft":
                    ExitActiveMultiplayerRoom("已离开房间。");
                    break;
                case "kickedFromRoom":
                    ExitActiveMultiplayerRoom("你已被房主移出房间。");
                    break;
                case "roundStarting":
                    SetMultiplayerStatus("本局将在约 3.5 秒后开始，正在等待所有成员加载。", false);
                    break;
                case "roundStarted":
                    SetMultiplayerStatus("房间已进入游玩状态。", false);
                    break;
                case "roundFinished":
                    SetMultiplayerStatus("本局已结束，等待下一轮准备。", false);
                    break;
                case "error":
                    string detail = message.code == "CLIENT_VERSION_MISMATCH"
                        ? "\n房间版本：" + (message.details == null ? message.expectedVersion : message.details.expectedVersion) +
                          "\n当前版本：" + (message.details == null ? message.actualVersion : message.details.actualVersion) : "";
                    SetMultiplayerStatus((message.message ?? "服务器拒绝了操作。") + detail, true);
                    break;
                case "idleWarning":
                    SetMultiplayerStatus("连接将在 " + message.remainingSeconds + " 秒后因无操作断开。", true);
                    break;
                case "idleTimeout":
                    SetMultiplayerStatus("连接已因长时间无操作断开。", true);
                    break;
            }
        }

        private void RenderMultiplayerRoomList(MultiplayerRoomDto[] rooms)
        {
            if (multiplayerStatus == null || multiplayerRoomListContent == null) return;
            foreach (Transform child in multiplayerRoomListContent) Destroy(child.gameObject);
            selectedMultiplayerRoom = null;
            SetMultiplayerRoomActionsVisible(false);
            if (rooms == null || rooms.Length == 0)
            {
                multiplayerRoomListContent.sizeDelta = new Vector2(0, 0);
                multiplayerStatus.text = "当前没有可加入的房间。";
                multiplayerRoomDetails.text = "选择一个房间查看详情。";
                return;
            }
            const float rowHeight = 58f;
            const float gap = 5f;
            int visibleCount = 0;
            foreach (MultiplayerRoomDto room in rooms)
            {
                if (room == null) continue;
                string compatible = string.Equals(room.clientVersion, Application.version, StringComparison.Ordinal) ? "可加入" : "版本不一致";
                Button button = ButtonText(room.roomName + "  " + room.roomId + "\n" + room.memberCount + "/" + room.maxPlayers + " · " + room.mode + " · " + compatible,
                    multiplayerRoomListContent, Vector2.zero, Vector2.one,
                    compatible == "可加入" ? surfaceRaised : new Color(.20f, .15f, .15f, .98f),
                    () => SelectMultiplayerRoom(room));
                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(.5f, 1);
                float top = visibleCount * (rowHeight + gap) + 4f;
                rect.offsetMin = new Vector2(4f, -top - rowHeight);
                rect.offsetMax = new Vector2(-4f, -top);
                Text label = button.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.fontSize = 15;
                    label.alignment = TextAnchor.MiddleLeft;
                    label.rectTransform.offsetMin = new Vector2(14, 0);
                }
                visibleCount++;
            }
            multiplayerRoomListContent.sizeDelta = new Vector2(0, visibleCount * (rowHeight + gap) + 8f);
            multiplayerStatus.text = "已刷新房间列表 · " + visibleCount + " 个房间";
        }

        private void SelectMultiplayerRoom(MultiplayerRoomDto room)
        {
            selectedMultiplayerRoom = room;
            if (multiplayerRoomIdInput != null) multiplayerRoomIdInput.SetTextWithoutNotify(room.roomId ?? "");
            if (multiplayerRoomDetails == null || room == null) return;
            string versionState = string.Equals(room.clientVersion, Application.version, StringComparison.Ordinal)
                ? "版本一致，可加入" : "版本不一致，无法加入";
            multiplayerRoomDetails.text = "房间：" + room.roomName + "  " + room.roomId +
                "\n房主：" + room.hostName + "\n人数：" + room.memberCount + "/" + room.maxPlayers +
                " · " + room.mode + " · " + room.selectionMode + "\n客户端版本：" + room.clientVersion +
                "\n" + (room.hasPassword ? "需要密码" : "无密码") + " · " + versionState;
        }

        private void SendMultiplayerRoomListRequest()
        {
            if (multiplayerClient == null || !multiplayerAuthenticated) return;
            string search = multiplayerSearchInput == null ? "" : multiplayerSearchInput.text.Trim();
            string exactRoomId = RegexLikeRoomId(search) ? search : "";
            multiplayerClient.RequestRoomList(exactRoomId.Length == 0 ? search : "", exactRoomId);
            multiplayerPendingAction = "";
        }

        private void EnterOrUpdateMultiplayerRoom(MultiplayerRoomDto room, string status)
        {
            if (room == null) return;
            activeMultiplayerRoom = room;
            selectedMultiplayerRoom = room;
            if (multiplayerRoomIdInput != null) multiplayerRoomIdInput.SetTextWithoutNotify(room.roomId ?? "");
            RenderActiveMultiplayerRoom();
            if (!string.IsNullOrWhiteSpace(status))
                SetMultiplayerStatus(status + " · 房间号 " + (room.roomId ?? "-"), false);
        }

        private void RenderActiveMultiplayerRoom()
        {
            MultiplayerRoomDto room = activeMultiplayerRoom;
            if (room == null || multiplayerRoomListContent == null) return;
            foreach (Transform child in multiplayerRoomListContent) Destroy(child.gameObject);
            SetMultiplayerRoomActionsVisible(true);

            string state = MultiplayerRoomStateLabel(room.state);
            string mode = string.Equals(room.mode, "normal", StringComparison.OrdinalIgnoreCase) ? "普通模式" : "休闲模式";
            string selection = string.Equals(room.selectionMode, "loop", StringComparison.OrdinalIgnoreCase) ? "循环选歌" : "房主选歌";
            string song = room.song == null
                ? "尚未选择歌曲"
                : room.song.songId + " · " + room.song.difficulty + " Lv." + room.song.level;
            if (multiplayerRoomDetails != null)
            {
                multiplayerRoomDetails.text = "房间：" + room.roomName + "  " + room.roomId +
                    "\n房主：" + room.hostName + " · " + room.memberCount + "/" + room.maxPlayers +
                    " · " + state + "\n" + mode + " · " + selection + " · 版本 " + room.clientVersion +
                    "\n选曲：" + song + "\nMOD：" + MultiplayerModSummary(room.mods);
            }

            MultiplayerRoomMemberDto[] members = room.members ?? Array.Empty<MultiplayerRoomMemberDto>();
            const float rowHeight = 54f;
            const float gap = 5f;
            bool localHost = IsLocalMultiplayerHost();
            int visibleCount = 0;
            foreach (MultiplayerRoomMemberDto member in members)
            {
                if (member == null) continue;
                string memberId = member.gameId ?? member.playerId;
                bool canKick = localHost && !member.isHost &&
                               !string.Equals(memberId, multiplayerGameId, StringComparison.Ordinal) &&
                               room.state != "loading" && room.state != "playing";
                string role = member.isHost ? "房主" : "成员";
                string ready = member.ready ? "已准备" : "未准备";
                string suffix = canKick ? " · 点击踢出" : "";
                Button button = ButtonText(member.name + "  " + role + "\n" + ready + suffix,
                    multiplayerRoomListContent, Vector2.zero, Vector2.one,
                    member.isHost ? new Color(.08f, .34f, .43f, .98f) :
                    member.ready ? new Color(.10f, .31f, .20f, .98f) : surfaceRaised,
                    () => ConfirmKickMultiplayerMember(memberId, member.name));
                button.interactable = canKick;
                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0, 1);
                rect.anchorMax = new Vector2(1, 1);
                rect.pivot = new Vector2(.5f, 1);
                float top = visibleCount * (rowHeight + gap) + 4f;
                rect.offsetMin = new Vector2(4f, -top - rowHeight);
                rect.offsetMax = new Vector2(-4f, -top);
                Text label = button.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.fontSize = 15;
                    label.alignment = TextAnchor.MiddleLeft;
                    label.rectTransform.offsetMin = new Vector2(14f, 0f);
                }
                visibleCount++;
            }
            multiplayerRoomListContent.sizeDelta = new Vector2(0, Mathf.Max(0, visibleCount * (rowHeight + gap) + 8f));
            RefreshMultiplayerRoomActions();
        }

        private void SetMultiplayerRoomActionsVisible(bool visible)
        {
            if (multiplayerSelectSongButton != null) multiplayerSelectSongButton.gameObject.SetActive(visible);
            if (multiplayerReadyButton != null) multiplayerReadyButton.gameObject.SetActive(visible);
            if (multiplayerStartButton != null) multiplayerStartButton.gameObject.SetActive(visible);
            if (multiplayerLeaveButton != null) multiplayerLeaveButton.gameObject.SetActive(visible);
        }

        private void RefreshMultiplayerRoomActions()
        {
            if (activeMultiplayerRoom == null) return;
            MultiplayerRoomMemberDto self = (activeMultiplayerRoom.members ?? Array.Empty<MultiplayerRoomMemberDto>())
                .FirstOrDefault(member => member != null &&
                    string.Equals(member.gameId ?? member.playerId, multiplayerGameId, StringComparison.Ordinal));
            bool roundActive = activeMultiplayerRoom.state == "loading" || activeMultiplayerRoom.state == "playing";
            bool canChangeLobby = !roundActive;
            bool isHost = IsLocalMultiplayerHost();
            bool canSelect = canChangeLobby && (isHost || activeMultiplayerRoom.selectionMode == "loop");
            bool allReady = activeMultiplayerRoom.members != null && activeMultiplayerRoom.members.Length > 0 &&
                            activeMultiplayerRoom.members.All(member => member != null && member.ready);

            if (multiplayerSelectSongButton != null) multiplayerSelectSongButton.interactable = canSelect;
            if (multiplayerReadyButton != null)
            {
                multiplayerReadyButton.interactable = canChangeLobby;
                SetButtonLabel(multiplayerReadyButton, self != null && self.ready ? "取消准备" : "准备");
            }
            if (multiplayerStartButton != null)
            {
                SetButtonLabel(multiplayerStartButton, roundActive ? "结束本局" : "开始");
                multiplayerStartButton.interactable = isHost &&
                    (roundActive || (activeMultiplayerRoom.song != null && allReady));
            }
            if (multiplayerLeaveButton != null) multiplayerLeaveButton.interactable = !roundActive;
        }

        private bool IsLocalMultiplayerHost()
        {
            return activeMultiplayerRoom != null &&
                   string.Equals(activeMultiplayerRoom.hostPlayerId, multiplayerGameId, StringComparison.Ordinal);
        }

        private void SendMultiplayerSongSelection()
        {
            if (activeMultiplayerRoom == null || multiplayerClient == null) return;
            if (selectedSong == null || selectedChart == null)
            {
                SetMultiplayerStatus("请先在歌曲列表选择歌曲和难度。", true);
                return;
            }
            if (!IsLocalMultiplayerHost() && activeMultiplayerRoom.selectionMode != "loop")
            {
                SetMultiplayerStatus("当前房间仅允许房主选歌。", true);
                return;
            }
            string chartFile = Path.GetFileName(selectedChart.Path ?? "").Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(chartFile))
            {
                SetMultiplayerStatus("当前谱面路径无效，无法同步选曲。", true);
                return;
            }
            multiplayerClient.SelectSong(new MultiplayerSelectSongMessage {
                songId = selectedSong.Id,
                chartPath = chartFile,
                difficulty = selectedChart.Difficulty,
                level = selectedChart.Level,
                chartHash = null
            });
            SetMultiplayerStatus("正在同步当前选曲：《" + selectedSong.Name + "》 " + selectedChart.Difficulty, false);
        }

        private void ToggleMultiplayerReady()
        {
            if (activeMultiplayerRoom == null || multiplayerClient == null) return;
            MultiplayerRoomMemberDto self = (activeMultiplayerRoom.members ?? Array.Empty<MultiplayerRoomMemberDto>())
                .FirstOrDefault(member => member != null &&
                    string.Equals(member.gameId ?? member.playerId, multiplayerGameId, StringComparison.Ordinal));
            multiplayerClient.SetReady(self == null || !self.ready);
        }

        private void StartOrFinishMultiplayerRound()
        {
            if (activeMultiplayerRoom == null || multiplayerClient == null || !IsLocalMultiplayerHost()) return;
            if (activeMultiplayerRoom.state == "loading" || activeMultiplayerRoom.state == "playing")
                multiplayerClient.ExitRound();
            else
                multiplayerClient.StartRound();
        }

        private void LeaveMultiplayerRoom()
        {
            if (activeMultiplayerRoom == null || multiplayerClient == null) return;
            multiplayerClient.LeaveRoom();
        }

        private void ConfirmKickMultiplayerMember(string playerId, string playerName)
        {
            if (!IsLocalMultiplayerHost() || string.IsNullOrWhiteSpace(playerId)) return;
            ShowModal("移出成员", "确定将“" + (playerName ?? "该玩家") + "”移出房间？",
                () => multiplayerClient?.KickPlayer(playerId), true);
        }

        private void ExitActiveMultiplayerRoom(string status)
        {
            activeMultiplayerRoom = null;
            selectedMultiplayerRoom = null;
            RenderMultiplayerRoomList(multiplayerRooms);
            SetMultiplayerStatus(status, false);
            if (multiplayerAuthenticated) SendMultiplayerRoomListRequest();
        }

        private static string MultiplayerRoomStateLabel(string state)
        {
            switch (state)
            {
                case "preparing": return "准备中";
                case "loading": return "加载中";
                case "playing": return "游玩中";
                case "results": return "结算中";
                default: return "大厅";
            }
        }

        private static string MultiplayerModSummary(MultiplayerModsMessage roomMods)
        {
            if (roomMods == null) return "NORMAL";
            List<string> labels = new List<string>();
            if (roomMods.autoPlay) labels.Add("AUTO");
            if (roomMods.airAreaAuto) labels.Add("AIR AUTO");
            if (roomMods.directionalAirAuto) labels.Add("AIR UP/DOWN AUTO");
            if (roomMods.airLongNoMotion) labels.Add("AIR 长条简单");
            if (roomMods.mineImmunity) labels.Add("MINE OFF");
            if (roomMods.mirrorChart) labels.Add("MIRROR");
            if (Math.Abs(roomMods.musicRate - 1f) > .01f) labels.Add(roomMods.musicRate.ToString("0.0") + "x");
            return labels.Count == 0 ? "NORMAL" : string.Join(" + ", labels);
        }

        private void OnMultiplayerConnectionError(string error)
        {
            multiplayerAuthenticated = false;
            SetMultiplayerStatus("连接失败：" + error, true);
        }

        private void OnMultiplayerDisconnected()
        {
            multiplayerAuthenticated = false;
            activeMultiplayerRoom = null;
            SetMultiplayerRoomActionsVisible(false);
            if (currentPage == AppPage.Multiplayer && multiplayerStatus != null)
                multiplayerStatus.text = UiLocalization.Translate("服务器连接已断开。\n可以点击刷新房间重新连接。\n\n") + multiplayerStatus.text;
        }

        private void RefreshMultiplayerQuotaLabel()
        {
            if (multiplayerQuota == null) return;
            string create = multiplayerQuota.createRoomRemaining < 0 ? "不限" : multiplayerQuota.createRoomRemaining.ToString(CultureInfo.InvariantCulture);
            string join = multiplayerQuota.joinRoomRemaining < 0 ? "不限" : multiplayerQuota.joinRoomRemaining.ToString(CultureInfo.InvariantCulture);
            if (multiplayerQuotaLabel != null) multiplayerQuotaLabel.text = "创建剩余 " + create + "    加入剩余 " + join;
            if (multiplayerCreateButton != null) multiplayerCreateButton.interactable = multiplayerQuota.createRoomRemaining != 0;
            if (multiplayerJoinButton != null) multiplayerJoinButton.interactable = multiplayerQuota.joinRoomRemaining != 0;
            SetButtonLabel(multiplayerCreateButton, "创建房间\n剩余 " + create);
            SetButtonLabel(multiplayerJoinButton, "加入房间\n剩余 " + join);
        }

        private static void SetButtonLabel(Button button, string value)
        {
            if (button == null) return;
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.text = UiLocalization.Translate(value);
        }

        private IReadOnlyList<SongEntry> ManagedSongs()
        {
            if (string.IsNullOrWhiteSpace(managementSearchText)) return catalog.ToArray();
            string needle = NormalizeSearch(managementSearchText);
            return catalog.Where(song => SearchText(song).Contains(needle)).ToArray();
        }

        private void SetManagementMode(bool skinMode)
        {
            managementSkinMode = skinMode;
            managedSkinId = "";
            managedSkinPreviewPage = 0;
            managementSearchText = "";
            managementSearch?.SetTextWithoutNotify("");
            if (managementSearch != null) {
                Text placeholder = managementSearch.placeholder as Text;
                if (placeholder != null) placeholder.text = skinMode ? "搜索皮肤名称、作者或 ID" : "搜索歌曲、作者、分类或 ID";
            }
            Color managementActive = HighContrastUi ? UiThemeContrast.Yellow : new Color(.04f, .34f, .46f);
            if (managementSongTab != null) managementSongTab.GetComponent<Image>().color = skinMode ? surfaceRaised : managementActive;
            if (managementSkinTab != null) managementSkinTab.GetComponent<Image>().color = skinMode ? managementActive : surfaceRaised;
            if (managementSelectAllButton != null) managementSelectAllButton.gameObject.SetActive(!skinMode);
            if (managementBatchButton != null) managementBatchButton.gameObject.SetActive(!skinMode);
            if (managementEnterSongButton != null) managementEnterSongButton.gameObject.SetActive(!skinMode);
            if (managementHint != null) {
                managementHint.gameObject.SetActive(true);
                managementHint.text = skinMode ? "点击皮肤包查看详情、预览资源和缺失回退项" : "选择歌曲后可批量编辑或删除";
            }
            SetButtonLabel(managementImportButton, skinMode ? "导入皮肤" : "导入");
            SetButtonLabel(managementRefreshButton, skinMode ? "刷新皮肤" : "刷新");
            SetButtonLabel(managementDeleteButton, skinMode ? "删除皮肤" : "删除");
            if (skinMode) RebuildSkinManagementList(true);
            else RebuildManagementList(true);
        }

        private void OnManagementImport()
        {
            if (managementSkinMode) BeginGameplaySkinImport();
            else BeginSongImport();
        }

        private void OnManagementRefresh()
        {
            if (managementSkinMode) {
                GameplaySkinManager.Refresh();
                RebuildSkinManagementList();
            } else RefreshCatalog();
        }

        private void OnManagementDelete()
        {
            if (managementSkinMode) ConfirmManagedSkinDelete();
            else ConfirmManagedDelete();
        }

        private IReadOnlyList<GameplaySkinInfo> ManagedSkins()
        {
            IReadOnlyList<GameplaySkinInfo> skins = GameplaySkinManager.Installed;
            if (string.IsNullOrWhiteSpace(managementSearchText)) return skins;
            string needle = NormalizeSearch(managementSearchText);
            return skins.Where(skin => NormalizeSearch((skin.Name ?? "") + " " + (skin.Author ?? "") + " " + skin.Id).Contains(needle)).ToArray();
        }

        private void RebuildSkinManagementList(bool scrollToTop = false)
        {
            if (managementContent == null) return;
            ClearManagedContent();
            managementContentHasSkins = true;
            if (Application.isPlaying) managementBuild = StartCoroutine(BuildSkinManagementRows(scrollToTop));
            else {
                IEnumerator build = BuildSkinManagementRows(scrollToTop);
                while (build.MoveNext()) { }
            }
        }

        private IEnumerator BuildSkinManagementRows(bool scrollToTop)
        {
            yield return null;
            IReadOnlyList<GameplaySkinInfo> skins = ManagedSkins();
            const float collapsedHeight = 126f;
            const float expandedHeight = 330f;
            const float gap = 8f;
            float top = 8f;
            foreach (GameplaySkinInfo skin in skins)
            {
                yield return null;
                bool selected = string.Equals(managedSkinId, skin.Id, StringComparison.OrdinalIgnoreCase);
                float rowHeight = selected ? expandedHeight : collapsedHeight;
                RectTransform row = Panel("Managed skin " + skin.Id, managementContent,
                    selected ? new Color(.06f, .28f, .35f, 1f) : surfaceRaised, Vector2.zero, Vector2.one);
                row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(.5f, 1);
                row.offsetMin = new Vector2(8, -top - rowHeight); row.offsetMax = new Vector2(-8, -top);

                RectTransform previewBox = Panel("Skin preview", row, new Color(.02f, .06f, .09f, 1f),
                    selected ? new Vector2(.02f, .57f) : new Vector2(.02f, .14f),
                    selected ? new Vector2(.16f, .90f) : new Vector2(.16f, .86f));
                GameplaySkinResourceSummary summary = GameplaySkinManager.GetResourceSummary(skin);
                string previewPath = summary.PreviewPath ?? summary.PreviewPaths.FirstOrDefault();
                Texture2D previewTexture = GameplaySkinManager.LoadSkinImage(skin, previewPath);
                if (previewTexture != null) {
                    GameObject imageObject = new GameObject("Skin preview image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                    imageObject.transform.SetParent(previewBox, false);
                    RawImage image = imageObject.GetComponent<RawImage>(); image.texture = previewTexture; image.color = Color.white; image.raycastTarget = false;
                    RectTransform imageRect = image.rectTransform; imageRect.anchorMin = Vector2.zero; imageRect.anchorMax = Vector2.one;
                    imageRect.offsetMin = Vector2.zero; imageRect.offsetMax = Vector2.zero;
                    AspectRatioFitter fitter = imageObject.AddComponent<AspectRatioFitter>(); fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                    fitter.aspectRatio = Mathf.Max(.1f, previewTexture.width / (float)Mathf.Max(1, previewTexture.height));
                } else Label(skin.BuiltIn ? "原版" : "无预览", previewBox, 20, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, muted);

                Label(skin.Name, row, 24, TextAnchor.MiddleLeft,
                    selected ? new Vector2(.19f, .72f) : new Vector2(.19f, .61f),
                    selected ? new Vector2(.60f, .90f) : new Vector2(.60f, .88f), Color.white);
                Label((skin.BuiltIn ? "内置皮肤" : "外部皮肤包") + "  ·  " + (skin.Author ?? "未填写"), row, 16,
                    TextAnchor.MiddleLeft, selected ? new Vector2(.19f, .60f) : new Vector2(.19f, .42f),
                    selected ? new Vector2(.64f, .73f) : new Vector2(.64f, .62f), muted);
                Label("ID  " + skin.Id + "\n版本  " + (string.IsNullOrWhiteSpace(skin.Version) ? "未填写" : skin.Version), row, 15,
                    TextAnchor.MiddleLeft, selected ? new Vector2(.19f, .43f) : new Vector2(.19f, .13f),
                    selected ? new Vector2(.55f, .61f) : new Vector2(.55f, .43f), muted);
                Label(skin.BuiltIn ? "内置原版资源" : $"{summary.FileCount} 个文件  ·  {FormatBytes(summary.TotalBytes)}\n音符 {summary.NoteCount}  轨道 {summary.TrackCount}  特效 {summary.EffectCount}  Mesh {summary.MeshCount}  音频 {summary.AudioCount}",
                    row, 15, TextAnchor.MiddleRight, selected ? new Vector2(.56f, .52f) : new Vector2(.56f, .18f),
                    selected ? new Vector2(.97f, .86f) : new Vector2(.97f, .82f), muted);

                Button button = row.gameObject.AddComponent<Button>();
                button.targetGraphic = row.GetComponent<Image>();
                button.onClick.AddListener(() => {
                    managedSkinId = selected ? "" : skin.Id;
                    managedSkinPreviewPage = 0;
                    RebuildSkinManagementList();
                });

                if (!selected) {
                    top += rowHeight + gap;
                    continue;
                }
                Label("资源预览", row, 15, TextAnchor.MiddleLeft, new Vector2(.19f, .03f), new Vector2(.34f, .12f), cyan);
                IReadOnlyList<string> previews = summary.PreviewPaths;
                const int previewPageSize = 4;
                int pageCount = Mathf.Max(1, Mathf.CeilToInt(previews.Count / (float)previewPageSize));
                managedSkinPreviewPage = Mathf.Clamp(managedSkinPreviewPage, 0, pageCount - 1);
                string[] pagePreviews = previews.Skip(managedSkinPreviewPage * previewPageSize).Take(previewPageSize).ToArray();
                for (int index = 0; index < pagePreviews.Length; index++) {
                    Texture2D texture = GameplaySkinManager.LoadSkinImage(skin, pagePreviews[index]);
                    if (texture == null) continue;
                    float left = .20f + index * .125f;
                    RectTransform box = Panel("Resource preview " + index, row, new Color(.02f, .06f, .09f, 1f),
                        new Vector2(left, .12f), new Vector2(left + .105f, .39f));
                    GameObject imageObject = new GameObject("Resource image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                    imageObject.transform.SetParent(box, false);
                    RawImage image = imageObject.GetComponent<RawImage>(); image.texture = texture; image.color = Color.white; image.raycastTarget = false;
                    image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one;
                    image.rectTransform.offsetMin = Vector2.zero; image.rectTransform.offsetMax = Vector2.zero;
                    AspectRatioFitter fitter = imageObject.AddComponent<AspectRatioFitter>(); fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                    fitter.aspectRatio = Mathf.Max(.1f, texture.width / (float)Mathf.Max(1, texture.height));
                    Label(Path.GetFileNameWithoutExtension(pagePreviews[index]), box, 10, TextAnchor.LowerCenter,
                        new Vector2(.02f, .01f), new Vector2(.98f, .22f), Color.white);
                }
                Label(previews.Count == 0 ? "没有可预览的 PNG" : $"图片 {previews.Count} 张  ·  第 {managedSkinPreviewPage + 1} / {pageCount} 页",
                    row, 13, TextAnchor.MiddleCenter, new Vector2(.20f, .025f), new Vector2(.575f, .115f), muted);
                Button previous = ButtonText("‹", row, new Vector2(.58f, .04f), new Vector2(.625f, .115f), surface,
                    managedSkinPreviewPage > 0 ? () => { managedSkinPreviewPage--; RebuildSkinManagementList(); } : null);
                Button next = ButtonText("›", row, new Vector2(.63f, .04f), new Vector2(.675f, .115f), surface,
                    managedSkinPreviewPage + 1 < pageCount ? () => { managedSkinPreviewPage++; RebuildSkinManagementList(); } : null);
                previous.interactable = managedSkinPreviewPage > 0;
                next.interactable = managedSkinPreviewPage + 1 < pageCount;
                string missingText = skin.BuiltIn
                    ? "内置原版皮肤：所有外部皮肤缺少的资源都会从这里补全。"
                    : summary.Missing.Count == 0
                    ? "缺少资源：无，当前皮肤包资源完整。"
                    : "缺少资源：" + summary.Missing.Count + " 项，以下项目会使用原版皮肤资源\n" + string.Join("\n", summary.Missing.Take(14).Select(path => "· " + path)) +
                      (summary.Missing.Count > 14 ? "\n· 其余项目请继续查看导入详情" : "");
                Label(missingText, row, 13, TextAnchor.UpperLeft, new Vector2(.70f, .03f), new Vector2(.97f, .40f),
                    skin.BuiltIn || summary.Missing.Count == 0 ? new Color(.40f, 1f, .62f) : new Color(1f, .72f, .28f));
                top += rowHeight + gap;
            }

            float height = skins.Count == 0 ? 120f : top - 8f;
            if (skins.Count > 0) {
                height = 8f;
                foreach (GameplaySkinInfo skin in skins)
                    height += (string.Equals(managedSkinId, skin.Id, StringComparison.OrdinalIgnoreCase) ? expandedHeight : collapsedHeight) + gap;
            }
            managementContent.sizeDelta = new Vector2(0, height);
            if (skins.Count == 0)
                Label("没有匹配的皮肤包\n点击上方“导入皮肤”添加 .cmgameplay 文件", managementContent, 24, TextAnchor.MiddleCenter,
                    new Vector2(.1f, 0), new Vector2(.9f, 1), muted);
            RefreshManagementSummary();
            if (scrollToTop && managementScroll != null) {
                managementScroll.StopMovement(); managementScroll.verticalNormalizedPosition = 1f;
            }
            managementBuild = null;
        }

        private void ConfirmManagedSkinDelete()
        {
            GameplaySkinInfo skin = GameplaySkinManager.Installed.FirstOrDefault(item =>
                string.Equals(item.Id, managedSkinId, StringComparison.OrdinalIgnoreCase));
            if (skin == null) {
                ShowModal("没有选择皮肤", "请先点击皮肤包查看详情，再执行删除。", null, false);
                return;
            }
            if (skin.BuiltIn) {
                ShowModal("无法删除内置皮肤", "当前原版皮肤由软件内置，不能删除。", null, false);
                return;
            }
            ShowModal("删除游玩皮肤", "将删除“" + skin.Name + "”及其全部资源。\n此操作无法撤销。", DeleteManagedSkin, true);
        }

        private void DeleteManagedSkin()
        {
            GameplaySkinInfo skin = GameplaySkinManager.Installed.FirstOrDefault(item =>
                string.Equals(item.Id, managedSkinId, StringComparison.OrdinalIgnoreCase));
            if (skin == null || skin.BuiltIn) return;
            bool deleted = GameplaySkinManager.DeleteSkin(skin);
            if (string.Equals(settings.GameplaySkinId, skin.Id, StringComparison.OrdinalIgnoreCase)) {
                settings.GameplaySkinId = GameplaySkinManager.BuiltinId;
                settings.Save();
                ApplyRuntimeSettings();
            }
            managedSkinId = "";
            RebuildSkinManagementList();
            ShowModal(deleted ? "删除完成" : "删除失败", deleted ? "皮肤资源已删除。" : "无法删除该皮肤资源。", null, false);
        }

        private void RebuildManagementList(bool scrollToTop = false)
        {
            if (managementSkinMode) {
                RebuildSkinManagementList(scrollToTop);
                return;
            }
            if (managementContent == null) return;
            StopManagementBuild();
            if (managementContentHasSkins) ClearManagedContent();
            managementSongs = ManagedSongs();
            managementContent.sizeDelta = new Vector2(0, Mathf.Max(120f, 8f + managementSongs.Count * 104f));
            foreach (ManagedSongRow row in managementRows) row.Song = null;
            if (scrollToTop && managementScroll != null)
            {
                managementScroll.StopMovement();
                managementScroll.verticalNormalizedPosition = 1f;
            }
            RefreshManagementSummary();
            RequestManagedRows();
        }

        private void RefreshManagementSummary()
        {
            if (managementSummary == null) return;
            if (managementSkinMode) {
                int external = GameplaySkinManager.Installed.Count(item => !item.BuiltIn);
                managementSummary.text = $"共 {GameplaySkinManager.Installed.Count} 套 · 外部皮肤 {external} 套 · 点击项目查看详情";
                return;
            }
            string indexState = SongCatalog.LastLoadUsedCache ? "沿用现有索引" : "索引已重建";
            managementSummary.text = $"共 {catalog.Count} 首 · 已选 {managedSongRoots.Count} 首 · {indexState}";
        }

        private void ToggleAllManagedSongs()
        {
            IReadOnlyList<SongEntry> visible = ManagedSongs();
            bool allSelected = visible.Count > 0 && visible.All(song => managedSongRoots.Contains(song.Root));
            foreach (SongEntry song in visible)
            {
                if (allSelected) managedSongRoots.Remove(song.Root);
                else managedSongRoots.Add(song.Root);
            }
            RebuildManagementList();
        }

        private void ConfirmManagedDelete()
        {
            SongEntry[] selected = catalog.Where(song => managedSongRoots.Contains(song.Root)).ToArray();
            if (selected.Length == 0)
            {
                ShowModal("没有选择歌曲", "请先在数据列表中选择要删除的歌曲。", null, false);
                return;
            }
            string names = string.Join("\n", selected.Take(6).Select(song => "· " + song.Name));
            if (selected.Length > 6) names += $"\n· 以及另外 {selected.Length - 6} 首";
            ShowModal("删除所选歌曲", $"将删除 {selected.Length} 首歌曲的音乐、封面和全部谱面。\n此操作无法撤销。\n\n{names}", DeleteManagedSelection, true);
        }

        private void DeleteManagedSelection()
        {
            string[] roots = managedSongRoots.ToArray();
            int deleted = SongCatalog.DeleteSongDirectories(roots);
            managedSongRoots.Clear();
            ReloadCatalogAfterDataChange();
            ShowModal("删除完成", $"已删除 {deleted} 首歌曲，索引已重新建立。", null, false);
        }

        private void BuildSettings()
        {
            RectTransform page = Page(AppPage.Settings, ink);
            AddMeteorBackground(page, MeteorBackgroundProfile.Quiet);
            ButtonText("‹", page, new Vector2(.02f, .90f), new Vector2(.07f, .98f), surfaceRaised, () => SwitchTo(AppPage.Home));
            Label("设置", page, 38, TextAnchor.MiddleLeft, new Vector2(.09f, .90f), new Vector2(.40f, .98f), Color.white);
            RectTransform nav = Panel("Categories", page, Color.clear, new Vector2(.025f, .06f), new Vector2(.225f, .87f));
            Label("设置分类", nav, 27, TextAnchor.MiddleLeft, new Vector2(.10f, .81f), new Vector2(.90f, .94f), cyan);
            if (Array.IndexOf(SettingsCategories, settingsCategory) < 0) settingsCategory = "延迟";
            for (int index = 0; index < SettingsCategories.Length; index++) {
                string category = SettingsCategories[index];
                float top = .78f - index * .125f;
                settingsCategoryButtons[category] = ButtonText(category, nav, new Vector2(.08f, top - .11f), new Vector2(.92f, top),
                    category == settingsCategory ? (HighContrastUi ? cyan : new Color(.04f, .34f, .46f)) : surfaceRaised,
                    () => ShowSettingsCategory(category));
            }
            RectTransform viewport = Panel("Settings viewport", page, surface, new Vector2(.24f, .06f), new Vector2(.975f, .87f));
            viewport.gameObject.AddComponent<RectMask2D>();
            settingsScroll = viewport.gameObject.AddComponent<ScrollRect>();
            settingsScroll.horizontal = false;
            settingsScroll.vertical = true;
            settingsScroll.movementType = ScrollRect.MovementType.Elastic;
            settingsScroll.elasticity = .09f;
            settingsScroll.decelerationRate = .12f;
            settingsScroll.scrollSensitivity = 48f;
            settingsScroll.viewport = viewport;
            settingsBody = RectObject("Settings content", viewport, new Vector2(0, 1), new Vector2(1, 1));
            settingsBody.pivot = new Vector2(.5f, 1f);
            settingsBody.sizeDelta = new Vector2(0, 900);
            settingsScroll.content = settingsBody;
            settingsContentGroup = settingsBody.gameObject.AddComponent<CanvasGroup>();
            SettingsPageSwipe swipe = viewport.gameObject.AddComponent<SettingsPageSwipe>();
            swipe.Scroll = settingsScroll;
            swipe.Next = () => MoveSettingsCategory(1);
            swipe.Previous = () => MoveSettingsCategory(-1);
            RenderSettingsCategory();
        }

        private void ShowSettingsCategory(string category)
        {
            if (transitioning || Array.IndexOf(SettingsCategories, category) < 0) return;
            if (category == settingsCategory) {
                if (settingsScroll != null) StartCoroutine(SmoothSettingsScrollToTop());
                return;
            }
            if (settingsTransition != null) {
                StopCoroutine(settingsTransition);
                settingsTransition = null;
                if (settingsContentGroup != null) settingsContentGroup.alpha = 1f;
                if (settingsBody != null) settingsBody.anchoredPosition = Vector2.zero;
            }
            settingsTransition = StartCoroutine(AnimateSettingsCategory(category));
        }

        private void MoveSettingsCategory(int direction)
        {
            int current = Array.IndexOf(SettingsCategories, settingsCategory);
            int next = Mathf.Clamp(current + Math.Sign(direction), 0, SettingsCategories.Length - 1);
            if (next == current) return;
            ShowSettingsCategory(SettingsCategories[next]);
        }

        private IEnumerator AnimateSettingsCategory(string category)
        {
            settingsScroll?.StopMovement();
            settingsContentGroup.interactable = false;
            settingsContentGroup.blocksRaycasts = false;
            int oldIndex = Array.IndexOf(SettingsCategories, settingsCategory);
            int newIndex = Array.IndexOf(SettingsCategories, category);
            float direction = newIndex >= oldIndex ? 1f : -1f;
            float time = 0;
            const float outDuration = .12f;
            while (time < outDuration) {
                time += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0, 1, time / outDuration);
                settingsContentGroup.alpha = 1f - t;
                settingsBody.anchoredPosition = new Vector2(direction * -34f * t, settingsBody.anchoredPosition.y);
                yield return null;
            }
            settingsCategory = category;
            RenderSettingsCategory();
            settingsContentGroup.alpha = 0;
            settingsBody.anchoredPosition = new Vector2(direction * 46f, 0);
            time = 0;
            const float inDuration = .22f;
            while (time < inDuration) {
                time += Time.unscaledDeltaTime;
                float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(time / inDuration), 3f);
                settingsContentGroup.alpha = t;
                settingsBody.anchoredPosition = new Vector2(Mathf.Lerp(direction * 46f, 0, t), 0);
                yield return null;
            }
            settingsBody.anchoredPosition = Vector2.zero;
            settingsContentGroup.alpha = 1;
            settingsContentGroup.interactable = true;
            settingsContentGroup.blocksRaycasts = true;
            settingsTransition = null;
        }

        private IEnumerator SmoothSettingsScrollToTop()
        {
            if (settingsScroll == null) yield break;
            float from = settingsScroll.verticalNormalizedPosition;
            float time = 0;
            while (time < .24f) {
                time += Time.unscaledDeltaTime;
                settingsScroll.verticalNormalizedPosition = Mathf.Lerp(from, 1f,
                    1f - Mathf.Pow(1f - Mathf.Clamp01(time / .24f), 3f));
                yield return null;
            }
            settingsScroll.verticalNormalizedPosition = 1f;
        }

        private void UpdateSettingsCategoryButtons()
        {
            foreach (var pair in settingsCategoryButtons) {
                Color color = pair.Key == settingsCategory ? (HighContrastUi ? cyan : new Color(.04f, .34f, .46f)) : surfaceRaised;
                pair.Value.GetComponent<Image>().color = color;
                ColorBlock block = pair.Value.colors; block.normalColor = Color.white;
                block.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
                block.selectedColor = Color.white;
                block.pressedColor = new Color(.8f, .8f, .8f); pair.Value.colors = block;
            }
        }

        private void RenderSettingsCategory(bool resetScroll = true)
        {
            if (settingsBody == null) return;
            float preservedScrollY = Mathf.Max(0f, settingsBody.anchoredPosition.y);
            foreach (Transform child in settingsBody) {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            UpdateSettingsCategoryButtons();
            settingsCursor = 28f;
            switch (settingsCategory) {
                case "延迟": BuildDelaySettings(); break;
                case "画面": BuildVisualSettings(); break;
                case "皮肤": BuildSkinSettings(); break;
                case "音频": BuildAudioSettings(); break;
                default:
                    BuildAboutSettings(); break;
            }
            settingsBody.sizeDelta = new Vector2(0, settingsCursor + 24f);
            settingsBody.anchoredPosition = Vector2.zero;
            settingsScroll?.StopMovement();
            if (settingsScroll != null) {
                Canvas.ForceUpdateCanvases();
                float maxScroll = Mathf.Max(0f, settingsBody.rect.height - settingsScroll.viewport.rect.height);
                settingsBody.anchoredPosition = new Vector2(0, resetScroll ? 0 : Mathf.Min(preservedScrollY, maxScroll));
            }
        }

        private void BuildDelaySettings()
        {
            SettingsHeader("延迟", "调整与校准");
            SettingsAction("进入延迟自测", "开始测试", OpenDelayCalibration);
            SettingsStepper("音乐延迟", () => settings.MusicOffsetMs,
                value => settings.MusicOffsetMs = value);
            SettingsStepper("判定延迟", () => settings.JudgeOffsetMs,
                value => settings.JudgeOffsetMs = value);
            SkinSection("判定时间显示", cyan);
            SkinToggle("JC", "", settings.ShowJusticeCriticalMs,
                value => ChangeSetting(() => settings.ShowJusticeCriticalMs = value));
            SkinToggle("JUSTICE", "", settings.ShowJusticeMs,
                value => ChangeSetting(() => settings.ShowJusticeMs = value));
            SkinToggle("ATTACK", "", settings.ShowAttackMs,
                value => ChangeSetting(() => settings.ShowAttackMs = value));
            SettingsFooter("画面", () => ShowSettingsCategory("画面"));
        }

        private void OpenDelayCalibration()
        {
            if (transitioning || gameplaySession == null) return;
            StartCoroutine(PrepareDelayCalibration());
        }

        private IEnumerator PrepareDelayCalibration()
        {
            RuntimeLoadingOverlay loading = FindObjectOfType<RuntimeLoadingOverlay>();
            loading?.Show("正在准备延迟自测资源…", .05f);
            string root = Path.Combine(Application.persistentDataPath, "Calibration");
            string[] files = { "chart/delay_calibration.c2s", "audio/gameplay.mp3" };
            for (int index = 0; index < files.Length; index++)
            {
                string relative = files[index];
                string source = Path.Combine(Application.streamingAssetsPath, "DelayCalibration", relative)
                    .Replace('\\', '/');
                string sourceUri = source.Contains("://") ? source : new Uri(source).AbsoluteUri;
                using UnityWebRequest request = UnityWebRequest.Get(sourceUri);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    loading?.Hide();
                    ShowModal("延迟自测资源错误", "无法读取 " + relative + "：\n" + request.error, null, false);
                    yield break;
                }
                string destination = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? root);
                File.WriteAllBytes(destination, request.downloadHandler.data);
                loading?.SetProgress("正在准备 " + Path.GetFileName(relative) + "…", .25f + index * .35f);
            }

            string chartPath = Path.Combine(root, "chart", "delay_calibration.c2s");
            playedChart = new SongChartEntry {
                Difficulty = "CALIBRATION", Level = "60 BPM", Designer = "CHUNITHM Mobile", Path = chartPath
            };
            playedSong = new SongEntry {
                Root = root, Id = "delay-calibration", Name = "延迟自测", Artist = "第三拍判定",
                Category = "SYSTEM", Bpm = "60", DurationText = "循环"
            };
            playedSong.Charts.Add(playedChart);
            playedRecordsScore = false;
            playedHasAnyMod = false;
            playedModLabel = "NORMAL";
            playedDeltas.Clear();
            delayCalibrationActive = true;
            preview?.StopPreview();
            SetGameplayScene(true);
            ApplyRuntimeSettings();
            gameplayBackdrop?.ClearJacket();
            SetGameplaySongInfo(playedSong, playedChart);
            gameplayPausePanel.gameObject.SetActive(false);
            gameplayCountdown.text = "";
            SetDelayCalibrationUi(true);
            loading?.Hide();
            SwitchTo(AppPage.Gameplay, StartDelayCalibrationPlayback);
        }

        private void StartDelayCalibrationPlayback()
        {
            if (!delayCalibrationActive || playedSong == null || playedChart == null || gameplaySession == null) return;
            CaptureGameplayTimingSettings();
            gameplaySession.PlaySong(playedSong, playedChart, 1f, false, false, false, false, false,
                musicOffsetMs: playedMusicOffsetMs, judgementOffsetMs: playedJudgeOffsetMs,
                onFailed: message => StartCoroutine(HandleDelayCalibrationLoadFailure(message)));
        }

        private IEnumerator HandleDelayCalibrationLoadFailure(string message)
        {
            FindObjectOfType<RuntimeLoadingOverlay>()?.Hide();
            delayCalibrationActive = false;
            gameplaySession?.StopPlayback();
            ClearGameplayVisuals();
            SetDelayCalibrationUi(false);
            SetGameplayScene(false);
            yield return null;
            ShowModal("延迟自测加载失败", string.IsNullOrWhiteSpace(message)
                ? "无法读取自测音乐或谱面。" : message, null, false);
            SwitchTo(AppPage.Settings);
        }

        private void ExitDelayCalibration()
        {
            if (!delayCalibrationActive) return;
            delayCalibrationActive = false;
            gameplaySession?.StopPlayback();
            ClearGameplayVisuals();
            SetDelayCalibrationUi(false);
            SetGameplayScene(false);
            SwitchTo(AppPage.Settings);
        }

        private void SetDelayCalibrationUi(bool active)
        {
            if (gameplayPauseButton != null) gameplayPauseButton.gameObject.SetActive(!active);
            if (delayCalibrationExitButton != null) delayCalibrationExitButton.gameObject.SetActive(active);
            if (delayCalibrationLabel != null) delayCalibrationLabel.gameObject.SetActive(active);
            if (delayCalibrationLive != null) delayCalibrationLive.gameObject.SetActive(active);
        }

        private void BuildVisualSettings()
        {
            SettingsHeader("画面", "轨道、触摸区域与游玩界面");
            SettingsVisualEditorButton();
            SettingsSectionTitle("帧率", "设置画面刷新上限", cyan);
            int[] frameRates = { 60, 90, 120, 0 };
            SkinDropdown("帧率上限", new[] { "60", "90", "120", "无限制" },
                Array.IndexOf(frameRates, AppSettings.NormalizeFrameRateLimit(settings.FrameRateLimit)),
                index => {
                    settings.FrameRateLimit = frameRates[index];
                    settings.Save();
                    HighRefreshRateController.Configure(settings.FrameRateLimit);
                });
            SkinToggle("显示帧数", "左下角显示实际渲染帧率", settings.ShowGameplayFps,
                value => ChangeSetting(() => settings.ShowGameplayFps = value));
            SettingsSectionTitle("画面与背景", "亮度和歌曲封面背景", cyan);
            SettingsSlider("背景亮度", "%", 20, 100, settings.BackgroundBrightness,
                value => settings.BackgroundBrightness = Mathf.RoundToInt(value));
            SettingsSlider("封面背景强度", "%", 20, 100, settings.JacketBackgroundOpacity,
                value => settings.JacketBackgroundOpacity = Mathf.RoundToInt(value));
            SettingsSectionTitle("轨道", "位置、宽度、流速与遮罩", violet);
            SettingsSlider("轨道整体上下位置", "%", -40, 40, settings.TrackVerticalOffset,
                value => settings.TrackVerticalOffset = Mathf.RoundToInt(value));
            SettingsSlider("轨道宽度", "%", 60, 140, settings.TrackWidth,
                value => settings.TrackWidth = Mathf.RoundToInt(value));
            SettingsSlider("轨道上端宽度", "%", 45, 160, settings.TrackTopWidth,
                value => settings.TrackTopWidth = Mathf.RoundToInt(value));
            SkinToggle("轨道顶部遮罩", "", settings.TrackMaskEnabled,
                value => ChangeSetting(() => settings.TrackMaskEnabled = value));
            if (settings.TrackMaskEnabled) {
            SettingsSlider("遮罩高度", "%", 4, 45, settings.TrackMaskHeight,
                value => settings.TrackMaskHeight = Mathf.RoundToInt(value));
            SettingsSlider("遮罩不透明度", "%", 20, 100, settings.TrackMaskOpacity,
                value => settings.TrackMaskOpacity = Mathf.RoundToInt(value));
            }
            SettingsOption("谱面流速", OriginalNoteSpeed.Label(settings.NoteSpeedOption),
                () => ChangeNoteSpeed(-1), () => ChangeNoteSpeed(1));
            SettingsSectionTitle("触控与 AIR", "触控板、判定区域和反馈", cyan);
            SettingsSlider("触控板 / AIR 分界", "%", 8, 45, settings.TouchpadHeight,
                value => SetSharedInputBoundary(settings, Mathf.RoundToInt(value)));
            SettingsSlider("触控板横向宽度", "%", 60, 100, settings.TouchpadWidth,
                value => settings.TouchpadWidth = Mathf.RoundToInt(value));
            SettingsSlider("触控板斜度", "°", -20, 20, settings.TouchpadTilt,
                value => settings.TouchpadTilt = Mathf.RoundToInt(value));
            SettingsSlider("触控板透明度", "%", 0, 100, settings.TouchpadOpacity,
                value => settings.TouchpadOpacity = Mathf.RoundToInt(value));
            SettingsSlider("触控特效透明度", "%", 0, 100, settings.TouchEffectOpacity,
                value => settings.TouchEffectOpacity = Mathf.RoundToInt(value));
            SettingsSlider("手指触控范围", "", 5, 30, settings.TouchContactRangeTenths,
                value => settings.TouchContactRangeTenths = Mathf.RoundToInt(value), false,
                value => (value / 10f).ToString("0.0", CultureInfo.InvariantCulture) + " 格");
            SettingsSlider("AIR 区域上边界", "%", settings.TouchpadHeight + 5, 100, settings.AirAreaTop,
                value => settings.AirAreaTop = Mathf.Max(Mathf.RoundToInt(value), settings.AirAreaBottom + 5));
            SkinToggle("AIR 平均高度线", "", settings.ShowAirGuide,
                value => ChangeSetting(() => settings.ShowAirGuide = value));
            SkinToggle("触摸反馈", "", settings.ShowTouchFeedback,
                value => ChangeSetting(() => settings.ShowTouchFeedback = value));
            SettingsSectionTitle("游玩 HUD", "暂停、分数、COMBO 与歌曲信息", violet);
            SkinToggle("暂停按钮需连续点击两次", "", settings.PauseRequiresDoubleTap,
                value => ChangeSetting(() => settings.PauseRequiresDoubleTap = value));
            SkinToggle("游玩歌曲信息", "", settings.ShowGameplaySongInfo,
                value => ChangeSetting(() => settings.ShowGameplaySongInfo = value));
            SettingsSlider("分数大小", "%", 50, 180, settings.GameplayScoreScale,
                value => settings.GameplayScoreScale = Mathf.RoundToInt(value));
            SettingsSlider("COMBO 大小", "%", 50, 180, settings.GameplayComboScale,
                value => settings.GameplayComboScale = Mathf.RoundToInt(value));
            SettingsSlider("当前判定大小", "%", 50, 180, settings.GameplayJudgementScale,
                value => settings.GameplayJudgementScale = Mathf.RoundToInt(value));
            SettingsSlider("歌曲信息大小", "%", 50, 180, settings.GameplaySongInfoScale,
                value => settings.GameplaySongInfoScale = Mathf.RoundToInt(value));
            SettingsFooter("皮肤", () => ShowSettingsCategory("皮肤"));
        }

        private void BuildSkinSettings()
        {
            SettingsHeader("皮肤", "背景拖尾、游玩资源与导入管理");
            int imageCount = BackgroundImageStore.GetImagePaths().Count;
            SkinSection("背景设置", cyan);
            SkinDropdown("背景拖尾", new[] { "音符拖尾背景", "Emoji 拖尾背景", "图片拖尾背景" },
                settings.BackgroundSkinMode, SelectBackgroundSkin);
            if (settings.BackgroundSkinMode == 2) {
                SkinAction("导入背景图片", imageCount + " / " + BackgroundImageStore.MaxImages, BeginBackgroundImageImport);
                SkinAction("清除自定义背景", imageCount == 0 ? "暂无图片" : "删除已导入图片",
                    imageCount > 0 ? ConfirmClearBackgroundImages : null);
            }
            SkinSection("游玩皮肤设置", violet);
            IReadOnlyList<GameplaySkinInfo> skins = GameplaySkinManager.Installed;
            int activeIndex = 0;
            for (int i = 0; i < skins.Count; i++) if (skins[i].Id == GameplaySkinManager.Active.Id) activeIndex = i;
            SkinDropdown("游玩皮肤", skins.Select(skin => skin.Name).ToArray(), activeIndex, index => {
                if (index < 0 || index >= skins.Count) return;
                settings.GameplaySkinId = skins[index].Id;
                GameplaySkinManager.SetActive(settings.GameplaySkinId);
                settings.Save(); ApplyRuntimeSettings(); RenderSettingsCategory(false);
            }, false);
            SkinToggle("AIR 拖尾颜色", "彩虹色", settings.AirTrailRainbow,
                value => ChangeSetting(() => settings.AirTrailRainbow = value));
            GameplaySkinResourceSummary activeSummary = GameplaySkinManager.GetResourceSummary(GameplaySkinManager.Active);
            string resourceState = GameplaySkinManager.Active.BuiltIn ? "内置原版资源"
                : activeSummary.Missing.Count == 0 ? "资源完整"
                : activeSummary.Missing.Count + " 项缺失，将使用原版皮肤";
            SkinAction("当前皮肤资源", resourceState, null);
            SkinAction("导入游玩皮肤", ".cmgameplay", BeginGameplaySkinImport);
            SkinAction("管理游玩皮肤", "›", OpenGameplaySkinManagement);
            SettingsFooter("音频", () => ShowSettingsCategory("音频"));
        }

        private void SettingsSectionTitle(string title, string subtitle, Color accentColor)
        {
            SkinSection(title, accentColor);
        }

        private void OpenGameplaySkinManagement()
        {
            EnsurePageBuilt(AppPage.DataManagement);
            managementReturnPage = AppPage.Settings;
            SetManagementMode(true);
            SwitchTo(AppPage.DataManagement);
        }

        private void OpenSongDataManagement()
        {
            EnsurePageBuilt(AppPage.DataManagement);
            managementReturnPage = currentPage == AppPage.Settings ? AppPage.Settings : AppPage.Home;
            SetManagementMode(false);
            SwitchTo(AppPage.DataManagement);
        }

        private void ReturnFromDataManagement()
        {
            AppPage destination = managementReturnPage;
            managementReturnPage = AppPage.Home;
            SwitchTo(destination);
        }

        private void CycleGameplaySkin(int direction)
        {
            IReadOnlyList<GameplaySkinInfo> available = GameplaySkinManager.Installed;
            if (available == null || available.Count == 0) return;
            int current = 0;
            for (int index = 0; index < available.Count; index++)
                if (string.Equals(available[index].Id, settings.GameplaySkinId, StringComparison.OrdinalIgnoreCase)) {
                    current = index;
                    break;
                }
            int next = (current + (direction < 0 ? -1 : 1) + available.Count) % available.Count;
            settings.GameplaySkinId = available[next].Id;
            GameplaySkinManager.SetActive(settings.GameplaySkinId);
            settings.Save();
            ApplyRuntimeSettings();
            gameplayChartRenderer?.RefreshSkin();
            gameplayPlayfield?.RefreshSkin();
            RenderSettingsCategory(false);
        }

        private void BeginGameplaySkinImport()
        {
            if (importRunning) return;
            gameplaySkinImportPending = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            try {
                PlatformStoragePaths.OpenGameplaySkinPicker();
            }
            catch (Exception exception) {
                gameplaySkinImportPending = false;
                ShowModal("无法打开文件选择器", exception.Message, null, false);
            }
#else
            string importFolder = Path.Combine(PlatformServices.Current.PersistentDataPath, "Import");
            Directory.CreateDirectory(importFolder);
            string source = Directory.GetFiles(importFolder, "*.cmgameplay").FirstOrDefault()
                ?? Directory.GetFiles(importFolder, "*.zip").FirstOrDefault();
            if (source == null) {
                gameplaySkinImportPending = false;
                ShowModal("导入游玩皮肤", "请把 .cmgameplay 文件放入：\n" + importFolder, null, false);
            }
            else OnImportFilePicked(source);
#endif
        }

        private string BackgroundTrailOptionLabel(int mode)
        {
            return EffectiveBackgroundSkinMode() == mode ? "当前选择" : "点击切换";
        }

        private int EffectiveBackgroundSkinMode()
        {
            if (settings == null) return 0;
            if (settings.BackgroundSkinMode == 2 && BackgroundImageStore.GetImagePaths().Count > 0) return 2;
            if (settings.BackgroundSkinMode == 1 || settings.EmojiBackground) return 1;
            return 0;
        }

        private void SelectBackgroundSkin(int mode)
        {
            if (transitioning || settings == null) return;
            mode = Mathf.Clamp(mode, 0, 2);
            if (settings.BackgroundSkinMode == mode) return;
            StartCoroutine(AnimateBackgroundSwitch(mode));
        }

        private void ConfirmClearBackgroundImages()
        {
            ShowModal("清除自定义背景", "将删除所有已导入的背景图片。", () => {
                BackgroundImageStore.Clear();
                settings.BackgroundSkinMode = 0;
                settings.EmojiBackground = false;
                settings.Save();
                ApplyRuntimeSettings();
                RenderSettingsCategory(false);
            }, true);
        }

        private void BuildAudioSettings()
        {
            SettingsHeader("音频", "音乐、按键音与正解音");
            SettingsOption("DSP 缓冲", DspBufferLabel(settings.DspBufferSize),
                () => ChangeDspBuffer(-1), () => ChangeDspBuffer(1));
            SettingsSlider("音乐音量", "%", 0, 100, settings.MusicVolume,
                value => settings.MusicVolume = Mathf.RoundToInt(value));
            SettingsSlider("背景 BGM 音量", "%", 0, 100, settings.BackgroundMusicVolume,
                value => settings.BackgroundMusicVolume = Mathf.RoundToInt(value));
            SettingsAudioRow("按键音", settings.KeySoundEnabled, settings.KeySoundVolume,
                value => settings.KeySoundEnabled = value, value => settings.KeySoundVolume = value,
                () => PlaySettingsAudio("Audio/delay_ready", settings.KeySoundEnabled ? settings.KeySoundVolume / 100f : 0f));
            SettingsAudioRow("正解音", settings.AnswerSoundEnabled, settings.AnswerSoundVolume,
                value => settings.AnswerSoundEnabled = value, value => settings.AnswerSoundVolume = value,
                () => PlaySettingsAudio("Audio/se_touch_answer", settings.AnswerSoundEnabled ? settings.AnswerSoundVolume / 100f : 0f));
            SettingsFooter("更多", () => ShowSettingsCategory("更多"));
        }

        private static string DspBufferLabel(int samples)
        {
            return AppSettings.NormalizeDspBufferSize(samples) + " samples";
        }

        private void ChangeDspBuffer(int direction)
        {
            int[] options = { 128, 256, 512, 1024 };
            int current = Array.IndexOf(options, AppSettings.NormalizeDspBufferSize(settings.DspBufferSize));
            int next = Mathf.Clamp(current + Math.Sign(direction), 0, options.Length - 1);
            if (next == current) return;
            settings.DspBufferSize = options[next];
            settings.Save();
            LowLatencyAudioController.Apply(settings.DspBufferSize);
            interfaceAudio?.RestartBackgroundMusic(currentPage);
            RenderSettingsCategory(false);
        }

        private void BuildAboutSettings()
        {
            SettingsHeader("更多", "数据、日志与许可");
            SettingsOption("语言", settings.LanguageModeLabel,
                () => ChangeLanguage(-1), () => ChangeLanguage(1));
            SettingsAction(UiLocalization.Translate("更新日志"),
                UiLocalization.Translate(updateLogsExpanded ? "收起更新日志" : "查看历代版本"), ToggleUpdateLogs);
            if (updateLogsExpanded) BuildUpdateLogsSettings();
            SettingsAction("打开数据管理", "导入、删除与批量编辑", OpenSongDataManagement);
            SettingsAction("强制重建歌曲索引", "重新扫描歌曲数据", RefreshCatalog);
            SettingsAction("索引状态", SongCatalog.LastLoadUsedCache ? "本次沿用缓存" : "本次已重建", null);
            SettingsAction("制作与感谢", "查看制作与感谢", ShowCredits);
            SettingsAction("导出最近日志", "使用系统文件保存窗口", ExportLogs);
            SettingsAction("关于与许可", "CHUNITHM Mobile " + Application.version, ShowAbout);
            SettingsAction("恢复全部默认设置", "需要确认",
                () => ShowModal("恢复默认", "确定恢复全部界面、判定和音频设置？", ResetSettings, true));
        }

        private void BuildUpdateLogsSettings()
        {
            int index = Mathf.Clamp(updateLogIndex, 0, UpdateLogVersions.Length - 1);
            updateLogIndex = index;
            SettingsHeader(UiLocalization.Translate("更新日志"), UiLocalization.Translate("历代版本"));
            SettingsOption(UiLocalization.Translate("选择版本"),
                UiLocalization.Translate(UpdateLogVersions[index] + " 更新日志"),
                () => ChangeUpdateLog(-1), () => ChangeUpdateLog(1));

            string[] lines = UpdateLogLineKeys[index].Select(UiLocalization.Translate).ToArray();
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                bool heading = lineIndex == 0;
                RectTransform row = SettingsRect("Update log item " + lineIndex,
                    heading ? 82f : 72f, heading ? new Color(.04f, .25f, .32f, 1f) : surfaceRaised);
                Text label = Label(lines[lineIndex], row, heading ? 22 : 18,
                    TextAnchor.MiddleLeft, new Vector2(.06f, .10f), new Vector2(.94f, .90f),
                    heading ? cyan : Color.white);
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        private void ToggleUpdateLogs()
        {
            updateLogsExpanded = !updateLogsExpanded;
            if (updateLogsExpanded) updateLogIndex = 0;
            RenderSettingsCategory(false);
        }

        private void ChangeUpdateLog(int direction)
        {
            if (UpdateLogVersions.Length == 0) return;
            updateLogIndex = (updateLogIndex + Math.Sign(direction) + UpdateLogVersions.Length) % UpdateLogVersions.Length;
            RenderSettingsCategory(false);
        }

        private void OpenUpdateLogsFromNotice()
        {
            AcknowledgeUpdateNotice();
            updateLogsExpanded = true;
            updateLogIndex = 0;
            settingsCategory = "更多";
            if (currentPage == AppPage.Settings)
                RenderSettingsCategory();
            else
                SwitchTo(AppPage.Settings, () => RenderSettingsCategory());
        }

        private void ChangeLanguage(int direction)
        {
            int next = settings.LanguageMode + Math.Sign(direction);
            if (next < -1) next = 2;
            if (next > 2) next = -1;
            settings.LanguageMode = next;
            settings.Save();
            UiLocalization.SetLanguage(settings.EffectiveLanguage);
            RebuildUiForLanguage();
        }

        private IEnumerator AnimateBackgroundSwitch(int targetMode)
        {
            if (transitioning || canvas == null) yield break;
            transitioning = true;
            const float duration = .3f;
            interfaceAudio?.PlayStandaloneTransition(duration);

            var overlayObject = new GameObject("Background switch transition", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            RectTransform overlay = (RectTransform)overlayObject.transform;
            overlay.SetParent(canvas.transform, false);
            overlay.SetAsLastSibling();
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = Vector2.zero;
            overlay.offsetMax = Vector2.zero;
            Image overlayImage = overlayObject.GetComponent<Image>();
            overlayImage.sprite = null;
            overlayImage.type = Image.Type.Simple;
            overlayImage.color = HighContrastUi ? UiThemeContrast.Navy : new Color(.008f, .018f, .032f, 1f);
            CanvasGroup group = overlayObject.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = true;
            group.interactable = true;

            Texture2D texture = MeteorKeyBackground.RandomEmojiTexture();
            RawImage emblem = null;
            if (texture != null)
            {
                var emblemObject = new GameObject("Emoji transition emblem", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(RawImage));
                RectTransform emblemRect = (RectTransform)emblemObject.transform;
                emblemRect.SetParent(overlay, false);
                emblemRect.anchorMin = emblemRect.anchorMax = new Vector2(.5f, .5f);
                emblemRect.pivot = new Vector2(.5f, .5f);
                emblemRect.sizeDelta = new Vector2(112f, 112f);
                emblem = emblemObject.GetComponent<RawImage>();
                emblem.texture = texture;
                emblem.raycastTarget = false;
            }

            bool switched = false;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                group.alpha = t < .5f
                    ? Mathf.SmoothStep(0f, 1f, t * 2f)
                    : Mathf.SmoothStep(1f, 0f, (t - .5f) * 2f);
                if (emblem != null)
                {
                    float pulse = 1f + Mathf.Sin(t * Mathf.PI) * .18f;
                    emblem.rectTransform.localScale = Vector3.one * pulse;
                    emblem.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-18f, 18f, t));
                }
                if (!switched && t >= .5f)
                {
                    switched = true;
                    settings.BackgroundSkinMode = targetMode;
                    settings.EmojiBackground = targetMode == 1;
                    settings.Save();
                    ApplyRuntimeSettings();
                    RenderSettingsCategory(false);
                }
                yield return null;
            }

            if (!switched)
            {
                settings.BackgroundSkinMode = targetMode;
                settings.EmojiBackground = targetMode == 1;
                settings.Save();
                ApplyRuntimeSettings();
                RenderSettingsCategory(false);
            }
            Destroy(overlayObject);
            transitioning = false;
        }

        private void PlaySettingsAudio(string resource, float volume)
        {
            if (volume <= 0) return;
            if (settingsPreviewSource == null) {
                settingsPreviewSource = gameObject.AddComponent<AudioSource>();
                settingsPreviewSource.playOnAwake = false;
                settingsPreviewSource.spatialBlend = 0;
                settingsPreviewSource.ignoreListenerPause = true;
            }
            AudioClip clip = Resources.Load<AudioClip>(resource);
            if (clip != null) settingsPreviewSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        private void SettingsHeader(string title, string subtitle)
        {
            RectTransform header = SettingsRect(title + " header", 64f, Color.clear);
            Label(title, header, 28, TextAnchor.MiddleLeft, new Vector2(.025f, 0), new Vector2(.95f, 1), cyan);
        }

        private RectTransform SettingsBlock(string name, float height, Color color) => SettingsRect(name, height, color);

        private RectTransform SettingsRect(string name, float height, Color color)
        {
            RectTransform rect = Panel(name, settingsBody, color, Vector2.zero, Vector2.one);
            rect.GetComponent<Image>().sprite = null;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(.5f, 1);
            rect.sizeDelta = new Vector2(-40f, height);
            rect.anchoredPosition = new Vector2(0, -settingsCursor);
            settingsCursor += height + 4f;
            return rect;
        }

        private void SettingsVisualEditorButton()
        {
            RectTransform shell = SettingsRect("Visual editor command", 142f, new Color(.04f, .32f, .42f, 1f));
            Image accent = Panel("Accent", shell, violet, new Vector2(0, 0), new Vector2(.012f, 1)).GetComponent<Image>();
            accent.raycastTarget = false;
            Button button = ButtonText("", shell, Vector2.zero, Vector2.one, Color.clear, OpenLayoutEditor);
            Label("可视化布局调整", button.transform, 29, TextAnchor.MiddleLeft,
                new Vector2(.055f, .40f), new Vector2(.76f, .84f), Color.white);
            Label("全屏调整轨道、AIR、触控板、分数、COMBO 与歌曲信息", button.transform, 17,
                TextAnchor.MiddleLeft, new Vector2(.055f, .14f), new Vector2(.82f, .43f), new Color(.78f, .94f, 1f));
            Label("›", button.transform, 38, TextAnchor.MiddleCenter, new Vector2(.87f, .20f), new Vector2(.96f, .80f), Color.white);
        }

        private Text SettingsAction(string title, string value, UnityEngine.Events.UnityAction action)
        {
            RectTransform row = SettingsRect(title, 108f, surfaceRaised);
            Button button = ButtonText("", row, Vector2.zero, Vector2.one, Color.clear, action);
            FitModText(Label(title, button.transform, 23, TextAnchor.MiddleLeft, new Vector2(.03f, .12f), new Vector2(.48f, .88f), Color.white), 23);
            return Label(value, button.transform, 18, TextAnchor.MiddleRight,
                new Vector2(.51f, .12f), new Vector2(.96f, .88f), action == null ? muted : cyan);
        }

        private void SettingsOption(string title, string value, UnityEngine.Events.UnityAction previous,
            UnityEngine.Events.UnityAction next)
        {
            RectTransform row = SettingsRect(title, 108f, surfaceRaised);
            FitModText(Label(title, row, 23, TextAnchor.MiddleLeft, new Vector2(.03f, .12f), new Vector2(.43f, .88f), Color.white), 23);
            ButtonText("‹", row, new Vector2(.46f, .20f), new Vector2(.54f, .80f), surface, previous);
            FitModText(Label(value, row, 21, TextAnchor.MiddleCenter, new Vector2(.55f, .12f), new Vector2(.87f, .88f), cyan), 21);
            ButtonText("›", row, new Vector2(.88f, .20f), new Vector2(.96f, .80f), surface, next);
        }

        private void SettingsStepper(string title, Func<int> current, Action<int> changed)
        {
            RectTransform row = SettingsRect(title, 108f, surfaceRaised);
            Label(title, row, 23, TextAnchor.MiddleLeft,
                new Vector2(.04f, .16f), new Vector2(.46f, .84f), Color.white);
            Button minus = ButtonText("-5 ms", row, new Vector2(.49f, .20f), new Vector2(.65f, .80f), surface, null);
            Text valueLabel = Label("", row, 21, TextAnchor.MiddleCenter,
                new Vector2(.66f, .16f), new Vector2(.84f, .84f), cyan);
            Button plus = ButtonText("+5 ms", row, new Vector2(.85f, .20f), new Vector2(.99f, .80f), surface, null);
            Action setMinus = () => {
                int value = Mathf.Clamp((current != null ? current() : 0) - 5, -500, 500);
                changed?.Invoke(value); settings.Save();
                valueLabel.text = value + " ms";
            };
            Action setPlus = () => {
                int value = Mathf.Clamp((current != null ? current() : 0) + 5, -500, 500);
                changed?.Invoke(value); settings.Save();
                valueLabel.text = value + " ms";
            };
            valueLabel.text = Mathf.Clamp(current != null ? current() : 0, -500, 500) + " ms";
            AttachHoldRepeat(minus, setMinus);
            AttachHoldRepeat(plus, setPlus);
        }

        private static void AttachHoldRepeat(Button button, Action action)
        {
            if (button == null) return;
            UiHoldRepeatButton repeat = button.gameObject.AddComponent<UiHoldRepeatButton>();
            repeat.Pressed = action;
        }

        private Slider SettingsSlider(string title, string suffix, float minimum, float maximum, float current,
            Action<float> changed, bool direct = false, Func<float, string> valueFormatter = null)
        {
            RectTransform row = SettingsRect(title, 118f, surfaceRaised);
            Label(title, row, 23, TextAnchor.MiddleLeft, new Vector2(.04f, .48f), new Vector2(.52f, .92f), Color.white);
            Text valueLabel = null;
            InputField directInput = null;
            if (direct) {
                directInput = Input("0", row, new Vector2(.70f, .49f), new Vector2(.89f, .91f));
                directInput.characterValidation = InputField.CharacterValidation.Integer;
                directInput.SetTextWithoutNotify(Mathf.RoundToInt(current).ToString(CultureInfo.InvariantCulture));
                Label(suffix.Trim(), row, 17, TextAnchor.MiddleRight, new Vector2(.89f, .49f), new Vector2(.95f, .91f), muted);
            } else {
                string display = valueFormatter != null ? valueFormatter(current) : Mathf.RoundToInt(current) + suffix;
                valueLabel = Label(display, row, 19, TextAnchor.MiddleRight,
                    new Vector2(.70f, .49f), new Vector2(.95f, .91f), cyan);
            }
            RectTransform track = Panel("Track", row, new Color(.09f, .14f, .18f, 1f),
                new Vector2(.40f, .14f), new Vector2(.92f, .38f));
            track.offsetMin = new Vector2(3, 5);
            track.offsetMax = new Vector2(-3, -5);
            Slider slider = track.gameObject.AddComponent<Slider>();
            slider.minValue = minimum;
            slider.maxValue = maximum;
            slider.wholeNumbers = true;
            RectTransform fillArea = RectObject("Fill Area", track, new Vector2(.025f, .18f), new Vector2(.975f, .82f));
            RectTransform fill = Panel("Fill", fillArea, cyan, Vector2.zero, Vector2.one);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().raycastTarget = false;
            RectTransform handleArea = RectObject("Handle Area", track, new Vector2(.025f, 0), new Vector2(.975f, 1));
            RectTransform handle = Panel("Handle", handleArea, Color.white, new Vector2(0, .5f), new Vector2(0, .5f));
            handle.sizeDelta = new Vector2(28, 28);
            if (HighContrastUi) {
                handle.GetComponent<Image>().color = UiThemeContrast.Navy;
                Outline outline = handle.gameObject.AddComponent<Outline>();
                outline.effectColor = Color.white;
                outline.effectDistance = new Vector2(2, -2);
            }
            handle.pivot = new Vector2(.5f, .5f);
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.SetValueWithoutNotify(Mathf.Clamp(current, minimum, maximum));
            slider.onValueChanged.AddListener(value => {
                int rounded = Mathf.RoundToInt(value);
                if (valueLabel != null)
                    valueLabel.text = valueFormatter != null ? valueFormatter(rounded) : rounded + suffix;
                if (directInput != null) directInput.SetTextWithoutNotify(rounded.ToString(CultureInfo.InvariantCulture));
                changed?.Invoke(value);
                settings.Save();
                ApplyRuntimeSettings();
            });
            if (directInput != null) directInput.onEndEdit.AddListener(text => {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    slider.value = Mathf.Clamp(parsed, minimum, maximum);
                directInput.SetTextWithoutNotify(Mathf.RoundToInt(slider.value).ToString(CultureInfo.InvariantCulture));
            });
            return slider;
        }

        private void SettingsFooter(string next, UnityEngine.Events.UnityAction action)
        {
            RectTransform footer = SettingsRect("Next settings page", 104f, Color.clear);
            Button button = ButtonText("", footer, new Vector2(.16f, .12f), new Vector2(.84f, .88f), surfaceRaised, action);
            Label("下一项  " + next, button.transform, 20, TextAnchor.MiddleCenter,
                new Vector2(.10f, .24f), new Vector2(.82f, .84f), muted);
            Label("↓", button.transform, 24, TextAnchor.MiddleCenter,
                new Vector2(.82f, .22f), new Vector2(.94f, .86f), cyan);
        }

        private static string OnOff(bool value) => UiLocalization.Translate(value ? "开启" : "关闭");

        private void ChangeNoteSpeed(int direction)
        {
            int next = OriginalNoteSpeed.ClampOption(settings.NoteSpeedOption + Math.Sign(direction));
            if (next == settings.NoteSpeedOption) return;
            settings.NoteSpeedOption = next;
            settings.Save();
            ApplyRuntimeSettings();
            RenderSettingsCategory(false);
        }

        private static void SetSharedInputBoundary(AppSettings target, int value)
        {
            if (target == null) return;
            int boundary = Mathf.Clamp(value, 8, 45);
            target.TouchpadHeight = boundary;
            target.GroundAreaHeight = boundary;
            target.AirAreaBottom = boundary;
            target.AirAreaTop = Mathf.Max(target.AirAreaTop, boundary + 5);
        }

        private void ChangeSetting(Action mutation)
        {
            mutation?.Invoke();
            settings.Save();
            ApplyRuntimeSettings();
            RenderSettingsCategory(false);
        }

        private void BuildLayoutEditor()
        {
            layoutEditorLayer = Panel("Visual layout editor", canvas.transform, Color.clear, Vector2.zero, Vector2.one);
            layoutEditorLayer.offsetMin = Vector2.zero; layoutEditorLayer.offsetMax = Vector2.zero;
            layoutEditorLayer.GetComponent<Image>().raycastTarget = false;
            layoutEditorPreview = Panel("Full-screen gameplay edit surface", layoutEditorLayer, new Color(0, 0, 0, .02f),
                Vector2.zero, Vector2.one);
            layoutEditorPreview.offsetMin = Vector2.zero; layoutEditorPreview.offsetMax = Vector2.zero;

            editorTouchpadHandle = EditorBoundary("触控板 / AIR 分界", new Color(.03f, .86f, .92f, .22f), value =>
                SetSharedInputBoundary(draftSettings, Percent(value.y, 8, 45)));
            editorTouchpadWidthHandle = EditorVerticalBoundary("触控板横向宽度", new Color(.08f, .92f, .72f, .22f), value =>
                draftSettings.TouchpadWidth = Mathf.Clamp(Mathf.RoundToInt((value.x - .5f) * 200f), 60, 100), .08f, .96f);
            editorAirTopHandle = EditorBoundary("AIR 区域上边界", new Color(.68f, .72f, 1f, .22f), value =>
                draftSettings.AirAreaTop = Mathf.Max(Percent(value.y, 20, 100), draftSettings.AirAreaBottom + 5));
            editorTrackHandle = EditorBoundary("轨道整体上下位置", new Color(.22f, 1f, .62f, .20f), value =>
                draftSettings.TrackVerticalOffset = Mathf.Clamp(Mathf.RoundToInt((value.y - .5f) * 100f), -40, 40));
            editorTrackWidthHandle = EditorVerticalBoundary("轨道下端宽度", new Color(1f, .48f, .20f, .24f), value =>
                draftSettings.TrackWidth = Mathf.Clamp(Mathf.RoundToInt((value.x - .5f) / .28f * 100f), 60, 140), 0f, .38f);
            editorTrackTopWidthHandle = EditorVerticalBoundary("轨道上端宽度", new Color(.72f, .48f, 1f, .24f), value =>
                draftSettings.TrackTopWidth = Mathf.Clamp(Mathf.RoundToInt((value.x - .5f) / .28f * 100f), 45, 160), .62f, 1f);
            editorTrackMaskHandle = EditorBoundary("轨道遮罩下边缘", new Color(.46f, .58f, .72f, .24f), value => {
                draftSettings.TrackMaskEnabled = true;
                draftSettings.TrackMaskHeight = Mathf.Clamp(Mathf.RoundToInt((1f - value.y) * 100f), 4, 45);
            });
            editorPauseHandle = EditorBox("暂停按钮", new Color(.95f, .46f, .18f, 1f), new Vector2(110, 104), value => {
                draftSettings.GameplayPauseX = Mathf.Clamp(Percent(value.x), 5, 95);
                draftSettings.GameplayPauseY = Mathf.Clamp(Percent(value.y), 8, 95);
            }, delta => draftSettings.GameplayPauseScale = Mathf.Clamp(
                draftSettings.GameplayPauseScale + Mathf.RoundToInt(delta), 50, 180));
            editorHudHandle = EditorBox("得分与判定详情", cyan, new Vector2(350, 160), value => {
                draftSettings.GameplayHudX = Percent(value.x); draftSettings.GameplayHudY = Percent(value.y);
            }, delta => draftSettings.GameplayScoreScale = Mathf.Clamp(
                draftSettings.GameplayScoreScale + Mathf.RoundToInt(delta), 50, 180));
            editorComboHandle = EditorBox("COMBO", new Color(1f, .72f, .12f, 1f), new Vector2(320, 180), value => {
                draftSettings.GameplayComboX = Percent(value.x); draftSettings.GameplayComboY = Percent(value.y);
            }, delta => draftSettings.GameplayComboScale = Mathf.Clamp(
                draftSettings.GameplayComboScale + Mathf.RoundToInt(delta), 50, 180));
            editorJudgementHandle = EditorBox("当前判定", new Color(.42f, 1f, .54f), new Vector2(430, 100), value => {
                draftSettings.GameplayJudgementX = Percent(value.x, 5, 95);
                draftSettings.GameplayJudgementY = Percent(value.y, 5, 95);
            }, delta => draftSettings.GameplayJudgementScale = Mathf.Clamp(
                draftSettings.GameplayJudgementScale + Mathf.RoundToInt(delta), 50, 180));
            editorSongInfoHandle = EditorBox("歌曲信息", new Color(.92f, .36f, 1f, 1f), new Vector2(460, 132), value => {
                draftSettings.GameplaySongInfoX = Percent(value.x); draftSettings.GameplaySongInfoY = Percent(value.y);
            }, delta => draftSettings.GameplaySongInfoScale = Mathf.Clamp(
                draftSettings.GameplaySongInfoScale + Mathf.RoundToInt(delta), 50, 180));

            Button menuButton = ButtonText("≡", layoutEditorLayer, new Vector2(.92f, .89f), new Vector2(.92f, .89f),
                new Color(.02f, .36f, .44f, .98f), ToggleLayoutEditorMenu);
            layoutEditorFloatingButton = (RectTransform)menuButton.transform;
            layoutEditorFloatingButton.sizeDelta = new Vector2(76, 76);
            layoutEditorFloatingButton.pivot = new Vector2(.5f, .5f);
            UiDragHandle menuDrag = layoutEditorFloatingButton.gameObject.AddComponent<UiDragHandle>();
            menuDrag.Changed = PositionLayoutEditorFloatingButton;

            layoutEditorMenu = Panel("Editor menu", layoutEditorFloatingButton, new Color(.012f, .028f, .045f, .98f),
                new Vector2(.5f, .5f), new Vector2(.5f, .5f));
            layoutEditorMenu.sizeDelta = new Vector2(410, 650);
            Label("可视化布局", layoutEditorMenu, 28, TextAnchor.MiddleLeft,
                new Vector2(.07f, .89f), new Vector2(.93f, .97f), Color.white);
            Label("描边框可移动；角点可调大小；橙色调下端宽度，紫色调上端宽度，灰线调遮罩。",
                layoutEditorMenu, 16, TextAnchor.UpperLeft, new Vector2(.07f, .78f), new Vector2(.93f, .89f), muted);
            editorBackgroundBrightnessSlider = LayoutEditorSliderRow("背景亮度", "%", 20, 100,
                settings.BackgroundBrightness, .68f, value => {
                    if (draftSettings != null) draftSettings.BackgroundBrightness = Mathf.Clamp(value, 20, 100);
                }, value => Mathf.RoundToInt(value) + "%", out editorBackgroundBrightnessValue);
            editorTouchpadOpacitySlider = LayoutEditorSliderRow("触控板透明度", "%", 0, 100,
                settings.TouchpadOpacity, .59f, value => {
                    if (draftSettings != null) draftSettings.TouchpadOpacity = Mathf.Clamp(value, 0, 100);
                }, value => Mathf.RoundToInt(value) + "%", out editorTouchpadOpacityValue);
            editorTouchRangeSlider = LayoutEditorSliderRow("触控范围", "格", 5, 30,
                settings.TouchContactRangeTenths, .50f, value => {
                    if (draftSettings != null) draftSettings.TouchContactRangeTenths = Mathf.Clamp(value, 5, 30);
                }, value => (Mathf.RoundToInt(value) / 10f).ToString("0.0", CultureInfo.InvariantCulture) + " 格",
                out editorTouchRangeValue);
            editorTouchpadTiltSlider = LayoutEditorSliderRow("触控板斜度", "°", -20, 20,
                settings.TouchpadTilt, .41f, value => {
                    if (draftSettings != null) draftSettings.TouchpadTilt = Mathf.Clamp(Mathf.RoundToInt(value), -20, 20);
                }, value => Mathf.RoundToInt(value) + "°", out editorTouchpadTiltValue);
            editorNoteSpeedSlider = LayoutEditorSliderRow("流速", "", 0, OriginalNoteSpeed.Count - 1,
                settings.NoteSpeedOption, .32f, value => {
                    if (draftSettings != null) draftSettings.NoteSpeedOption = OriginalNoteSpeed.ClampOption(value);
                }, value => OriginalNoteSpeed.Label(Mathf.RoundToInt(value)), out editorNoteSpeedValue);
            ButtonText("保存外观", layoutEditorMenu, new Vector2(.07f, .27f), new Vector2(.93f, .37f), cyan, SaveLayoutDraft);
            ButtonText("恢复默认布局", layoutEditorMenu, new Vector2(.07f, .14f), new Vector2(.93f, .24f), surfaceRaised, ResetLayoutDraft);
            ButtonText("取消并返回", layoutEditorMenu, new Vector2(.07f, .02f), new Vector2(.93f, .11f), surfaceRaised, CloseLayoutEditor);
            layoutEditorMenu.gameObject.SetActive(true);
            layoutEditorLayer.gameObject.SetActive(false);
        }

        private RectTransform EditorBoundary(string label, Color color, Action<Vector2> changed)
        {
            RectTransform handle = Panel(label, layoutEditorPreview, color, new Vector2(0, .25f), new Vector2(1, .265f));
            handle.offsetMin = Vector2.zero; handle.offsetMax = Vector2.zero;
            var outline = handle.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(color.r, color.g, color.b, 1f); outline.effectDistance = new Vector2(0, 2);
            Label(label, handle, 15, TextAnchor.MiddleLeft, new Vector2(.02f, -2.2f), new Vector2(.34f, -.15f), outline.effectColor).raycastTarget = false;
            UiDragHandle drag = handle.gameObject.AddComponent<UiDragHandle>(); drag.LockX = true;
            drag.Changed = value => { changed(value); RefreshLayoutEditor(); };
            EditorGesturePoint(handle, "↕", outline.effectColor, new Vector2(.5f, .5f), new Vector2(38, 38));
            return handle;
        }

        private RectTransform EditorVerticalBoundary(string label, Color color, Action<Vector2> changed,
            float bottom = 0f, float top = 1f)
        {
            RectTransform handle = Panel(label, layoutEditorPreview, color,
                new Vector2(.78f, bottom), new Vector2(.79f, top));
            handle.offsetMin = Vector2.zero; handle.offsetMax = Vector2.zero;
            var outline = handle.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(color.r, color.g, color.b, 1f);
            outline.effectDistance = new Vector2(2, 0);
            Text handleLabel = Label(label, handle, 15, TextAnchor.MiddleRight,
                new Vector2(.5f, .93f), new Vector2(.5f, .93f), outline.effectColor);
            handleLabel.rectTransform.pivot = new Vector2(1, .5f);
            handleLabel.rectTransform.sizeDelta = new Vector2(220, 38);
            handleLabel.rectTransform.anchoredPosition = new Vector2(-10, 0);
            handleLabel.raycastTarget = false;
            UiDragHandle drag = handle.gameObject.AddComponent<UiDragHandle>(); drag.LockY = true;
            drag.Changed = value => { changed(value); RefreshLayoutEditor(); };
            EditorGesturePoint(handle, "↔", outline.effectColor, new Vector2(.5f, .50f), new Vector2(42, 42));
            return handle;
        }

        private RectTransform EditorBox(string label, Color color, Vector2 size, Action<Vector2> changed,
            Action<float> resized = null)
        {
            RectTransform handle = Panel(label, layoutEditorPreview, new Color(color.r, color.g, color.b, .075f), new Vector2(.5f, .5f), new Vector2(.5f, .5f));
            handle.sizeDelta = size; handle.pivot = new Vector2(.5f, .5f);
            var outline = handle.gameObject.AddComponent<Outline>(); outline.effectColor = color; outline.effectDistance = new Vector2(2, -2);
            Label(label, handle, 17, TextAnchor.UpperCenter, new Vector2(0, 1.02f), new Vector2(1, 1.24f), color).raycastTarget = false;
            UiDragHandle drag = handle.gameObject.AddComponent<UiDragHandle>();
            drag.Changed = value => { changed(value); RefreshLayoutEditor(); };
            EditorGesturePoint(handle, "✥", color, new Vector2(.5f, .5f), new Vector2(42, 42));
            if (resized != null) {
                RectTransform resize = EditorGesturePoint(handle, "↘", color, new Vector2(1f, 0f), new Vector2(48, 48));
                resize.pivot = new Vector2(.5f, .5f);
                resize.anchoredPosition = new Vector2(-4, 4);
                UiResizeHandle resizeDrag = resize.gameObject.AddComponent<UiResizeHandle>();
                resizeDrag.Changed = delta => { resized(delta); RefreshLayoutEditor(); };
            }
            return handle;
        }

        private RectTransform EditorGesturePoint(Transform parent, string symbol, Color color, Vector2 anchor, Vector2 size)
        {
            RectTransform point = Panel("Gesture point", parent, new Color(.01f, .04f, .06f, .94f), anchor, anchor);
            point.anchorMin = point.anchorMax = anchor;
            point.sizeDelta = size;
            point.pivot = new Vector2(.5f, .5f);
            point.anchoredPosition = Vector2.zero;
            Outline outline = point.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(2, -2);
            Text icon = Label(symbol, point, 22, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, color);
            icon.raycastTarget = false;
            return point;
        }

        private Slider LayoutEditorSliderRow(string title, string suffix, float minimum, float maximum,
            float current, float bottom, Action<int> changed, Func<float, string> formatter, out Text valueLabel)
        {
            RectTransform row = Panel(title + " editor setting", layoutEditorMenu, surfaceRaised,
                new Vector2(.07f, bottom), new Vector2(.93f, bottom + .075f));
            Label(title, row, 17, TextAnchor.MiddleLeft, new Vector2(.035f, .16f),
                new Vector2(.43f, .84f), Color.white);
            Text displayLabel = Label(formatter != null ? formatter(current) : Mathf.RoundToInt(current) + suffix,
                row, 15, TextAnchor.MiddleRight, new Vector2(.43f, .16f), new Vector2(.59f, .84f), cyan);
            valueLabel = displayLabel;
            RectTransform track = Panel("Track", row, new Color(.09f, .14f, .18f, 1f),
                new Vector2(.61f, .25f), new Vector2(.965f, .67f));
            track.offsetMin = new Vector2(2, 3);
            track.offsetMax = new Vector2(-2, -3);
            Slider slider = track.gameObject.AddComponent<Slider>();
            slider.minValue = minimum;
            slider.maxValue = maximum;
            slider.wholeNumbers = true;
            RectTransform fillArea = RectObject("Fill Area", track, new Vector2(.025f, .18f), new Vector2(.975f, .82f));
            RectTransform fill = Panel("Fill", fillArea, cyan, Vector2.zero, Vector2.one);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().raycastTarget = false;
            RectTransform handleArea = RectObject("Handle Area", track, new Vector2(.025f, 0), new Vector2(.975f, 1));
            RectTransform handle = Panel("Handle", handleArea, Color.white, new Vector2(0, .5f), new Vector2(0, .5f));
            handle.sizeDelta = new Vector2(22, 22);
            handle.pivot = new Vector2(.5f, .5f);
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.SetValueWithoutNotify(Mathf.Clamp(current, minimum, maximum));
            slider.onValueChanged.AddListener(value => {
                int rounded = Mathf.Clamp(Mathf.RoundToInt(value), Mathf.RoundToInt(minimum), Mathf.RoundToInt(maximum));
                if (displayLabel != null) displayLabel.text = formatter != null ? formatter(rounded) : rounded + suffix;
                changed?.Invoke(rounded);
                RefreshLayoutEditor();
            });
            return slider;
        }

        private void OpenLayoutEditor()
        {
            draftSettings = CloneSettings(settings);
            SetPageImmediate(pages[currentPage], false);
            SetPageImmediate(pages[AppPage.Gameplay], true);
            SetGameplayScene(true);
            gameplayBackdrop?.Configure(settings.BackgroundBrightness / 100f, settings.JacketBackgroundOpacity / 100f);
            gameplayBackdrop?.ShowJacket(selectedSong?.JacketPath);
            gameplayPausePanel.gameObject.SetActive(false);
            gameplayScore.text = "1007500";
            gameplayJudgements.text = "JC 1120   J 12   AT 3   MISS 1";
            gameplayCombo.text = "128\nCOMBO";
            ShowGameplayJudgement(ChunithmMobile.Chart.Judgement.JusticeCritical, 12.3, true);
            SetGameplaySongInfo(selectedSong, selectedChart, true);
            PositionLayoutEditorFloatingButton(new Vector2(.94f, .92f));
            layoutEditorMenu.gameObject.SetActive(true);
            RefreshLayoutEditor();
            layoutEditorLayer.gameObject.SetActive(true);
        }

        private void RefreshLayoutEditor()
        {
            if (draftSettings == null || layoutEditorPreview == null) return;
            SetLayoutEditorSliderValue(editorBackgroundBrightnessSlider, editorBackgroundBrightnessValue,
                draftSettings.BackgroundBrightness, value => Mathf.RoundToInt(value) + "%");
            SetLayoutEditorSliderValue(editorTouchpadOpacitySlider, editorTouchpadOpacityValue,
                draftSettings.TouchpadOpacity, value => Mathf.RoundToInt(value) + "%");
            SetLayoutEditorSliderValue(editorTouchRangeSlider, editorTouchRangeValue,
                draftSettings.TouchContactRangeTenths,
                value => (Mathf.RoundToInt(value) / 10f).ToString("0.0", CultureInfo.InvariantCulture) + " 格");
            SetLayoutEditorSliderValue(editorTouchpadTiltSlider, editorTouchpadTiltValue,
                draftSettings.TouchpadTilt, value => Mathf.RoundToInt(value) + "°");
            SetLayoutEditorSliderValue(editorNoteSpeedSlider, editorNoteSpeedValue,
                draftSettings.NoteSpeedOption, value => OriginalNoteSpeed.Label(Mathf.RoundToInt(value)));
            PositionEditorBox(editorPauseHandle, draftSettings.GameplayPauseX, draftSettings.GameplayPauseY);
            PositionEditorBox(editorHudHandle, draftSettings.GameplayHudX, draftSettings.GameplayHudY);
            PositionEditorBox(editorComboHandle, draftSettings.GameplayComboX, draftSettings.GameplayComboY);
            PositionEditorBox(editorJudgementHandle, draftSettings.GameplayJudgementX, draftSettings.GameplayJudgementY);
            PositionEditorBox(editorSongInfoHandle, draftSettings.GameplaySongInfoX, draftSettings.GameplaySongInfoY);
            PositionEditorBoundary(editorTouchpadHandle, draftSettings.TouchpadHeight);
            PositionEditorTouchWidth(editorTouchpadWidthHandle, draftSettings.TouchpadWidth);
            PositionEditorBoundary(editorAirTopHandle, draftSettings.AirAreaTop);
            PositionEditorBoundary(editorTrackHandle, 50 + draftSettings.TrackVerticalOffset);
            PositionEditorTrackWidth(editorTrackWidthHandle, draftSettings.TrackWidth, 60, 140, 0f, .38f);
            PositionEditorTrackWidth(editorTrackTopWidthHandle, draftSettings.TrackTopWidth, 45, 160, .62f, 1f);
            PositionEditorBoundary(editorTrackMaskHandle, 100 - draftSettings.TrackMaskHeight);
            if (editorTrackMaskHandle != null)
                editorTrackMaskHandle.gameObject.SetActive(draftSettings.TrackMaskEnabled);
            ApplyGameplayElementLayout(draftSettings);
            ResizeEditorBox(editorHudHandle, new Vector2(350, 160), draftSettings.GameplayScoreScale);
            ResizeEditorBox(editorComboHandle, new Vector2(320, 180), draftSettings.GameplayComboScale);
            ResizeEditorBox(editorJudgementHandle, new Vector2(430, 100), draftSettings.GameplayJudgementScale);
            ResizeEditorBox(editorSongInfoHandle, new Vector2(460, 132), draftSettings.GameplaySongInfoScale);
            if (gameplaySongInfoBlock != null) gameplaySongInfoBlock.gameObject.SetActive(true);
            if (gameplayTouchpad != null) {
                float width = draftSettings.TouchpadWidth / 100f;
                gameplayTouchpad.anchorMin = new Vector2((1f - width) * .5f, 0);
                gameplayTouchpad.anchorMax = new Vector2((1f + width) * .5f, draftSettings.TouchpadHeight / 100f);
                gameplayTouchpad.offsetMin = Vector2.zero; gameplayTouchpad.offsetMax = Vector2.zero;
                gameplayTouchpad.localRotation = Quaternion.Euler(0f, 0f, -draftSettings.TouchpadTilt);
                Image touchpadImage = gameplayTouchpad.GetComponent<Image>();
                if (touchpadImage != null)
                    touchpadImage.color = new Color(.015f, .08f, .105f, .82f * draftSettings.TouchpadOpacity / 100f);
            }
            float editorGroundHeight = Mathf.Clamp(draftSettings.GroundAreaHeight / 100f, .08f, .45f);
            inputRig?.Ground?.SetPlayArea(new Rect(0, 0, 1, editorGroundHeight));
            inputRig?.Ground?.SetTouchpadWidth(draftSettings.TouchpadWidth / 100f);
            inputRig?.Ground?.SetContactRangeLanes(draftSettings.TouchContactRangeTenths / 10f);
            float editorTouchOpacity = draftSettings.TouchpadOpacity / 100f;
            foreach (Image lane in gameplayLaneHighlights)
                if (lane != null) lane.color = new Color(.02f, .15f, .19f, .72f * editorTouchOpacity);
            gameplayTouchContacts?.Configure(draftSettings.ShowTouchFeedback, editorGroundHeight,
                draftSettings.TouchEffectOpacity / 100f);
            UpdateAirZone(gameplayAirZone, draftSettings.TouchpadHeight / 100f, draftSettings.AirAreaTop / 100f);
            PositionHorizontalLine(gameplayAirBoundaryBottom, draftSettings.TouchpadHeight / 100f);
            PositionHorizontalLine(gameplayAirBoundaryTop, draftSettings.AirAreaTop / 100f);
            if (gameplaySession != null) {
                gameplaySession.transform.localPosition = new Vector3(0, draftSettings.TrackVerticalOffset * .04f, 0);
                gameplaySession.transform.localScale = new Vector3(draftSettings.TrackWidth / 100f, 1f, 1f);
            }
            gameplayPlayfield?.ConfigureTopWidth(draftSettings.TrackTopWidth);
            gameplayTrackMask?.Configure(draftSettings.TrackMaskEnabled,
                draftSettings.TrackMaskHeight, draftSettings.TrackMaskOpacity);
            if (gameplayEffects != null)
                gameplayEffects.transform.localScale = new Vector3(draftSettings.TrackWidth / 100f, 1f, 1f);
            gameplayBackdrop?.Configure(draftSettings.BackgroundBrightness / 100f,
                draftSettings.JacketBackgroundOpacity / 100f);
            if (gameplayChartRenderer != null)
                gameplayChartRenderer.VisualSpeed = OriginalNoteSpeed.VisualMultiplier(draftSettings.NoteSpeedOption);
            ResizeEditorBox(editorPauseHandle, new Vector2(110, 104), draftSettings.GameplayPauseScale);
            UpdateEditorHandleLabel(editorHudHandle, $"得分与判定详情  {draftSettings.GameplayHudX}% / {draftSettings.GameplayHudY}%");
            UpdateEditorHandleLabel(editorComboHandle, $"COMBO  {draftSettings.GameplayComboX}% / {draftSettings.GameplayComboY}%");
            UpdateEditorHandleLabel(editorJudgementHandle, $"当前判定  {draftSettings.GameplayJudgementX}% / {draftSettings.GameplayJudgementY}%");
            UpdateEditorHandleLabel(editorSongInfoHandle, $"歌曲信息  {draftSettings.GameplaySongInfoX}% / {draftSettings.GameplaySongInfoY}%");
            UpdateEditorHandleLabel(editorTouchpadHandle, "触控板 / AIR 分界  " + draftSettings.TouchpadHeight + "%");
            UpdateEditorHandleLabel(editorTouchpadWidthHandle, "触控板横向宽度  " + draftSettings.TouchpadWidth + "%");
            UpdateEditorHandleLabel(editorAirTopHandle, "AIR 区域上边界  " + draftSettings.AirAreaTop + "%");
            UpdateEditorHandleLabel(editorTrackHandle, "轨道整体上下位置  " + draftSettings.TrackVerticalOffset + "%");
            UpdateEditorHandleLabel(editorTrackWidthHandle, "轨道下端宽度  " + draftSettings.TrackWidth + "%");
            UpdateEditorHandleLabel(editorTrackTopWidthHandle, "轨道上端宽度  " + draftSettings.TrackTopWidth + "%");
            UpdateEditorHandleLabel(editorTrackMaskHandle, "遮罩高度  " + draftSettings.TrackMaskHeight + "%");
            UpdateEditorHandleLabel(editorPauseHandle, $"暂停按钮  {draftSettings.GameplayPauseX}% / {draftSettings.GameplayPauseY}%");
        }

        private static void SetLayoutEditorSliderValue(Slider slider, Text valueLabel, float value,
            Func<float, string> formatter)
        {
            if (slider == null) return;
            slider.SetValueWithoutNotify(value);
            if (valueLabel != null) valueLabel.text = formatter != null ? formatter(value) : Mathf.RoundToInt(value).ToString();
        }

        private static void UpdateEditorHandleLabel(RectTransform handle, string value)
        {
            Text text = handle?.GetComponentInChildren<Text>();
            if (text != null) text.text = value;
        }

        private void ToggleLayoutEditorMenu()
        {
            if (layoutEditorMenu != null) layoutEditorMenu.gameObject.SetActive(!layoutEditorMenu.gameObject.activeSelf);
        }

        private void PositionLayoutEditorFloatingButton(Vector2 value)
        {
            if (layoutEditorFloatingButton == null) return;
            value = new Vector2(Mathf.Clamp(value.x, LayoutEditorSafeInset, 1f - LayoutEditorSafeInset),
                Mathf.Clamp(value.y, LayoutEditorSafeInset, 1f - LayoutEditorSafeInset));
            layoutEditorFloatingButton.anchorMin = layoutEditorFloatingButton.anchorMax = value;
            layoutEditorFloatingButton.anchoredPosition = Vector2.zero;
            if (layoutEditorMenu == null) return;
            bool right = value.x >= .5f, top = value.y >= .5f;
            layoutEditorMenu.pivot = new Vector2(right ? 1 : 0, top ? 1 : 0);
            layoutEditorMenu.anchoredPosition = new Vector2(right ? 36 : -36, top ? -36 : 36);
        }

        private static void PositionEditorBox(RectTransform rect, int x, int y)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(
                Mathf.Clamp(x / 100f, LayoutEditorSafeInset, 1f - LayoutEditorSafeInset),
                Mathf.Clamp(y / 100f, LayoutEditorSafeInset, 1f - LayoutEditorSafeInset));
            rect.anchoredPosition = Vector2.zero;
        }

        private static void ResizeEditorBox(RectTransform rect, Vector2 baseSize, int scalePercent)
        {
            if (rect == null) return;
            rect.sizeDelta = baseSize * (Mathf.Clamp(scalePercent, 50, 180) / 100f);
        }

        private static void PositionEditorBoundary(RectTransform rect, int y)
        {
            if (rect == null) return;
            float value = y / 100f;
            rect.anchorMin = new Vector2(LayoutEditorSafeInset, value - .0075f);
            rect.anchorMax = new Vector2(1f - LayoutEditorSafeInset, value + .0075f);
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        }

        private static void PositionEditorTrackWidth(RectTransform rect, int width, int minimum, int maximum,
            float bottom, float top)
        {
            if (rect == null) return;
            float value = Mathf.Clamp(.5f + Mathf.Clamp(width, minimum, maximum) / 100f * .28f,
                LayoutEditorSafeInset, 1f - LayoutEditorSafeInset);
            rect.anchorMin = new Vector2(value - .004f, bottom);
            rect.anchorMax = new Vector2(value + .004f, top);
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        }

        private static void PositionEditorTouchWidth(RectTransform rect, int width)
        {
            if (rect == null) return;
            float right = Mathf.Clamp(.5f + Mathf.Clamp(width, 60, 100) / 200f,
                LayoutEditorSafeInset, 1f - LayoutEditorSafeInset);
            rect.anchorMin = new Vector2(right - .004f, .08f);
            rect.anchorMax = new Vector2(right + .004f, .45f);
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        }

        private void SaveLayoutDraft()
        {
            if (draftSettings == null) return;
            settings = CloneSettings(draftSettings);
            settings.Save();
            CloseLayoutEditorView();
            draftSettings = null;
            RenderSettingsCategory(false);
        }

        private void ResetLayoutDraft()
        {
            if (draftSettings == null) return;
            AppSettings defaults = new AppSettings();
            draftSettings.TouchpadHeight = defaults.TouchpadHeight;
            draftSettings.TouchpadWidth = defaults.TouchpadWidth;
            draftSettings.GroundAreaHeight = defaults.GroundAreaHeight;
            draftSettings.AirAreaBottom = defaults.AirAreaBottom;
            draftSettings.AirAreaTop = defaults.AirAreaTop;
            draftSettings.TrackVerticalOffset = defaults.TrackVerticalOffset;
            draftSettings.TrackWidth = defaults.TrackWidth;
            draftSettings.TrackTopWidth = defaults.TrackTopWidth;
            draftSettings.TrackMaskEnabled = defaults.TrackMaskEnabled;
            draftSettings.TrackMaskHeight = defaults.TrackMaskHeight;
            draftSettings.TrackMaskOpacity = defaults.TrackMaskOpacity;
            draftSettings.GameplayHudX = defaults.GameplayHudX;
            draftSettings.GameplayHudY = defaults.GameplayHudY;
            draftSettings.GameplayScoreScale = defaults.GameplayScoreScale;
            draftSettings.GameplayComboX = defaults.GameplayComboX;
            draftSettings.GameplayComboY = defaults.GameplayComboY;
            draftSettings.GameplayComboScale = defaults.GameplayComboScale;
            draftSettings.GameplayJudgementX = defaults.GameplayJudgementX;
            draftSettings.GameplayJudgementY = defaults.GameplayJudgementY;
            draftSettings.GameplayJudgementScale = defaults.GameplayJudgementScale;
            draftSettings.GameplaySongInfoX = defaults.GameplaySongInfoX;
            draftSettings.GameplaySongInfoY = defaults.GameplaySongInfoY;
            draftSettings.GameplaySongInfoScale = defaults.GameplaySongInfoScale;
            draftSettings.GameplayPauseX = defaults.GameplayPauseX;
            draftSettings.GameplayPauseY = defaults.GameplayPauseY;
            draftSettings.GameplayPauseScale = defaults.GameplayPauseScale;
            draftSettings.BackgroundBrightness = defaults.BackgroundBrightness;
            draftSettings.TouchpadOpacity = defaults.TouchpadOpacity;
            draftSettings.TouchContactRangeTenths = defaults.TouchContactRangeTenths;
            draftSettings.TouchpadTilt = defaults.TouchpadTilt;
            draftSettings.NoteSpeedOption = defaults.NoteSpeedOption;
            RefreshLayoutEditor();
        }

        private void CloseLayoutEditor()
        {
            if (layoutEditorLayer == null || !layoutEditorLayer.gameObject.activeSelf) return;
            bool changed = draftSettings != null && JsonUtility.ToJson(draftSettings) != JsonUtility.ToJson(settings);
            if (changed) ShowModal("放弃未保存修改？", "当前可视化布局尚未保存。", DiscardLayoutDraft, true);
            else DiscardLayoutDraft();
        }

        private void DiscardLayoutDraft()
        {
            CloseLayoutEditorView();
            draftSettings = null;
        }

        private void CloseLayoutEditorView()
        {
            if (layoutEditorLayer != null) layoutEditorLayer.gameObject.SetActive(false);
            if (layoutEditorMenu != null) layoutEditorMenu.gameObject.SetActive(false);
            ClearGameplayVisuals();
            SetGameplayScene(false);
            SetPageImmediate(pages[AppPage.Gameplay], false);
            SetPageImmediate(pages[currentPage], true);
            ApplyRuntimeSettings();
        }

        private static AppSettings CloneSettings(AppSettings source) =>
            JsonUtility.FromJson<AppSettings>(JsonUtility.ToJson(source)) ?? new AppSettings();

        private static int Percent(float value, int minimum = 0, int maximum = 100) =>
            Mathf.Clamp(Mathf.RoundToInt(value * 100f), minimum, maximum);

        private void BuildGameplay()
        {
            RectTransform page = Page(AppPage.Gameplay, Color.clear);
            page.GetComponent<Image>().raycastTarget = false;
            RectTransform progressTrack = RectObject("Gameplay progress track", page,
                new Vector2(0f, .985f), Vector2.one);
            gameplayProgressTrack = progressTrack.gameObject.AddComponent<Image>();
            gameplayProgressTrack.color = new Color(.015f, .035f, .055f, .96f);
            gameplayProgressTrack.raycastTarget = false;
            RectTransform progressFill = RectObject("Gameplay progress", progressTrack,
                Vector2.zero, new Vector2(0f, 1f));
            gameplayProgressFill = progressFill.gameObject.AddComponent<Image>();
            gameplayProgressFill.color = GameplayAccent;
            gameplayProgressFill.raycastTarget = false;
            gameplayFpsLabel = Label("FPS 0", page, 18, TextAnchor.LowerLeft,
                new Vector2(.018f, .035f), new Vector2(.16f, .085f), new Color(1f, 1f, 1f, .58f));
            AddReadableOutline(gameplayFpsLabel, .42f);
            gameplayFpsLabel.gameObject.SetActive(false);
            float initialTouchpadWidth = settings.TouchpadWidth / 100f;
            gameplayTouchpad = Panel("Touchpad", page, new Color(.015f, .08f, .105f, .82f),
                new Vector2((1f - initialTouchpadWidth) * .5f, 0),
                new Vector2((1f + initialTouchpadWidth) * .5f, settings.TouchpadHeight / 100f));
            gameplayTouchpad.offsetMin = Vector2.zero; gameplayTouchpad.offsetMax = Vector2.zero;
            gameplayTouchpad.localRotation = Quaternion.Euler(0f, 0f, -settings.TouchpadTilt);
            gameplayTouchpad.GetComponent<Image>().raycastTarget = false;
            gameplayLaneHighlights.Clear();
            for (int lane = 0; lane < 16; lane++) {
                RectTransform cell = Panel("Touch lane " + (lane + 1), gameplayTouchpad,
                    new Color(.02f, .15f, .19f, .72f), new Vector2(lane / 16f, .04f),
                    new Vector2((lane + 1f) / 16f, .96f));
                cell.offsetMin = new Vector2(1, 1); cell.offsetMax = new Vector2(-1, -1);
                Image image = cell.GetComponent<Image>(); image.raycastTarget = false;
                gameplayLaneHighlights.Add(image);
            }
            gameplayAirZone = Panel("AIR input zone", page, new Color(.05f, .34f, .42f, .10f),
                new Vector2(0, settings.TouchpadHeight / 100f), new Vector2(1, settings.AirAreaTop / 100f));
            gameplayAirZone.offsetMin = Vector2.zero; gameplayAirZone.offsetMax = Vector2.zero;
            gameplayAirZone.GetComponent<Image>().raycastTarget = false;
            gameplayAirBoundaryBottom = Panel("AIR lower judgement boundary", page, new Color(.16f, .92f, 1f, .88f),
                new Vector2(0, .278f), new Vector2(1, .282f)).GetComponent<Image>();
            gameplayAirBoundaryBottom.raycastTarget = false;
            gameplayAirBoundaryTop = Panel("AIR upper judgement boundary", page, new Color(.64f, .70f, 1f, .72f),
                new Vector2(0, .958f), new Vector2(1, .962f)).GetComponent<Image>();
            gameplayAirBoundaryTop.raycastTarget = false;
            gameplayAirGuide = Panel("AIR average guide", page, new Color(.35f, .92f, 1f, .94f),
                new Vector2(0, .5f), new Vector2(1, .505f)).GetComponent<Image>();
            gameplayAirGuide.raycastTarget = false;
            gameplayAirGuide.gameObject.SetActive(false);
            RectTransform trackMask = RectObject("Track top mask", page, Vector2.zero, Vector2.one);
            gameplayTrackMask = trackMask.gameObject.AddComponent<TrackMaskOverlay>();
            RectTransform touchContacts = RectObject("Touch contact feedback", page, Vector2.zero, Vector2.one);
            gameplayTouchContacts = touchContacts.gameObject.AddComponent<TouchContactOverlay>();
            RectTransform effectsLayer = RectObject("Judgement effects", page, Vector2.zero, Vector2.one);
            gameplayEffects = effectsLayer.gameObject.AddComponent<GameplayJudgementEffects>();
            gameplayPauseButton = ButtonText("Ⅱ", page, new Vector2(.018f, .88f), new Vector2(.075f, .975f),
                HighContrastUi ? UiThemeContrast.Yellow : new Color(.025f, .07f, .11f, .92f), RequestPauseGameplay);
            gameplayPauseButton.name = "Pause button";
            delayCalibrationExitButton = ButtonText("‹  返回设置", page, new Vector2(.018f, .88f), new Vector2(.145f, .975f),
                new Color(.025f, .16f, .20f, .94f), ExitDelayCalibration);
            delayCalibrationExitButton.gameObject.SetActive(false);
            delayCalibrationLabel = Label("延迟自测  ·  60 BPM  ·  第三拍判定", page, 22, TextAnchor.MiddleCenter,
                new Vector2(.30f, .90f), new Vector2(.70f, .975f), Color.white);
            AddReadableOutline(delayCalibrationLabel, .75f);
            delayCalibrationLabel.gameObject.SetActive(false);
            delayCalibrationLive = Label("TAP  --  0 ms  JUST", page, 26, TextAnchor.MiddleCenter,
                new Vector2(.30f, .22f), new Vector2(.70f, .30f), Color.white);
            AddReadableOutline(delayCalibrationLive, .74f);
            delayCalibrationLive.gameObject.SetActive(false);
            gameplayCombo = Label("", page, 58, TextAnchor.MiddleCenter, new Vector2(.38f, .40f), new Vector2(.62f, .62f), Color.white);
            gameplayHud = Panel("Gameplay HUD", page, Color.clear, new Vector2(.77f, .80f), new Vector2(.985f, .985f));
            gameplayHud.GetComponent<Image>().raycastTarget = false;
            RectTransform hud = gameplayHud;
            gameplayScore = Label("0000000", hud, 42, TextAnchor.MiddleCenter, new Vector2(.04f, .42f), new Vector2(.96f, .92f), Color.white);
            gameplayJudgements = Label("JC 0   J 0   AT 0   MISS 0", hud, 16, TextAnchor.MiddleCenter, new Vector2(.04f, .08f), new Vector2(.96f, .42f), muted);
            AddReadableOutline(gameplayScore, .78f);
            AddReadableOutline(gameplayJudgements, .82f);
            gameplayJudgementBlock = RectObject("Current judgement", page, Vector2.zero, Vector2.one);
            RectTransform judgementImageSlot = RectObject("Judgement image slot", gameplayJudgementBlock,
                new Vector2(0, .3f), Vector2.one);
            gameplayJudgementImage = RectObject("Judgement image", judgementImageSlot,
                Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            gameplayJudgementImage.raycastTarget = false;
            gameplayJudgementImage.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            gameplayTiming = Label("", gameplayJudgementBlock, 22, TextAnchor.MiddleCenter,
                Vector2.zero, new Vector2(1, .3f), Color.white);
            AddReadableOutline(gameplayTiming, .74f);
            RefreshJudgementSkin();
            gameplayJudgementBlock.gameObject.SetActive(false);
            gameplaySongInfoBlock = Panel("Song information", page, new Color(.008f, .025f, .040f, .82f),
                new Vector2(.025f, .72f), new Vector2(.31f, .86f));
            var infoOutline = gameplaySongInfoBlock.gameObject.AddComponent<Outline>();
            infoOutline.effectColor = new Color(.12f, .78f, .92f, .42f); infoOutline.effectDistance = new Vector2(1, -1);
            RectTransform jacketMask = gameplaySongInfoCoverMask = Panel("Jacket mask", gameplaySongInfoBlock, Color.white,
                new Vector2(0, .5f), new Vector2(0, .5f));
            jacketMask.pivot = new Vector2(.5f, .5f);
            jacketMask.sizeDelta = new Vector2(128, 128);
            jacketMask.anchoredPosition = new Vector2(80, 0);
            var mask = jacketMask.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = false;
            gameplaySongInfoCover = Panel("Jacket", jacketMask, new Color(.08f, .14f, .18f, 1f), Vector2.zero, Vector2.one).GetComponent<Image>();
            gameplaySongInfoCover.rectTransform.offsetMin = Vector2.zero; gameplaySongInfoCover.rectTransform.offsetMax = Vector2.zero;
            gameplaySongInfoCover.raycastTarget = false; gameplaySongInfoCover.preserveAspect = true;
            gameplaySongInfo = Label("", gameplaySongInfoBlock, 20, TextAnchor.MiddleLeft,
                new Vector2(0, .10f), new Vector2(1, .90f), Color.white);
            gameplaySongInfo.rectTransform.offsetMin = new Vector2(160, 0);
            gameplaySongInfo.rectTransform.offsetMax = new Vector2(-20, 0);
            gameplayCountdown = Label("", page, 96, TextAnchor.MiddleCenter, new Vector2(.38f, .36f), new Vector2(.62f, .66f), Color.white);

            gameplayPausePanel = Panel("Pause", page, new Color(.008f, .018f, .035f, .96f), new Vector2(.34f, .24f), new Vector2(.66f, .76f));
            Label("暂停", gameplayPausePanel, 42, TextAnchor.MiddleCenter, new Vector2(.08f, .72f), new Vector2(.92f, .92f), Color.white);
            ButtonText("继续游玩", gameplayPausePanel, new Vector2(.10f, .49f), new Vector2(.90f, .66f), cyan, ResumeGameplayWithCountdown);
            gameplayRestartButton = ButtonText("重新开始", gameplayPausePanel, new Vector2(.10f, .28f), new Vector2(.90f, .45f), surfaceRaised, RestartGameplay);
            gameplayExitButton = ButtonText("返回选曲", gameplayPausePanel, new Vector2(.10f, .07f), new Vector2(.90f, .24f), surfaceRaised, ExitGameplay);
            gameplayPausePanel.gameObject.SetActive(false);
            // Keep the thin progress line visible above the optional track mask
            // and every gameplay overlay.
            gameplayProgressTrack.transform.SetAsLastSibling();
        }

        private static void AddReadableOutline(Text text, float alpha)
        {
            if (text == null) return;
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, alpha);
            outline.effectDistance = new Vector2(2, -2);
        }

        private void SetGameplaySongInfo(SongEntry song, SongChartEntry chart, bool sampleIfMissing = false)
        {
            if (gameplaySongInfo == null || gameplaySongInfoCover == null) return;
            if (song == null) {
                gameplaySongInfo.text = sampleIfMissing ? "CHUNITHM Mobile\n布局预览\nMASTER  Lv.14+" : "";
                LocalCoverLoader.Reset(gameplaySongInfoCover);
                gameplaySongInfoCover.sprite = null;
                gameplaySongInfoCover.color = new Color(.08f, .14f, .18f, 1f);
                return;
            }
            gameplaySongInfo.text = song.Name + "\n" + song.Artist + "\n" +
                (chart?.Difficulty ?? "-") + "  Lv." + (chart?.Level ?? "-");
            LocalCoverLoader.Reset(gameplaySongInfoCover);
            gameplaySongInfoCover.sprite = null;
            gameplaySongInfoCover.color = new Color(.08f, .14f, .18f, 1f);
            if (!string.IsNullOrWhiteSpace(song.JacketPath))
                LocalCoverLoader.Load(this, song.JacketPath, gameplaySongInfoCover, true);
        }

        private void BuildResults()
        {
            RectTransform page = Page(AppPage.Results, ink);
            AddMeteorBackground(page, MeteorBackgroundProfile.Results);
            Label("TRACK RESULT", page, 32, TextAnchor.MiddleLeft, new Vector2(.05f, .90f), new Vector2(.45f, .98f), cyan);
            // Keep the result jacket in its original left-side slot. The slot
            // owns the layout; a separate child owns the 1:1 image so loading a
            // jacket can never resize or replace the result-page background.
            RectTransform cover = Panel("Cover slot", page, surfaceRaised, new Vector2(.07f, .35f), new Vector2(.34f, .80f));
            RectTransform coverImage = Panel("Square jacket", cover, Color.white, new Vector2(.025f, .025f), new Vector2(.975f, .975f));
            coverImage.offsetMin = Vector2.zero;
            coverImage.offsetMax = Vector2.zero;
            var coverAspect = coverImage.gameObject.AddComponent<AspectRatioFitter>();
            coverAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            coverAspect.aspectRatio = 1f;
            resultCover = coverImage.GetComponent<Image>();
            resultCoverPlaceholder = Label("♪", cover, 120, TextAnchor.MiddleCenter,
                new Vector2(.025f, .025f), new Vector2(.975f, .975f), Color.white).gameObject;
            resultSong = Label("歌曲", page, 32, TextAnchor.UpperLeft, new Vector2(.07f, .17f), new Vector2(.40f, .34f), Color.white);
            resultRank = Label("SSS+", page, 108, TextAnchor.MiddleCenter, new Vector2(.40f, .58f), new Vector2(.65f, .84f), cyan);
            resultScore = Label("1,009,000", page, 68, TextAnchor.MiddleCenter, new Vector2(.38f, .42f), new Vector2(.68f, .61f), Color.white);
            resultPreviousScore = Label("最好分数  0", page, 20, TextAnchor.MiddleCenter,
                new Vector2(.38f, .35f), new Vector2(.68f, .43f), muted);
            resultNewBadge = Label("NEW!!!", page, 32, TextAnchor.MiddleCenter,
                new Vector2(.61f, .72f), new Vector2(.72f, .82f), new Color(1f, .82f, .12f, 1f));
            resultNewBadge.rectTransform.localRotation = Quaternion.Euler(0, 0, -45f);
            resultNewBadge.gameObject.SetActive(false);
            resultJudgements = Label("JC 0   J 0   ATTACK 0   MISS 0", page, 25, TextAnchor.UpperLeft, new Vector2(.70f, .38f), new Vector2(.95f, .80f), Color.white);
            resultReplayButton = ButtonText("重新游玩", page, new Vector2(.43f, .09f), new Vector2(.64f, .20f), surfaceRaised, PrepareGameplay);
            resultContinueButton = ButtonText("返回选曲", page, new Vector2(.68f, .09f), new Vector2(.91f, .20f), cyan, ReturnToSongSelectFromResults);
        }

        private void BuildDelayCalibrationResults()
        {
            RectTransform page = Page(AppPage.DelayCalibrationResults, ink);
            Label("DELAY TEST", page, 32, TextAnchor.MiddleLeft,
                new Vector2(.07f, .86f), new Vector2(.50f, .96f), cyan);
            Label("延迟自测完成", page, 48, TextAnchor.MiddleCenter,
                new Vector2(.25f, .66f), new Vector2(.75f, .80f), Color.white);
            delayCalibrationResultSummary = Label("本轮没有采集到判定数据", page, 25, TextAnchor.MiddleCenter,
                new Vector2(.18f, .54f), new Vector2(.82f, .65f), muted);
            delayCalibrationResultStats = Label("", page, 28, TextAnchor.MiddleCenter,
                new Vector2(.18f, .30f), new Vector2(.82f, .53f), Color.white);
            ButtonText("重新测试", page, new Vector2(.22f, .10f), new Vector2(.47f, .22f), surfaceRaised,
                RestartDelayCalibration);
            ButtonText("返回设置", page, new Vector2(.53f, .10f), new Vector2(.78f, .22f), cyan,
                ExitDelayCalibrationResults);
        }

        private void BuildModal()
        {
            modalLayer = Panel("Modal", canvas.transform, new Color(0, 0, 0, .72f), Vector2.zero, Vector2.one);
            RectTransform card = Panel("Modal Card", modalLayer, surfaceRaised, new Vector2(.31f, .25f), new Vector2(.69f, .75f));
            modalTitle = Label("提示", card, 34, TextAnchor.MiddleLeft, new Vector2(.08f, .72f), new Vector2(.92f, .90f), Color.white);
            modalBody = Label("", card, 23, TextAnchor.UpperLeft, new Vector2(.08f, .28f), new Vector2(.92f, .70f), muted);
            ButtonText("取消", card, new Vector2(.06f, .08f), new Vector2(.28f, .23f), surface, HideModal);
            modalOptionA = ButtonText("选项 A", card, new Vector2(.30f, .08f), new Vector2(.51f, .23f), surface, null);
            modalOptionB = ButtonText("选项 B", card, new Vector2(.53f, .08f), new Vector2(.74f, .23f), surface, null);
            modalConfirm = ButtonText("确定", card, new Vector2(.76f, .08f), new Vector2(.94f, .23f), cyan, ConfirmModal);
            modalOptionA.gameObject.SetActive(false); modalOptionB.gameObject.SetActive(false);
            modalLayer.gameObject.SetActive(false);
        }

        private void BuildUpdateNotice()
        {
            updateNoticeLayer = Panel("Version update notice", canvas.transform, Color.clear, Vector2.zero, Vector2.one);
            updateNoticeLayer.offsetMin = Vector2.zero;
            updateNoticeLayer.offsetMax = Vector2.zero;
            Image layerImage = updateNoticeLayer.GetComponent<Image>();
            if (layerImage != null) layerImage.raycastTarget = true;

            RectTransform card = Panel("Version update card", updateNoticeLayer, surfaceRaised,
                new Vector2(.48f, .04f), new Vector2(.975f, .74f));
            Label(UiLocalization.Translate(UpdateLogVersions[0] + " 更新版本说明"), card, 27, TextAnchor.MiddleLeft,
                new Vector2(.07f, .77f), new Vector2(.93f, .94f), cyan);
            string prefix = UpdateLogVersions[0] + " ";
            string[] notes = UpdateLogLineKeys[0].Skip(1).Select(key => {
                string translated = UiLocalization.Translate(key);
                return translated.StartsWith(prefix, StringComparison.Ordinal) ? translated.Substring(prefix.Length) : translated;
            }).ToArray();
            for (int index = 0; index < notes.Length; index++)
            {
                float top = .70f - index * .068f;
                Text note = Label(notes[index], card, 16, TextAnchor.MiddleLeft,
                    new Vector2(.07f, top - .062f), new Vector2(.93f, top), Color.white);
                note.horizontalOverflow = HorizontalWrapMode.Wrap;
                note.verticalOverflow = VerticalWrapMode.Overflow;
            }
            updateNoticeLogsButton = ButtonText(UiLocalization.Translate("查看更新日志"), card,
                new Vector2(.37f, .08f), new Vector2(.64f, .24f), surface, OpenUpdateLogsFromNotice);
            updateNoticeConfirm = ButtonText("请稍候 2 秒", card,
                new Vector2(.67f, .08f), new Vector2(.93f, .24f), surface, AcknowledgeUpdateNotice);
            updateNoticeLogsButton.interactable = false;
            updateNoticeConfirm.interactable = false;
            updateNoticeLayer.gameObject.SetActive(false);
        }

        private void ShowUpdateNoticeIfNeeded()
        {
            const string updateKey = "chunithm_mobile_update_notice_version";
            if (PlayerPrefs.GetString(updateKey, "") == UpdateLogVersions[0] || updateNoticeLayer == null) return;
            updateNoticeLayer.gameObject.SetActive(true);
            CanvasGroup group = updateNoticeLayer.GetComponent<CanvasGroup>() ?? updateNoticeLayer.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            StartCoroutine(Fade(group, 0f, 1f, .18f));
            StartCoroutine(UnlockUpdateNotice());
        }

        private IEnumerator UnlockUpdateNotice()
        {
            yield return new WaitForSecondsRealtime(2f);
            if (updateNoticeConfirm == null) yield break;
            if (updateNoticeLogsButton != null) updateNoticeLogsButton.interactable = true;
            updateNoticeConfirm.interactable = true;
            Text label = updateNoticeConfirm.GetComponentInChildren<Text>();
            if (label != null) label.text = UiLocalization.Translate("我知道了");
        }

        private void AcknowledgeUpdateNotice()
        {
            PlayerPrefs.SetString("chunithm_mobile_update_notice_version", UpdateLogVersions[0]);
            PlayerPrefs.Save();
            if (updateNoticeLayer != null) updateNoticeLayer.gameObject.SetActive(false);
        }

        private void BuildMods()
        {
            RectTransform card = BuildModDialog("Mods", out modsLayer, out _, "MODS", HideMods);
            airLongNoMotionButton = ModOption(card, 0, .77f, true, () => {
                mods.AirLongNoMotion = !mods.AirLongNoMotion; RefreshMods();
            });
            mirrorChartButton = ModOption(card, 1, .77f, true, () => {
                mods.MirrorChart = !mods.MirrorChart; RefreshMods();
            });
            autoPlayButton = ModOption(card, 0, .60f, false, () => {
                mods.AutoPlay = !mods.AutoPlay; RefreshMods();
            });
            airAreaAutoButton = ModOption(card, 1, .60f, false, () => {
                mods.AirAreaAuto = !mods.AirAreaAuto; RefreshMods();
            });
            directionalAirAutoButton = ModOption(card, 0, .49f, false, () => {
                mods.DirectionalAirAuto = !mods.DirectionalAirAuto; RefreshMods();
            });
            mineImmunityButton = ModOption(card, 1, .49f, false, () => {
                mods.MineImmunity = !mods.MineImmunity; RefreshMods();
            });
            nonMissJusticeCriticalButton = ModOption(card, 0, .38f, false, () => {
                mods.NonMissJusticeCritical = !mods.NonMissJusticeCritical; RefreshMods();
            });
            float[] rates = { .8f, 1.2f, 1.5f };
            rateButtons.Clear();
            for (int i = 0; i < rates.Length; i++) {
                float rate = rates[i];
                Button button = ModOption(card, i, .21f, false,
                    () => { mods.MusicRate = Math.Abs(mods.MusicRate - rate) < .01f ? 1f : rate; RefreshMods(); });
                rateButtons.Add(button);
            }
            ButtonText("恢复默认", card, new Vector2(.05f, .045f), new Vector2(.27f, .135f), surface,
                ResetSinglePlayerMods);
            ButtonText("完成", card, new Vector2(.73f, .045f), new Vector2(.95f, .135f), cyan, HideMods);
            modsLayer.gameObject.SetActive(false);
        }

        private void BuildMetadataEditor()
        {
            metadataLayer = Panel("Metadata editor", canvas.transform, new Color(0, 0, 0, .76f), Vector2.zero, Vector2.one);
            metadataLayer.offsetMin = Vector2.zero;
            metadataLayer.offsetMax = Vector2.zero;
            RectTransform card = Panel("Metadata editor body", metadataLayer, surfaceRaised,
                new Vector2(.26f, .14f), new Vector2(.74f, .86f));
            Label("批量编辑歌曲信息", card, 34, TextAnchor.MiddleLeft,
                new Vector2(.07f, .84f), new Vector2(.93f, .95f), Color.white);
            metadataSelectionLabel = Label("已选择 0 首", card, 19, TextAnchor.MiddleLeft,
                new Vector2(.07f, .76f), new Vector2(.93f, .84f), muted);

            Label("统一标题", card, 19, TextAnchor.MiddleLeft, new Vector2(.07f, .66f), new Vector2(.28f, .74f), Color.white);
            metadataNameInput = Input("留空则不修改", card, new Vector2(.29f, .64f), new Vector2(.93f, .74f));
            Label("统一作者", card, 19, TextAnchor.MiddleLeft, new Vector2(.07f, .52f), new Vector2(.28f, .60f), Color.white);
            metadataArtistInput = Input("留空则不修改", card, new Vector2(.29f, .50f), new Vector2(.93f, .60f));
            Label("统一分类", card, 19, TextAnchor.MiddleLeft, new Vector2(.07f, .38f), new Vector2(.28f, .46f), Color.white);
            metadataCategoryInput = Input("留空则不修改", card, new Vector2(.29f, .36f), new Vector2(.93f, .46f));
            metadataHint = Label("仅填写需要统一调整的字段", card, 18, TextAnchor.MiddleLeft,
                new Vector2(.07f, .25f), new Vector2(.93f, .34f), muted);
            ButtonText("取消", card, new Vector2(.07f, .07f), new Vector2(.42f, .20f), surface, HideMetadataEditor);
            ButtonText("保存并重建索引", card, new Vector2(.45f, .07f), new Vector2(.93f, .20f), cyan, ApplyMetadataEdits);
            metadataLayer.gameObject.SetActive(false);
        }

        private void OpenMetadataEditor()
        {
            SongEntry[] selected = catalog.Where(song => managedSongRoots.Contains(song.Root)).ToArray();
            if (selected.Length == 0)
            {
                ShowModal("没有选择歌曲", "请先选择需要调整的歌曲。", null, false);
                return;
            }
            metadataSelectionLabel.text = selected.Length == 1
                ? "正在编辑：《" + selected[0].Name + "》"
                : $"已选择 {selected.Length} 首歌曲；填写内容将统一应用";
            metadataNameInput.text = "";
            metadataArtistInput.text = "";
            metadataCategoryInput.text = "";
            metadataHint.text = "仅填写需要统一调整的字段";
            metadataHint.color = muted;
            metadataLayer.gameObject.SetActive(true);
        }

        private void HideMetadataEditor()
        {
            if (metadataLayer != null) metadataLayer.gameObject.SetActive(false);
        }

        private void ApplyMetadataEdits()
        {
            string name = metadataNameInput.text;
            string artist = metadataArtistInput.text;
            string category = metadataCategoryInput.text;
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(category))
            {
                metadataHint.text = "至少填写一个需要修改的字段";
                metadataHint.color = new Color(1f, .43f, .38f);
                return;
            }

            try
            {
                int updated = SongCatalog.UpdateMetadata(managedSongRoots, name, artist, category);
                ReloadCatalogAfterDataChange();
                HideMetadataEditor();
                ShowModal("编辑完成", $"已更新 {updated} 首歌曲并重新建立索引。", null, false);
            }
            catch (Exception exception)
            {
                Debug.LogError("Song metadata update failed: " + exception);
                metadataHint.text = "保存失败：" + exception.Message;
                metadataHint.color = new Color(1f, .43f, .38f);
            }
        }

        private void BuildImportOverlay()
        {
            importLayer = Panel("Import progress", canvas.transform, new Color(0, 0, 0, .80f), Vector2.zero, Vector2.one);
            importLayer.offsetMin = Vector2.zero;
            importLayer.offsetMax = Vector2.zero;
            RectTransform card = Panel("Import progress body", importLayer, surfaceRaised,
                new Vector2(.18f, .10f), new Vector2(.82f, .90f));
            importTitle = Label("导入歌曲", card, 34, TextAnchor.MiddleLeft,
                new Vector2(.06f, .86f), new Vector2(.94f, .96f), Color.white);
            importStatus = Label("准备中", card, 22, TextAnchor.MiddleLeft,
                new Vector2(.06f, .77f), new Vector2(.31f, .86f), cyan);
            importEta = Label("预计剩余：计算中", card, 18, TextAnchor.MiddleLeft,
                new Vector2(.31f, .77f), new Vector2(.57f, .86f), muted);
            importCurrent = Label("", card, 19, TextAnchor.MiddleRight,
                new Vector2(.57f, .77f), new Vector2(.94f, .86f), muted);
            RectTransform track = Panel("Import progress track", card, new Color(.08f, .14f, .20f),
                new Vector2(.06f, .735f), new Vector2(.94f, .765f));
            importProgressFill = Panel("Import progress fill", track, cyan, Vector2.zero, new Vector2(0, 1)).GetComponent<Image>();

            Label("导入详情", card, 19, TextAnchor.MiddleLeft,
                new Vector2(.06f, .65f), new Vector2(.94f, .72f), Color.white);
            importDetailsViewport = Panel("Import details viewport", card, new Color(.018f, .038f, .064f, 1f),
                new Vector2(.06f, .20f), new Vector2(.94f, .65f));
            importDetailsViewport.gameObject.AddComponent<RectMask2D>();
            importDetails = Label("等待选择文件", importDetailsViewport, 18, TextAnchor.UpperLeft,
                Vector2.zero, Vector2.one, muted);
            RectTransform detailsRect = importDetails.rectTransform;
            detailsRect.anchorMin = new Vector2(0, 1);
            detailsRect.anchorMax = new Vector2(1, 1);
            detailsRect.pivot = new Vector2(.5f, 1);
            detailsRect.offsetMin = new Vector2(18, -240);
            detailsRect.offsetMax = new Vector2(-18, 0);
            importDetails.verticalOverflow = VerticalWrapMode.Overflow;
            var detailsScroll = importDetailsViewport.gameObject.AddComponent<ScrollRect>();
            detailsScroll.viewport = importDetailsViewport;
            detailsScroll.content = detailsRect;
            detailsScroll.horizontal = false;
            detailsScroll.movementType = ScrollRect.MovementType.Clamped;

            importClose = ButtonText("完成", card, new Vector2(.72f, .055f), new Vector2(.94f, .155f), cyan, HideImportOverlay);
            importClose.interactable = false;
            Label("导入期间请保持软件在前台", card, 17, TextAnchor.MiddleLeft,
                new Vector2(.06f, .055f), new Vector2(.68f, .155f), muted);
            importLayer.gameObject.SetActive(false);
        }

        private void ShowImportOverlay(string title = "导入歌曲")
        {
            while (pendingImportProgress.TryDequeue(out _)) { }
            importDetailLines.Clear();
            importRunning = true;
            backgroundImageImport = false;
            backgroundImageTotal = 0;
            backgroundImageStaging = null;
            importHadCopyPhase = false;
            importProgressIndeterminate = true;
            importStartedRealtime = Time.realtimeSinceStartup;
            importCopyStartedRealtime = importStartedRealtime;
            importDataStartedRealtime = -1f;
            importDataTotalBytes = -1;
            importDataStage = "";
            importStatus.text = "正在准备导入";
            importEta.text = "预计剩余：计算中";
            importCurrent.text = "";
            importDetails.text = "文件已选择，正在后台读取…\n读取速度受文件大小、手机存储性能和剩余容量影响，可能需要一些时间。";
            if (importTitle != null) importTitle.text = UiLocalization.Translate(title);
            SetImportProgress(0);
            importProgressFill.color = cyan;
            importClose.interactable = false;
            importLayer.gameObject.SetActive(true);
        }

        private void HideImportOverlay()
        {
            if (importRunning) return;
            if (importLayer != null) importLayer.gameObject.SetActive(false);
        }

        private void DrainImportProgress()
        {
            SongImportProgress progress;
            while (pendingImportProgress.TryDequeue(out progress))
            {
                string count = progress.TotalItems > 0 ? $"  {progress.ProcessedItems}/{progress.TotalItems}" : "";
                importStatus.text = progress.Stage + count;
                importCurrent.text = string.IsNullOrWhiteSpace(progress.CurrentItem)
                    ? progress.Message
                    : progress.CurrentItem + "\n" + progress.Message;
                float fraction = importHadCopyPhase ? .20f + .80f * progress.Fraction : progress.Fraction;
                importProgressIndeterminate = false;
                SetImportProgress(fraction);
                UpdateImportEta(progress, fraction);
                if (progress.Detail != null)
                {
                    importDetailLines.Add(FormatImportDetail(progress.Detail));
                    RefreshImportDetailsText();
                }
            }
        }

        private void CompleteImportOverlay(SongImportResult result)
        {
            DrainImportProgress();
            importRunning = false;
            importProgressIndeterminate = false;
            importStatus.text = "导入完成";
            importCurrent.text = $"成功 {result.Imported} 首  ·  跳过 {result.Skipped} 首";
            importEta.text = "总用时：" + FormatDuration(Time.realtimeSinceStartup - importStartedRealtime);
            importDetailLines.Clear();
            importDetailLines.AddRange(result.Details.Select(FormatImportDetail));
            importDetailLines.Add("日志：" + result.LogPath);
            RefreshImportDetailsText();
            SetImportProgress(1);
            importClose.interactable = true;
        }

        private void FailImportOverlay(Exception exception)
        {
            DrainImportProgress();
            importRunning = false;
            importProgressIndeterminate = false;
            bool memoryFailure = IsImportMemoryFailure(exception);
            importStatus.text = memoryFailure ? "内存不足，导入已取消" : "导入失败";
            importCurrent.text = memoryFailure
                ? "设备可用内存不足，本次导入已取消，不会继续导入。"
                : exception.Message;
            importEta.text = memoryFailure ? "已取消" : "已停止";
            importProgressFill.color = new Color(.90f, .20f, .22f);
            if (memoryFailure)
            {
                importDetailLines.Add("内存不足：本次导入已取消，已清理临时数据并回滚本次已导入歌曲。");
                importDetailLines.Add("请关闭其他应用后重试，或分批导入较小的压缩包。");
            }
            importDetailLines.Add("详情：" + exception.Message);
            if (exception is SongImportException importException && !string.IsNullOrWhiteSpace(importException.LogPath))
                importDetailLines.Add("日志：" + importException.LogPath);
            RefreshImportDetailsText();
            importClose.interactable = true;
        }

        private static bool IsImportMemoryFailure(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
                if (current is ImportMemoryException || current is OutOfMemoryException) return true;
            return false;
        }

        private void RefreshImportDetailsText()
        {
            const int maxVisibleDetails = 500;
            IEnumerable<string> lines = importDetailLines.Count <= maxVisibleDetails
                ? importDetailLines
                : importDetailLines.Take(maxVisibleDetails).Concat(new[] { $"其余 {importDetailLines.Count - maxVisibleDetails} 项请查看导入日志" });
            importDetails.text = string.Join("\n", lines);
            int lineCount = Math.Max(1, importDetails.text.Count(character => character == '\n') + 1);
            float height = Math.Max(importDetailsViewport.rect.height, 20 + lineCount * 30f);
            importDetails.rectTransform.sizeDelta = new Vector2(-36, height);
            importDetails.rectTransform.anchoredPosition = Vector2.zero;
        }

        private void SetImportProgress(float value)
        {
            RectTransform rect = importProgressFill.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void UpdateIndeterminateImportProgress()
        {
            if (!importRunning || !importProgressIndeterminate || importProgressFill == null) return;
            float cycle = Mathf.Repeat(Time.unscaledTime * .45f, 1f);
            float start = Mathf.Lerp(-.20f, 1f, cycle);
            RectTransform rect = importProgressFill.rectTransform;
            rect.anchorMin = new Vector2(Mathf.Clamp01(start), 0f);
            rect.anchorMax = new Vector2(Mathf.Clamp01(start + .20f), 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void UpdateImportEta(SongImportProgress progress, float fraction)
        {
            bool hasByteProgress = progress != null && progress.TotalBytes > 0 &&
                (string.Equals(progress.Stage, "读取", StringComparison.Ordinal) ||
                 string.Equals(progress.Stage, "解压", StringComparison.Ordinal));
            if (!hasByteProgress)
            {
                UpdateImportEta(fraction);
                return;
            }

            if (importDataTotalBytes != progress.TotalBytes ||
                !string.Equals(importDataStage, progress.Stage, StringComparison.Ordinal))
            {
                importDataTotalBytes = progress.TotalBytes;
                importDataStage = progress.Stage;
                importDataStartedRealtime = Time.realtimeSinceStartup;
            }

            long processed = Math.Max(0, Math.Min(progress.ProcessedBytes, progress.TotalBytes));
            float elapsed = Mathf.Max(0, Time.realtimeSinceStartup - importDataStartedRealtime);
            if (processed <= 0 || elapsed < 1f)
            {
                importEta.text = "预计剩余" + progress.Stage + "：计算中";
                return;
            }

            double bytesPerSecond = processed / Math.Max(.001f, elapsed);
            double remainingSeconds = Math.Max(0, progress.TotalBytes - processed) / bytesPerSecond;
            importEta.text = "预计剩余" + progress.Stage + "：" + FormatDuration((float)remainingSeconds);
        }

        private void UpdateImportEta(float fraction)
        {
            float elapsed = Mathf.Max(0, Time.realtimeSinceStartup - importStartedRealtime);
            if (fraction < .01f || elapsed < 1f) { importEta.text = "预计剩余：计算中"; return; }
            float seconds = elapsed * (1f - Mathf.Clamp01(fraction)) / Mathf.Max(.001f, fraction);
            importEta.text = "预计剩余：" + FormatDuration(seconds);
        }

        private static string FormatDuration(float seconds)
        {
            int total = Math.Max(0, Mathf.CeilToInt(seconds));
            if (total < 60) return total + " 秒";
            int minutes = total / 60;
            if (minutes < 60) return $"{minutes} 分 {total % 60} 秒";
            return $"{minutes / 60} 小时 {minutes % 60} 分";
        }

        private static string FormatImportDetail(SongImportDetail detail)
        {
            string id = string.IsNullOrWhiteSpace(detail.SongId) ? "" : " [" + detail.SongId + "]";
            return $"{detail.Status}  {detail.SongName}{id}  {detail.Message}";
        }

        private void OpenSongSelect()
        {
            EnsurePageBuilt(AppPage.SongSelect);
            ConfigureSongSelectContextV2(false);
            if (selectedSong == null && catalog.Count > 0)
            {
                SongEntry initial = catalog.FirstOrDefault(song => PreferredDefaultChart(song) != null) ?? catalog[0];
                SelectSong(initial, false, null, false);
            }
            SyncBucketToSelection();
            RebuildSongGrid(true);
            SwitchTo(AppPage.SongSelect, () => {
                if (selectedSong != null) preview?.SelectSong(selectedSong.Root);
            });
        }

        private void RebuildSongGrid(bool scrollToCurrent)
        {
            RebuildSongGrid(scrollToCurrent, false);
        }

        private void RebuildSongGrid(bool scrollToCurrent, bool preserveScrollPosition)
        {
            float preservedScrollY = float.NaN;
            if (preserveScrollPosition && songGrid != null)
                preservedScrollY = Mathf.Max(0f, ((RectTransform)songGrid).anchoredPosition.y);
            visibleSongs = SongsForCurrentBucket().ToList();
            if (songGrid == null) return;
            ReleaseAllSongCards();
            ((RectTransform)songGrid).anchoredPosition = new Vector2(0f,
                float.IsNaN(preservedScrollY) ? 0f : preservedScrollY);
            UpdateBucketLabel();
            UpdateResponsiveGrid();
            RenderVirtualSongCards();
            if (gridLayoutRefresh != null) StopCoroutine(gridLayoutRefresh);
            gridLayoutRefresh = StartCoroutine(RefreshGridLayout(scrollToCurrent, preservedScrollY));
        }

        private IEnumerable<SongListItem> SongsForCurrentBucket()
        {
            IReadOnlyList<string> buckets = CurrentBuckets();
            if (buckets.Count == 0) return Enumerable.Empty<SongListItem>();
            int index = Mathf.Clamp(CurrentBucketIndex(), 0, buckets.Count - 1);
            SetCurrentBucketIndex(index);
            string bucket = buckets[index];
            IEnumerable<SongListItem> query;
            if (sortMode == SongSortMode.Category)
            {
                query = catalog
                    .Where(song => HighestChart(song) != null && string.Equals(song.Category, bucket, StringComparison.OrdinalIgnoreCase))
                    .Select(song => new SongListItem {
                        Song = song,
                        Chart = ChartFor(song, selectedChart?.Difficulty) ?? HighestChart(song)
                    })
                    .OrderBy(item => LevelValue(item.Chart?.Level))
                    .ThenBy(item => item.Song.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(item => item.Song.Root, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                // Level mode is chart-based. A song can therefore appear more
                // than once when it has multiple charts at the selected level.
                    query = catalog
                    .Where(song => song.Charts != null)
                    .SelectMany(song => song.Charts
                        .Where(chart => string.Equals(LevelBucketKey(chart), bucket, StringComparison.OrdinalIgnoreCase))
                        .Select(chart => new SongListItem { Song = song, Chart = chart }))
                    .OrderBy(item => DifficultyPriority(item.Chart?.Difficulty))
                    .ThenBy(item => item.Song.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(item => item.Song.Root, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Chart.Path, StringComparer.OrdinalIgnoreCase);
            }
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                string needle = NormalizeSearch(searchText);
                query = query.Where(item => SearchText(item.Song).Contains(needle));
            }
            return query;
        }

        private RectTransform MakeSongCard(SongListItem item, int index)
        {
            SongCardVisual visual = AcquireSongCardVisual();
            if (visual == null || item?.Song == null) return null;
            BindSongCardVisual(visual, item, index);
            virtualSongCards[index] = visual.Card;
            return visual.Card;
        }

        private SongCardVisual AcquireSongCardVisual()
        {
            while (songCardPool.Count > 0)
            {
                SongCardVisual pooled = songCardPool.Pop();
                if (pooled != null && pooled.Card != null)
                {
                    pooled.Card.gameObject.SetActive(true);
                    return pooled;
                }
            }
            return CreateSongCardVisual();
        }

        private SongCardVisual CreateSongCardVisual()
        {
            Color inactive = Color.Lerp(surfaceRaised, muted, .20f);
            Button button = ButtonText("", songGrid, Vector2.zero, Vector2.one, inactive, null);
            RectTransform card = (RectTransform)button.transform;
            Text buttonText = button.GetComponentInChildren<Text>();
            if (buttonText != null) buttonText.gameObject.SetActive(false);
            // GridLayoutGroup children must not retain the stretch anchors created by
            // ButtonText. Stretching makes the cell wider than its allocated slot and
            // pushes its left edge underneath the viewport mask.
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(0f, 1f);
            card.pivot = new Vector2(.5f, .5f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = Vector2.zero;

            RectTransform body = Panel("Card Body", card, surfaceRaised, new Vector2(.018f, .014f), new Vector2(.982f, .986f));
            Image art = Panel("Jacket", body, new Color(.11f, .06f, .20f), new Vector2(.035f, .30f), new Vector2(.965f, .965f)).GetComponent<Image>();
            Text placeholder = Label("♪", body, 55, TextAnchor.MiddleCenter,
                new Vector2(.035f, .30f), new Vector2(.965f, .965f), cyan);
            RectTransform caption = Panel("Caption", body, new Color(.005f, .01f, .02f, .86f),
                new Vector2(.035f, .30f), new Vector2(.965f, .49f));
            Image captionImage = caption.GetComponent<Image>();
            captionImage.sprite = null; captionImage.type = Image.Type.Simple;
            // The level badge sits above this caption, so the title and artist can use
            // the full card width. This keeps ordinary names static and complete.
            Text title = MarqueeLabel("", body, 19, new Vector2(.07f, .395f), new Vector2(.93f, .475f), Color.white);
            Text artist = MarqueeLabel("", body, 15, new Vector2(.07f, .315f), new Vector2(.93f, .395f), muted);
            RectTransform badge = Panel("Level", body, inactive, new Vector2(.72f, .465f), new Vector2(.965f, .565f));
            Image badgeImage = badge.GetComponent<Image>();
            badgeImage.sprite = rounded; badgeImage.type = Image.Type.Sliced;
            Text level = Label("", badge, 17, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Color.white);
            level.resizeTextForBestFit = true; level.resizeTextMinSize = 11; level.resizeTextMaxSize = 17;
            Text score = Label("", body, 22, TextAnchor.MiddleLeft, new Vector2(.07f, .15f), new Vector2(.68f, .285f), Color.white);
            score.resizeTextForBestFit = true; score.resizeTextMinSize = 13; score.resizeTextMaxSize = 22;
            Text rank = Label("", body, 31, TextAnchor.MiddleRight, new Vector2(.68f, .14f), new Vector2(.93f, .29f), inactive);
            rank.resizeTextForBestFit = true; rank.resizeTextMinSize = 17; rank.resizeTextMaxSize = 31;
            Text bpm = Label("", body, 15, TextAnchor.MiddleLeft, new Vector2(.07f, .055f), new Vector2(.48f, .15f), muted);
            Text duration = Label("", body, 15, TextAnchor.MiddleRight, new Vector2(.48f, .055f), new Vector2(.93f, .15f), muted);
            SongCardVisual visual = new SongCardVisual {
                Card = card, Button = button, Border = card.GetComponent<Image>(), Art = art,
                Placeholder = placeholder, Title = title, Artist = artist, Level = level,
                Score = score, Rank = rank, Bpm = bpm, Duration = duration
            };
            // The card root owns the click. Child graphics such as the jacket,
            // caption and labels do not need their own raycast target; leaving
            // them active makes a touch inside a ScrollRect depend on which
            // recycled child was hit first on some Android devices.
            foreach (Graphic graphic in card.GetComponentsInChildren<Graphic>(true))
                if (graphic != visual.Border) graphic.raycastTarget = false;
            button.targetGraphic = visual.Border;
            songCardVisuals[card] = visual;
            return visual;
        }

        private void BindSongCardVisual(SongCardVisual visual, SongListItem item, int index)
        {
            SongEntry song = item?.Song;
            if (visual == null || song == null) return;
            if (!string.IsNullOrWhiteSpace(visual.Root)) RemoveSongCardBorder(visual.Root, visual.Border);
            visual.Root = song.Root;
            visual.Card.gameObject.SetActive(true);
            visual.Button.interactable = true;
            visual.Button.targetGraphic = visual.Border;
            visual.Button.onClick.RemoveAllListeners();
            SongChartEntry listChart = item.Chart;
            visual.Button.onClick.AddListener(() => SelectSong(song, true,
                listChart?.Difficulty ?? selectedChart?.Difficulty, false, listChart?.Path));
            Color inactive = Color.Lerp(surfaceRaised, muted, .20f);
            Color borderColor = selectedSong != null &&
                                string.Equals(song.Root, selectedSong.Root, StringComparison.OrdinalIgnoreCase)
                ? cyan : inactive;
            visual.Border.color = borderColor;
            ColorBlock buttonColors = visual.Button.colors;
            buttonColors.normalColor = borderColor;
            buttonColors.highlightedColor = Color.Lerp(borderColor, Color.white, .12f);
            buttonColors.pressedColor = Color.Lerp(borderColor, Color.black, .25f);
            visual.Button.colors = buttonColors;
            if (!songCardBorders.TryGetValue(song.Root, out List<Image> borders))
            {
                borders = new List<Image>();
                songCardBorders.Add(song.Root, borders);
            }
            borders.Add(visual.Border);

            LocalCoverLoader.Reset(visual.Art);
            visual.Art.sprite = null;
            visual.Art.color = new Color(.11f, .06f, .20f, 1f);
            visual.Art.preserveAspect = false;
            visual.Art.rectTransform.localRotation = Quaternion.identity;
            visual.Art.rectTransform.localScale = Vector3.one;
            bool hasJacket = !string.IsNullOrWhiteSpace(song.JacketPath);
            visual.Placeholder.gameObject.SetActive(!hasJacket);
            if (hasJacket) LocalCoverLoader.Load(this, song.JacketPath, visual.Art);

            visual.Title.text = song.Name ?? "";
            visual.Artist.text = song.Artist ?? "";
            visual.Title.transform.parent?.GetComponent<UiMarqueeText>()?.Restart();
            visual.Artist.transform.parent?.GetComponent<UiMarqueeText>()?.Restart();
            SongChartEntry chart = listChart ?? (selectedSong != null && selectedChart != null &&
                                   string.Equals(song.Root, selectedSong.Root, StringComparison.OrdinalIgnoreCase)
                ? selectedChart
                : ChartFor(song, selectedChart?.Difficulty) ?? HighestChart(song));
            bool hasChart = chart != null;
            visual.Level.gameObject.SetActive(hasChart);
            visual.Score.gameObject.SetActive(false);
            visual.Rank.gameObject.SetActive(false);
            if (hasChart)
            {
                visual.Level.text = "Lv. " + chart.Level;
                visual.Level.transform.parent.GetComponent<Image>().color = DifficultyColor(chart.Difficulty);
                if (SongRecordStore.TryGet(song.Root, chart.Difficulty, chart.Path, out int bestScore))
                {
                    visual.Score.text = bestScore.ToString("N0");
                    visual.Rank.text = ResultViewModel.RankForScore(bestScore);
                    visual.Rank.color = DifficultyColor(chart.Difficulty);
                    visual.Score.gameObject.SetActive(true);
                    visual.Rank.gameObject.SetActive(true);
                }
            }
            visual.Bpm.text = "BPM  " + (song.Bpm ?? "-");
            visual.Duration.text = song.DurationText ?? "";
            visual.Duration.gameObject.SetActive(!string.IsNullOrWhiteSpace(song.DurationText));
            PositionVirtualSongCard(visual.Card, index);
        }

        private void ReleaseAllSongCards()
        {
            foreach (int index in virtualSongCards.Keys.ToArray()) ReleaseSongCard(index);
            virtualSongCards.Clear();
            songCardBorders.Clear();
        }

        private void ReleaseSongCard(int index)
        {
            if (!virtualSongCards.TryGetValue(index, out RectTransform card) || card == null) return;
            if (songCardVisuals.TryGetValue(card, out SongCardVisual visual))
            {
                if (!string.IsNullOrWhiteSpace(visual.Root)) RemoveSongCardBorder(visual.Root, visual.Border);
                visual.Root = null;
                visual.Button.onClick.RemoveAllListeners();
                visual.Card.gameObject.SetActive(false);
                songCardPool.Push(visual);
            }
            virtualSongCards.Remove(index);
        }

        private void RenderVirtualSongCards()
        {
            if (songGrid == null || songViewport == null || visibleSongs == null) return;
            float rowStep = Mathf.Max(1f, songCardSize.y + songGridGap);
            float scrollY = Mathf.Max(0, ((RectTransform)songGrid).anchoredPosition.y);
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(scrollY / rowStep) - 1);
            int visibleRows = Mathf.Max(2, Mathf.CeilToInt(Mathf.Max(songCardSize.y, songViewport.rect.height) / rowStep) + 2);
            int lastRow = firstRow + visibleRows;
            int firstIndex = firstRow * Mathf.Max(1, gridColumns);
            int lastIndex = Mathf.Min(visibleSongs.Count - 1, (lastRow + 1) * Mathf.Max(1, gridColumns) - 1);

            foreach (int index in virtualSongCards.Keys.Where(index => index < firstIndex || index > lastIndex).ToArray()) {
                ReleaseSongCard(index);
            }
            if (lastIndex < firstIndex) return;
            for (int index = firstIndex; index <= lastIndex; index++) {
                if (!virtualSongCards.TryGetValue(index, out RectTransform card) || card == null)
                    card = MakeSongCard(visibleSongs[index], index);
                if (card != null) PositionVirtualSongCard(card, index);
            }
            RefreshSongCardSelection();
        }

        private void PositionVirtualSongCard(RectTransform card, int index)
        {
            if (card == null) return;
            int column = index % Mathf.Max(1, gridColumns);
            int row = index / Mathf.Max(1, gridColumns);
            card.anchorMin = card.anchorMax = new Vector2(0, 1);
            card.pivot = new Vector2(0, 1);
            card.sizeDelta = songCardSize;
            card.anchoredPosition = new Vector2(column * (songCardSize.x + songGridGap), -row * (songCardSize.y + songGridGap));
        }

        private void SelectSong(SongEntry song) => SelectSong(song, true, selectedChart?.Difficulty, true);

        private void SelectSong(SongEntry song, bool playPreview, string preferredDifficulty, bool rebuild,
            string preferredChartPath = null)
        {
            string previousRoot = selectedSong?.Root;
            selectedSong = song;
            if (song == null)
            {
                selectedChart = null;
                RenderSongDetails();
                SaveSongSelectionState();
                if (playPreview) preview?.StopPreview();
                if (rebuild) RebuildSongGrid(false);
                return;
            }
            selectedChart = ChartForPath(song, preferredChartPath) ?? ChartFor(song, preferredDifficulty) ??
                            HighestChart(song) ?? PreferredDefaultChart(song);
            SyncBucketToSelection();
            RenderSongDetails();
            SaveSongSelectionState();
            bool previewIsStopped = preview != null && !preview.IsPlaying && !preview.IsLoading;
            if (playPreview && (!string.Equals(previousRoot, song.Root, StringComparison.OrdinalIgnoreCase) || previewIsStopped))
                preview?.SelectSong(song.Root);
            if (rebuild) RebuildSongGrid(true);
            else StartSongCardSelectionPulse(previousRoot, song.Root);
        }

        private void SaveSongSelectionState()
        {
            if (uiSession == null) uiSession = new UiSessionState();
            uiSession.SongId = selectedSong?.Id ?? string.Empty;
            uiSession.SongRoot = selectedSong?.Root ?? string.Empty;
            uiSession.ChartPath = selectedChart?.Path ?? string.Empty;
            uiSession.Difficulty = selectedChart?.Difficulty ?? string.Empty;
            uiSession.SortMode = (int)sortMode;
            uiSession.Save();
        }

        private void RenderSongDetails()
        {
            if (difficultyRow == null) return;
            foreach (Transform child in difficultyRow) Destroy(child.gameObject);
            if (selectedSong == null)
            {
                detailTitle.text = "没有符合条件的歌曲"; detailMeta.text = "请更改搜索条件";
                LocalCoverLoader.Reset(detailCover);
                detailCover.sprite = rounded; detailCover.type = Image.Type.Sliced; detailCover.color = new Color(.12f, .06f, .22f); detailCoverPlaceholder.SetActive(true);
                return;
            }
            detailTitle.text = selectedSong.Name;
            detailMeta.text = selectedSong.Artist + "\n" + selectedSong.Category + "   " + selectedChart?.Difficulty + "  Lv." + selectedChart?.Level +
                              (string.IsNullOrWhiteSpace(selectedChart?.Designer) ? "" : "\n谱师  " + selectedChart.Designer);
            LocalCoverLoader.Reset(detailCover);
            detailCover.sprite = rounded; detailCover.type = Image.Type.Sliced; detailCover.color = new Color(.12f, .06f, .22f);
            detailCoverPlaceholder.SetActive(string.IsNullOrWhiteSpace(selectedSong.JacketPath));
            if (!string.IsNullOrWhiteSpace(selectedSong.JacketPath)) LocalCoverLoader.Load(this, selectedSong.JacketPath, detailCover);
            float height = 1f / Mathf.Max(1, selectedSong.Charts.Count);
            for (int i = 0; i < selectedSong.Charts.Count; i++)
            {
                SongChartEntry chart = selectedSong.Charts[i];
                bool active = chart == selectedChart;
                float top = 1f - i * height;
                float bottom = 1f - (i + 1) * height;
                Color difficultyColor = DifficultyColor(chart.Difficulty);
                Button button = ButtonText("", difficultyRow, new Vector2(.01f, bottom + .012f), new Vector2(.99f, top - .012f),
                    active ? Color.Lerp(difficultyColor, Color.white, .16f) : Color.Lerp(surfaceRaised, difficultyColor, .32f), () => SelectChart(chart));
                Panel("Accent", button.transform, difficultyColor, new Vector2(.012f, .16f), new Vector2(.026f, .84f));
                Text difficulty = Label(chart.Difficulty, button.transform, 21, TextAnchor.MiddleLeft, new Vector2(.035f, .12f), new Vector2(.34f, .88f), Color.white);
                difficulty.resizeTextForBestFit = true; difficulty.resizeTextMinSize = 12; difficulty.resizeTextMaxSize = 21;
                Label("Lv. " + chart.Level, button.transform, 19, TextAnchor.MiddleLeft, new Vector2(.36f, .12f), new Vector2(.53f, .88f), active ? Color.white : muted);
                if (SongRecordStore.TryGet(selectedSong.Root, chart.Difficulty, chart.Path, out int bestScore))
                {
                    Text score = Label(bestScore.ToString("N0"), button.transform, 18, TextAnchor.MiddleRight, new Vector2(.54f, .12f), new Vector2(.82f, .88f), Color.white);
                    score.resizeTextForBestFit = true; score.resizeTextMinSize = 12; score.resizeTextMaxSize = 18;
                    Label(ResultViewModel.RankForScore(bestScore), button.transform, 22, TextAnchor.MiddleRight, new Vector2(.83f, .12f), new Vector2(.965f, .88f), Color.white);
                }
            }
        }

        private void SelectChart(SongChartEntry chart)
        {
            if (selectedSong == null || chart == null) return;
            RequestSongListTransition(() =>
            {
                selectedChart = chart;
                SyncBucketToSelection();
                RenderSongDetails();
                SaveSongSelectionState();
            }, false, false);
        }

        private void RefreshSongCardSelection()
        {
            foreach (KeyValuePair<string, List<Image>> pair in songCardBorders)
            {
                bool active = selectedSong != null && string.Equals(pair.Key, selectedSong.Root, StringComparison.OrdinalIgnoreCase);
                Color color = active ? cyan : Color.Lerp(surfaceRaised, muted, .20f);
                foreach (Image image in pair.Value.ToArray())
                {
                    if (image == null) continue;
                    image.color = color;
                    Button button = image.GetComponent<Button>();
                    if (button == null) continue;
                    ColorBlock colors = button.colors;
                    colors.normalColor = color;
                    colors.highlightedColor = Color.Lerp(color, Color.white, .12f);
                    colors.pressedColor = Color.Lerp(color, Color.black, .25f);
                    button.colors = colors;
                }
            }
        }

        private void StartSongCardSelectionPulse(string previousRoot, string nextRoot)
        {
            if (string.Equals(previousRoot, nextRoot, StringComparison.OrdinalIgnoreCase))
            {
                RefreshSongCardSelection();
                return;
            }
            if (songSelectionPulse != null)
            {
                StopCoroutine(songSelectionPulse);
                RefreshSongCardSelection();
            }
            songSelectionPulse = StartCoroutine(AnimateSongCardSelection(previousRoot, nextRoot));
        }

        private IEnumerator AnimateSongCardSelection(string previousRoot, string nextRoot)
        {
            Color inactive = Color.Lerp(surfaceRaised, muted, .20f);
            const float extinguishDuration = .055f;
            float time = 0f;
            while (time < extinguishDuration)
            {
                time += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(time / extinguishDuration);
                SetSongCardBorder(previousRoot, Color.Lerp(cyan, inactive, t));
                SetSongCardBorder(nextRoot, inactive);
                yield return null;
            }
            const float lightDuration = .095f;
            time = 0f;
            Color flash = Color.Lerp(cyan, Color.white, .38f);
            while (time < lightDuration)
            {
                time += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(time / lightDuration);
                Color color = t < .58f
                    ? Color.Lerp(inactive, flash, t / .58f)
                    : Color.Lerp(flash, cyan, (t - .58f) / .42f);
                SetSongCardBorder(nextRoot, color);
                yield return null;
            }
            RefreshSongCardSelection();
            songSelectionPulse = null;
        }

        private void SetSongCardBorder(string root, Color color)
        {
            if (string.IsNullOrWhiteSpace(root) || !songCardBorders.TryGetValue(root, out List<Image> images)) return;
            foreach (Image image in images.ToArray())
            {
                if (image == null) continue;
                image.color = color;
                Button button = image.GetComponent<Button>();
                if (button == null) continue;
                ColorBlock colors = button.colors;
                colors.normalColor = color;
                colors.highlightedColor = color;
                colors.pressedColor = color;
                button.colors = colors;
            }
        }

        private void RemoveSongCardBorder(string root, Image image)
        {
            if (string.IsNullOrWhiteSpace(root) || !songCardBorders.TryGetValue(root, out List<Image> images)) return;
            images.Remove(image);
            images.RemoveAll(value => value == null);
            if (images.Count == 0) songCardBorders.Remove(root);
        }

        private void RequestSongListTransition(Action mutation, bool selectFallback, bool playFallbackPreview)
        {
            if (songGridTransition != null) StopCoroutine(songGridTransition);
            if (gridLayoutRefresh != null) StopCoroutine(gridLayoutRefresh);
            gridLayoutRefresh = null;
            if (songGrid != null) {
                RectTransform grid = (RectTransform)songGrid;
                grid.anchoredPosition = new Vector2(0, grid.anchoredPosition.y);
            }
            if (songGridGroup != null) {
                songGridGroup.alpha = 1f;
                songGridGroup.blocksRaycasts = true;
            }
            songGridTransition = StartCoroutine(AnimateSongListChange(mutation, selectFallback, playFallbackPreview));
        }

        private IEnumerator AnimateSongListChange(Action mutation, bool selectFallback, bool playFallbackPreview)
        {
            if (songGridGroup == null)
            {
                mutation?.Invoke();
                RebuildSongGrid(true);
                songGridTransition = null;
                yield break;
            }
            songGridGroup.blocksRaycasts = false;
            RectTransform grid = (RectTransform)songGrid;
            Vector2 basePosition = new Vector2(0, grid.anchoredPosition.y);
            grid.anchoredPosition = basePosition;
            float time = 0f;
            float startAlpha = songGridGroup.alpha;
            const float outDuration = .10f;
            while (time < outDuration)
            {
                time += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(time / outDuration);
                songGridGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
                grid.anchoredPosition = basePosition + new Vector2(-12f * t, 0f);
                yield return null;
            }
            songGridGroup.alpha = 0f;
            mutation?.Invoke();
            visibleSongs = SongsForCurrentBucket().ToList();
            bool containsCurrent = selectedSong != null && visibleSongs.Any(item =>
                item?.Song != null && string.Equals(item.Song.Root, selectedSong.Root, StringComparison.OrdinalIgnoreCase));
            if (selectFallback && !containsCurrent && visibleSongs.Count > 0)
            {
                string previousRoot = selectedSong?.Root;
                SongListItem first = visibleSongs[0];
                selectedSong = first.Song;
                selectedChart = first.Chart ?? ChartFor(selectedSong, selectedChart?.Difficulty) ??
                                HighestChart(selectedSong) ?? PreferredDefaultChart(selectedSong);
                RenderSongDetails();
                if (playFallbackPreview && !string.Equals(previousRoot, selectedSong.Root, StringComparison.OrdinalIgnoreCase))
                    preview?.SelectSong(selectedSong.Root);
            }
            ReleaseAllSongCards();
            yield return null;
            UpdateBucketLabel();
            Canvas.ForceUpdateCanvases();
            UpdateResponsiveGrid();
            Canvas.ForceUpdateCanvases();
            ScrollToCurrentSong();
            RenderVirtualSongCards();
            basePosition = grid.anchoredPosition;
            grid.anchoredPosition = basePosition + new Vector2(16f, 0f);
            songGridGroup.alpha = 0f;
            time = 0f;
            const float inDuration = .17f;
            while (time < inDuration)
            {
                time += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / inDuration));
                songGridGroup.alpha = t;
                grid.anchoredPosition = Vector2.Lerp(basePosition + new Vector2(16f, 0f), basePosition, t);
                yield return null;
            }
            songGridGroup.alpha = 1f;
            grid.anchoredPosition = basePosition;
            songGridGroup.blocksRaycasts = true;
            songGridTransition = null;
            SaveSongSelectionState();
        }

        private void CycleSort()
        {
            RequestSongListTransition(() =>
            {
                sortMode = sortMode == SongSortMode.Category ? SongSortMode.Level : SongSortMode.Category;
                sortLabel.text = "排序：" + (sortMode == SongSortMode.Category ? "分类" : "等级");
                // Each mode has its own bucket index, but switching modes must
                // follow the chart currently shown in the right detail panel.
                // Reusing the previous mode's numeric index can land on an
                // unrelated category/level and make the selected song vanish.
                SyncBucketToSelection();
                SaveSongSelectionState();
            }, true, false);
        }

        private void ShiftBucket(int direction)
        {
            IReadOnlyList<string> buckets = CurrentBuckets();
            if (buckets.Count == 0) return;
            RequestSongListTransition(() => SetCurrentBucketIndex(
                (CurrentBucketIndex() + direction + buckets.Count) % buckets.Count), true, true);
        }

        private void SyncBucketToSelection()
        {
            IReadOnlyList<string> buckets = CurrentBuckets();
            if (buckets.Count == 0) { SetCurrentBucketIndex(0); return; }
            string value = sortMode == SongSortMode.Category
                ? selectedSong?.Category
                : LevelBucketKey(selectedChart ?? HighestChart(selectedSong));
            int index = value == null ? -1 : buckets.ToList().FindIndex(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
            SetCurrentBucketIndex(index >= 0 ? index : 0);
        }

        private void SyncBucketToLevel(string level)
        {
            IReadOnlyList<string> buckets = CurrentBuckets();
            string value = LevelKey(level);
            int index = buckets.ToList().FindIndex(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) levelBucketIndex = index;
        }

        private IReadOnlyList<string> CurrentBuckets()
        {
            if (sortMode == SongSortMode.Category)
                return catalog.Where(song => HighestChart(song) != null).Select(song => song.Category).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().OrderBy(value => value).ToArray();
            IEnumerable<string> values = catalog
                .Where(song => song.Charts != null)
                .SelectMany(song => song.Charts)
                .Select(LevelBucketKey);
            return values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(LevelValue).ToArray();
        }

        private bool SongBelongsToLevelBucket(SongEntry song, string bucket)
        {
            SongChartEntry highest = HighestChart(song);
            if (highest != null && string.Equals(LevelBucketKey(highest), bucket, StringComparison.OrdinalIgnoreCase)) return true;
            // A level bucket represents the level currently selected on the
            // right, so songs with that level must remain visible even when
            // their highest chart is another difficulty.
            if (song?.Charts != null && song.Charts.Any(chart =>
                    string.Equals(LevelBucketKey(chart), bucket, StringComparison.OrdinalIgnoreCase))) return true;
            return selectedSong != null && selectedChart != null &&
                   string.Equals(song.Root, selectedSong.Root, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(LevelBucketKey(selectedChart), bucket, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateBucketLabel()
        {
            IReadOnlyList<string> buckets = CurrentBuckets();
            if (bucketLabel == null) return;
            if (buckets.Count == 0) { bucketLabel.text = "—"; return; }
            string bucket = buckets[Mathf.Clamp(CurrentBucketIndex(), 0, buckets.Count - 1)];
            bucketLabel.text = sortMode == SongSortMode.Level
                ? (bucket == "WD" ? "WD" : string.IsNullOrEmpty(bucket) ? "未标等级" : "Lv. " + bucket)
                : bucket;
        }

        private int CurrentBucketIndex() => sortMode == SongSortMode.Category
            ? categoryBucketIndex
            : levelBucketIndex;

        private void SetCurrentBucketIndex(int value)
        {
            if (sortMode == SongSortMode.Category) categoryBucketIndex = value;
            else levelBucketIndex = value;
        }

        private void RefreshCatalog()
        {
            try
            {
                ReloadCatalogAfterDataChange();
                ShowModal("刷新完成", $"已忽略旧缓存并重新建立索引。\n当前共有 {catalog.Count} 首歌曲。", null, false);
            }
            catch (Exception exception)
            {
                Debug.LogError("Song catalog refresh failed: " + exception);
                ShowModal("刷新失败", exception.Message, null, false);
            }
        }

        private void ReloadCatalogAfterDataChange()
        {
            string root = selectedSong?.Root;
            string difficulty = selectedChart?.Difficulty;
            string chartPath = selectedChart?.Path;
            catalog = SongCatalog.Load(true);
            ApplyCatalogState(root, difficulty, chartPath);
            RebuildSongGrid(false);
        }

        private void RefreshCatalogAfterImport(SongImportResult importResult)
        {
            if (importResult == null || importResult.Imported <= 0) return;
            if (importResult.ImportedSongs.Count != importResult.Imported)
            {
                ReloadCatalogAfterDataChange();
                return;
            }

            string root = selectedSong?.Root;
            string difficulty = selectedChart?.Difficulty;
            string chartPath = selectedChart?.Path;
            try
            {
                catalog = SongCatalog.ApplyImportedSongs(catalog, importResult.ImportedSongs);
                ApplyCatalogState(root, difficulty, chartPath);
                RebuildSongGrid(false);
            }
            catch (Exception exception)
            {
                // The files were committed successfully. A full rebuild is a
                // correctness fallback if the incremental index write fails.
                Debug.LogWarning("Incremental catalog update failed; rebuilding: " + exception.Message);
                ReloadCatalogAfterDataChange();
            }
        }

        private void ApplyCatalogState(string preferredRoot, string preferredDifficulty,
            string preferredChartPath = null, string preferredSongId = null)
        {
            var availableRoots = new HashSet<string>(catalog.Select(song => song.Root), StringComparer.OrdinalIgnoreCase);
            managedSongRoots.RemoveWhere(root => !availableRoots.Contains(root));
            categories = catalog.Select(song => song.Category)
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().OrderBy(value => value).ToList();
            levels = catalog.SelectMany(song => song.Charts).Select(LevelBucketKey)
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().OrderBy(LevelValue).ToList();
            if (homeLibraryLabel != null)
                homeLibraryLabel.text = $"已加载歌曲：{catalog.Count}\nby: HMQYHM    v{Application.version}";

            SongEntry replacement = string.IsNullOrWhiteSpace(preferredSongId) ? null : catalog.FirstOrDefault(song =>
                string.Equals(song.Id, preferredSongId, StringComparison.OrdinalIgnoreCase));
            if (replacement == null && !string.IsNullOrWhiteSpace(preferredRoot))
                replacement = catalog.FirstOrDefault(song =>
                    string.Equals(song.Root, preferredRoot, StringComparison.OrdinalIgnoreCase));
            if (replacement != null || selectedSong == null || !catalog.Any(song =>
                    string.Equals(song.Root, selectedSong.Root, StringComparison.OrdinalIgnoreCase)))
                selectedSong = replacement ?? catalog.FirstOrDefault();
            selectedChart = ChartForPath(selectedSong, preferredChartPath) ?? ChartFor(selectedSong, preferredDifficulty) ??
                            HighestChart(selectedSong) ?? PreferredDefaultChart(selectedSong);
            SyncBucketToSelection();
            RenderSongDetails();
            RebuildManagementList();
            SaveSongSelectionState();
        }

        private static IEnumerable<SongChartEntry> ChartsFor(SongEntry song, string difficulty) =>
            song?.Charts.Where(chart => string.Equals(chart.Difficulty, difficulty, StringComparison.OrdinalIgnoreCase)) ??
            Enumerable.Empty<SongChartEntry>();
        private static SongChartEntry ChartFor(SongEntry song, string difficulty) => ChartsFor(song, difficulty).FirstOrDefault();
        private static SongChartEntry ChartForPath(SongEntry song, string path) =>
            string.IsNullOrWhiteSpace(path) ? null : song?.Charts.FirstOrDefault(chart =>
                string.Equals(chart.Path, path, StringComparison.OrdinalIgnoreCase));
        private static SongChartEntry HighestChart(SongEntry song) => song?.Charts
            .OrderByDescending(chart => LevelValue(chart.Level))
            .ThenByDescending(chart => DifficultyPriority(chart.Difficulty))
            .FirstOrDefault();
        private static int DifficultyPriority(string difficulty)
        {
            if (string.Equals(difficulty, "ULTIMA", StringComparison.OrdinalIgnoreCase)) return 6;
            if (string.Equals(difficulty, "MASTER", StringComparison.OrdinalIgnoreCase)) return 5;
            if (string.Equals(difficulty, "EXPERT", StringComparison.OrdinalIgnoreCase)) return 4;
            if (string.Equals(difficulty, "ADVANCED", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(difficulty, "BASIC", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }
        private static SongChartEntry PreferredDefaultChart(SongEntry song)
        {
            if (song == null) return null;
            string[] order = { "MASTER", "EXPERT", "ADVANCED", "BASIC" };
            foreach (string difficulty in order)
            {
                SongChartEntry chart = ChartFor(song, difficulty);
                if (chart != null) return chart;
            }
            return song.Charts.FirstOrDefault(chart =>
                !chart.Difficulty.Contains("WORLD", StringComparison.OrdinalIgnoreCase) &&
                !chart.Difficulty.Contains("ULTIMA", StringComparison.OrdinalIgnoreCase));
        }
        private static string SearchText(SongEntry song) => NormalizeSearch(song.Name + song.Artist + song.Id + song.Category + string.Join("", song.Charts.Select(chart => chart.Designer)));
        private static string NormalizeSearch(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string normalized = value.Normalize(NormalizationForm.FormKC);
            var builder = new StringBuilder(normalized.Length);
            foreach (char character in normalized) if (char.IsLetterOrDigit(character)) builder.Append(char.ToUpperInvariant(character));
            return builder.ToString();
        }

        private void ShowMods()
        {
            RefreshMods();
            modsLayer.gameObject.SetActive(true);
            CanvasGroup group = modsLayer.GetComponent<CanvasGroup>() ?? modsLayer.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0;
            StartCoroutine(Fade(group, 0, 1, .16f));
        }

        private void HideMods()
        {
            if (modsLayer != null) modsLayer.gameObject.SetActive(false);
        }

        private void RefreshMods()
        {
            SetModButton(autoPlayButton, "全谱面 AUTO", mods.AutoPlay);
            SetModButton(airAreaAutoButton, "AIR 区域 AUTO", mods.AirAreaAuto);
            SetModButton(directionalAirAutoButton, "上下 AIR AUTO", mods.DirectionalAirAuto);
            SetModButton(mineImmunityButton, "免疫地雷音符", mods.MineImmunity);
            SetModButton(airLongNoMotionButton, "air长条简单模式", mods.AirLongNoMotion);
            SetModButton(nonMissJusticeCriticalButton, "简单判定", mods.NonMissJusticeCritical);
            SetModButton(mirrorChartButton, "谱面镜像", mods.MirrorChart);
            float[] rates = { .8f, 1.2f, 1.5f };
            for (int i = 0; i < rateButtons.Count && i < rates.Length; i++)
                SetModButton(rateButtons[i], rates[i].ToString("0.0") + "x", Math.Abs(mods.MusicRate - rates[i]) < .01f);
            UpdateModLabel();
        }

        private void SetModButton(Button button, string label, bool selected)
        {
            if (button == null) return;
            UpdateModOption(button, label, selected, false);
        }

        private string CurrentModSummary()
        {
            var labels = new List<string>();
            if (mods.AutoPlay) labels.Add("AUTO");
            if (mods.AirAreaAuto) labels.Add("AIR AREA");
            if (mods.DirectionalAirAuto) labels.Add("AIR UP/DOWN");
            if (mods.AirLongNoMotion) labels.Add("AIR HOLD SIMPLE");
            if (mods.NonMissJusticeCritical) labels.Add("简单判定");
            if (mods.MirrorChart) labels.Add("MIRROR");
            if (mods.MineImmunity) labels.Add("MINE OFF");
            if (Math.Abs(mods.MusicRate - 1f) > .01f) labels.Add(mods.MusicRate.ToString("0.0") + "x");
            return labels.Count == 0 ? "NORMAL" : string.Join(" + ", labels);
        }

        private void UpdateModLabel()
        {
            if (modLabel == null) return;
            modLabel.text = "MODS\n" + CurrentModSummary();
            Image buttonImage = modLabel.GetComponentInParent<Button>()?.GetComponent<Image>();
            if (buttonImage == null) return;
            bool hasNonScoringMod = mods.HasAnyMod && !mods.RecordsScore;
            buttonImage.color = hasNonScoringMod
                ? Color.white
                : mods.HasAnyMod ? new Color(.18f, .76f, .38f, 1f) : new Color(1f, .48f, .20f, 1f);
            modLabel.color = hasNonScoringMod ? new Color(.008f, .015f, .030f, 1f) : Color.white;
        }

        private void ConfirmDelete()
        {
            if (selectedSong == null) return;
            SongEntry song = selectedSong;
            ShowModal("删除歌曲", $"删除《{song.Name}》的全部难度与音乐文件？\n此操作无法撤销。", () => {
                int deleted = SongCatalog.DeleteSongDirectories(new[] { song.Root });
                ReloadCatalogAfterDataChange();
                ShowModal(deleted == 1 ? "删除完成" : "删除失败",
                    deleted == 1 ? "歌曲已删除，索引已重新建立。" : "无法删除歌曲目录，请查看设备日志。", null, false);
            }, true);
        }
        private void PrepareGameplay()
        {
            if (multiplayerSelectingSong)
            {
                SendMultiplayerSongSelectionV2();
                return;
            }
            if (selectedSong == null || selectedChart == null) { ShowModal("尚未选择歌曲", "请先选择一首歌曲和难度。", null, false); return; }
            if (gameplaySession == null) { ShowModal("游玩模块不可用", "没有找到本地谱面播放模块。", null, false); return; }
            if (transitioning) return;
            if (currentPage == AppPage.SongSelect) {
                StartCoroutine(EnterSelectedGameplayThroughLoading());
                return;
            }
            PrepareSelectedGameplayScene();
            SwitchTo(AppPage.Gameplay, StartSelectedGameplayPlayback);
        }

        private void PrepareSelectedGameplayScene()
        {
            playedSong = selectedSong;
            playedChart = selectedChart;
            delayCalibrationActive = false;
            SetDelayCalibrationUi(false);
            playedHasAnyMod = mods.HasAnyMod;
            playedModLabel = CurrentModSummary();
            playedRecordsScore = mods.RecordsScore;
            if (!multiplayerInRound)
            {
                if (gameplayRestartButton != null) gameplayRestartButton.gameObject.SetActive(true);
                if (gameplayExitButton != null)
                {
                    SetButtonLabel(gameplayExitButton, "返回选曲");
                    gameplayExitButton.onClick.RemoveAllListeners();
                    gameplayExitButton.onClick.AddListener(ExitGameplay);
                }
            }
            CaptureGameplayTimingSettings();
            playedDeltas.Clear();
            pauseTapPendingUntil = 0f;
            preview?.StopPreview();
            SetGameplayScene(true);
            ApplyRuntimeSettings();
            gameplayBackdrop?.Configure(settings.BackgroundBrightness / 100f, settings.JacketBackgroundOpacity / 100f);
            gameplayBackdrop?.ShowJacket(playedSong.JacketPath);
            SetGameplaySongInfo(playedSong, playedChart);
            gameplayPausePanel.gameObject.SetActive(false);
            gameplayCountdown.text = "";
        }

        private void StartSelectedGameplayPlayback()
        {
            gameplaySession.PlaySong(playedSong, playedChart,
                mods.MusicRate, mods.AutoPlay, mods.AirAreaAuto, mods.DirectionalAirAuto,
                mods.MineImmunity, mods.AirLongNoMotion,
                musicOffsetMs: playedMusicOffsetMs, judgementOffsetMs: playedJudgeOffsetMs,
                mirrorChart: mods.MirrorChart,
                nonMissJusticeCritical: mods.NonMissJusticeCritical,
                onFailed: message => StartCoroutine(HandleGameplayLoadFailure(
                    FindObjectOfType<RuntimeLoadingOverlay>(), message)));
        }

        private IEnumerator EnterSelectedGameplayThroughLoading()
        {
            transitioning = true;
            const float transitionDuration = .3f;
            RuntimeLoadingOverlay loading = FindObjectOfType<RuntimeLoadingOverlay>();
            interfaceAudio?.BeginTransition(currentPage, AppPage.Gameplay, transitionDuration);
            if (loading != null)
                yield return loading.ShowAnimated("正在进入加载画面…", 0f, transitionDuration);
            else
                yield return new WaitForSecondsRealtime(transitionDuration);

            AppPage previousPage = currentPage;
            PrepareSelectedGameplayScene();
            SetPageImmediate(pages[previousPage], false);
            SetPageImmediate(pages[AppPage.Gameplay], true);
            currentPage = AppPage.Gameplay;
            loading?.SetProgress("正在读取谱面…", .02f);
            gameplaySession.PlaySong(playedSong, playedChart,
                mods.MusicRate, mods.AutoPlay, mods.AirAreaAuto, mods.DirectionalAirAuto,
                    mods.MineImmunity, mods.AirLongNoMotion,
                musicOffsetMs: playedMusicOffsetMs, judgementOffsetMs: playedJudgeOffsetMs,
                mirrorChart: mods.MirrorChart,
                nonMissJusticeCritical: mods.NonMissJusticeCritical,
                onReady: () => StartCoroutine(RevealGameplayAfterLoading(loading)),
                onFailed: message => StartCoroutine(HandleGameplayLoadFailure(loading, message)),
                keepLoadingVisible: true, deferStart: true);
        }

        private IEnumerator RevealGameplayAfterLoading(RuntimeLoadingOverlay loading)
        {
            if (loading != null) yield return loading.HideAnimated(.3f);
            transitioning = false;
            if (currentPage == AppPage.Gameplay) gameplaySession?.StartPreparedWithPreparation();
        }

        private IEnumerator HandleGameplayLoadFailure(RuntimeLoadingOverlay loading, string message)
        {
            loading?.Hide();
            ClearGameplayVisuals();
            SetGameplayScene(false);
            SetPageImmediate(pages[AppPage.Gameplay], false);
            SetPageImmediate(pages[AppPage.SongSelect], true);
            currentPage = AppPage.SongSelect;
            transitioning = false;
            if (selectedSong != null) preview?.SelectSong(selectedSong.Root);
            yield return null;
            ShowModal("歌曲加载失败", string.IsNullOrWhiteSpace(message) ? "无法读取歌曲资源。" : message, null, false);
        }

        private void SetGameplayScene(bool active)
        {
            HighRefreshRateController.SetGameplayActive(active);
            if (gameplayCamera != null) gameplayCamera.SetActive(active);
            if (gameplaySession != null) gameplaySession.gameObject.SetActive(active);
        }

        private void PauseGameplay()
        {
            if (currentPage != AppPage.Gameplay || gameplaySession == null) return;
            pauseTapPendingUntil = 0f;
            gameplaySession.PausePlayback();
            ConfigureMultiplayerPauseV2();
            gameplayPausePanel.gameObject.SetActive(true);
        }

        private void RequestPauseGameplay()
        {
            if (!settings.PauseRequiresDoubleTap) {
                PauseGameplay();
                return;
            }
            float now = Time.unscaledTime;
            if (now <= pauseTapPendingUntil) {
                pauseTapPendingUntil = 0f;
                PauseGameplay();
            } else {
                pauseTapPendingUntil = now + .55f;
            }
        }

        private void ResumeGameplayWithCountdown()
        {
            if (currentPage != AppPage.Gameplay) return;
            gameplayPausePanel.gameObject.SetActive(false);
            if (gameplaySession != null && gameplaySession.Clock.Preparing) {
                gameplaySession.ResumePlayback();
                return;
            }
            StartCoroutine(ResumeCountdown());
        }

        private IEnumerator ResumeCountdown()
        {
            for (int value = 3; value >= 1; value--)
            {
                gameplayCountdown.text = value.ToString();
                yield return new WaitForSecondsRealtime(1f);
                if (currentPage != AppPage.Gameplay) yield break;
            }
            gameplayCountdown.text = "";
            gameplaySession?.ResumePlayback();
        }

        private void RestartGameplay()
        {
            if (playedSong == null || playedChart == null || gameplaySession == null) return;
            pauseTapPendingUntil = 0f;
            gameplayPausePanel.gameObject.SetActive(false);
            gameplayCountdown.text = "";
            playedDeltas.Clear();
            CaptureGameplayTimingSettings();
            gameplaySession.PlaySong(playedSong, playedChart, mods.MusicRate, mods.AutoPlay,
                mods.AirAreaAuto, mods.DirectionalAirAuto, mods.MineImmunity, mods.AirLongNoMotion,
                musicOffsetMs: playedMusicOffsetMs, judgementOffsetMs: playedJudgeOffsetMs,
                mirrorChart: mods.MirrorChart, nonMissJusticeCritical: mods.NonMissJusticeCritical,
                onFailed: message => StartCoroutine(HandleGameplayLoadFailure(
                    FindObjectOfType<RuntimeLoadingOverlay>(), message)));
        }

        private void CaptureGameplayTimingSettings()
        {
            AppSettings latest = AppSettings.Load();
            playedMusicOffsetMs = AppSettings.NormalizeLatencyOffset(latest.MusicOffsetMs);
            playedJudgeOffsetMs = AppSettings.NormalizeLatencyOffset(latest.JudgeOffsetMs);
            playedDspBufferSize = AppSettings.NormalizeDspBufferSize(latest.DspBufferSize);
            LowLatencyAudioController.Apply(playedDspBufferSize);
            AudioConfiguration active = AudioSettings.GetConfiguration();
            Debug.Log($"Gameplay timing snapshot: musicOffset={playedMusicOffsetMs}ms, " +
                      $"judgeOffset={playedJudgeOffsetMs}ms, requestedDsp={playedDspBufferSize}, " +
                      $"actualDsp={active.dspBufferSize}, sampleRate={active.sampleRate}Hz, " +
                      $"touchRange={settings.TouchContactRangeTenths / 10f:F1} lanes");
        }

        private void ExitGameplay()
        {
            if (multiplayerInRound)
            {
                ExitMultiplayerRoundV2();
                return;
            }
            pauseTapPendingUntil = 0f;
            gameplayPausePanel.gameObject.SetActive(false);
            gameplayCountdown.text = "";
            gameplaySession?.StopPlayback();
            ClearGameplayVisuals();
            SetGameplayScene(false);
            SwitchTo(AppPage.SongSelect, () => {
                if (selectedSong != null) preview?.SelectSong(selectedSong.Root);
            });
        }

        private void ClearGameplayVisuals()
        {
            gameplayBackdrop?.ClearJacket();
            gameplayEffects?.Clear();
            SetGameplayProgress(0f);
            if (gameplayAirGuide != null) gameplayAirGuide.gameObject.SetActive(false);
            if (gameplayJudgementBlock != null) gameplayJudgementBlock.gameObject.SetActive(false);
            foreach (Image lane in gameplayLaneHighlights)
                if (lane != null) lane.color = new Color(.02f, .15f, .19f,
                    .72f * (settings?.TouchpadOpacity ?? 100) / 100f);
        }

        private void ReturnToSongSelectFromResults()
        {
            SwitchTo(AppPage.SongSelect, () => {
                RefreshSongSelectScores();
                if (selectedSong != null) preview?.SelectSong(selectedSong.Root);
            });
        }

        private void RefreshSongSelectScores()
        {
            if (selectedSong == null) return;
            // Results are written before the page transition completes. Re-render
            // both score surfaces so they read the newly saved record immediately.
            RenderSongDetails();
            if (currentPage == AppPage.SongSelect && songGrid != null)
                RebuildSongGrid(false, true);
        }

        private void BeginSongImport()
        {
            if (importRunning) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try {
                PlatformStoragePaths.OpenSongImportPicker();
            }
            catch (Exception exception) {
                Debug.LogError("Import picker failed: " + exception);
                ShowModal("无法打开文件选择器", exception.Message, null, false);
            }
#else
            string importFolder = System.IO.Path.Combine(PlatformServices.Current.PersistentDataPath, "Import");
            System.IO.Directory.CreateDirectory(importFolder);
            string source = System.IO.Directory.GetFiles(importFolder, "*.zip").FirstOrDefault();
            if (source == null) ShowModal("导入歌曲", "请把 ZIP 放入：\n" + importFolder, null, false);
            else OnImportFilePicked(source);
#endif
        }

        private void BeginBackgroundImageImport()
        {
            if (importRunning) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                PlatformStoragePaths.OpenBackgroundImagePicker();
            }
            catch (Exception exception)
            {
                Debug.LogError("Background image picker failed: " + exception);
                ShowModal("无法打开文件选择器", exception.Message, null, false);
            }
#else
            string importFolder = Path.Combine(PlatformServices.Current.PersistentDataPath,
                "Import", "BackgroundImages");
            Directory.CreateDirectory(importFolder);
            ShowModal("导入背景图片", "Android 版本可直接选择图片。\n编辑器测试请把 PNG/JPG 放入：\n" + importFolder, null, false);
#endif
        }

        public void OnBackgroundImagesStarted(string payload)
        {
            if (!int.TryParse(payload ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int count) || count < 1 || count > BackgroundImageStore.MaxImages)
            {
                ShowImportOverlay("导入背景图片");
                FailImportOverlay(new InvalidOperationException("背景图片数量必须为 1 至 50 张"));
                return;
            }
            ShowImportOverlay("导入背景图片");
            backgroundImageImport = true;
            backgroundImageTotal = count;
            importHadCopyPhase = true;
            importCopyStartedRealtime = Time.realtimeSinceStartup;
            importStatus.text = "正在复制图片";
            importCurrent.text = $"已选择 {count} 张";
            importProgressIndeterminate = true;
            importDetails.text = "图片已选择，正在后台读取…\n读取速度受图片大小、手机存储性能和剩余容量影响，可能需要一些时间。";
        }

        public void OnBackgroundImageCopyProgress(string payload)
        {
            if (!backgroundImageImport) return;
            string[] parts = (payload ?? "").Split('|');
            if (parts.Length < 5 ||
                !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ||
                !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long written))
                return;
            long total = long.TryParse(parts[3], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long parsedTotal) ? parsedTotal : -1;
            float itemFraction = total > 0 ? Mathf.Clamp01(written / (float)total) : 0f;
            float overall = Mathf.Clamp01((index + itemFraction) / Mathf.Max(1, count));
            if (total > 0) importProgressIndeterminate = false;
            importStatus.text = $"正在复制图片  {Mathf.Clamp(index + 1, 1, count)}/{count}";
            importCurrent.text = parts[4] + (total > 0
                ? $"\n{FormatBytes(written)} / {FormatBytes(total)}"
                : $"\n已复制 {FormatBytes(written)}");
            SetImportProgress(.20f * overall);
            float elapsed = Mathf.Max(0, Time.realtimeSinceStartup - importCopyStartedRealtime);
            if (overall > .01f && elapsed >= 1f)
                importEta.text = "预计剩余：" + FormatDuration(elapsed * (1f - overall) / overall);
        }

        public void OnBackgroundImagesPicked(string payload)
        {
            if (!backgroundImageImport) return;
            string[] paths = (payload ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (paths.Length == 0)
            {
                importRunning = false;
                backgroundImageImport = false;
                HideImportOverlay();
                return;
            }
            if (paths.Length > BackgroundImageStore.MaxImages)
            {
                backgroundImageImport = false;
                FailImportOverlay(new InvalidOperationException("背景图片数量必须为 1 至 50 张"));
                return;
            }
            importStatus.text = "正在处理图片";
            importProgressIndeterminate = false;
            importCurrent.text = $"共 {paths.Length} 张，按比例处理后保存";
            importDetails.text = "复制完成，开始检查图片尺寸…";
            StartCoroutine(ProcessBackgroundImageImport(paths));
        }

        public void OnBackgroundImageCopyFailed(string message)
        {
            if (!backgroundImageImport) backgroundImageImport = true;
            backgroundImageImport = false;
            if (importLayer == null || !importLayer.gameObject.activeSelf) ShowImportOverlay("导入背景图片");
            FailImportOverlay(new IOException("复制背景图片失败：" + message));
        }

        private IEnumerator ProcessBackgroundImageImport(IReadOnlyList<string> paths)
        {
            string staging = null;
            int processed = 0;
            int failed = 0;
            try
            {
                staging = BackgroundImageStore.BeginImport();
                backgroundImageStaging = staging;
            }
            catch (Exception exception)
            {
                BackgroundImageStore.AbortImport();
                backgroundImageStaging = null;
                FailImportOverlay(exception);
                TryDeletePickedBackgroundImports(paths);
                backgroundImageImport = false;
                yield break;
            }

            importStartedRealtime = Time.realtimeSinceStartup;
            for (int index = 0; index < paths.Count; index++)
            {
                yield return null;
                string source = paths[index];
                try
                {
                    BackgroundImageStore.ProcessToStaging(source, staging, index);
                    processed++;
                    importDetailLines.Add("完成  " + Path.GetFileName(source) + "  已保持原始长宽比");
                }
                catch (Exception exception)
                {
                    failed++;
                    importDetailLines.Add("失败  " + Path.GetFileName(source) + "  " + exception.Message);
                }
                float fraction = (index + 1f) / Mathf.Max(1, paths.Count);
                importStatus.text = $"正在处理图片  {index + 1}/{paths.Count}";
                importCurrent.text = $"成功 {processed} 张  ·  失败 {failed} 张";
                SetImportProgress(.20f + .80f * fraction);
                UpdateImportEta(.20f + .80f * fraction);
                RefreshImportDetailsText();
            }

            if (processed == 0)
            {
                BackgroundImageStore.AbortImport();
                backgroundImageStaging = null;
                FailImportOverlay(new InvalidDataException("没有可用的图片可以导入"));
                TryDeletePickedBackgroundImports(paths);
                backgroundImageImport = false;
                yield break;
            }

            IReadOnlyList<string> committed;
            try
            {
                committed = BackgroundImageStore.CommitImport(staging);
            }
            catch (Exception exception)
            {
                BackgroundImageStore.AbortImport();
                backgroundImageStaging = null;
                FailImportOverlay(exception);
                TryDeletePickedBackgroundImports(paths);
                backgroundImageImport = false;
                yield break;
            }

            backgroundImageStaging = null;
            settings.BackgroundSkinMode = 2;
            settings.EmojiBackground = false;
            settings.Save();
            CompleteBackgroundImageOverlay(committed.Count, failed);
            HideImportOverlay();
            StartCoroutine(AnimateBackgroundSwitch(2));
            TryDeletePickedBackgroundImports(paths);
            backgroundImageImport = false;
        }

        private void CompleteBackgroundImageOverlay(int imported, int failed)
        {
            importRunning = false;
            importProgressIndeterminate = false;
            importStatus.text = "背景图片导入完成";
            importCurrent.text = $"成功 {imported} 张  ·  跳过 {failed} 张";
            importEta.text = "总用时：" + FormatDuration(Time.realtimeSinceStartup - importStartedRealtime);
            importDetailLines.Add($"已提交 {imported} 张背景图片，显示时会保持长宽比并按屏幕裁切铺满。");
            RefreshImportDetailsText();
            SetImportProgress(1);
            importClose.interactable = true;
        }

        private static void TryDeletePickedBackgroundImports(IEnumerable<string> paths)
        {
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                try
                {
                    string fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                    {
                        directories.Add(Path.GetDirectoryName(fullPath));
                        File.Delete(fullPath);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Unable to remove selected background image cache: " + exception.Message);
                }
            }
            foreach (string directory in directories)
            {
                try
                {
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory, true);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Unable to remove background image cache directory: " + exception.Message);
                }
            }
        }

        // Called by the Android proxy activity after it has streamed the SAF
        // document into this app's cache directory.
        public void OnImportStarted(string displayName)
        {
            ShowImportOverlay(gameplaySkinImportPending ? "导入游玩皮肤" : "导入歌曲");
            importHadCopyPhase = true;
            importCopyStartedRealtime = Time.realtimeSinceStartup;
            importStatus.text = "正在复制到应用缓存";
            importCurrent.text = string.IsNullOrWhiteSpace(displayName) ? "所选 ZIP" : displayName;
            importProgressIndeterminate = true;
            importDetails.text = "文件已选择，正在后台读取…\n读取速度受文件大小、手机存储性能和剩余容量影响，可能需要一些时间。";
        }

        public void OnImportCopyProgress(string payload)
        {
            if (importLayer == null || !importLayer.gameObject.activeSelf)
                OnImportStarted(gameplaySkinImportPending ? "所选皮肤包" : "所选 ZIP");
            string[] parts = (payload ?? "").Split('|');
            if (parts.Length == 0 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long written)) return;
            long total = parts.Length > 1 && long.TryParse(parts[1], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long parsedTotal) ? parsedTotal : -1;
            importStatus.text = "正在复制到应用缓存";
            importCurrent.text = total > 0
                ? $"{FormatBytes(written)} / {FormatBytes(total)}"
                : "已复制 " + FormatBytes(written);
            if (total > 0)
            {
                importProgressIndeterminate = false;
                float fraction = Mathf.Clamp01(written / (float)total);
                SetImportProgress(.20f * fraction);
                float elapsed = Mathf.Max(0, Time.realtimeSinceStartup - importCopyStartedRealtime);
                if (written > 0 && elapsed >= 1f)
                    importEta.text = "复制预计剩余：" + FormatDuration(elapsed * (total - written) / written);
            }
        }

        public void OnImportCopyFailed(string message)
        {
            if (importLayer == null || !importLayer.gameObject.activeSelf) ShowImportOverlay();
            FailImportOverlay(new IOException("复制所选文件失败：" + message));
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "未知";
            const double mb = 1024d * 1024d;
            const double gb = mb * 1024d;
            return bytes >= gb ? (bytes / gb).ToString("0.00", CultureInfo.InvariantCulture) + " GB"
                : (bytes / mb).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        public async void OnImportFilePicked(string path)
        {
            if (gameplaySkinImportPending)
            {
                if (string.IsNullOrWhiteSpace(path)) {
                    gameplaySkinImportPending = false;
                    ShowModal("导入已取消", "没有选择皮肤包。", null, false);
                    return;
                }
                await ImportGameplaySkinPackage(path);
                return;
            }
            if (string.IsNullOrWhiteSpace(path)) { ShowModal("导入已取消", "没有选择文件。", null, false); return; }
            string temporaryCachePath = "";
            if (importLayer == null || !importLayer.gameObject.activeSelf) ShowImportOverlay();
            importStatus.text = "正在分析 ZIP";
            importCurrent.text = Path.GetFileName(path);
            try {
                string persistentDataPath = PlatformServices.Current.PersistentDataPath;
                temporaryCachePath = Path.Combine(Application.temporaryCachePath, "Imports");
                string destinationRoot = PlatformStoragePaths.EnsureAppDirectories().First();
                long availableMemoryBytes = PlatformMemoryInfo.GetAvailableMemoryBytes();
                Debug.Log("Song import available memory: " +
                    (availableMemoryBytes > 0 ? FormatBytes(availableMemoryBytes) : "unknown"));
                SongImportResult imported = await SongImportManager.ImportAsync(path, persistentDataPath,
                    temporaryCachePath, destinationRoot, progress => pendingImportProgress.Enqueue(progress),
                    availableMemoryBytes);
                RefreshCatalogAfterImport(imported);
                CompleteImportOverlay(imported);
            }
            catch (Exception exception) {
                Debug.LogError("Song import UI flow failed: " + exception);
                FailImportOverlay(exception);
            }
            finally {
                TryDeletePickedImport(path, temporaryCachePath);
            }
        }

        private async Task ImportGameplaySkinPackage(string path)
        {
            string temporaryCachePath = Path.Combine(Application.temporaryCachePath, "Imports");
            if (importLayer == null || !importLayer.gameObject.activeSelf) ShowImportOverlay("导入游玩皮肤");
            importStatus.text = "正在读取皮肤资源包";
            importCurrent.text = Path.GetFileName(path);
            importEta.text = "正在校验资源，请稍候";
            importDetails.text = "已选择皮肤包，正在后台读取和校验资源…\n资源数量较多时，读取速度会受设备磁盘性能影响。";
            try
            {
                GameplaySkinInfo skin = await Task.Run(() => GameplaySkinManager.InstallPackage(path));
                GameplaySkinManager.SetActive(skin.Id);
                GameplaySkinManager.Refresh();
                settings.GameplaySkinId = skin.Id;
                settings.Save();
                ApplyRuntimeSettings();
                gameplayChartRenderer?.RefreshSkin();
                gameplayPlayfield?.RefreshSkin();
                importRunning = false;
                importProgressIndeterminate = false;
                importStatus.text = "皮肤导入完成";
                importCurrent.text = skin.Name;
                importEta.text = "版本 " + skin.Version;
                importDetailLines.Clear();
                importDetailLines.Add("已安装：" + skin.Name);
                importDetailLines.Add("作者：" + (string.IsNullOrWhiteSpace(skin.Author) ? "未填写" : skin.Author));
                GameplaySkinResourceSummary summary = GameplaySkinManager.GetResourceSummary(skin);
                if (summary.Missing.Count > 0) {
                    importStatus.text = "皮肤导入完成，部分资源将使用原版";
                    importEta.text = $"版本 {skin.Version}  ·  缺少 {summary.Missing.Count} 项";
                }
                importDetailLines.Add($"资源：音符 {summary.NoteCount}，轨道 {summary.TrackCount}，特效 {summary.EffectCount}，Mesh {summary.MeshCount}，音频 {summary.AudioCount}，UI {summary.UiCount}");
                if (summary.Missing.Count == 0) {
                    importDetailLines.Add("资源检查完成：没有发现缺少的标准资源。");
                } else {
                    importDetailLines.Add($"缺少 {summary.Missing.Count} 项资源；以下缺少项目会使用原版皮肤：");
                    foreach (string missing in summary.Missing) importDetailLines.Add("· " + missing);
                }
                importDetailLines.Add("资源已缓存，下一次进入游玩时使用。");
                RefreshImportDetailsText();
                SetImportProgress(1f);
                importClose.interactable = true;
                RenderSettingsCategory(false);
                if (managementSkinMode) RebuildSkinManagementList();
            }
            catch (Exception exception)
            {
                Debug.LogError("Gameplay skin import failed: " + exception);
                FailImportOverlay(exception);
            }
            finally
            {
                gameplaySkinImportPending = false;
                TryDeletePickedImport(path, temporaryCachePath);
            }
        }

        private static void TryDeletePickedImport(string path, string temporaryCachePath)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(temporaryCachePath)) return;
            try
            {
                string fullPath = Path.GetFullPath(path);
                string cacheRoot = Path.GetFullPath(temporaryCachePath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (fullPath.StartsWith(cacheRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
                    File.Delete(fullPath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unable to remove selected import cache: " + exception.Message);
            }
        }

        private void UpdateGameplayHud()
        {
            if (currentPage != AppPage.Gameplay || gameplaySession == null || gameplayScore == null) return;
            UpdateGameplayFps();
            double duration = gameplaySession.PlaybackDurationMs;
            SetGameplayProgress(duration > 0
                ? Mathf.Clamp01((float)(gameplaySession.PlaybackTimeMs / duration))
                : 0f);
            GameplayScoreState score = gameplaySession.ScoreState;
            if (score == null) return;
            gameplayScore.text = score.Score(gameplaySession.JudgeTotal).ToString("D7");
            gameplayCombo.text = score.Combo > 0 ? score.Combo + "\nCOMBO" : "";
            gameplayJudgements.text = $"JC {score.JusticeCritical}   J {score.Justice}   AT {score.Attack}   MISS {score.Miss}";
            if (gameplayJudgementBlock != null && gameplayJudgementBlock.gameObject.activeSelf && Time.unscaledTime >= gameplayTimingHideAt)
                gameplayJudgementBlock.gameObject.SetActive(false);
            UpdateGameplayInputFeedback();
        }

        private void UpdateGameplayFps()
        {
            if (gameplayFpsLabel == null) return;
            if (settings == null || !settings.ShowGameplayFps) {
                gameplayFpsLabel.gameObject.SetActive(false);
                return;
            }
            gameplayFpsLabel.gameObject.SetActive(true);
            gameplayFpsFrames++;
            gameplayFpsTimer += Time.unscaledDeltaTime;
            if (gameplayFpsTimer >= .5f) {
                float fps = gameplayFpsFrames / Mathf.Max(.001f, gameplayFpsTimer);
                gameplayFpsLabel.text = $"FPS {fps:0}";
                gameplayFpsFrames = 0;
                gameplayFpsTimer = 0f;
            }
        }

        private void SetGameplayProgress(float value)
        {
            if (gameplayProgressFill == null) return;
            RectTransform rect = gameplayProgressFill.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void UpdateGameplayInputFeedback()
        {
            ArcadeInputFrame frame = inputRig?.Ground?.Current;
            if (frame == null) return;
            for (int lane = 0; lane < gameplayLaneHighlights.Count; lane++) {
                bool touched = settings.ShowTouchFeedback && frame.Covers(lane, 1);
                float baseOpacity = settings.TouchpadOpacity / 100f;
                float effectOpacity = settings.TouchEffectOpacity / 100f;
                Color baseColor = new Color(.02f, .15f, .19f, .72f * baseOpacity);
                Color activeColor = new Color(.08f, .92f, 1f, .84f);
                gameplayLaneHighlights[lane].color = touched
                    ? Color.Lerp(baseColor, activeColor, effectOpacity) : baseColor;
            }
            if (gameplayAirGuide == null) return;
            bool show = settings.ShowAirGuide && frame.Air.Present && frame.Air.ContactCount > 0;
            gameplayAirGuide.gameObject.SetActive(show);
            if (!show) return;
            float bottom = settings.AirAreaBottom / 100f;
            float top = settings.AirAreaTop / 100f;
            float y = Mathf.Lerp(bottom, top, frame.Air.MeanHeight);
            gameplayAirGuide.rectTransform.anchorMin = new Vector2(0, y - .0025f);
            gameplayAirGuide.rectTransform.anchorMax = new Vector2(1, y + .0025f);
            gameplayAirGuide.rectTransform.offsetMin = Vector2.zero;
            gameplayAirGuide.rectTransform.offsetMax = Vector2.zero;
            float edge = Mathf.Abs(frame.Air.MeanHeight - .5f) * 2f;
            gameplayAirGuide.color = Color.Lerp(new Color(.95f, .98f, 1f, .95f),
                new Color(1f, .16f, .32f, .95f), edge);
        }

        private void ApplyRuntimeSettings()
        {
            if (settings == null) return;
            HighRefreshRateController.Configure(settings.FrameRateLimit);
            if (!string.Equals(GameplaySkinManager.Active.Id, settings.GameplaySkinId,
                    StringComparison.OrdinalIgnoreCase))
                GameplaySkinManager.SetActive(settings.GameplaySkinId);
            GameplaySkinStyle gameplaySkin = GameplaySkinManager.Style;
            int backgroundMode = EffectiveBackgroundSkinMode();
            foreach (MeteorKeyBackground background in meteorBackgrounds)
                if (background != null) {
                    background.SetHighContrastPalette(HighContrastUi);
                    background.SetBackgroundMode(backgroundMode);
                }
            // Gameplay audio applies music and chart-answer levels while
            // building its single PCM track. Interface music uses its own
            // sources, so the global listener must stay neutral.
            AudioListener.volume = 1f;
            interfaceAudio?.Configure(settings.KeySoundEnabled, settings.KeySoundVolume / 100f,
                settings.AnswerSoundEnabled, settings.AnswerSoundVolume / 100f,
                settings.BackgroundMusicVolume / 100f);
            interfaceAudio?.RefreshSkinAudio();
            float groundHeight = Mathf.Clamp(settings.GroundAreaHeight / 100f, .08f, .45f);
            float airBottom = Mathf.Clamp(settings.AirAreaBottom / 100f, .08f, .85f);
            float airTop = Mathf.Clamp(settings.AirAreaTop / 100f, airBottom + .05f, 1f);
            inputRig?.Ground?.SetPlayArea(new Rect(0, 0, 1, groundHeight));
            inputRig?.Ground?.SetTouchpadWidth(settings.TouchpadWidth / 100f);
            inputRig?.Ground?.SetContactRangeLanes(settings.TouchContactRangeTenths / 10f);
            inputRig?.Air?.SetAirArea(new Rect(0, airBottom, 1, airTop - airBottom));
            if (gameplayTouchpad != null) {
                float width = Mathf.Clamp(settings.TouchpadWidth / 100f, .6f, 1f);
                gameplayTouchpad.anchorMin = new Vector2((1f - width) * .5f, 0);
                gameplayTouchpad.anchorMax = new Vector2((1f + width) * .5f,
                    Mathf.Clamp(settings.TouchpadHeight / 100f, .05f, .45f));
                gameplayTouchpad.offsetMin = Vector2.zero; gameplayTouchpad.offsetMax = Vector2.zero;
                gameplayTouchpad.localRotation = Quaternion.Euler(0f, 0f, -settings.TouchpadTilt);
                Image touchpadImage = gameplayTouchpad.GetComponent<Image>();
                if (touchpadImage != null)
                    touchpadImage.color = new Color(.015f, .08f, .105f, .82f * settings.TouchpadOpacity / 100f);
            }
            UpdateAirZone(gameplayAirZone, airBottom, airTop);
            PositionHorizontalLine(gameplayAirBoundaryBottom, airBottom);
            PositionHorizontalLine(gameplayAirBoundaryTop, airTop);
            gameplayTouchContacts?.Configure(settings.ShowTouchFeedback, groundHeight,
                settings.TouchEffectOpacity / 100f);
            gameplayPlayfield?.ConfigureTopWidth(settings.TrackTopWidth);
            gameplayTrackMask?.Configure(settings.TrackMaskEnabled, settings.TrackMaskHeight, settings.TrackMaskOpacity);
            if (gameplaySession != null) {
                gameplaySession.transform.localPosition = new Vector3(0, settings.TrackVerticalOffset * .04f, 0);
                gameplaySession.transform.localScale = new Vector3(settings.TrackWidth / 100f, 1f, 1f);
            }
            if (gameplayEffects != null)
                gameplayEffects.transform.localScale = new Vector3(settings.TrackWidth / 100f, 1f, 1f);
            if (gameplayChartRenderer != null)
            {
                gameplayChartRenderer.RefreshSkin();
                gameplayChartRenderer.ConfigureAirTrail(settings.AirTrailRainbow);
                gameplayChartRenderer.VisualSpeed = OriginalNoteSpeed.VisualMultiplier(settings.NoteSpeedOption);
            }
            gameplayPlayfield?.RefreshSkin();
            gameplayEffects?.ConfigureSkin(gameplaySkin);
            if (gameplayScore != null) gameplayScore.color = gameplaySkin.ScoreColor;
            if (gameplayCombo != null) gameplayCombo.color = gameplaySkin.ComboColor;
            RefreshJudgementSkin();
            if (gameplayJudgements != null) gameplayJudgements.color = gameplaySkin.JudgementColor;
            if (resultScore != null) resultScore.color = gameplaySkin.ScoreColor;
            if (resultRank != null) resultRank.color = gameplaySkin.RankColor;
            if (resultJudgements != null) resultJudgements.color = gameplaySkin.JudgementColor;
            ApplyGameplayElementLayout(settings);
            if (gameplaySongInfoBlock != null) gameplaySongInfoBlock.gameObject.SetActive(settings.ShowGameplaySongInfo);
            gameplayBackdrop?.Configure(settings.BackgroundBrightness / 100f, settings.JacketBackgroundOpacity / 100f);
            FindObjectOfType<RuntimeLoadingOverlay>()?.SetHighContrastPalette(HighContrastUi);
        }

        private static void UpdateAirZone(RectTransform zone, float bottom, float top)
        {
            if (zone == null) return;
            bottom = Mathf.Clamp01(bottom);
            top = Mathf.Clamp(top, bottom, 1f);
            zone.anchorMin = new Vector2(0, bottom);
            zone.anchorMax = new Vector2(1, top);
            zone.offsetMin = Vector2.zero;
            zone.offsetMax = Vector2.zero;
        }

        private static void PositionHorizontalLine(Image line, float y)
        {
            if (line == null) return;
            line.rectTransform.anchorMin = new Vector2(0, Mathf.Clamp01(y) - .002f);
            line.rectTransform.anchorMax = new Vector2(1, Mathf.Clamp01(y) + .002f);
            line.rectTransform.offsetMin = Vector2.zero;
            line.rectTransform.offsetMax = Vector2.zero;
        }

        private static void PositionGameplayElement(RectTransform rect, int xPercent, int yPercent, Vector2 size)
        {
            if (rect == null) return;
            Vector2 anchor = new Vector2(Mathf.Clamp01(xPercent / 100f), Mathf.Clamp01(yPercent / 100f));
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        private void ApplyGameplayElementLayout(AppSettings source)
        {
            if (source == null) return;
            float pauseScale = Mathf.Clamp(source.GameplayPauseScale, 50, 180) / 100f;
            float scoreScale = Mathf.Clamp(source.GameplayScoreScale, 50, 180) / 100f;
            float comboScale = Mathf.Clamp(source.GameplayComboScale, 50, 180) / 100f;
            float judgementScale = Mathf.Clamp(source.GameplayJudgementScale, 50, 180) / 100f;
            float songScale = Mathf.Clamp(source.GameplaySongInfoScale, 50, 180) / 100f;

            PositionGameplayElement(gameplayPauseButton?.transform as RectTransform,
                source.GameplayPauseX, source.GameplayPauseY, new Vector2(110, 104) * pauseScale);
            PositionGameplayElement(gameplayHud, source.GameplayHudX, source.GameplayHudY,
                new Vector2(350, 160) * scoreScale);
            PositionGameplayElement(gameplayCombo?.rectTransform, source.GameplayComboX, source.GameplayComboY,
                new Vector2(320, 180) * comboScale);
            PositionGameplayElement(gameplayJudgementBlock, source.GameplayJudgementX, source.GameplayJudgementY,
                new Vector2(430, 100) * judgementScale);
            PositionGameplayElement(gameplaySongInfoBlock, source.GameplaySongInfoX, source.GameplaySongInfoY,
                new Vector2(460, 132) * songScale);

            if (gameplayScore != null) gameplayScore.fontSize = Mathf.Max(18, Mathf.RoundToInt(42 * scoreScale));
            if (gameplayJudgements != null) gameplayJudgements.fontSize = Mathf.Max(9, Mathf.RoundToInt(16 * scoreScale));
            if (gameplayCombo != null) gameplayCombo.fontSize = Mathf.Max(24, Mathf.RoundToInt(58 * comboScale));
            if (gameplayTiming != null) gameplayTiming.fontSize = Mathf.Max(11, Mathf.RoundToInt(22 * judgementScale));
            if (gameplaySongInfo != null) {
                gameplaySongInfo.fontSize = Mathf.Max(11, Mathf.RoundToInt(20 * songScale));
                gameplaySongInfo.rectTransform.offsetMin = new Vector2(128 * songScale, 0);
                gameplaySongInfo.rectTransform.offsetMax = new Vector2(-16 * songScale, 0);
            }
            if (gameplaySongInfoCoverMask != null) {
                gameplaySongInfoCoverMask.sizeDelta = Vector2.one * 108 * songScale;
                gameplaySongInfoCoverMask.anchoredPosition = new Vector2(64 * songScale, 0);
            }
        }

        private void OnGameplayJudged(ScheduledJudgePoint point, JudgementResult judgement)
        {
            gameplayChartRenderer?.MarkJudged(point.NoteIndex, point.PointIndex);
            if (point.PointIndex == 0 && judgement.Grade == ChunithmMobile.Chart.Judgement.Miss &&
                gameplayJudge != null && gameplayJudge.TryGetNote(point.NoteIndex, out ChartNote soundNote) &&
                soundNote.Kind == NoteKind.Mine)
                interfaceAudio?.PlayMineKeySound();
            if (delayCalibrationActive && delayCalibrationLive != null)
            {
                bool calibrationMiss = judgement.Grade == ChunithmMobile.Chart.Judgement.Miss;
                if (calibrationMiss)
                {
                    // A timeout only tells us that no valid tap arrived. Its
                    // late delta is not a measured input delay and must not be
                    // presented as LATE to the player.
                    delayCalibrationLive.text = "TAP  " + (point.PointIndex + 1).ToString("D2") + "    MISS";
                    delayCalibrationLive.color = new Color(1f, .20f, .28f);
                }
                else
                {
                    double delta = Math.Max(-500, Math.Min(500, judgement.DeltaMs));
                    string calibrationDirection = delta < -.05 ? "FAST" : delta > .05 ? "LATE" : "JUST";
                    delayCalibrationLive.text = "TAP  " + (point.PointIndex + 1).ToString("D2") +
                        "    " + delta.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " ms  " + calibrationDirection;
                    delayCalibrationLive.color = calibrationDirection == "JUST" ? new Color(.40f, 1f, .54f) :
                        calibrationDirection == "FAST" ? GameplayAccent : new Color(1f, .68f, .12f);
                }
            }
            bool miss = judgement.Grade == ChunithmMobile.Chart.Judgement.Miss;
            if (point.IsScoring && (!miss || (!delayCalibrationActive && !settings.ExcludeMissFromAverage)))
                playedDeltas.Add(judgement.DeltaMs);
            bool showMilliseconds = judgement.Grade switch {
                ChunithmMobile.Chart.Judgement.JusticeCritical => settings.ShowJusticeCriticalMs,
                ChunithmMobile.Chart.Judgement.Justice => settings.ShowJusticeMs,
                ChunithmMobile.Chart.Judgement.Attack => settings.ShowAttackMs,
                _ => false
            };
            if (gameplayTiming == null) return;
            ShowGameplayJudgement(judgement.Grade, judgement.DeltaMs, showMilliseconds);
            if (gameplayJudge != null && gameplayJudge.TryGetNote(point.NoteIndex, out ChartNote note))
                gameplayEffects?.Play(note, judgement, GameplayJudgementEffectY(), point.PointIndex);
        }

        private void RefreshJudgementSkin()
        {
            for (int i = 0; i < gameplayJudgementTextures.Length; i++)
                gameplayJudgementTextures[i] = GameplaySkinManager.LoadJudgementTexture((ChunithmMobile.Chart.Judgement)i);
            if (gameplayJudgementImage != null) gameplayJudgementImage.texture = null;
            if (gameplayJudgementBlock != null) gameplayJudgementBlock.gameObject.SetActive(false);
        }

        private void ShowGameplayJudgement(ChunithmMobile.Chart.Judgement result, double deltaMs, bool showMilliseconds)
        {
            if (gameplayJudgementBlock == null) return;
            Texture2D texture = gameplayJudgementTextures[(int)result];
            gameplayJudgementImage.texture = texture;
            gameplayJudgementImage.gameObject.SetActive(texture != null);
            if (texture != null)
                gameplayJudgementImage.GetComponent<AspectRatioFitter>().aspectRatio = (float)texture.width / texture.height;
            string grade = result switch {
                ChunithmMobile.Chart.Judgement.JusticeCritical => "JUSTICE CRITICAL",
                ChunithmMobile.Chart.Judgement.Justice => "JUSTICE",
                ChunithmMobile.Chart.Judgement.Attack => "ATTACK",
                _ => "MISS"
            };
            string direction = deltaMs < -.05 ? "FAST" : deltaMs > .05 ? "LATE" : "JUST";
            gameplayTiming.text = (texture == null ? grade : "") + (showMilliseconds && result != ChunithmMobile.Chart.Judgement.Miss
                ? "  " + direction + " " + deltaMs.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " ms"
                : "");
            gameplayTiming.color = result switch {
                ChunithmMobile.Chart.Judgement.JusticeCritical => GameplayAccent,
                ChunithmMobile.Chart.Judgement.Justice => new Color(.40f, 1f, .54f),
                ChunithmMobile.Chart.Judgement.Attack => new Color(1f, .68f, .12f),
                _ => new Color(1f, .20f, .28f)
            };
            gameplayJudgementBlock.gameObject.SetActive(true);
            gameplayTimingHideAt = Time.unscaledTime + .62f;
        }

        private float GameplayJudgementEffectY()
        {
            Camera camera = gameplayCamera != null ? gameplayCamera.GetComponent<Camera>() : null;
            // Judgement effects belong to the same world-space gold line as
            // the note heads. The touchpad boundary is only an input surface;
            // using it here placed feedback below the actual judgement point.
            if (camera == null || gameplaySession == null) return .18f;
            Vector3 lineCenter = gameplaySession.transform.TransformPoint(
                new Vector3(0, ChartSpace.GroundY, ChartSpace.JudgementZ));
            Vector3 viewport = camera.WorldToViewportPoint(lineCenter);
            return Mathf.Clamp(viewport.y, .04f, .92f);
        }

        private void OnGameplayCompleted()
        {
            if (currentPage != AppPage.Gameplay) return;
            if (multiplayerInRound)
            {
                FinishMultiplayerGameplayV2();
                return;
            }
            if (delayCalibrationActive) {
                ShowDelayCalibrationResults();
                return;
            }
            StartCoroutine(FinishGameplay());
        }

        private void ShowDelayCalibrationResults()
        {
            delayCalibrationActive = false;
            gameplaySession?.StopPlayback();
            ClearGameplayVisuals();
            SetGameplayScene(false);

            int samples = playedDeltas.Count;
            double average = samples == 0 ? 0 : playedDeltas.Average();
            double[] fast = playedDeltas.Where(value => value < 0).ToArray();
            double[] late = playedDeltas.Where(value => value > 0).ToArray();
            double fastAverage = fast.Length == 0 ? 0 : fast.Average();
            double lateAverage = late.Length == 0 ? 0 : late.Average();
            if (delayCalibrationResultSummary != null)
                delayCalibrationResultSummary.text = samples == 0
                    ? "本轮没有采集到判定数据"
                    : $"已完成 {samples} 次判定";
            if (delayCalibrationResultStats != null)
                delayCalibrationResultStats.text =
                    $"平均偏差  {average:+0.0;-0.0;0.0} ms\n" +
                    $"FAST 平均  {fastAverage:+0.0;-0.0;0.0} ms\n" +
                    $"LATE 平均  {lateAverage:+0.0;-0.0;0.0} ms";
            SwitchTo(AppPage.DelayCalibrationResults);
        }

        private void RestartDelayCalibration()
        {
            if (playedSong == null || playedChart == null || gameplaySession == null) return;
            playedDeltas.Clear();
            delayCalibrationActive = true;
            SetGameplayScene(true);
            ApplyRuntimeSettings();
            gameplayPausePanel.gameObject.SetActive(false);
            gameplayCountdown.text = "";
            SetDelayCalibrationUi(true);
            SwitchTo(AppPage.Gameplay, StartDelayCalibrationPlayback);
        }

        private void ExitDelayCalibrationResults()
        {
            gameplaySession?.StopPlayback();
            delayCalibrationActive = false;
            ClearGameplayVisuals();
            SetDelayCalibrationUi(false);
            SetGameplayScene(false);
            SwitchTo(AppPage.Settings);
        }

        private IEnumerator FinishGameplay()
        {
            yield return null;
            if (playedSong == null || playedChart == null || gameplaySession == null) yield break;
            GameplayScoreState score = gameplaySession.ScoreState;
            int total = gameplaySession.JudgeTotal;
            double[] fast = playedDeltas.Where(value => value < 0).ToArray();
            double[] late = playedDeltas.Where(value => value > 0).ToArray();
            var model = new ResultViewModel {
                SongName = playedSong.Name,
                Artist = playedSong.Artist,
                Difficulty = playedChart.Difficulty,
                Level = playedChart.Level,
                Score = score?.Score(total) ?? 0,
                JusticeCritical = score?.JusticeCritical ?? 0,
                Justice = score?.Justice ?? 0,
                Attack = score?.Attack ?? 0,
                Miss = score?.Miss ?? 0,
                FastAverageMs = fast.Length == 0 ? 0 : fast.Average(),
                LateAverageMs = late.Length == 0 ? 0 : late.Average(),
                TotalAverageMs = playedDeltas.Count == 0 ? 0 : playedDeltas.Average(),
                ModLabel = playedHasAnyMod ? playedModLabel : "NORMAL"
            };
            gameplaySession.StopPlayback();
            ClearGameplayVisuals();
            SetGameplayScene(false);
            ShowResults(model, playedSong, playedChart, playedRecordsScore);
        }

        private void ShowResults(ResultViewModel model, SongEntry playedSong, SongChartEntry playedChart, bool recordsScore)
        {
            if (!multiplayerPersonalResultPending)
            {
                if (resultReplayButton != null)
                {
                    resultReplayButton.gameObject.SetActive(true);
                    resultReplayButton.interactable = true;
                    resultReplayButton.onClick.RemoveAllListeners();
                    resultReplayButton.onClick.AddListener(PrepareGameplay);
                }
                if (resultContinueButton != null)
                {
                    resultContinueButton.gameObject.SetActive(true);
                    resultContinueButton.interactable = true;
                    resultContinueButton.onClick.RemoveAllListeners();
                    resultContinueButton.onClick.AddListener(ReturnToSongSelectFromResults);
                    SetButtonLabel(resultContinueButton, "返回选曲");
                }
            }
            int previousBest = 0;
            int previousLast = 0;
            if (recordsScore && model.Score > 0)
                SongRecordStore.SaveResult(playedSong.Root, playedChart.Difficulty,
                    playedChart.Path, model.Score, out previousBest, out previousLast);
            else
                SongRecordStore.TryGet(playedSong.Root, playedChart.Difficulty, playedChart.Path, out previousBest);
            bool hasCover = !string.IsNullOrWhiteSpace(playedSong.JacketPath);
            LocalCoverLoader.Reset(resultCover);
            resultCover.gameObject.SetActive(hasCover);
            resultCoverPlaceholder.SetActive(!hasCover);
            if (hasCover) LocalCoverLoader.Load(this, playedSong.JacketPath, resultCover, false);
            Image coverFrame = resultCover.transform.parent?.GetComponent<Image>();
            if (coverFrame != null) coverFrame.color = Color.Lerp(surfaceRaised,
                DifficultyColor(playedChart.Difficulty), .36f);
            result = model; resultSong.text = model.SongName + "\n" + model.Artist + "\n" + model.Difficulty + "  Lv." + model.Level;
            resultRank.text = model.Rank; resultScore.text = model.Score.ToString("N0");
            bool hadPreviousRecord = previousBest > 0 || previousLast > 0;
            bool improvedLast = recordsScore && hadPreviousRecord && previousLast > 0 && model.Score > previousLast;
            bool showNew = recordsScore && model.Score > 0 && (!hadPreviousRecord || improvedLast);
            if (!hadPreviousRecord)
            {
                // A first result has no historical score to compare against. Keep
                // the result panel clean and use NEW as the only first-play signal.
                resultPreviousScore.text = "";
                resultPreviousScore.gameObject.SetActive(false);
            }
            else
            {
                resultPreviousScore.gameObject.SetActive(true);
                if (!recordsScore)
                    resultPreviousScore.text = "最好分数  " + previousBest.ToString("N0");
                else if (improvedLast)
                    resultPreviousScore.text = "上次分数  " + previousLast.ToString("N0");
                else
                    resultPreviousScore.text = "最好分数  " + previousBest.ToString("N0");
            }
            if (resultNewPulse != null) StopCoroutine(resultNewPulse);
            resultNewBadge.gameObject.SetActive(showNew);
            if (showNew) resultNewPulse = StartCoroutine(PulseNewResultBadge());
            Color resultColor = playedHasAnyMod ? Color.white
                : GameplaySkinManager.Style.RankColorFor(model.Rank);
            if (!playedHasAnyMod && resultColor == Color.white)
                resultColor = new Color(1f, .48f, .20f, 1f);
            resultRank.color = resultColor;
            resultScore.color = playedHasAnyMod ? Color.white : new Color(1f, .48f, .20f, 1f);
            resultJudgements.text = $"{model.ModLabel}\n\nJC   {model.JusticeCritical}\nJUSTICE   {model.Justice}\nATTACK   {model.Attack}\nMISS   {model.Miss}\n\nFAST 平均   {model.FastAverageMs:+0.0;-0.0;0.0} ms\nLATE 平均   {model.LateAverageMs:+0.0;-0.0;0.0} ms\n总平均   {model.TotalAverageMs:+0.0;-0.0;0.0} ms";
            SwitchTo(AppPage.Results);
        }

        private IEnumerator PulseNewResultBadge()
        {
            float started = Time.unscaledTime;
            while (resultNewBadge != null && resultNewBadge.gameObject.activeSelf)
            {
                float pulse = .72f + .28f * Mathf.Sin((Time.unscaledTime - started) * 10f);
                Color color = new Color(1f, .82f, .12f, pulse);
                resultNewBadge.color = color;
                resultNewBadge.rectTransform.localScale = Vector3.one * Mathf.Lerp(.96f, 1.08f, pulse);
                yield return null;
            }
            if (resultNewBadge != null) {
                resultNewBadge.color = new Color(1f, .82f, .12f, 1f);
                resultNewBadge.rectTransform.localScale = Vector3.one;
            }
            resultNewPulse = null;
        }

        private void SwitchTo(AppPage page, Action onCompleted = null)
        {
            if (page == currentPage) { onCompleted?.Invoke(); return; }
            if (transitioning)
            {
                // Keep the latest navigation request. Rapid taps should settle on
                // the page the user most recently asked for without stacking fades.
                queuedPage = page;
                queuedPageCompletion = onCompleted;
                return;
            }
            EnsurePageBuilt(page);
            if (!pages.ContainsKey(page)) return;
            StartCoroutine(AnimateSwitch(page, onCompleted));
        }

        private IEnumerator AnimateSwitch(AppPage next, Action onCompleted)
        {
            transitioning = true;
            EnsurePageBuilt(next);
            if (currentPage == AppPage.DataManagement) StopManagementBuild();
            const float duration = .24f;
            if (next != AppPage.Results && resultNewPulse != null)
            {
                StopCoroutine(resultNewPulse);
                resultNewPulse = null;
            }
            RectTransform from = pages[currentPage];
            RectTransform to = pages[next];
            CanvasGroup fromGroup = from.GetComponent<CanvasGroup>();
            CanvasGroup toGroup = to.GetComponent<CanvasGroup>();
            interfaceAudio?.BeginTransition(currentPage, next, duration);
            fromGroup.blocksRaycasts = false;
            fromGroup.interactable = false;
            to.gameObject.SetActive(true);
            toGroup.alpha = 0;
            toGroup.blocksRaycasts = false;
            toGroup.interactable = false;
            Vector2 end = to.anchoredPosition;
            Vector2 start = end + new Vector2(72, 0);
            to.anchoredPosition = start;
            to.localScale = new Vector3(.985f, .985f, 1);
            float time = 0;
            while (time < duration) {
                time += Time.unscaledDeltaTime;
                float linear = Mathf.Clamp01(time / duration);
                float eased = 1f - Mathf.Pow(1f - linear, 3f);
                fromGroup.alpha = 1f - eased;
                toGroup.alpha = eased;
                to.anchoredPosition = Vector2.Lerp(start, end, eased);
                to.localScale = Vector3.one * Mathf.Lerp(.985f, 1f, eased);
                yield return null;
            }
            to.anchoredPosition = end;
            to.localScale = Vector3.one;
            SetPageImmediate(from, false);
            SetPageImmediate(to, true);
            currentPage = next;
            transitioning = false;
            if (next == AppPage.DataManagement) RebuildManagementList();
            onCompleted?.Invoke();
            if (!transitioning && queuedPage.HasValue)
            {
                AppPage pendingPage = queuedPage.Value;
                Action pendingCompletion = queuedPageCompletion;
                queuedPage = null;
                queuedPageCompletion = null;
                if (pendingPage != currentPage) StartCoroutine(AnimateSwitch(pendingPage, pendingCompletion));
                else pendingCompletion?.Invoke();
            }
        }

        private void SetPageImmediate(RectTransform page, bool shown)
        {
            if (page == null) return;
            page.gameObject.SetActive(shown);
            page.anchoredPosition = Vector2.zero;
            page.localScale = Vector3.one;
            CanvasGroup group = page.GetComponent<CanvasGroup>();
            group.alpha = shown ? 1 : 0;
            group.blocksRaycasts = shown;
            group.interactable = shown;
        }
        private RectTransform Page(AppPage page, Color color)
        {
            RectTransform rect = Panel(page.ToString(), canvas.transform, color, Vector2.zero, Vector2.one);
            // Panel() adds breathing room for cards; a page is a true full-screen
            // surface and must cover the camera all the way to every pixel edge.
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.gameObject.AddComponent<CanvasGroup>();
            pages[page] = rect;
            return rect;
        }

        private void AddMeteorBackground(RectTransform page, MeteorBackgroundProfile profile)
        {
            var backgroundObject = new GameObject("Meteor key background", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(MeteorKeyBackground));
            RectTransform rect = (RectTransform)backgroundObject.transform;
            rect.SetParent(page, false);
            rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            MeteorKeyBackground background = backgroundObject.GetComponent<MeteorKeyBackground>();
            background.raycastTarget = false;
            background.Configure(profile);
            background.SetHighContrastPalette(HighContrastUi);
            background.SetBackgroundMode(settings == null ? 0 : EffectiveBackgroundSkinMode());
            meteorBackgrounds.Add(background);
        }
        private void SetLoading(float value, string text) { loadingLabel.text = text; RectTransform rect = loadingFill.rectTransform; rect.anchorMax = new Vector2(Mathf.Clamp01(value), 1); rect.offsetMax = Vector2.zero; }
        private static float LevelValue(string level)
        {
            if (string.IsNullOrWhiteSpace(level)) return float.PositiveInfinity;
            string normalized = level.Trim();
            if (string.Equals(normalized, "WD", StringComparison.OrdinalIgnoreCase)) return 1000f;
            bool plus = normalized.EndsWith("+", StringComparison.Ordinal);
            if (plus) normalized = normalized.Substring(0, normalized.Length - 1);
            if (!float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return float.PositiveInfinity;
            return plus ? value + .5f : value;
        }
        private static string LevelKey(string level) => string.IsNullOrWhiteSpace(level) ? string.Empty : level.Trim();
        private static string LevelBucketKey(SongChartEntry chart)
        {
            if (chart == null) return string.Empty;
            return SongCatalog.IsWorldsEnd(chart.Difficulty)
                ? "WD" : LevelKey(chart.Level);
        }
        private Color DifficultyColor(string difficulty)
        {
            switch (difficulty?.ToUpperInvariant())
            {
                case "BASIC": return new Color(.18f, .72f, .38f, 1f);
                case "ADVANCED": return new Color(.94f, .66f, .16f, 1f);
                case "EXPERT": return new Color(.92f, .22f, .30f, 1f);
                case "ULTIMA": return new Color(.95f, .18f, .48f, 1f);
                case "WORLDSEND":
                case "WORLD'S END": return new Color(.72f, .72f, .78f, 1f);
                default: return new Color(.58f, .10f, .92f, 1f);
            }
        }

        private void ShowModal(string title, string body, UnityEngine.Events.UnityAction action, bool showConfirm)
        {
            modalOptionA?.gameObject.SetActive(false); modalOptionB?.gameObject.SetActive(false);
            if (modalConfirm != null) modalConfirm.GetComponentInChildren<Text>().text = UiLocalization.Translate("确定");
            modalTitle.text = title; modalBody.text = body; modalAction = action; modalConfirm.gameObject.SetActive(showConfirm); modalLayer.gameObject.SetActive(true);
            CanvasGroup group = modalLayer.GetComponent<CanvasGroup>() ?? modalLayer.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0; StartCoroutine(Fade(group, 0, 1, .16f));
        }
        private void HideModal() { if (modalLayer != null) modalLayer.gameObject.SetActive(false); modalAction = null; }
        private void ConfirmModal() { UnityEngine.Events.UnityAction action = modalAction; HideModal(); action?.Invoke(); }
        private IEnumerator Fade(CanvasGroup group, float from, float to, float duration) { float time = 0; while (time < duration) { time += Time.unscaledDeltaTime; group.alpha = Mathf.Lerp(from, to, time / duration); yield return null; } group.alpha = to; }
        private void ToggleSetting(string title, string message) => ShowModal(title, message, null, false);
        private void ShowAbout() => ShowModal("CHUNITHM Mobile  " + Application.version, "by: HMQYHM\n\n使用 HarmonyOS Sans 字体。字体版权归 Huawei Device Co., Ltd. 所有。\n\nEmoji 图形使用 Twemoji，采用 CC BY 4.0 许可。许可证全文随软件保留。", null, false);

        private void ConfigureModalOption(Button button, string label, UnityEngine.Events.UnityAction action)
        {
            button.gameObject.SetActive(true); button.onClick.RemoveAllListeners(); button.onClick.AddListener(action); button.GetComponentInChildren<Text>().text = label;
        }

        private void CycleJusticeAttackTiming()
        {
            if (settings.ShowJusticeMs && settings.ShowAttackMs) settings.ShowAttackMs = false;
            else if (settings.ShowJusticeMs) { settings.ShowJusticeMs = false; settings.ShowAttackMs = true; }
            else if (settings.ShowAttackMs) settings.ShowAttackMs = false;
            else { settings.ShowJusticeMs = true; settings.ShowAttackMs = true; }
        }

        private void ResetSettings()
        {
            bool rebuild = HighContrastUi;
            settings.Reset();
            if (rebuild) { RebuildUiForLanguage(); return; }
            ApplyRuntimeSettings();
            RenderSettingsCategory(false);
        }

        private void ExportLogs()
        {
            try
            {
                string directory = Path.Combine(Application.temporaryCachePath, "Exports");
                Directory.CreateDirectory(directory);
                string fileName = "chunithm-mobile-logs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt";
                string path = Path.Combine(directory, fileName);
                File.WriteAllText(path, BuildDiagnosticLog(), new UTF8Encoding(false));
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var bridge = new AndroidJavaClass("com.zhongermate.chunithmmobile.LogExportActivity"))
                    bridge.CallStatic("exportFile", path);
#else
                ShowModal("日志已生成", path, null, false);
#endif
            }
            catch (Exception exception)
            {
                Debug.LogError("Log export preparation failed: " + exception);
                ShowModal("日志导出失败", exception.Message, null, false);
            }
        }

        private string BuildDiagnosticLog()
        {
            var output = new StringBuilder(32768);
            output.AppendLine("CHUNITHM Mobile diagnostic export");
            output.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
            output.AppendLine("Version: " + Application.version);
            output.AppendLine("Unity: " + Application.unityVersion);
            output.AppendLine("Platform: " + Application.platform);
            output.AppendLine("OS: " + SystemInfo.operatingSystem);
            output.AppendLine("Device: " + SystemInfo.deviceModel);
            output.AppendLine("CPU: " + SystemInfo.processorType + " (" + SystemInfo.processorCount + ")");
            output.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceVersion);
            output.AppendLine("Memory MB: " + SystemInfo.systemMemorySize);
            output.AppendLine("Screen: " + Screen.width + "x" + Screen.height + " @ " + Screen.currentResolution.refreshRateRatio.value.ToString("0.00", CultureInfo.InvariantCulture));
            output.AppendLine("Persistent data: " + PlatformServices.Current.PersistentDataPath);
            output.AppendLine("Catalog songs: " + catalog.Count);
            output.AppendLine("Catalog load: " + (SongCatalog.LastLoadUsedCache ? "cached index" : "rebuilt index"));
            output.AppendLine();
            output.AppendLine("--- Settings ---");
            output.AppendLine(JsonUtility.ToJson(settings, true));

            var logDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                Path.Combine(PlatformServices.Current.PersistentDataPath, "Logs")
            };
            foreach (string root in PlatformStoragePaths.Current.GetSongRoots())
            {
                string parent = string.IsNullOrWhiteSpace(root) ? null : Directory.GetParent(Path.GetFullPath(root))?.FullName;
                if (!string.IsNullOrWhiteSpace(parent)) logDirectories.Add(Path.Combine(parent, "Logs"));
            }
            FileInfo[] logs = logDirectories.Where(Directory.Exists)
                .SelectMany(path => new DirectoryInfo(path).GetFiles("import-*.log"))
                .GroupBy(file => file.FullName, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
                .OrderByDescending(file => file.LastWriteTimeUtc).Take(5).ToArray();
            output.AppendLine();
            output.AppendLine("--- Recent import logs (" + logs.Length + ") ---");
            foreach (FileInfo log in logs)
            {
                output.AppendLine();
                output.AppendLine("### " + log.Name + " | " + log.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                try { output.AppendLine(File.ReadAllText(log.FullName, Encoding.UTF8)); }
                catch (Exception exception) { output.AppendLine("[Unable to read: " + exception.Message + "]"); }
            }
            output.AppendLine();
            output.AppendLine("--- Recent multiplayer logs ---");
            output.AppendLine(MultiplayerLog.ReadRecent(2000));
            return output.ToString();
        }

        public void OnLogExported(string message)
        {
            string value = string.IsNullOrWhiteSpace(message) ? "日志导出已取消。" : message;
            bool success = value.StartsWith("OK|", StringComparison.Ordinal);
            ShowModal(success ? "日志导出完成" : "日志导出", success ? value.Substring(3) : value, null, false);
        }

        private void MenuButton(string title, string subtitle, Transform parent, float bottom, Color color, UnityEngine.Events.UnityAction action)
        {
            Button button = ButtonText("", parent, new Vector2(0, bottom), new Vector2(1, bottom + .19f), color, action);
            Label(title, button.transform, 30, TextAnchor.MiddleLeft, new Vector2(.08f, .38f), new Vector2(.92f, .84f), Color.white);
            Label(subtitle, button.transform, 18, TextAnchor.MiddleLeft, new Vector2(.08f, .10f), new Vector2(.92f, .42f), muted);
        }
        private Text SettingRow(string title, string value, Transform parent, float bottom, UnityEngine.Events.UnityAction action)
        {
            Button button = ButtonText("", parent, new Vector2(.05f, bottom), new Vector2(.95f, bottom + .15f), surfaceRaised, action);
            Label(title, button.transform, 25, TextAnchor.MiddleLeft, new Vector2(.04f, .15f), new Vector2(.63f, .85f), Color.white);
            return Label(value, button.transform, 19, TextAnchor.MiddleRight, new Vector2(.60f, .15f), new Vector2(.94f, .85f), muted);
        }

        private void SettingOptionStepperRow(string title, string value, Transform parent, float bottom,
            UnityEngine.Events.UnityAction previous, UnityEngine.Events.UnityAction next)
        {
            RectTransform row = Panel(title + " selector", parent, surfaceRaised,
                new Vector2(.05f, bottom), new Vector2(.95f, bottom + .15f));
            Label(title, row, 23, TextAnchor.MiddleLeft, new Vector2(.04f, .15f), new Vector2(.48f, .85f), Color.white);
            ButtonText("‹", row, new Vector2(.60f, .18f), new Vector2(.69f, .82f), surface, previous);
            Label(value, row, 21, TextAnchor.MiddleCenter, new Vector2(.70f, .15f), new Vector2(.85f, .85f), cyan);
            ButtonText("›", row, new Vector2(.86f, .18f), new Vector2(.95f, .82f), surface, next);
        }

        private Slider SettingSliderRow(string title, string suffix, float minimum, float maximum, float current,
            Transform parent, float bottom, Action<float> changed, bool allowDirectInput = false)
        {
            RectTransform row = Panel(title + " slider", parent, surfaceRaised,
                new Vector2(.05f, bottom), new Vector2(.95f, bottom + .15f));
            Label(title, row, 23, TextAnchor.MiddleLeft, new Vector2(.04f, .46f), new Vector2(.48f, .92f), Color.white);
            Text valueLabel = null;
            InputField directInput = null;
            if (allowDirectInput) {
                directInput = Input("0", row, new Vector2(.70f, .46f), new Vector2(.89f, .92f));
                directInput.characterValidation = InputField.CharacterValidation.Integer;
                directInput.SetTextWithoutNotify(Mathf.RoundToInt(current).ToString(CultureInfo.InvariantCulture));
                Label(suffix.Trim(), row, 17, TextAnchor.MiddleRight, new Vector2(.89f, .46f), new Vector2(.95f, .92f), muted);
            } else {
                valueLabel = Label(Mathf.RoundToInt(current) + suffix, row, 19, TextAnchor.MiddleRight,
                    new Vector2(.70f, .46f), new Vector2(.94f, .92f), cyan);
            }

            RectTransform track = Panel("Track", row, new Color(.09f, .14f, .18f, 1f),
                new Vector2(.40f, .14f), new Vector2(.92f, .39f));
            track.offsetMin = new Vector2(3, 5); track.offsetMax = new Vector2(-3, -5);
            Slider slider = track.gameObject.AddComponent<Slider>();
            slider.minValue = minimum; slider.maxValue = maximum; slider.wholeNumbers = true;

            RectTransform fillArea = RectObject("Fill Area", track, new Vector2(.025f, .18f), new Vector2(.975f, .82f));
            RectTransform fill = Panel("Fill", fillArea, cyan, Vector2.zero, Vector2.one);
            fill.offsetMin = Vector2.zero; fill.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().raycastTarget = false;
            RectTransform handleArea = RectObject("Handle Area", track, new Vector2(.025f, 0), new Vector2(.975f, 1));
            RectTransform handle = Panel("Handle", handleArea, Color.white, new Vector2(0, .5f), new Vector2(0, .5f));
            handle.sizeDelta = new Vector2(28, 28); handle.pivot = new Vector2(.5f, .5f);
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.SetValueWithoutNotify(Mathf.Clamp(current, minimum, maximum));
            slider.onValueChanged.AddListener(value => {
                int roundedValue = Mathf.RoundToInt(value);
                if (valueLabel != null) valueLabel.text = roundedValue + suffix;
                if (directInput != null) directInput.SetTextWithoutNotify(roundedValue.ToString(CultureInfo.InvariantCulture));
                changed?.Invoke(value);
                settings.Save();
                ApplyRuntimeSettings();
            });
            if (directInput != null) directInput.onEndEdit.AddListener(text => {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    slider.value = Mathf.Clamp(parsed, minimum, maximum);
                directInput.SetTextWithoutNotify(Mathf.RoundToInt(slider.value).ToString(CultureInfo.InvariantCulture));
            });
            return slider;
        }

        private static RectTransform RectObject(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)child.transform;
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            return rect;
        }

        private IEnumerator RefreshGridLayout(bool scrollToCurrent, float preservedScrollY = float.NaN)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            UpdateResponsiveGrid();
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (scrollToCurrent) ScrollToCurrentSong();
            else if (!float.IsNaN(preservedScrollY))
            {
                float max = Mathf.Max(0f, ((RectTransform)songGrid).rect.height - songViewport.rect.height);
                ((RectTransform)songGrid).anchoredPosition = new Vector2(0f, Mathf.Clamp(preservedScrollY, 0f, max));
                songScroll?.StopMovement();
            }
            RenderVirtualSongCards();
            gridLayoutRefresh = null;
        }

        private void UpdateResponsiveGrid()
        {
            lastScreenWidth = Screen.width; lastScreenHeight = Screen.height;
            if (songViewport == null || songGrid == null) return;
            UpdateResponsiveSongSplit();
            Canvas.ForceUpdateCanvases();
            float width = Mathf.Max(0, songViewport.rect.width - SongGridInset * 2f);
            if (width < 80) return;
            float screenAspect = Screen.height <= 0 ? 1.78f : Screen.width / (float)Screen.height;
            // Keep the complete portrait card readable on tablet-like 3:2 and 4:3 screens.
            // 16:8 and wider devices gain columns instead of shrinking card contents.
            gridColumns = screenAspect >= 2.18f ? 5 : screenAspect >= 1.78f ? 4 : 3;
            songGridGap = Mathf.Clamp(width * .014f, 12f, 20f);
            float cell = Mathf.Max(120f, (width - songGridGap * (gridColumns - 1)) / gridColumns);
            songCardSize = new Vector2(cell, cell * 1.28f);
            LayoutSongCards();
            RenderVirtualSongCards();
        }

        private void LayoutSongCards()
        {
            if (songGrid == null) return;
            foreach (var pair in virtualSongCards) PositionVirtualSongCard(pair.Value, pair.Key);
            int rows = Mathf.CeilToInt(visibleSongs.Count / (float)Mathf.Max(1, gridColumns));
            float height = rows <= 0 ? 0 : rows * songCardSize.y + Mathf.Max(0, rows - 1) * songGridGap;
            RectTransform content = (RectTransform)songGrid;
            content.sizeDelta = new Vector2(-SongGridInset * 2f, height);
        }

        private void UpdateResponsiveSongSplit()
        {
            if (songListPanel == null || songDetailPanel == null) return;
            float aspect = Screen.height <= 0 ? 1.78f : Screen.width / (float)Screen.height;
            // Right preparation area varies between 34% and 42%. The clamp protects
            // both the portrait song cards and the horizontal difficulty rows.
            float blend = Mathf.InverseLerp(1.35f, 2.10f, aspect);
            float detailWidth = Mathf.Lerp(.42f, .34f, blend);
            const float left = .018f, right = .982f, gutter = .010f;
            float detailMin = right - detailWidth;
            songListPanel.anchorMin = new Vector2(left, .035f);
            songListPanel.anchorMax = new Vector2(detailMin - gutter, .895f);
            songDetailPanel.anchorMin = new Vector2(detailMin, .035f);
            songDetailPanel.anchorMax = new Vector2(right, .895f);
            songListPanel.offsetMin = new Vector2(3, 3); songListPanel.offsetMax = new Vector2(-3, -3);
            songDetailPanel.offsetMin = new Vector2(3, 3); songDetailPanel.offsetMax = new Vector2(-3, -3);
        }

        private void ScrollToCurrentSong()
        {
            if (songScroll == null || selectedSong == null || visibleSongs.Count == 0) return;
            int index = visibleSongs.FindIndex(item =>
                item?.Song != null && string.Equals(item.Song.Root, selectedSong.Root, StringComparison.OrdinalIgnoreCase) &&
                (selectedChart == null || item.Chart == null ||
                 string.Equals(item.Chart.Path, selectedChart.Path, StringComparison.OrdinalIgnoreCase)));
            if (index < 0)
                index = visibleSongs.FindIndex(item => item?.Song != null &&
                    string.Equals(item.Song.Root, selectedSong.Root, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            int row = index / Mathf.Max(1, gridColumns);
            float rowStep = songCardSize.y + songGridGap;
            float target = row * rowStep - Mathf.Max(0, (songViewport.rect.height - songCardSize.y) * .5f);
            float max = Mathf.Max(0, ((RectTransform)songGrid).rect.height - songViewport.rect.height);
            RectTransform content = (RectTransform)songGrid;
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Clamp(target, 0, max));
            songScroll.StopMovement();
        }

        private Text MarqueeLabel(string value, Transform parent, int size, Vector2 min, Vector2 max, Color color)
        {
            RectTransform viewport = Panel("Marquee", parent, Color.clear, min, max);
            viewport.GetComponent<Image>().raycastTarget = false;
            viewport.gameObject.AddComponent<RectMask2D>();
            Text text = Label(value, viewport, size, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, color);
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform textRect = text.rectTransform; textRect.anchorMin = new Vector2(0, 0); textRect.anchorMax = new Vector2(0, 1); textRect.pivot = new Vector2(0, .5f); textRect.anchoredPosition = Vector2.zero;
            var fitter = text.gameObject.AddComponent<ContentSizeFitter>(); fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var marquee = viewport.gameObject.AddComponent<UiMarqueeText>(); marquee.Text = text; marquee.Viewport = viewport;
            return text;
        }

        private Button ArrowButton(Transform parent, bool pointsRight, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction action)
        {
            RectTransform rect = Panel(pointsRight ? "Next" : "Previous", parent, surfaceRaised, min, max);
            Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>(); button.onClick.AddListener(action);
            rect.gameObject.AddComponent<UiPressAnimation>();
            var iconObject = new GameObject("Arrow Icon", typeof(RectTransform), typeof(Image)); iconObject.transform.SetParent(rect, false);
            RectTransform iconRect = (RectTransform)iconObject.transform; iconRect.anchorMin = new Vector2(.30f, .24f); iconRect.anchorMax = new Vector2(.70f, .76f); iconRect.offsetMin = Vector2.zero; iconRect.offsetMax = Vector2.zero;
            Image icon = iconObject.GetComponent<Image>(); icon.sprite = CreateArrowSprite(!pointsRight); icon.color = HighContrastUi ? UiThemeContrast.Navy : Color.white; icon.preserveAspect = true; icon.raycastTarget = false;
            return button;
        }

        private static Sprite CreateArrowSprite(bool right)
        {
            const int size = 48;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                int px = right ? x : size - 1 - x;
                bool inside = px >= 10 && px <= 38 && Mathf.Abs(y - 23.5f) <= (px - 9) * .72f;
                pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100);
        }

        private RectTransform Panel(string name, Transform parent, Color color, Vector2 min, Vector2 max, bool preserveColor = false)
        {
            bool gameplaySurface = IsGameplaySurface(parent);
            bool pauseSurface = IsPauseSurface(parent) || string.Equals(name, "Pause", StringComparison.OrdinalIgnoreCase);
            if (HighContrastUi && !preserveColor && (!gameplaySurface || pauseSurface))
                color = UiThemeContrast.ResolveSurface(color, name);
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image)); gameObject.transform.SetParent(parent, false); RectTransform rect = (RectTransform)gameObject.transform;
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = new Vector2(3, 3); rect.offsetMax = new Vector2(-3, -3); Image image = gameObject.GetComponent<Image>(); image.color = color;
            if (rounded != null) { image.sprite = rounded; image.type = Image.Type.Sliced; }
            return rect;
        }

        private static bool IsGameplaySurface(Transform parent)
        {
            for (Transform current = parent; current != null; current = current.parent)
                if (string.Equals(current.name, AppPage.Gameplay.ToString(), StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool IsPauseSurface(Transform parent)
        {
            for (Transform current = parent; current != null; current = current.parent)
                if (current.name.IndexOf("pause", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static Sprite CreateRoundedSprite(int radius = 18)
        {
            const int size = 64;
            radius = Mathf.Clamp(radius, 1, size / 2 - 1);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Rounded UI Mask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float cx = x < radius ? radius : x >= size - radius ? size - radius - 1 : x;
                float cy = y < radius ? radius : y >= size - radius ? size - radius - 1 : y;
                float distance = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(radius + .5f - distance));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }
        private Text Label(string value, Transform parent, int size, TextAnchor alignment, Vector2 min, Vector2 max, Color color, bool preserveColor = false)
        {
            var gameObject = new GameObject("Text", typeof(RectTransform), typeof(Text)); gameObject.transform.SetParent(parent, false); RectTransform rect = (RectTransform)gameObject.transform;
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; Text text = gameObject.GetComponent<Text>();
            text.font = size >= 30 ? titleFont : font; text.fontSize = size; text.alignment = alignment; text.color = color; text.text = UiLocalization.Translate(value); text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            if (HighContrastUi && !preserveColor && (!IsGameplaySurface(parent) || IsPauseSurface(parent)))
                gameObject.AddComponent<UiThemeContrast>().Bind(text);
            return text;
        }
        private Button ButtonText(string value, Transform parent, Vector2 min, Vector2 max, Color color, UnityEngine.Events.UnityAction action, bool preserveColor = false)
        {
            if (HighContrastUi && IsGameplaySurface(parent) &&
                (IsPauseSurface(parent) || value == "Ⅱ" || value.IndexOf("暂停", StringComparison.OrdinalIgnoreCase) >= 0))
                color = UiThemeContrast.ResolveSurface(color, "Pause button");
            bool themeButton = HighContrastUi && !preserveColor && (!IsGameplaySurface(parent) || IsPauseSurface(parent) || value == "Ⅱ");
            RectTransform rect = Panel("Button", parent, color, min, max, preserveColor); Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
            ColorBlock colors = button.colors; colors.highlightedColor = themeButton ? new Color(1.1f, 1.1f, 1.1f) : Color.Lerp(color, Color.white, .12f); colors.pressedColor = themeButton ? new Color(.8f, .8f, .8f) : Color.Lerp(color, Color.black, .25f); colors.fadeDuration = .08f; button.colors = colors; if (action != null) button.onClick.AddListener(action);
            rect.gameObject.AddComponent<UiPressAnimation>();
            if (themeButton && value == "Ⅱ") rect.name = "Pause button";
            Label(value, rect, 23, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Color.white, preserveColor); return button;
        }
        private InputField Input(string placeholder, Transform parent, Vector2 min, Vector2 max)
        {
            RectTransform rect = Panel("Search", parent, surfaceRaised, min, max); InputField input = rect.gameObject.AddComponent<InputField>(); Text value = Label("", rect, 22, TextAnchor.MiddleLeft, new Vector2(.05f, .08f), new Vector2(.95f, .92f), Color.white);
            Text hint = Label(placeholder, rect, 22, TextAnchor.MiddleLeft, new Vector2(.05f, .08f), new Vector2(.95f, .92f), muted); input.textComponent = value; input.placeholder = hint; input.lineType = InputField.LineType.SingleLine; return input;
        }
        private void EnsureEventSystem() { if (FindObjectOfType<EventSystem>() == null) { GameObject events = new GameObject("UI Event System", typeof(EventSystem), typeof(StandaloneInputModule)); events.transform.SetParent(transform, false); } }
    }
}
