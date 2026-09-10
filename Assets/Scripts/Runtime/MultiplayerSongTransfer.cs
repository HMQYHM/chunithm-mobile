using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ChunithmMobile.Platform;
using UnityEngine;
using UnityEngine.Networking;

namespace ChunithmMobile.Runtime
{
    public sealed class MultiplayerPreparedPackage
    {
        public string Path;
        public string PackageHash;
        public string ChartHash;
        public long Size;
    }

    public sealed class MultiplayerSongTransfer : MonoBehaviour
    {
        public Task<MultiplayerPreparedPackage> PrepareHostPackageAsync(string songRoot, string chartPath,
            Action<float, string> progress = null)
        {
            string cacheRoot = Path.Combine(Application.temporaryCachePath, "MultiplayerTransfers");
            Directory.CreateDirectory(cacheRoot);
            return Task.Run(() => BuildPackage(songRoot, chartPath, cacheRoot, progress));
        }

        public static Task<string> ComputeSha256Async(string filePath)
        {
            return Task.Run(() => ComputeSha256(filePath));
        }

        public IEnumerator Upload(string serverAddress, string roomId, string sessionToken,
            MultiplayerPreparedPackage package, Action<float, string> progress, Action<bool, string> completed)
        {
            if (package == null || !File.Exists(package.Path) || !TryBuildTransferUri(serverAddress, roomId,
                    package.PackageHash, out Uri uri))
            {
                completed?.Invoke(false, "无法准备上传地址或歌曲包。");
                yield break;
            }
            progress?.Invoke(0f, "正在上传歌曲与谱面…");
            using (var request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerFile(package.Path);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", "Bearer " + sessionToken);
                request.SetRequestHeader("Content-Type", "application/zip");
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    progress?.Invoke(Mathf.Clamp01(request.uploadProgress), "正在上传歌曲与谱面…");
                    yield return null;
                }
                if (request.result != UnityWebRequest.Result.Success || request.responseCode < 200 || request.responseCode >= 300)
                {
                    completed?.Invoke(false, "歌曲上传失败：" + (request.error ?? request.responseCode.ToString()));
                    yield break;
                }
            }
            progress?.Invoke(1f, "歌曲包已上传，等待其他成员校验。");
            completed?.Invoke(true, null);
        }

