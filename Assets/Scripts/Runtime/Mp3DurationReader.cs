using System;
using System.IO;

namespace ChunithmMobile.Runtime
{
    // MP3 时长读取工具：只解析文件头/帧信息，用于导入进度和歌曲索引，不负责播放。
    /// <summary>Small allocation-free MP3 duration reader for the song catalog.</summary>
    internal static class Mp3DurationReader
    {
        private static readonly int[] BitrateV1L3 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
        private static readonly int[] BitrateV2L3 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 };
        private static readonly int[] SampleV1 = { 44100, 48000, 32000, 0 };

        public static bool TryReadSeconds(string path, out double seconds)
        {
            seconds = 0;
            try {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                long audioStart = SkipId3(stream);
                byte[] scan = new byte[(int)Math.Min(131072, Math.Max(0, stream.Length - audioStart))];
                stream.Position = audioStart;
                int read = stream.Read(scan, 0, scan.Length);
                for (int i = 0; i + 4 <= read; i++) {
                    uint header = (uint)(scan[i] << 24 | scan[i + 1] << 16 | scan[i + 2] << 8 | scan[i + 3]);
                    if (!TryHeader(header, out int bitrate, out int sampleRate, out int samplesPerFrame, out bool mono, out bool version1)) continue;
                    int sideInfo = version1 ? (mono ? 17 : 32) : (mono ? 9 : 17);
                    int xing = i + 4 + sideInfo;
                    if (xing + 12 <= read && (Ascii(scan, xing, "Xing") || Ascii(scan, xing, "Info"))) {
                        uint flags = ReadBe32(scan, xing + 4);
                        if ((flags & 1) != 0) {
                            uint frames = ReadBe32(scan, xing + 8);
                            if (frames > 0) { seconds = frames * (double)samplesPerFrame / sampleRate; return seconds > 0; }
                        }
                    }
                    long audioBytes = stream.Length - (audioStart + i);
                    seconds = audioBytes * 8.0 / (bitrate * 1000.0);
                    return seconds > 0;
                }
            }
            catch { }
            return false;
        }

        private static long SkipId3(Stream stream)
        {
            byte[] header = new byte[10];
            if (stream.Read(header, 0, 10) != 10 || header[0] != 'I' || header[1] != 'D' || header[2] != '3') return 0;
            int size = (header[6] & 0x7f) << 21 | (header[7] & 0x7f) << 14 | (header[8] & 0x7f) << 7 | header[9] & 0x7f;
            return 10L + size + ((header[5] & 0x10) != 0 ? 10 : 0);
        }

        private static bool TryHeader(uint h, out int bitrate, out int sampleRate, out int samples, out bool mono, out bool version1)
        {
            bitrate = sampleRate = samples = 0; mono = false; version1 = false;
            if ((h & 0xffe00000u) != 0xffe00000u) return false;
            int version = (int)((h >> 19) & 3), layer = (int)((h >> 17) & 3);
            int bitrateIndex = (int)((h >> 12) & 15), sampleIndex = (int)((h >> 10) & 3);
            if (version == 1 || layer != 1 || bitrateIndex == 0 || bitrateIndex == 15 || sampleIndex == 3) return false;
            version1 = version == 3;
            bitrate = (version1 ? BitrateV1L3 : BitrateV2L3)[bitrateIndex];
            sampleRate = SampleV1[sampleIndex] / (version1 ? 1 : version == 2 ? 2 : 4);
            samples = version1 ? 1152 : 576;
            mono = ((h >> 6) & 3) == 3;
            return bitrate > 0 && sampleRate > 0;
        }

        private static bool Ascii(byte[] data, int offset, string value)
        {
            for (int i = 0; i < value.Length; i++) if (data[offset + i] != value[i]) return false;
            return true;
        }

        private static uint ReadBe32(byte[] data, int offset) =>
            (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);
    }
}
