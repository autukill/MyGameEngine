namespace GameEngine.Features.Audio;

using System.Buffers.Binary;
using System.Text;

/// <summary>Strict RIFF/WAVE decoder for short, uncompressed PCM8/PCM16 gameplay sounds.</summary>
public static class WaveAudioDecoder
{
    private const ushort PcmFormat = 1;

    public static DecodedAudioClip Decode(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("The WAV stream must be readable.", nameof(source));
        if (!source.CanSeek)
        {
            using var buffered = new MemoryStream();
            source.CopyTo(buffered);
            buffered.Position = 0;
            return Decode(buffered);
        }

        using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
        if (ReadFourCc(reader) != "RIFF")
            throw new InvalidDataException("Audio asset is not a RIFF file.");
        _ = ReadUInt32(reader, "RIFF size");
        if (ReadFourCc(reader) != "WAVE")
            throw new InvalidDataException("RIFF asset is not a WAVE file.");

        ushort? format = null;
        ushort? channels = null;
        uint? sampleRate = null;
        ushort? blockAlign = null;
        ushort? bitsPerSample = null;
        byte[]? pcm = null;

        while (source.Position < source.Length)
        {
            string chunkId = ReadFourCc(reader);
            uint chunkSize = ReadUInt32(reader, $"'{chunkId}' chunk size");
            long chunkEnd = checked(source.Position + chunkSize);
            if (chunkEnd > source.Length)
                throw new InvalidDataException($"WAV chunk '{chunkId}' exceeds the stream length.");

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16)
                    throw new InvalidDataException("WAV format chunk is too small.");
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                blockAlign = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
            }
            else if (chunkId == "data")
            {
                if (chunkSize > int.MaxValue)
                    throw new InvalidDataException("WAV data is too large for a static clip.");
                pcm = reader.ReadBytes((int)chunkSize);
                if (pcm.Length != chunkSize)
                    throw new EndOfStreamException("WAV PCM data ended unexpectedly.");
            }

            source.Position = chunkEnd;
            if ((chunkSize & 1u) != 0 && source.Position < source.Length)
                source.Position++;
        }

        if (format != PcmFormat)
            throw new InvalidDataException("Only uncompressed integer PCM WAV assets are supported.");
        if (channels is not (1 or 2))
            throw new InvalidDataException("WAV assets must be mono or stereo.");
        if (sampleRate is null or < 8_000 or > 384_000)
            throw new InvalidDataException("WAV sample rate is outside the supported range.");
        AudioSampleFormat sampleFormat = bitsPerSample switch
        {
            8 => AudioSampleFormat.Unsigned8,
            16 => AudioSampleFormat.Signed16,
            _ => throw new InvalidDataException("Only PCM8 and PCM16 WAV assets are supported.")
        };
        int expectedAlign = channels.Value * (bitsPerSample.Value / 8);
        if (blockAlign != expectedAlign)
            throw new InvalidDataException("WAV block alignment does not match its channel/sample format.");
        if (pcm is null)
            throw new InvalidDataException("WAV asset has no data chunk.");

        return new DecodedAudioClip(pcm, sampleFormat, channels.Value, checked((int)sampleRate.Value));
    }

    public static DecodedAudioClip DecodeFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = File.OpenRead(path);
        return Decode(stream);
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (bytes.Length != 4)
            throw new EndOfStreamException("WAV chunk header ended unexpectedly.");
        return Encoding.ASCII.GetString(bytes);
    }

    private static uint ReadUInt32(BinaryReader reader, string field)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (reader.Read(bytes) != 4)
            throw new EndOfStreamException($"WAV {field} ended unexpectedly.");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }
}
