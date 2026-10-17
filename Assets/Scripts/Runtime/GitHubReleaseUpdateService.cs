using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// Reads the latest public GitHub Release without blocking the loading flow.
    /// It only reports a newer stable version; installation remains an explicit
    /// action through the release page so each platform keeps its normal signing
    /// and store rules.
    /// </summary>
    public sealed class GitHubReleaseUpdateService : MonoBehaviour
    {
        public const string LatestReleaseEndpoint =
            "https://api.github.com/repos/HMQYHM/chunithm-mobile/releases/latest";

        [Serializable]
        private sealed class ReleaseResponse
        {
            public string tag_name;
            public string name;
            public string body;
            public string html_url;
            public bool draft;
            public bool prerelease;
        }

        public sealed class ReleaseUpdate
        {
            public string Version;
            public string Name;
            public string Body;
            public string Url;
        }

        public void Check(string currentVersion, Action<ReleaseUpdate> newerRelease,
            Action<string> failed = null)
        {
            StartCoroutine(CheckRoutine(currentVersion, newerRelease, failed));
        }

        private IEnumerator CheckRoutine(string currentVersion, Action<ReleaseUpdate> newerRelease,
            Action<string> failed)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(LatestReleaseEndpoint))
            {
                request.timeout = 6;
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "CHUNITHM-Mobile-UpdateChecker");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    failed?.Invoke(request.error ?? ("HTTP " + request.responseCode));
                    yield break;
                }

                ReleaseResponse response;
                try { response = JsonUtility.FromJson<ReleaseResponse>(request.downloadHandler.text); }
                catch (Exception exception)
                {
                    failed?.Invoke("invalid GitHub release response: " + exception.Message);
                    yield break;
                }

                if (response == null || response.draft || response.prerelease ||
                    !TryParseVersion(response.tag_name, out Version latest) ||
                    !TryParseVersion(currentVersion, out Version current) || latest.CompareTo(current) <= 0)
                    yield break;

                newerRelease?.Invoke(new ReleaseUpdate {
                    Version = latest.ToString(),
                    Name = string.IsNullOrWhiteSpace(response.name) ? response.tag_name : response.name,
                    Body = response.body ?? string.Empty,
                    Url = response.html_url ?? "https://github.com/HMQYHM/chunithm-mobile/releases"
                });
            }
        }

        private static bool TryParseVersion(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string normalized = Regex.Replace(value.Trim(), "^[vV]", string.Empty);
            int separator = normalized.IndexOfAny(new[] { '-', '+' });
            if (separator >= 0) normalized = normalized.Substring(0, separator);
            return Version.TryParse(normalized, out version);
        }
    }
}