        public IEnumerator DownloadAndImport(string serverAddress, string roomId, string sessionToken,
            MultiplayerSongDto song, string destinationRoot, Action<float, string> progress,
            Action<bool, string, SongImportResult> completed)
        {
            if (song == null || string.IsNullOrWhiteSpace(song.packageHash) ||
                !TryBuildTransferUri(serverAddress, roomId, song.packageHash, out Uri uri))
            {
                completed?.Invoke(false, "服务器没有提供有效的歌曲下载地址。", null);
                yield break;
            }
            string cacheRoot = Path.Combine(Application.temporaryCachePath, "MultiplayerTransfers");
            Directory.CreateDirectory(cacheRoot);
            string partialPath = Path.Combine(cacheRoot, song.packageHash + ".download");
            long existing = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            if (existing >= song.packageSize || existing < 0)
            {
                TryDelete(partialPath);
                existing = 0;
            }

            bool retryFromStart = false;
            do
            {
                retryFromStart = false;
                progress?.Invoke(song.packageSize <= 0 ? 0f : existing / (float)song.packageSize,
                    existing > 0 ? "正在继续下载歌曲数据…" : "正在下载歌曲数据…");
                using (var request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET))
                {
                    request.downloadHandler = new DownloadHandlerFile(partialPath, existing > 0);
                    request.SetRequestHeader("Authorization", "Bearer " + sessionToken);
                    if (existing > 0) request.SetRequestHeader("Range", "bytes=" + existing + "-");
                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        long downloaded = existing + (long)request.downloadedBytes;
                        float fraction = song.packageSize <= 0 ? request.downloadProgress : downloaded / (float)song.packageSize;
                        progress?.Invoke(Mathf.Clamp01(fraction), "正在下载歌曲数据…");
                        yield return null;
                    }
                    if (existing > 0 && request.responseCode == 200)
                    {
                        TryDelete(partialPath);
                        existing = 0;
                        retryFromStart = true;
                        continue;
                    }
                    if (request.result != UnityWebRequest.Result.Success || (request.responseCode != 200 && request.responseCode != 206))
                    {
                        completed?.Invoke(false, "歌曲下载失败：" + (request.error ?? request.responseCode.ToString()), null);
                        yield break;
                    }
                }
            }
            while (retryFromStart);

            progress?.Invoke(.96f, "正在校验歌曲数据…");
            Task<string> hashTask = ComputeSha256Async(partialPath);
            while (!hashTask.IsCompleted) yield return null;
            if (hashTask.IsFaulted || !string.Equals(hashTask.Result, song.packageHash, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(partialPath);
                completed?.Invoke(false, "歌曲数据校验失败，请重新下载。", null);
                yield break;
            }

            progress?.Invoke(.98f, "正在写入联机歌曲库…");
            SongImportResult importResult = null;
            Exception importError = null;
            Task<SongImportResult> importTask = SongImportManager.ImportAsync(partialPath,
                PlatformServices.Current.PersistentDataPath, Application.temporaryCachePath, destinationRoot,
                item => progress?.Invoke(.98f + .02f * Mathf.Clamp01(item.Fraction), item.Message),
                PlatformMemoryInfo.GetAvailableMemoryBytes());
            while (!importTask.IsCompleted) yield return null;
            if (importTask.IsFaulted) importError = importTask.Exception?.GetBaseException();
            else importResult = importTask.Result;
            TryDelete(partialPath);
            if (importError != null)
            {
                completed?.Invoke(false, "联机歌曲导入失败：" + importError.Message, null);
                yield break;
            }
            if (importResult == null || (importResult.Imported <= 0 && importResult.Skipped <= 0))
            {
                completed?.Invoke(false, "联机歌曲包中没有可用的歌曲。", importResult);
                yield break;
            }
            progress?.Invoke(1f, "歌曲与谱面已校验完成。");
            completed?.Invoke(true, null, importResult);
        }

        public static void DeletePreparedPackage(MultiplayerPreparedPackage package)
        {
            if (package != null) TryDelete(package.Path);
        }

        private static MultiplayerPreparedPackage BuildPackage(string songRoot, string chartPath, string cacheRoot,
            Action<float, string> progress)
        {
            if (string.IsNullOrWhiteSpace(songRoot) || !Directory.Exists(songRoot))
                throw new DirectoryNotFoundException("歌曲目录不存在。");
            if (string.IsNullOrWhiteSpace(chartPath) || !File.Exists(chartPath))
                throw new FileNotFoundException("谱面文件不存在。", chartPath);
            string[] files = Directory.EnumerateFiles(songRoot, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length == 0) throw new InvalidDataException("歌曲目录中没有可传输的文件。");
            long totalBytes = files.Sum(path => new FileInfo(path).Length);
            long completedBytes = 0;
            string outputPath = Path.Combine(cacheRoot, "host-" + Guid.NewGuid().ToString("N") + ".zip");
            string rootName = SafeEntryName(new DirectoryInfo(songRoot).Name);
            try
            {
                using (FileStream output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024))
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, false))
                {
                    foreach (string file in files)
                    {
                        string relative = file.Substring(songRoot.Length)
                            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            .Replace('\\', '/');
                        var entry = archive.CreateEntry(rootName + "/" + relative, System.IO.Compression.CompressionLevel.Fastest);
                        using Stream input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
                            FileOptions.SequentialScan);
                        using Stream target = entry.Open();
                        byte[] buffer = new byte[1024 * 1024];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            target.Write(buffer, 0, read);
                            completedBytes += read;
                            progress?.Invoke(totalBytes <= 0 ? 0f : completedBytes / (float)totalBytes,
                                "正在整理歌曲文件…");
                        }
                    }
                }
                return new MultiplayerPreparedPackage {
                    Path = outputPath,
                    ChartHash = ComputeSha256(chartPath),
                    PackageHash = ComputeSha256(outputPath),
                    Size = new FileInfo(outputPath).Length
                };
            }
            catch
            {
                TryDelete(outputPath);
                throw;
            }
        }

        private static string ComputeSha256(string filePath)
        {
            using SHA256 sha = SHA256.Create();
            using Stream input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
                FileOptions.SequentialScan);
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }

        private static string SafeEntryName(string value)
        {
            string result = new string((value ?? "song").Where(character => character != '/' && character != '\\').ToArray());
            return string.IsNullOrWhiteSpace(result) ? "song" : result;
        }

        private static bool TryBuildTransferUri(string serverAddress, string roomId, string packageHash, out Uri uri)
        {
            uri = null;
            if (!MultiplayerClient.TryBuildHttpBaseUri(serverAddress, out Uri baseUri) ||
                string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(packageHash)) return false;
            return Uri.TryCreate(baseUri, $"api/v1/transfers/{roomId}/{packageHash}", out uri);
        }

        private static void TryDelete(string filePath)
        {
            try { if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath)) File.Delete(filePath); }
            catch (Exception exception) { Debug.LogWarning("Unable to remove multiplayer transfer cache: " + exception.Message); }
        }
    }
}
