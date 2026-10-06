using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    // 制作与感谢页面：只负责滚动名单、文本展示和页面交互，不参与游戏数据或成绩逻辑。
    public sealed partial class AppUiShell
    {
        private RectTransform creditsLayer;
        private RectTransform creditsViewport;
        private RectTransform creditsContent;
        private ScrollRect creditsScroll;
        private Text creditsText;
        private Text creditsLoadingText;
        private Coroutine creditsReadRoutine;
        private string creditsTextCache;
        private bool creditsRead;
        private bool creditsApplyingAutoScroll;
        private float creditsAutoScrollStarted;
        private float creditsAutoScrollDuration;
        private float creditsManualPauseUntil;

        private void BuildCredits()
        {
            if (creditsReadRoutine != null)
            {
                StopCoroutine(creditsReadRoutine);
                creditsReadRoutine = null;
            }

            creditsLayer = Panel("Credits layer", UiRootTransform, new Color(0, 0, 0, .92f),
                Vector2.zero, Vector2.one);
            creditsLayer.offsetMin = Vector2.zero;
            creditsLayer.offsetMax = Vector2.zero;
            Label("制作与感谢", creditsLayer, 38, TextAnchor.MiddleLeft,
                new Vector2(.07f, .89f), new Vector2(.55f, .97f), Color.white);
            ButtonText("关闭", creditsLayer, new Vector2(.82f, .89f), new Vector2(.94f, .97f),
                surfaceRaised, HideCredits);

            RectTransform frame = Panel("Credits frame", creditsLayer, surfaceRaised,
                new Vector2(.07f, .12f), new Vector2(.93f, .86f));
            creditsViewport = Panel("Credits viewport", frame, ink,
                new Vector2(.018f, .018f), new Vector2(.982f, .982f));
            creditsViewport.gameObject.AddComponent<RectMask2D>();
            creditsContent = RectObject("Credits content", creditsViewport,
                new Vector2(0, 1), new Vector2(1, 1));
            creditsContent.pivot = new Vector2(.5f, 1f);
            creditsContent.anchoredPosition = Vector2.zero;
            creditsContent.sizeDelta = new Vector2(0, 0);
            creditsText = Label("", creditsContent, 25, TextAnchor.UpperCenter,
                new Vector2(0, 1), new Vector2(1, 1), Color.white);
            creditsText.raycastTarget = false;
            creditsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            creditsText.verticalOverflow = VerticalWrapMode.Overflow;
            creditsText.rectTransform.pivot = new Vector2(.5f, 1f);
            creditsText.rectTransform.anchoredPosition = new Vector2(0, -30);
            creditsText.rectTransform.sizeDelta = new Vector2(-60, 0);
            creditsLoadingText = Label("正在读取制作与感谢...", creditsViewport, 22, TextAnchor.MiddleCenter,
                new Vector2(.08f, .42f), new Vector2(.92f, .58f), muted);
            creditsLoadingText.raycastTarget = false;

            creditsScroll = frame.gameObject.AddComponent<ScrollRect>();
            creditsScroll.horizontal = false;
            creditsScroll.vertical = true;
            creditsScroll.inertia = true;
            creditsScroll.decelerationRate = .10f;
            creditsScroll.movementType = ScrollRect.MovementType.Clamped;
            creditsScroll.viewport = creditsViewport;
            creditsScroll.content = creditsContent;
            creditsScroll.onValueChanged.AddListener(_ => {
                if (!creditsApplyingAutoScroll)
                    creditsManualPauseUntil = Time.unscaledTime + 1.2f;
            });
            CreditsPointerPause pointerPause = creditsViewport.gameObject.AddComponent<CreditsPointerPause>();
            pointerPause.Pause = () => creditsManualPauseUntil = Time.unscaledTime + 1.2f;

            creditsLayer.gameObject.SetActive(false);
        }

        private void ShowCredits()
        {
            if (creditsLayer == null) return;
            creditsLayer.gameObject.SetActive(true);
            creditsLayer.SetAsLastSibling();
            creditsManualPauseUntil = Time.unscaledTime + .35f;
            creditsAutoScrollStarted = Time.unscaledTime;
            creditsAutoScrollDuration = 0;
            if (creditsRead)
                ApplyCreditsText();
            else
            {
                creditsText.text = string.Empty;
                creditsLoadingText.gameObject.SetActive(true);
                if (creditsReadRoutine == null)
                    creditsReadRoutine = StartCoroutine(ReadCredits());
            }
        }

        private void HideCredits()
        {
            if (creditsLayer != null) creditsLayer.gameObject.SetActive(false);
            creditsScroll?.StopMovement();
        }

        private IEnumerator ReadCredits()
        {
            string source = Path.Combine(Application.streamingAssetsPath, "Credits", "credits.txt")
                .Replace('\\', '/');
            string uri;
            try
            {
                uri = source.Contains("://") ? source : new Uri(source).AbsoluteUri;
            }
            catch (Exception exception)
            {
                creditsTextCache = "无法读取制作与感谢文件。\n\n" + exception.Message;
                Debug.LogError("Credits path invalid: " + source + " | " + exception);
                creditsRead = true;
                creditsReadRoutine = null;
                if (creditsLayer != null && creditsLayer.gameObject.activeSelf)
                    ApplyCreditsText();
                yield break;
            }

            using (UnityWebRequest request = UnityWebRequest.Get(uri))
            {
                request.timeout = 12;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    byte[] bytes = request.downloadHandler.data ?? Array.Empty<byte>();
                    creditsTextCache = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
                    if (string.IsNullOrWhiteSpace(creditsTextCache))
                        creditsTextCache = "CHUNITHM Mobile\n\n暂无制作与感谢内容。";
                }
                else
                {
                    creditsTextCache = "无法读取制作与感谢文件。\n\n" + request.error;
                    Debug.LogError("Credits load failed: " + source + " | " + request.error);
                }
            }
            creditsRead = true;
            creditsReadRoutine = null;
            if (creditsLayer != null && creditsLayer.gameObject.activeSelf)
                ApplyCreditsText();
        }

        private void ApplyCreditsText()
        {
            if (creditsText == null || creditsContent == null || creditsViewport == null) return;
            creditsText.text = LocalizeCreditsText(creditsTextCache ?? "正在读取制作与感谢...");
            if (creditsLoadingText != null) creditsLoadingText.gameObject.SetActive(false);
            Canvas.ForceUpdateCanvases();
            float height = Mathf.Max(creditsViewport.rect.height, creditsText.preferredHeight + 60f);
            creditsContent.sizeDelta = new Vector2(0, height);
            creditsText.rectTransform.sizeDelta = new Vector2(-60, height - 30f);
            creditsScroll.StopMovement();
            creditsScroll.verticalNormalizedPosition = 1f;
            creditsAutoScrollStarted = Time.unscaledTime;
            creditsAutoScrollDuration = Mathf.Max(10f,
                (height - creditsViewport.rect.height) / 32f);
        }

        private void UpdateCreditsAutoScroll()
        {
            if (creditsLayer == null || !creditsLayer.gameObject.activeSelf || !creditsRead ||
                creditsScroll == null || creditsContent == null || creditsViewport == null ||
                creditsContent.rect.height <= creditsViewport.rect.height + 1f ||
                Time.unscaledTime < creditsManualPauseUntil || creditsAutoScrollDuration <= 0)
                return;
            float progress = Mathf.Clamp01((Time.unscaledTime - creditsAutoScrollStarted) /
                creditsAutoScrollDuration);
            creditsApplyingAutoScroll = true;
            creditsScroll.verticalNormalizedPosition = 1f - progress;
            creditsApplyingAutoScroll = false;
            if (progress >= 1f) creditsManualPauseUntil = Time.unscaledTime + 3600f;
        }

        private static string LocalizeCreditsText(string raw)
        {
            if (string.IsNullOrEmpty(raw) || UiLocalization.Current == AppLanguage.SimplifiedChinese)
                return raw;

            string[] lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            string[] rolePrefixes = {
                "制作人", "游戏设计", "程序开发", "音乐选曲 & 编曲", "UI / 视觉设计"
            };
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                string translated = UiLocalization.Translate(trimmed);
                if (translated == trimmed)
                {
                    foreach (string prefix in rolePrefixes)
                    {
                        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal) ||
                            trimmed.Length <= prefix.Length)
                            continue;
                        translated = UiLocalization.Translate(prefix) + trimmed.Substring(prefix.Length);
                        break;
                    }
                }

                int leadingLength = line.Length - line.TrimStart().Length;
                lines[index] = line.Substring(0, leadingLength) + translated;
            }
            return string.Join("\n", lines);
        }

        private sealed class CreditsPointerPause : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
        {
            public Action Pause;

            public void OnPointerDown(PointerEventData eventData) => Pause?.Invoke();
            public void OnPointerUp(PointerEventData eventData) => Pause?.Invoke();
        }
    }
}
