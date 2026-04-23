using System.Buffers.Binary;
using System.Globalization;

namespace SleuthRay;

internal ref struct WavPcmWaveView
{
    public uint SampleRate;
    public ushort Channels;
    public ushort BitsPerSample;
    public bool IsFloat32;
    public ReadOnlySpan<byte> Data;
    public int FrameCount;

    public readonly float DurationSeconds => FrameCount / (float)SampleRate;
}

internal static class WavWaveform
{
    /// <summary>Returns sample rate (Hz) and duration (seconds) for PCM / IEEE-float WAV data.</summary>
    public static bool TryGetAudioInfo(ReadOnlySpan<byte> wavBytes, out int sampleRateHz, out float durationSeconds)
    {
        sampleRateHz = 0;
        durationSeconds = 0f;
        if (!TryPreparePcmWave(wavBytes, out WavPcmWaveView view))
        {
            return false;
        }

        sampleRateHz = (int)view.SampleRate;
        durationSeconds = view.DurationSeconds;
        return sampleRateHz > 0 && durationSeconds >= 0f;
    }

    /// <summary>Formats duration and sample rate for UI (invariant).</summary>
    public static string FormatAudioInfoLine(ReadOnlySpan<byte> wavBytes)
    {
        if (!TryGetAudioInfo(wavBytes, out int sr, out float dur))
        {
            return "";
        }

        string d = dur.ToString("0.###", CultureInfo.InvariantCulture);
        string s = sr.ToString("N0", CultureInfo.InvariantCulture);
        return $"{d} s · {s} Hz";
    }

    /// <summary>
    /// Computes absolute peak amplitudes (0..1) for a waveform preview.
    /// Supports uncompressed PCM (8/16/24/32-bit) and IEEE float (32-bit).
    /// </summary>
    public static bool TryComputePeaks(ReadOnlySpan<byte> wavBytes, int peakCount, out float[] peaks)
    {
        peaks = Array.Empty<float>();
        if (peakCount <= 0 || !TryPreparePcmWave(wavBytes, out WavPcmWaveView view))
        {
            return false;
        }

        ReadOnlySpan<byte> data = view.Data;
        int frameCount = view.FrameCount;
        ushort channels = view.Channels;
        ushort bitsPerSample = view.BitsPerSample;
        bool f32 = view.IsFloat32;

        int bytesPerSample = (bitsPerSample + 7) / 8;
        int frameBytes = bytesPerSample * channels;

        peaks = new float[peakCount];
        int framesPerBucket = Math.Max(1, frameCount / peakCount);

        int frameIndex = 0;
        for (int i = 0; i < peakCount; i++)
        {
            float peak = 0f;
            int end = (i == peakCount - 1) ? frameCount : Math.Min(frameCount, frameIndex + framesPerBucket);

            for (; frameIndex < end; frameIndex++)
            {
                int frameOff = frameIndex * frameBytes;
                float sumAbs = 0f;

                for (int ch = 0; ch < channels; ch++)
                {
                    int sOff = frameOff + ch * bytesPerSample;
                    float v = f32
                        ? ReadF32(data, sOff)
                        : ReadPcm(data, sOff, bitsPerSample);
                    sumAbs += MathF.Abs(v);
                }

                float avgAbs = sumAbs / channels;
                if (avgAbs > peak)
                {
                    peak = avgAbs;
                }
            }

            peaks[i] = Math.Clamp(peak, 0f, 1f);
        }

        return true;
    }

    static bool TryPreparePcmWave(ReadOnlySpan<byte> wavBytes, out WavPcmWaveView view)
    {
        view = default;
        if (wavBytes.Length < 44)
        {
            return false;
        }

        if (!wavBytes[..4].SequenceEqual("RIFF"u8) || !wavBytes[8..12].SequenceEqual("WAVE"u8))
        {
            return false;
        }

        ushort formatTag = 0;
        ushort channels = 0;
        uint sampleRate = 0;
        ushort bitsPerSample = 0;
        ReadOnlySpan<byte> data = default;
        ReadOnlySpan<byte> fmtChunk = default;

        int off = 12;
        while (off + 8 <= wavBytes.Length)
        {
            ReadOnlySpan<byte> id = wavBytes.Slice(off, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(wavBytes.Slice(off + 4, 4));
            off += 8;
            if (off + size > wavBytes.Length)
            {
                return false;
            }

            ReadOnlySpan<byte> chunk = wavBytes.Slice(off, (int)size);
            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                fmtChunk = chunk;
                formatTag = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(0, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(2, 2));
                sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(4, 4));
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(14, 2));
            }
            else if (id.SequenceEqual("data"u8))
            {
                data = chunk;
            }

