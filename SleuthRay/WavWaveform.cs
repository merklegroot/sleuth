using System.Buffers.Binary;

namespace SleuthRay;

internal static class WavWaveform
{
    /// <summary>
    /// Computes absolute peak amplitudes (0..1) for a waveform preview.
    /// Supports uncompressed PCM (8/16/24/32-bit) and IEEE float (32-bit).
    /// </summary>
    public static bool TryComputePeaks(ReadOnlySpan<byte> wavBytes, int peakCount, out float[] peaks)
    {
        peaks = Array.Empty<float>();
        if (peakCount <= 0 || wavBytes.Length < 44)
        {
            return false;
        }

        // RIFF header
        if (!wavBytes[..4].SequenceEqual("RIFF"u8) || !wavBytes[8..12].SequenceEqual("WAVE"u8))
        {
            return false;
        }

        ushort formatTag = 0;
        ushort channels = 0;
        uint sampleRate = 0;
        ushort bitsPerSample = 0;
        ReadOnlySpan<byte> data = default;

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
                off++; // word align
            }
        }

        if (data.Length == 0 || channels == 0 || sampleRate == 0 || bitsPerSample == 0)
        {
            return false;
        }

        bool pcm = formatTag == 1;
        bool f32 = formatTag == 3 && bitsPerSample == 32;
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
