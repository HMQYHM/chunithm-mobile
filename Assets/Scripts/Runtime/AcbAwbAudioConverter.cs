using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ChunithmMobile.Runtime
{
    // ACB/AWB 适配器：把 CRI 音频库解码成游戏统一可播放的 PCM WAV。
    // vgmstream 只负责解码，因此这里不删除原始库，也不假装把它编码成 MP3。
    public static class AcbAwbAudioConverter
    {
        private const int ConversionTimeoutMs = 120000;

        public static bool TryConvert(string songRoot, out string message)
        {
            message = "";
            if (string.IsNullOrWhiteSpace(songRoot) || !Directory.Exists(songRoot))
            {
                message = "歌曲目录不存在。";
                return false;
            }

            SongAudioSet audio = SongAudioFiles.Resolve(songRoot);
            if (audio.HasPlayableGameplay)
            {
                message = "歌曲已经存在可播放音频，无需转换。";
                return true;
            }
            if (string.IsNullOrWhiteSpace(audio.SourceAcb) && string.IsNullOrWhiteSpace(audio.SourceAwb))
            {
                message = "没有找到 ACB 或 AWB 音频库。";
                return false;
            }

            string decoder = FindDecoder();
            if (string.IsNullOrWhiteSpace(decoder))
            {
                message = "未找到 vgmstream-cli。桌面端请把 vgmstream-cli 放入 .tools/vgmstream 或 StreamingAssets/Audio/vgmstream；Android/iOS 需要随包提供原生解码库。";
                return false;
            }

            // ACB 通常保存 cue 与 AWB 的配对关系；有 ACB 时优先以 ACB 为入口。
            string input = !string.IsNullOrWhiteSpace(audio.SourceAcb) ? audio.SourceAcb : audio.SourceAwb;
            string outputDirectory = Path.Combine(songRoot, "audio");
            Directory.CreateDirectory(outputDirectory);
            string output = Path.Combine(outputDirectory, SongAudioFiles.GameplayWavFileName);
            string temporary = output + ".partial-" + Guid.NewGuid().ToString("N");

            try
            {
                // vgmstream 默认输出标准 PCM16 WAV；-i 关闭库内循环，避免导入时生成无限/重复音频。
                string arguments = "-i -o " + Quote(temporary) + " " + Quote(input);
                var startInfo = new ProcessStartInfo
                {
                    FileName = decoder,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(input) ?? songRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    message = "无法启动 vgmstream-cli。";
                    return false;
                }
                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(ConversionTimeoutMs))
                {
                    try { process.Kill(); } catch { }
                    message = "ACB/AWB 转换超时，已终止转换进程。";
                    return false;
                }
                if (process.ExitCode != 0 || !IsWaveFile(temporary))
                {
                    string detail = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
                    detail = detail?.Trim();
                    message = "ACB/AWB 转换失败" + (string.IsNullOrWhiteSpace(detail) ? "。" : "：" + detail);
                    return false;
                }

                if (File.Exists(output)) File.Delete(output);
                File.Move(temporary, output);
                message = "ACB/AWB 已转换为 audio/gameplay.wav。";
                UnityEngine.Debug.Log($"ACB/AWB conversion succeeded: input={input}, output={output}");
                return true;
            }
            catch (Exception exception)
            {
                message = "ACB/AWB 转换异常：" + exception.Message;
                UnityEngine.Debug.LogError(message);
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }

        private static string FindDecoder()
        {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
            string[] candidates = {
                Environment.GetEnvironmentVariable("CHUNITHM_VGMSTREAM_CLI"),
                Path.Combine(Application.dataPath, "..", ".tools", "vgmstream", "vgmstream-cli.exe"),
                Path.Combine(Application.dataPath, "..", ".tools", "vgmstream-source", "x64", "Release", "vgmstream-cli.exe"),
                Path.Combine(Application.streamingAssetsPath, "Audio", "vgmstream", "vgmstream-cli.exe"),
                Path.Combine(Application.persistentDataPath, "vgmstream", "vgmstream-cli.exe")
            };
            return candidates.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Select(Path.GetFullPath).FirstOrDefault();
#else
            // 移动端不能直接启动桌面 CLI；保留此分支，让缺少 native 解码库时给出可理解的导入错误。
            return null;
#endif
        }

        private static bool IsWaveFile(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length < 44) return false;
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);
                return new string(reader.ReadChars(4)) == "RIFF" &&
                       new string(reader.ReadChars(4)) != "" &&
                       new string(reader.ReadChars(4)) == "WAVE";
            }
            catch { return false; }
        }

        private static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
    }
}