            off += (int)size;
            if ((off & 1) == 1)
            {
                off++;
            }
        }

        if (data.Length == 0 || channels == 0 || sampleRate == 0 || bitsPerSample == 0)
        {
            return false;
        }

        const ushort WAVE_FORMAT_PCM = 1;
        const ushort WAVE_FORMAT_IEEE_FLOAT = 3;
        const ushort WAVE_FORMAT_EXTENSIBLE = 65534;

        ushort effectiveFormatTag = formatTag;
        if (formatTag == WAVE_FORMAT_EXTENSIBLE)
        {
            if (fmtChunk.Length < 40)
            {
                return false;
            }

            ReadOnlySpan<byte> subFormat = fmtChunk.Slice(24, 16);
            effectiveFormatTag = SubFormatToWavTagOrUnknown(subFormat);
        }

        bool pcm = effectiveFormatTag == WAVE_FORMAT_PCM;
        bool f32 = effectiveFormatTag == WAVE_FORMAT_IEEE_FLOAT && bitsPerSample == 32;
        if (!pcm && !f32)
        {
            return false;
        }

        int bytesPerSample = (bitsPerSample + 7) / 8;
        int frameBytes = bytesPerSample * channels;
        if (frameBytes <= 0 || data.Length < frameBytes)
        {
            return false;
        }

        int frameCount = data.Length / frameBytes;
        if (frameCount <= 0)
        {
            return false;
        }

        view = new WavPcmWaveView
        {
            SampleRate = sampleRate,
            Channels = channels,
            BitsPerSample = bitsPerSample,
            IsFloat32 = f32,
            Data = data,
            FrameCount = frameCount,
        };
        return true;
    }

    static ushort SubFormatToWavTagOrUnknown(ReadOnlySpan<byte> guid16)
    {
        if (guid16.Length != 16)
        {
            return 0;
        }

        uint data1 = BinaryPrimitives.ReadUInt32LittleEndian(guid16.Slice(0, 4));
        ushort data2 = BinaryPrimitives.ReadUInt16LittleEndian(guid16.Slice(4, 2));
        ushort data3 = BinaryPrimitives.ReadUInt16LittleEndian(guid16.Slice(6, 2));
        ReadOnlySpan<byte> data4 = guid16.Slice(8, 8);

        if (data2 != 0x0000 || data3 != 0x0010)
        {
            return 0;
        }

        ReadOnlySpan<byte> tail = stackalloc byte[] { 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71 };
        if (!data4.SequenceEqual(tail))
        {
            return 0;
        }

        return data1 switch
        {
            0x0000_0001 => 1,
            0x0000_0003 => 3,
            _ => 0
        };
    }

    static float ReadF32(ReadOnlySpan<byte> data, int offset)
    {
        int bits = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        float v = BitConverter.Int32BitsToSingle(bits);
        if (float.IsNaN(v) || float.IsInfinity(v))
        {
            return 0f;
        }
        return Math.Clamp(v, -1f, 1f);
    }

    static float ReadPcm(ReadOnlySpan<byte> data, int offset, int bitsPerSample)
    {
        return bitsPerSample switch
        {
            8 => (data[offset] - 128) / 128f,
            16 => BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2)) / 32768f,
            24 => ReadI24(data, offset) / 8388608f,
            32 => BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)) / 2147483648f,
            _ => 0f
        };
    }

    static int ReadI24(ReadOnlySpan<byte> data, int offset)
    {
        int b0 = data[offset + 0];
        int b1 = data[offset + 1];
        int b2 = data[offset + 2];
        int v = b0 | (b1 << 8) | (b2 << 16);
        if ((v & 0x0080_0000) != 0)
        {
            v |= unchecked((int)0xFF00_0000);
        }
        return v;
    }
}
