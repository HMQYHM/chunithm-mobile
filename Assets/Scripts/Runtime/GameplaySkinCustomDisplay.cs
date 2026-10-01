using System;
using System.Collections;
using System.Collections.Generic;
using ChunithmMobile.Chart;
using ChunithmMobile.Input;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// Plays skin-authored, declarative overlays. A package can describe when an
    /// image appears, but it cannot provide C#, a shader, a DLL or a callback.
    /// This keeps custom effects expressive without giving imported ZIP files code
    /// execution or a write path into gameplay state.
    /// </summary>
    public sealed class GameplaySkinCustomDisplay : MonoBehaviour
    {
        private readonly List<GameObject> active = new List<GameObject>();
        private readonly Dictionary<string, Texture2D> ownedTextures =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> edgeStates = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> previousMetrics = new Dictionary<string, int>(StringComparer.Ordinal);
        private GameplaySkinInfo skin;
        private GameplaySkinStyle style;
        private RectTransform root;
        private bool haveMetrics;
        private int previousCombo;
        private int previousScore;
        private string previousRank = "";
        private bool gameplayActive;
        private bool subscribed;
        private bool inputActive = true;

        /// <summary>Creates a full-screen, non-interactive overlay owned by the gameplay page.</summary>
        public void Initialize(RectTransform parent)
        {
            root = GetComponent<RectTransform>();
            if (root == null) root = gameObject.AddComponent<RectTransform>();
            if (parent != null) root.SetParent(parent, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            RefreshSkin();
            Subscribe(true);
        }

        /// <summary>Reloads definitions after a skin switch and disposes old animations.</summary>
        public void RefreshSkin()
        {
            StopDisplays();
            skin = GameplaySkinManager.Active;
            style = GameplaySkinManager.Style;
            edgeStates.Clear();
            previousMetrics.Clear();
            haveMetrics = false;
            previousCombo = previousScore = 0;
            previousRank = "";
            // Refreshing appearance must not invent a gameplay stop/start or
            // resume frozen effects while the pause menu is open.
            // Preload once per enabled session, not once per judgement. The
            // entire overlay has a 64 MiB decoded RGBA budget, shared by rules.
            long pixels = 0;
            if (skin != null && !skin.BuiltIn && style != null)
                foreach (var rule in style.CustomDisplays)
                {
                    if (rule.Action != "image") continue;
                    if (ownedTextures.ContainsKey(rule.Image)) continue;
                    Texture2D image = GameplaySkinManager.LoadCustomDisplayImage(
                        skin, rule.Image, 16L * 1024 * 1024 - pixels);
                    ownedTextures[rule.Image] = image;
                    if (image != null) pixels += (long)image.width * image.height;
                }
            if (root != null)
                root.gameObject.SetActive(skin != null && !skin.BuiltIn && style != null &&
                    style.CustomDisplays.Count > 0);
        }

        private void OnEnable()
        {
            if (root != null) RefreshSkin();
            Subscribe(true);
        }

        private void Subscribe(bool enabled)
        {
            if (!enabled || subscribed) return;
            GameplaySkinEvents.MetricsUpdated += OnMetrics;
            GameplaySkinEvents.DisplayMetricUpdated += OnDisplayMetric;
            GameplaySkinEvents.JudgementUpdated += OnJudgement;
            GameplaySkinEvents.InputUpdated += OnInput;
            GameplaySkinEvents.TouchZonesUpdated += OnTouchZones;
            GameplaySkinEvents.TouchTelemetryUpdated += OnTouchTelemetry;
            GameplaySkinEvents.GameplayStateChanged += OnGameplayState;
            subscribed = true;
        }

        private void OnDisable()
        {
            GameplaySkinEvents.MetricsUpdated -= OnMetrics;
            GameplaySkinEvents.DisplayMetricUpdated -= OnDisplayMetric;
            GameplaySkinEvents.JudgementUpdated -= OnJudgement;
            GameplaySkinEvents.InputUpdated -= OnInput;
            GameplaySkinEvents.TouchZonesUpdated -= OnTouchZones;
            GameplaySkinEvents.TouchTelemetryUpdated -= OnTouchTelemetry;
            GameplaySkinEvents.GameplayStateChanged -= OnGameplayState;
            subscribed = false;
            gameplayActive = false;
            StopDisplays();
        }

        private void OnDestroy() => OnDisable();

        private bool CanDisplay() => root != null && root.gameObject.activeInHierarchy &&
            skin != null && !skin.BuiltIn && style != null;

        private void OnMetrics(int combo, int score, int maxCombo, string rank)
        {
            if (!CanDisplay() || !gameplayActive) return;
            foreach (GameplaySkinCustomDisplayDefinition definition in style.CustomDisplays)
            {
                if (definition == null) continue;
                if (definition.Trigger == "combo" && Crossed(combo, previousCombo, definition))
                    Trigger(definition);
                else if (definition.Trigger == "score" && Crossed(score, previousScore, definition))
                    Trigger(definition);
                else if (definition.Trigger == "rank" && RankMatches(rank, definition) &&
                         (!haveMetrics || !string.Equals(previousRank, rank, StringComparison.OrdinalIgnoreCase)))
                    Trigger(definition);
            }
            previousCombo = combo;
            previousScore = score;
            previousRank = rank ?? "";
            haveMetrics = true;
        }

        private void OnDisplayMetric(string mode, int value)
        {
            if (!CanDisplay() || !gameplayActive) return;
            foreach (GameplaySkinCustomDisplayDefinition definition in style.CustomDisplays)
                if (definition != null && definition.Trigger == "displayed_metric" &&
                    (definition.Mode == "any" || string.Equals(definition.Mode, mode, StringComparison.OrdinalIgnoreCase)) &&
                    Crossed(value, previousMetrics.TryGetValue(mode, out int previous) ? previous : 0, definition))
                    Trigger(definition);
            previousMetrics[mode] = value;
        }

        private static bool Crossed(int current, int previous, GameplaySkinCustomDisplayDefinition definition)
        {
            float threshold = definition.Threshold;
            switch (definition.Condition)
            {
                case "equal": return current == Mathf.RoundToInt(threshold) && previous != current;
                case "less": return current < threshold && previous >= threshold;
                case "less_or_equal": return current <= threshold && previous > threshold;
                default: return current >= threshold && previous < threshold;
            }
        }

        private static bool RankMatches(string rank, GameplaySkinCustomDisplayDefinition definition)
        {
            return !string.IsNullOrEmpty(rank) && string.Equals(rank, definition.Value,
                StringComparison.OrdinalIgnoreCase);
        }

        private void OnJudgement(ChunithmMobile.Chart.Judgement judgement)
        {
            if (!CanDisplay() || !gameplayActive) return;
            string value = GameplaySkinManager.JudgementTextureKey(judgement);
            foreach (GameplaySkinCustomDisplayDefinition definition in style.CustomDisplays)
                if (definition != null && definition.Trigger == "judgement" &&
                    string.Equals(definition.Value, value, StringComparison.OrdinalIgnoreCase))
                    Trigger(definition);
        }

        private void OnInput(ArcadeInputFrame frame)
        {
            if (!CanDisplay() || !gameplayActive || !inputActive || frame == null) return;
            foreach (GameplaySkinCustomDisplayDefinition definition in style.CustomDisplays)
            {
                if (definition == null || definition.Trigger != "air") continue;
                bool current = AirStateMatches(frame.Air, definition.Value);
                string key = "air:" + definition.Id;
                bool previous = edgeStates.TryGetValue(key, out bool value) && value;
                if (definition.Condition == "active" && current && !previous ||
                    definition.Condition == "exit" && !current && previous ||
                    definition.Condition != "active" && definition.Condition != "exit" && current && !previous)
                    Trigger(definition);
                edgeStates[key] = current;
            }
        }

        private void OnTouchTelemetry(GameplaySkinEvents.TouchTelemetrySnapshot telemetry)
        {
            // The visual layer follows pause/countdown feedback only; it never
            // changes the separate judgement suppression state.
            inputActive = telemetry.InputActive;
        }

        private static bool AirStateMatches(AirState air, string value)
        {
            switch ((value ?? "active").ToLowerInvariant())
            {
                case "rising": return air.HasRisingMotion;
                case "falling": return air.HasFallingMotion;
                case "enter": return air.EnteredSensorMask != 0;
                case "exit": return air.ExitedSensorMask != 0;
                case "sensor1": return (air.ActiveSensorMask & 0x0001) != 0;
                case "sensor2": return (air.ActiveSensorMask & 0x0002) != 0;
                case "sensor3": return (air.ActiveSensorMask & 0x0004) != 0;
                case "sensor4": return (air.ActiveSensorMask & 0x0008) != 0;
                case "sensor5": return (air.ActiveSensorMask & 0x0010) != 0;
                case "sensor6": return (air.ActiveSensorMask & 0x0020) != 0;
                default: return air.Present && air.ContactCount > 0;
            }
        }

        private void OnTouchZones(GameplaySkinEvents.TouchZoneSnapshot zones)
        {
            if (!CanDisplay() || !gameplayActive || !inputActive) return;
            foreach (GameplaySkinCustomDisplayDefinition definition in style.CustomDisplays)
            {
                if (definition == null || definition.Trigger != "touch") continue;
                bool current = TouchZoneMatches(zones, definition.Value);
                string key = "touch:" + definition.Id;
                bool previous = edgeStates.TryGetValue(key, out bool value) && value;
                if (definition.Condition == "exit" && !current && previous ||
                    definition.Condition != "exit" && current && !previous)
                    Trigger(definition);
                edgeStates[key] = current;
            }
        }

        private static bool TouchZoneMatches(GameplaySkinEvents.TouchZoneSnapshot zones, string value)
        {
            switch ((value ?? "any").ToLowerInvariant())
            {
                case "lefttop": return zones.LeftTop;
                case "leftbottom": return zones.LeftBottom;
                case "righttop": return zones.RightTop;
                case "rightbottom": return zones.RightBottom;
                default: return zones.ActiveCount > 0;
            }
        }

        private void OnGameplayState(bool playing)
        {
            if (!CanDisplay()) return;
            if (playing && !gameplayActive) {
                // A restarted song has its own threshold crossings. Old Combo
                // and touch edges must not suppress the next song's effects.
                edgeStates.Clear();
                previousMetrics.Clear();
                previousCombo = previousScore = 0;
                previousRank = "";
                haveMetrics = false;
            }
            foreach (GameplaySkinCustomDisplayDefinition definition in style.CustomDisplays)
            {
                if (definition == null || definition.Trigger != "gameplay") continue;
                bool matches = string.Equals(definition.Value, playing ? "start" : "stop",
                    StringComparison.OrdinalIgnoreCase);
                if (matches && playing != gameplayActive) Trigger(definition);
            }
            gameplayActive = playing;
        }

        private void Trigger(GameplaySkinCustomDisplayDefinition definition)
        {
            // Motion requests are checked against this skin's bounded rules
            // again by the model renderer; they never target game objects.
            if (definition != null && definition.Action == "live2dMotion")
            {
                GameplaySkinEvents.PublishSkinMotion(definition.Motion);
                return;
            }
            if (definition == null || active.Count >= 24 || string.IsNullOrEmpty(definition.Image)) return;
            StartCoroutine(Animate(definition));
        }

        private IEnumerator Animate(GameplaySkinCustomDisplayDefinition definition)
        {
            GameObject display = new GameObject("Skin custom display " + definition.Id,
                typeof(RectTransform), typeof(CanvasGroup), typeof(RawImage));
            display.transform.SetParent(root, false);
            active.Add(display);
            RectTransform rect = display.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(definition.X, definition.Y);
            rect.anchorMax = new Vector2(definition.X + definition.Width, definition.Y + definition.Height);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(.5f, .5f);
            rect.localRotation = Quaternion.Euler(0, 0, definition.Rotation);
            CanvasGroup group = display.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            RawImage image = display.GetComponent<RawImage>();
            image.raycastTarget = false;
            // A bounded session-owned cache cannot evict images while an
            // animation uses them and avoids decoding on each dense judgement.
            ownedTextures.TryGetValue(definition.Image, out Texture2D texture);
            image.texture = texture;
            if (image.texture == null)
            {
                active.Remove(display);
                DisposeObject(display);
                yield break;
            }
            float duration = Mathf.Clamp(definition.Duration, .05f, 30f);
            float fadeIn = Mathf.Clamp(definition.FadeIn, 0, duration);
            float fadeOut = Mathf.Clamp(definition.FadeOut, 0, duration - fadeIn);
            float scaleFrom = Mathf.Clamp(definition.ScaleFrom, .01f, 4f);
            float scaleTo = Mathf.Clamp(definition.ScaleTo, .01f, 4f);
            float opacity = Mathf.Clamp(definition.Opacity, 0f, 1f);
            float start = Time.unscaledTime;
            // Initialize the first frame before yielding so a short effect is
            // still visible on devices with a slow first UI update.
            group.alpha = fadeIn > 0 ? 0f : 1f;
            display.transform.localScale = new Vector3(scaleFrom, scaleFrom, 1f);
            while (Time.unscaledTime - start < duration)
            {
                if (!inputActive) {
                    start += Time.unscaledDeltaTime;
                    yield return null;
                    continue;
                }
                float elapsed = Time.unscaledTime - start;
                float progress = Mathf.Clamp01(elapsed / duration);
                group.alpha = (fadeIn > 0 && elapsed < fadeIn ? elapsed / fadeIn :
                    fadeOut > 0 && duration - elapsed < fadeOut ? (duration - elapsed) / fadeOut : 1f) * opacity;
                float scale = Mathf.Lerp(scaleFrom, scaleTo, progress);
                display.transform.localScale = new Vector3(scale, scale, 1f);
                display.transform.localRotation = Quaternion.Euler(0, 0,
                    Mathf.Lerp(definition.Rotation, definition.RotationTo, progress));
                yield return null;
            }
            active.Remove(display);
            DisposeObject(display);
        }

        private void StopDisplays()
        {
            // RefreshSkin can happen while an effect is mid-animation. Stop
            // those coroutines before replacing the skin/style references so
            // an old package cannot keep spawning images into the new layer.
            StopAllCoroutines();
            foreach (GameObject item in active.ToArray())
            {
                if (item != null) DisposeObject(item);
            }
            foreach (Texture2D texture in ownedTextures.Values)
                if (texture != null) DisposeObject(texture);
            ownedTextures.Clear();
            active.Clear();
        }

        private static void DisposeObject(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
