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
                Set(shell, "searchText", "Beta");
                var matches = Items(shell);
                Require(matches.Length == 1 && ReferenceEquals(ItemSong(matches[0]), songs[1]), "search leaked another song/difficulty");
                Set(shell, "searchText", "no-match");
                Require(Items(shell).Length == 0, "empty search broadened to whole bucket");

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
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static object[] Items(AppUiShell shell) => ((IEnumerable)Call(shell, "SongsForCurrentBucket")).Cast<object>().ToArray();
        private static SongEntry ItemSong(object item) => (SongEntry)item.GetType().GetField("Song").GetValue(item);
        private static SongEntry Song(string name, string difficulty, string level) => new SongEntry {
            Root = name, Name = name, Artist = "Test", Category = "Test",
            Charts = new List<SongChartEntry> { new SongChartEntry { Difficulty = difficulty, Level = level, Path = name + ".c2s" } }
        };
    }
}
