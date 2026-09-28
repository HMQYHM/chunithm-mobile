using System;
using System.IO;

namespace ChunithmMobile.Runtime
{
    // WAV 时长读取工具：读取 PCM fmt/data 块，不加载整首歌曲到内存。
    internal static class WavDurationReader
    {
        public static bool TryReadSeconds(string path, out double seconds)
        {
            seconds = 0;
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);
                if (new string(reader.ReadChars(4)) != "RIFF") return false;
                reader.ReadUInt32();
                if (new string(reader.ReadChars(4)) != "WAVE") return false;
                int channels = 0;
                int sampleRate = 0;
                short bits = 0;
                long dataBytes = 0;
                while (stream.Position + 8 <= stream.Length)
                {
                    string id = new string(reader.ReadChars(4));
                    uint size = reader.ReadUInt32();
                    long next = Math.Min(stream.Length, stream.Position + size + (size & 1));
                    if (id == "fmt " && size >= 16)
                    {
                        reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        sampleRate = reader.ReadInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt16();
                        bits = reader.ReadInt16();
                    }
                    else if (id == "data") dataBytes = size;
                    stream.Position = next;
                }
                int bytesPerFrame = channels * Math.Max(1, bits / 8);
                if (sampleRate <= 0 || bytesPerFrame <= 0 || dataBytes <= 0) return false;
                seconds = dataBytes / (double)(sampleRate * bytesPerFrame);
                return seconds > 0;
            }
            catch { return false; }
        }
    }
}
