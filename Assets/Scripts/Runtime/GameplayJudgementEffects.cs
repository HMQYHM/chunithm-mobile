using System.Collections;
using System.Collections.Generic;
using ChunithmMobile.Chart;
using ChunithmMobile.Judgement;
using ChunithmMobile.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    public sealed class GameplayJudgementEffects : MonoBehaviour
    {
        private enum EffectStyle { Ground, AirDirectional, AirHold, AirAction, Miss }
        private readonly List<GameObject> activeEffects = new List<GameObject>();
        private GameplaySkinStyle skin = GameplaySkinStyle.Original();

        public void ConfigureSkin(GameplaySkinStyle value)
        {
            skin = value ?? GameplaySkinStyle.Original();
        }

        public void Play(ChartNote note, JudgementResult judgement, float judgementY, int pointIndex = 0)
        {
            if (note == null) return;
            activeEffects.RemoveAll(item => item == null);
            while (activeEffects.Count >= 24) {
                Destroy(activeEffects[0]);
                activeEffects.RemoveAt(0);
            }

            bool air = note.Kind is NoteKind.Air or NoteKind.AirHold or NoteKind.AirAction;
            bool down = note.AirDirection is AirDirection.Down or AirDirection.DownLeft or AirDirection.DownRight;
            bool left = note.AirDirection is AirDirection.UpRight or AirDirection.DownRight;
            bool right = note.AirDirection is AirDirection.UpLeft or AirDirection.DownLeft;
            bool miss = judgement.Grade == Chart.Judgement.Miss;
            EffectStyle style = miss ? EffectStyle.Miss
                : !air ? EffectStyle.Ground
                : note.Kind == NoteKind.AirHold ? EffectStyle.AirHold
                : note.Kind == NoteKind.AirAction ? EffectStyle.AirAction
                : EffectStyle.AirDirectional;

            float x = Mathf.Clamp01((note.Lane + Mathf.Max(1, note.Width) * .5f) / ChartSpace.Lanes);
            float normalizedWidth = Mathf.Clamp(Mathf.Max(1, note.Width) / (float)ChartSpace.Lanes, .04f, .52f);
            float width = Mathf.Clamp(Screen.width * normalizedWidth, 72f, air ? 250f : 310f);
            var effect = new GameObject(air ? "AIR compact judgement" : "Ground compact judgement",
                typeof(RectTransform), typeof(CanvasGroup));
            effect.transform.SetParent(transform, false);
            RectTransform root = (RectTransform)effect.transform;
            root.anchorMin = root.anchorMax = new Vector2(x, Mathf.Clamp(judgementY, .04f, .92f));
            root.pivot = new Vector2(.5f, .5f);
            root.sizeDelta = new Vector2(width, air ? 170f : 104f);
            root.anchoredPosition = Vector2.zero;
            Color tint = EffectColor(note.Kind, judgement.Grade, down, skin);

            switch (style) {
                case EffectStyle.Ground: BuildGround(root, tint); break;
                case EffectStyle.AirDirectional: BuildDirectionalAir(root, tint, down, left, right); break;
                case EffectStyle.AirHold: BuildAirHold(root, tint, pointIndex == 0); break;
                case EffectStyle.AirAction: BuildAirAction(root, tint, down, pointIndex == 0); break;
                default: BuildMiss(root, air, tint); break;
            }
            AddPackageEffect(root, note, judgement, style, down, pointIndex, tint);
            activeEffects.Add(effect);
            StartCoroutine(Animate(effect, root, effect.GetComponent<CanvasGroup>(), style, down));
        }

        public void Clear()
        {
            foreach (GameObject effect in activeEffects)
                if (effect != null) Destroy(effect);
            activeEffects.Clear();
        }

        private static void AddPackageEffect(RectTransform root, ChartNote note,
            JudgementResult judgement, EffectStyle style, bool down, int pointIndex, Color tint)
        {
            Texture2D texture = null;
            Rect uv = new Rect(0f, 0f, 1f, 1f);
            Vector2 size = new Vector2(Mathf.Min(root.sizeDelta.x * .72f, 210f), 96f);
            float alpha = .42f;

            if (style == EffectStyle.AirDirectional) {
                texture = GameplaySkinManager.LoadEffectTexture(
                    down ? "air/air_action_down" : "air/air_action_up");
                // The extracted AIR effect is a 2 x 2 grade atlas. Selecting a
                // cell keeps the authored AIR frame while avoiding four frames
                // being drawn over one another on mobile.
                uv = judgement.Grade switch {
                    Chart.Judgement.JusticeCritical => new Rect(0f, .5f, .5f, .5f),
                    Chart.Judgement.Justice => new Rect(.5f, .5f, .5f, .5f),
                    Chart.Judgement.Attack => new Rect(.5f, 0f, .5f, .5f),
                    _ => new Rect(0f, 0f, .5f, .5f)
                };
                size = new Vector2(Mathf.Min(root.sizeDelta.x * .64f, 180f), 108f);
                alpha = .48f;
            } else if (style == EffectStyle.AirHold) {
                // The ring/float source images are authored for the arcade
                // additive material and have an opaque black key color. The
                // success atlas has a real alpha channel and is safe in the
                // mobile UI overlay.
                texture = GameplaySkinManager.LoadEffectTexture("air/airhold_success");
                size = new Vector2(92f, 92f);
                alpha = pointIndex == 0 ? .46f : .30f;
            } else if (style == EffectStyle.Miss && note.Kind == NoteKind.Mine) {
                texture = GameplaySkinManager.LoadEffectTexture(
                    judgement.Grade == Chart.Judgement.Miss
                        ? "air/bomb_flare_orange" : "air/bomb_flare_yellow");
                size = new Vector2(96f, 96f);
                alpha = .36f;
            }

            if (texture == null) return;
            var objectWithImage = new GameObject("Package effect image",
                typeof(RectTransform), typeof(RawImage));
            objectWithImage.transform.SetParent(root, false);
            RectTransform rect = (RectTransform)objectWithImage.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            RawImage image = objectWithImage.GetComponent<RawImage>();
            image.texture = texture;
            image.uvRect = uv;
            image.color = new Color(tint.r, tint.g, tint.b, alpha);
            image.raycastTarget = false;
        }

        private static void BuildGround(RectTransform root, Color tint)
        {
            AbsoluteImage(root, "Soft strike", new Color(tint.r, tint.g, tint.b, .20f),
                new Vector2(.5f, .50f), new Vector2(root.sizeDelta.x * 1.08f, 34f));
            AbsoluteImage(root, "Color strike", new Color(tint.r, tint.g, tint.b, .92f),
                new Vector2(.5f, .50f), new Vector2(root.sizeDelta.x, 8f));
            AbsoluteImage(root, "White core", new Color(1f, 1f, 1f, .95f),
                new Vector2(.5f, .50f), new Vector2(root.sizeDelta.x * .72f, 3f));
            for (int index = 0; index < 4; index++) {
                float side = index < 2 ? -1f : 1f;
                float spread = index % 2 == 0 ? .22f : .38f;
                float rotation = side * (index % 2 == 0 ? 48f : 67f);
                AbsoluteImage(root, "Small spark", index % 2 == 0 ? Color.white : tint,
                    new Vector2(.5f + side * spread, .60f), new Vector2(5f, index % 2 == 0 ? 28f : 19f), rotation);
            }
        }

        private static void BuildDirectionalAir(RectTransform root, Color tint, bool down, bool left, bool right)
        {
            float rotation = (down ? 180f : 0f) + (left ? 18f : right ? -18f : 0f);
            RectTransform direction = Container(root, "Direction", new Vector2(.5f, .5f), new Vector2(116f, 142f), rotation);
            DiamondFrame(direction, "Outer pulse", new Color(tint.r, tint.g, tint.b, .62f),
                new Vector2(.5f, .44f), new Vector2(66f, 66f), 3f);
            DiamondFrame(direction, "Inner pulse", new Color(1f, 1f, 1f, .82f),
                new Vector2(.5f, .44f), new Vector2(40f, 40f), 2f);
            AbsoluteImage(direction, "Direction stem", new Color(tint.r, tint.g, tint.b, .88f),
                new Vector2(.5f, .60f), new Vector2(5f, 58f));
            AbsoluteImage(direction, "Arrow left", Color.white, new Vector2(.5f, .81f), new Vector2(4f, 28f), -48f,
                new Vector2(.5f, 1f));
            AbsoluteImage(direction, "Arrow right", Color.white, new Vector2(.5f, .81f), new Vector2(4f, 28f), 48f,
                new Vector2(.5f, 1f));
            AbsoluteImage(root, "AIR strike", new Color(tint.r, tint.g, tint.b, .72f),
                new Vector2(.5f, .5f), new Vector2(root.sizeDelta.x * .86f, 5f));
        }

        private static void BuildAirHold(RectTransform root, Color tint, bool head)
        {
            AbsoluteImage(root, "AIR HOLD line", new Color(tint.r, tint.g, tint.b, head ? .78f : .48f),
                new Vector2(.5f, .46f), new Vector2(root.sizeDelta.x * .82f, head ? 6f : 4f));
            DiamondFrame(root, "AIR HOLD pulse", new Color(tint.r, tint.g, tint.b, head ? .80f : .52f),
                new Vector2(.5f, .52f), new Vector2(head ? 72f : 52f, head ? 72f : 52f), head ? 4f : 3f);
            for (int index = -1; index <= 1; index++)
                AbsoluteImage(root, "AIR HOLD point", index == 0 ? Color.white : tint,
                    new Vector2(.5f + index * .16f, .52f), Vector2.one * (index == 0 ? 12f : 8f), 45f);
        }

        private static void BuildAirAction(RectTransform root, Color tint, bool down, bool head)
        {
            float sign = down ? -1f : 1f;
            AbsoluteImage(root, "AIR ACTION line", new Color(tint.r, tint.g, tint.b, .74f),
                new Vector2(.5f, .50f), new Vector2(root.sizeDelta.x * .84f, 5f));
            int count = head ? 3 : 2;
            for (int index = 0; index < count; index++) {
                float y = .50f + sign * (.10f + index * .11f);
                AbsoluteImage(root, "Chevron left", new Color(tint.r, tint.g, tint.b, .82f - index * .18f),
                    new Vector2(.5f, y), new Vector2(4f, 34f), sign > 0 ? -52f : 52f, new Vector2(.5f, 0f));
                AbsoluteImage(root, "Chevron right", new Color(tint.r, tint.g, tint.b, .82f - index * .18f),
                    new Vector2(.5f, y), new Vector2(4f, 34f), sign > 0 ? 52f : -52f, new Vector2(.5f, 0f));
            }
        }

        private static void BuildMiss(RectTransform root, bool air, Color tint)
        {
            Color red = new Color(tint.r, tint.g, tint.b, .72f);
            float y = .5f;
            AbsoluteImage(root, "MISS line", red, new Vector2(.5f, y), new Vector2(root.sizeDelta.x * .70f, 4f));
            AbsoluteImage(root, "MISS slash left", red, new Vector2(.5f, y), new Vector2(4f, 34f), 45f);
            AbsoluteImage(root, "MISS slash right", red, new Vector2(.5f, y), new Vector2(4f, 34f), -45f);
        }

        private IEnumerator Animate(GameObject effect, RectTransform root, CanvasGroup group, EffectStyle style, bool down)
        {
            bool air = style is EffectStyle.AirDirectional or EffectStyle.AirHold or EffectStyle.AirAction;
            float duration = air ? .28f : .22f;
            Vector2 start = root.anchoredPosition;
            float elapsed = 0f;
            while (elapsed < duration && effect != null) {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float ease = 1f - Mathf.Pow(1f - t, 3f);
                float scale = Mathf.Lerp(.78f, 1.05f, ease);
                root.localScale = new Vector3(scale, scale, 1f);
                float travel = style == EffectStyle.AirDirectional ? (down ? -12f : 12f) : air ? 7f : -5f;
                root.anchoredPosition = start + Vector2.up * travel * ease;
                group.alpha = t < .28f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - .28f) / .72f);
                yield return null;
            }
            activeEffects.Remove(effect);
            if (effect != null) Destroy(effect);
        }

        private static RectTransform Container(Transform parent, string name, Vector2 anchor, Vector2 size, float rotation)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)child.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.Euler(0, 0, rotation);
            return rect;
        }

        private static Image AbsoluteImage(Transform parent, string name, Color color, Vector2 anchor,
            Vector2 size, float rotation = 0f, Vector2? pivot = null)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(Image));
            child.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)child.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot ?? new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.Euler(0, 0, rotation);
            Image image = child.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void DiamondFrame(Transform parent, string name, Color color, Vector2 anchor,
            Vector2 size, float thickness)
        {
            RectTransform frame = Container(parent, name, anchor, size, 45f);
            AbsoluteImage(frame, "Top", color, new Vector2(.5f, 1f), new Vector2(size.x, thickness));
            AbsoluteImage(frame, "Bottom", color, new Vector2(.5f, 0f), new Vector2(size.x, thickness));
            AbsoluteImage(frame, "Left", color, new Vector2(0f, .5f), new Vector2(thickness, size.y));
            AbsoluteImage(frame, "Right", color, new Vector2(1f, .5f), new Vector2(thickness, size.y));
        }

        private static Color EffectColor(NoteKind kind, Chart.Judgement grade, bool down, GameplaySkinStyle skin)
        {
            if (grade == Chart.Judgement.Miss) return skin.MissColor;
            if (kind is NoteKind.Air or NoteKind.AirHold or NoteKind.AirAction) {
                Color air = kind == NoteKind.AirHold ? skin.AirHoldColor
                    : kind == NoteKind.AirAction ? skin.AirActionColor
                    : down ? skin.AirDownColor : skin.AirUpColor;
                return grade == Chart.Judgement.Attack ? Color.Lerp(air, skin.AttackColor, .38f) : air;
            }
            return grade switch {
                Chart.Judgement.JusticeCritical => skin.JusticeCriticalColor,
                Chart.Judgement.Justice => skin.JusticeColor,
                Chart.Judgement.Attack => skin.AttackColor,
                _ => skin.MissColor
            };
        }
    }
}
