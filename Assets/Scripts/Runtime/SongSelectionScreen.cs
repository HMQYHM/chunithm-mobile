using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    // 歌曲选择页面控制器：维护当前列表、歌曲、难度、搜索、排序和滚动位置。
    // 页面重建时应恢复当前选择，而不是无条件跳到列表第一首。
    /// <summary>Functional song-select flow; visual skin will be refined separately.</summary>
    public sealed class SongSelectionScreen : MonoBehaviour
    {
        private readonly Color background = new Color(.015f, .025f, .045f, .98f);
        private readonly Color panel = new Color(.035f, .06f, .095f, .96f);
        private readonly Color accent = new Color(.12f, .78f, .94f);
        private Font font;
        private GameObject screen;
        private Transform songContent;
        private Transform difficultyContent;
        private Text songName;
        private Text songInfo;
        private Button startButton;
        private SongSelectionPreviewPlayer preview;
        private LocalChartPlaybackSession playback;
        private SongEntry selectedSong;
        private SongChartEntry selectedChart;

        private void Start()
        {
            preview = FindObjectOfType<SongSelectionPreviewPlayer>();
            playback = FindObjectOfType<LocalChartPlaybackSession>();
            font = Font.CreateDynamicFontFromOSFont(new[] { "Noto Sans CJK JP", "Noto Sans CJK SC", "sans-serif", "Arial" }, 32);
            EnsureEventSystem();
            BuildScreen();
            PopulateSongs(SongCatalog.Scan());
            FindObjectOfType<RuntimeLoadingOverlay>()?.Hide();
        }

        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var events = new GameObject("UI Event System", typeof(EventSystem), typeof(StandaloneInputModule));
            events.transform.SetParent(transform, false);
        }

        private void BuildScreen()
        {
            screen = new GameObject("Song Selection Screen", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            screen.transform.SetParent(transform, false);
            var canvas = screen.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = screen.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;

            var safeObject = new GameObject("Safe content root", typeof(RectTransform), typeof(SafeAreaCanvasFitter));
            safeObject.transform.SetParent(screen.transform, false);
            RectTransform safeRoot = (RectTransform)safeObject.transform;
            safeRoot.anchorMin = Vector2.zero; safeRoot.anchorMax = Vector2.one;
            safeRoot.offsetMin = Vector2.zero; safeRoot.offsetMax = Vector2.zero;
            RectTransform root = Panel("Root", safeRoot, background, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Label("CHUNITHM Mobile   SONG SELECT", root, 34, TextAnchor.MiddleLeft,
                new Vector2(.035f, .90f), new Vector2(.965f, .985f), accent);

            RectTransform left = Panel("Song List", root, panel, new Vector2(.035f, .08f), new Vector2(.60f, .88f), Vector2.zero, Vector2.zero);
            RectTransform viewport = Panel("Viewport", left, Color.clear, Vector2.zero, Vector2.one, new Vector2(18, 18), new Vector2(-18, -18));
            viewport.gameObject.AddComponent<RectMask2D>();
            var contentObject = new GameObject("Songs", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            songContent = contentObject.transform;
            songContent.SetParent(viewport, false);
            var contentRect = (RectTransform)songContent;
            contentRect.anchorMin = new Vector2(0, 1); contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(.5f, 1); contentRect.offsetMin = Vector2.zero; contentRect.offsetMax = Vector2.zero;
            var layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10; layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = left.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = contentRect; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;

            RectTransform right = Panel("Song Details", root, panel, new Vector2(.615f, .08f), new Vector2(.965f, .88f), Vector2.zero, Vector2.zero);
            songName = Label("未发现歌曲", right, 42, TextAnchor.MiddleLeft, new Vector2(.07f, .73f), new Vector2(.93f, .90f), Color.white);
            songInfo = Label("请导入歌曲文件夹", right, 25, TextAnchor.UpperLeft, new Vector2(.07f, .57f), new Vector2(.93f, .73f), new Color(.72f, .82f, .9f));
            RectTransform difficulties = Panel("Difficulties", right, Color.clear, new Vector2(.06f, .28f), new Vector2(.94f, .55f), Vector2.zero, Vector2.zero);
            difficultyContent = difficulties;
            startButton = MakeButton("开始游玩", right, new Vector2(.52f, .07f), new Vector2(.94f, .22f), accent, StartGameplay);
            startButton.interactable = false;
        }

        private void PopulateSongs(IReadOnlyList<SongEntry> songs)
        {
            foreach (SongEntry song in songs)
            {
                SongEntry captured = song;
                Button button = MakeButton($"{song.Name}\n{song.Artist}", songContent, Vector2.zero, Vector2.one,
                    new Color(.06f, .12f, .18f), () => SelectSong(captured));
                button.GetComponent<LayoutElement>().preferredHeight = 105;
            }
            if (songs.Count > 0) SelectSong(songs[0]);
        }

        private void SelectSong(SongEntry song)
        {
            selectedSong = song;
            songName.text = song.Name;
            songInfo.text = $"{song.Artist}\n{song.Category}   ID {song.Id}";
            preview?.SelectSong(song.Root);
            foreach (Transform child in difficultyContent) Destroy(child.gameObject);
            selectedChart = null;
            float width = 1f / Mathf.Max(1, song.Charts.Count);
            for (int i = 0; i < song.Charts.Count; i++)
            {
                SongChartEntry chart = song.Charts[i];
                SongChartEntry captured = chart;
                MakeButton($"{chart.Difficulty}\nLv.{chart.Level}", difficultyContent,
                    new Vector2(i * width + .01f, .12f), new Vector2((i + 1) * width - .01f, .88f),
                    new Color(.20f, .08f, .34f), () => SelectChart(captured));
            }
            if (song.Charts.Count > 0) SelectChart(song.Charts[song.Charts.Count - 1]);
        }

        private void SelectChart(SongChartEntry chart)
        {
            selectedChart = chart;
            startButton.interactable = true;
            songInfo.text = $"{selectedSong.Artist}\n{selectedSong.Category}   {chart.Difficulty}  Lv.{chart.Level}";
        }

        private void StartGameplay()
        {
            if (selectedSong == null || selectedChart == null) return;
            preview?.StopPreview();
            screen.SetActive(false);
            playback?.PlaySong(selectedSong, selectedChart);
        }

        private RectTransform Panel(string name, Transform parent, Color color, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var rect = (RectTransform)gameObject.transform;
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
            gameObject.GetComponent<Image>().color = color;
            return rect;
        }

        private Text Label(string value, Transform parent, int size, TextAnchor alignment, Vector2 min, Vector2 max, Color color)
        {
            var gameObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            gameObject.transform.SetParent(parent, false);
            var rect = (RectTransform)gameObject.transform;
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            var text = gameObject.GetComponent<Text>();
            text.font = font; text.fontSize = size; text.alignment = alignment; text.color = color; text.text = value;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private Button MakeButton(string value, Transform parent, Vector2 min, Vector2 max, Color color, UnityEngine.Events.UnityAction action)
        {
            RectTransform rect = Panel("Button", parent, color, min, max, new Vector2(4, 4), new Vector2(-4, -4));
            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 72;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(action);
            Label(value, rect, 25, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Color.white);
            return button;
        }
    }
}
