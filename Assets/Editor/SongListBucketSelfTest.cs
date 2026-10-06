using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ChunithmMobile.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Editor
{
    // 歌曲列表分组回归测试：验证数字等级、半级和 WORLD'S END 分类的排序/筛选规则。
    public static class SongListBucketSelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
        private static void Set(AppUiShell shell, string name, object value) =>
            typeof(AppUiShell).GetField(name, Private).SetValue(shell, value);
        private static object Call(AppUiShell shell, string name) =>
            typeof(AppUiShell).GetMethod(name, Private).Invoke(shell, null);

        public static void BuildVerifiedApk()
        {
            Run();
            AndroidBuildConfigurator.BuildApk();
        }

        public static void Run()
        {
            var bucket = typeof(AppUiShell).GetMethod("LevelBucketKey", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (string text in new[] { "光", "狂", "?", "", null, "14.5", "14++", "-1", "0", "WD" })
                Require((string)bucket.Invoke(null, new object[] {
                    new SongChartEntry { Difficulty = "MASTER", Level = text } }) == "WD", "non-level escaped WD: " + text);
            foreach (string text in new[] { "1", "14", "14+", "15", "15+", "16" })
                Require((string)bucket.Invoke(null, new object[] {
                    new SongChartEntry { Difficulty = "BASIC", Level = text } }) == text, "numeric level changed");

            string pinState = PlayerPrefs.GetString(SongPinStore.PreferenceKey, null);
            FieldInfo pinCache = typeof(SongPinStore).GetField("pinnedIds", BindingFlags.Static | BindingFlags.NonPublic);
            var root = new GameObject("Song list bucket regression");
            root.SetActive(false);
            try {
                var shell = root.AddComponent<AppUiShell>();
                var songs = new List<SongEntry> {
                    Song("Alpha", "EXPERT", "14"), Song("Beta", "BASIC", "14"),
                    Song("Special", "MASTER", "光"), Song("Unknown", "EXPERT", "?"),
                    Song("Upper", "MASTER", "14+"), Song("Easy", "BASIC", "1"),
                    Song("Last", "ULTIMA", "16")
                };
                Set(shell, "catalog", songs);
                Set(shell, "sortMode", SongSortMode.Level);
                var buckets = (IReadOnlyList<string>)Call(shell, "CurrentBuckets");
                Require(buckets.SequenceEqual(new[] { "WD", "1", "14", "14+", "16" }), "bucket sequence wrong");
                Set(shell, "selectedSong", songs[2]);
                Set(shell, "selectedChart", songs[2].Charts[0]);
                Call(shell, "SyncBucketToSelection");
                Require(Items(shell).Length == 2, "WD did not merge different difficulties/text badges");
                Require(songs[2].Charts[0].Level == "光" && songs[2].Charts[0].Difficulty == "MASTER",
                    "grouping changed authored metadata");

                Set(shell, "selectedSong", songs[0]);
                Set(shell, "selectedChart", songs[0].Charts[0]);
                Call(shell, "SyncBucketToSelection");
                PlayerPrefs.SetString(SongPinStore.PreferenceKey, "{\"SongIds\":[]}");
                pinCache.SetValue(null, null);
                SongPinStore.Toggle(songs[1]);
                var pinnedItems = Items(shell);
                Require(pinnedItems.Length == 2 && ReferenceEquals(ItemSong(pinnedItems[0]), songs[1]),
                    "song-level pin did not precede every chart in the active level list");
                Set(shell, "searchText", "Beta");
                var matches = Items(shell);
                Require(matches.Length == 1 && ReferenceEquals(ItemSong(matches[0]), songs[1]), "search leaked another song/difficulty");
                Set(shell, "searchText", "no-match");
                Require(Items(shell).Length == 0, "empty search broadened to whole bucket");

                var cover = new GameObject("Cover", typeof(RectTransform)).GetComponent<RectTransform>();
                cover.SetParent(root.transform, false);
                var createPin = typeof(AppUiShell).GetMethod("CreatePinButton", Private);
                var pinButton = (Button)createPin.Invoke(shell, new object[] { cover, null });
                RectTransform pinRect = (RectTransform)pinButton.transform;
                Require(pinButton.transform.parent == cover && pinRect.anchorMin == Vector2.one &&
                        pinRect.anchorMax == Vector2.one && pinRect.pivot == Vector2.one &&
                        pinRect.sizeDelta == new Vector2(58f, 58f) &&
                        Mathf.Abs(pinRect.offsetMax.x + 8f) < .01f &&
                        Mathf.Abs(pinRect.offsetMax.y + 8f) < .01f &&
                        pinRect.localRotation == Quaternion.identity && pinRect.localScale == Vector3.one &&
                        pinButton.GetComponent<Graphic>() == null &&
                        pinButton.targetGraphic.GetType().Name == "PinIconGraphic" &&
                        pinButton.transition == Selectable.Transition.None,
                    "pin vector is not a transparent fixed-size control inside the fitted cover");
                Set(shell, "songPinButton", pinButton);
                Set(shell, "selectedSong", songs[1]);
                Call(shell, "UpdateSongPinButton");
                Color selectedLow = pinButton.targetGraphic.color;
                Require(selectedLow.b > selectedLow.r && selectedLow.g > selectedLow.r,
                    "low-contrast pinned icon did not use the cyan UI accent");
                SongPinStore.Toggle(songs[1]);
                Call(shell, "UpdateSongPinButton");
                Color idleLow = pinButton.targetGraphic.color;
                Require(Mathf.Abs(idleLow.r - idleLow.g) < .06f && idleLow.r > .6f,
                    "unpinned icon is not visible neutral gray");
                Set(shell, "settings", new AppSettings { HighContrastUi = true });
                SongPinStore.Toggle(songs[1]);
                Call(shell, "UpdateSongPinButton");
                Color selectedHigh = pinButton.targetGraphic.color;
                Require(selectedHigh.r > .9f && selectedHigh.g > .8f && selectedHigh.b < .2f,
                    "high-contrast pinned icon did not use the yellow UI accent");
                SongPinStore.Toggle(songs[1]);
                Call(shell, "UpdateSongPinButton");
                Color idleHigh = pinButton.targetGraphic.color;
                Require(idleHigh.r > .98f && idleHigh.g > .98f && idleHigh.b > .98f,
                    "high-contrast unpinned icon is not white");
                Set(shell, "settings", new AppSettings());
                Set(shell, "selectedSong", songs[0]);
                Set(shell, "selectedChart", songs[0].Charts[0]);

                var inputRoot = new GameObject("Search", typeof(RectTransform), typeof(InputField));
                inputRoot.transform.SetParent(root.transform, false);
                var input = inputRoot.GetComponent<InputField>();
                input.SetTextWithoutNotify("no-match");
                Set(shell, "songSearchInput", input);
                Set(shell, "searchText", "Beta");
                Call(shell, "SelectRandomSongFromCurrentList");
                Require((string)typeof(AppUiShell).GetField("searchText", Private).GetValue(shell) == "no-match",
                    "current random used stale search");
                Require(ReferenceEquals(typeof(AppUiShell).GetField("selectedSong", Private).GetValue(shell), songs[0]),
                    "empty random selection changed song");
                Set(shell, "catalog", Array.Empty<SongEntry>());
                Call(shell, "SelectRandomSongFromLibrary");
                Require(input.text == "" && (string)typeof(AppUiShell).GetField("searchText", Private).GetValue(shell) == "",
                    "library random failed to clear input and filter");
                Debug.Log("Song-list regression passed: WD merging/order, numeric levels, preserved metadata, filtered candidates, empty random, immediate search and search clear.");
            } finally {
                if (pinState == null) PlayerPrefs.DeleteKey(SongPinStore.PreferenceKey);
                else PlayerPrefs.SetString(SongPinStore.PreferenceKey, pinState);
                PlayerPrefs.Save();
                pinCache.SetValue(null, null);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static object[] Items(AppUiShell shell) => ((IEnumerable)Call(shell, "SongsForCurrentBucket")).Cast<object>().ToArray();
        private static SongEntry ItemSong(object item) => (SongEntry)item.GetType().GetField("Song").GetValue(item);
        private static SongEntry Song(string name, string difficulty, string level) => new SongEntry {
            Root = name, Name = name, Artist = "Test", Category = "Test",
            Charts = new List<SongChartEntry> { new SongChartEntry { Difficulty = difficulty, Level = level, Path = name + ".c2s" } }
        };
    }
}
