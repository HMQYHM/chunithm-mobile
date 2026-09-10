using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    public sealed partial class AppUiShell
    {
        private sealed class ManagedSongRow
        {
            public RectTransform Rect;
            public Image Background;
            public Text Mark, Id, Title, Detail, Charts;
            public SongEntry Song;
        }

        private readonly List<ManagedSongRow> managementRows = new List<ManagedSongRow>();
        private IReadOnlyList<SongEntry> managementSongs = Array.Empty<SongEntry>();
        private Coroutine managementBuild;
        private bool managementContentHasSkins;
        private Text managementEmptyLabel;

        private void StopManagementBuild()
        {
            if (managementBuild != null) StopCoroutine(managementBuild);
            managementBuild = null;
        }

        private void ClearManagedContent()
        {
            StopManagementBuild();
            managementRows.Clear();
            managementEmptyLabel = null;
            managementContentHasSkins = false;
            foreach (Transform child in managementContent) {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        private int ManagedRowCapacity => Mathf.Min(managementSongs.Count,
            Mathf.CeilToInt(Mathf.Max(720f, managementScroll.viewport.rect.height) / 104f) + 3);

        private void RequestManagedRows()
        {
            if (managementSkinMode || managementContent == null) return;
            if (managementRows.Count < ManagedRowCapacity && Application.isPlaying) {
                managementBuild = StartCoroutine(BuildManagedRows());
                return;
            }
            while (managementRows.Count < ManagedRowCapacity) managementRows.Add(CreateManagedSongRow());
            RenderManagedSongRows();
        }

        private IEnumerator BuildManagedRows()
        {
            // Let navigation and the first frame render before allocating rows.
            yield return null;
            while (managementRows.Count < ManagedRowCapacity) {
                for (int i = 0; i < 2 && managementRows.Count < ManagedRowCapacity; i++)
                    managementRows.Add(CreateManagedSongRow());
                RenderManagedSongRows();
                yield return null;
            }
            managementBuild = null;
            RenderManagedSongRows();
        }

        private ManagedSongRow CreateManagedSongRow()
        {
            var view = new ManagedSongRow();
            view.Rect = Panel("Managed song", managementContent, surfaceRaised, Vector2.zero, Vector2.one);
            view.Rect.anchorMin = new Vector2(0, 1);
            view.Rect.anchorMax = new Vector2(1, 1);
            view.Rect.pivot = new Vector2(.5f, 1);
            view.Background = view.Rect.GetComponent<Image>();
            view.Mark = Label("", view.Rect, 34, TextAnchor.MiddleCenter,
                new Vector2(.015f, .12f), new Vector2(.07f, .88f), muted);
            RectTransform idBox = Panel("ID", view.Rect, surface,
                new Vector2(.08f, .14f), new Vector2(.18f, .86f));
            view.Id = Label("", idBox, 20, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Color.white);
            view.Title = Label("", view.Rect, 24, TextAnchor.MiddleLeft,
                new Vector2(.20f, .48f), new Vector2(.58f, .90f), Color.white);
            view.Detail = Label("", view.Rect, 17, TextAnchor.MiddleLeft,
                new Vector2(.20f, .10f), new Vector2(.58f, .50f), muted);
            view.Charts = Label("", view.Rect, 16, TextAnchor.MiddleRight,
                new Vector2(.60f, .15f), new Vector2(.98f, .85f), muted);
            Button button = view.Rect.gameObject.AddComponent<Button>();
            button.targetGraphic = view.Background;
            button.onClick.AddListener(() => {
                if (view.Song == null) return;
                if (!managedSongRoots.Remove(view.Song.Root)) managedSongRoots.Add(view.Song.Root);
                UpdateManagedRowSelection(view);
                RefreshManagementSummary();
            });
            return view;
        }

        private void UpdateManagedRowSelection(ManagedSongRow view)
        {
            bool selected = managedSongRoots.Contains(view.Song.Root);
            view.Mark.text = selected ? "✓" : "□";
            view.Mark.color = selected ? cyan : muted;
            view.Background.color = selected
                ? (HighContrastUi ? UiThemeContrast.Yellow : new Color(.06f, .28f, .35f, 1f))
                : surfaceRaised;
        }

        private void RenderManagedSongRows()
        {
            if (managementSkinMode || managementContent == null) return;
            int first = Mathf.Max(0, Mathf.FloorToInt(managementContent.anchoredPosition.y / 104f) - 1);
            first = Mathf.Min(first, Mathf.Max(0, managementSongs.Count - managementRows.Count));
            int count = Mathf.Min(managementRows.Count, managementSongs.Count);
            foreach (ManagedSongRow view in managementRows) {
                if (count == 0) { view.Rect.gameObject.SetActive(false); continue; }
            }
            for (int offset = 0; offset < count; offset++) {
                int index = first + offset;
                ManagedSongRow view = managementRows[index % managementRows.Count];
                SongEntry song = managementSongs[index];
                view.Rect.gameObject.SetActive(true);
                float top = 8f + index * 104f;
                view.Rect.offsetMin = new Vector2(8, -top - 96f);
                view.Rect.offsetMax = new Vector2(-8, -top);
                if (ReferenceEquals(view.Song, song)) continue;
                view.Song = song;
                view.Id.text = song.Id;
                view.Title.text = song.Name;
                view.Detail.text = song.Artist + (string.IsNullOrWhiteSpace(song.Category) ? "" : "  ·  " + song.Category);
                view.Charts.text = string.Join("  ", song.Charts.Select(chart => chart.Difficulty + " " + chart.Level));
                UpdateManagedRowSelection(view);
            }
            // Filtering can leave more pooled rows than matching songs.
            if (count < managementRows.Count)
                for (int i = count; i < managementRows.Count; i++) managementRows[i].Rect.gameObject.SetActive(false);
            if (managementEmptyLabel == null)
                managementEmptyLabel = Label("没有匹配的歌曲\n点击左上角“导入”添加歌曲 ZIP", managementContent,
                    24, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, muted);
            managementEmptyLabel.gameObject.SetActive(managementSongs.Count == 0);
        }
    }
}
