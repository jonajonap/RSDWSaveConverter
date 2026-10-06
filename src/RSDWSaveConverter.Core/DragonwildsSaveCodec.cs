using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace RSDWSaveConverter.Core;

public sealed record DragonwildsSaveMetadata(
    string WorldName,
    string? SavedAtUtc,
    long UncompressedSize,
    bool IsDedicatedServer = false,
    bool HasPassword = false,
    int SessionPrivacy = 0);

public static partial class DragonwildsSaveCodec
{
    public const int WrapperHeaderSize = 12;
    public const int CompressionBlockSize = 65_536;

    private static ReadOnlySpan<byte> SaveMagic => "SAVE"u8;

    public static bool IsRawSave(ReadOnlySpan<byte> data) => data.StartsWith(SaveMagic);

    public static bool IsWrappedSave(ReadOnlySpan<byte> data)
    {
        if (data.Length < WrapperHeaderSize + 2)
        {
            return false;
        }

        return BinaryPrimitives.ReadInt32LittleEndian(data) == WrapperHeaderSize
            && BinaryPrimitives.ReadInt32LittleEndian(data[4..]) == CompressionBlockSize
            && data[12] == 0x78;
    }

    public static byte[] ReadRawSave(string path)
    {
        var data = File.ReadAllBytes(path);
        return IsWrappedSave(data) ? Unwrap(data) : ValidateRawSave(data);
    }

    public static byte[] Wrap(ReadOnlySpan<byte> rawSave)
    {
        ValidateRawSave(rawSave);

        using var output = new MemoryStream();
        Span<byte> header = stackalloc byte[WrapperHeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(header, WrapperHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], CompressionBlockSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], rawSave.Length);
        output.Write(header);

        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(rawSave);
        }

        return output.ToArray();
    }

    public static byte[] Unwrap(ReadOnlySpan<byte> wrappedSave)
    {
        if (!IsWrappedSave(wrappedSave))
        {
            throw new InvalidDataException("The file is not a supported Dragonwilds Game Pass payload.");
        }

        var expectedLength = BinaryPrimitives.ReadInt32LittleEndian(wrappedSave[8..]);
        if (expectedLength <= 0)
        {
            throw new InvalidDataException("The Game Pass payload has an invalid uncompressed length.");
        }

        using var input = new MemoryStream(wrappedSave[WrapperHeaderSize..].ToArray(), writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(expectedLength);
        zlib.CopyTo(output);
        var rawSave = output.ToArray();

        if (rawSave.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"The Game Pass payload declared {expectedLength:N0} bytes but produced {rawSave.Length:N0} bytes.");
        }

        return ValidateRawSave(rawSave);
    }

    public static DragonwildsSaveMetadata ReadMetadata(ReadOnlySpan<byte> rawSave, string? fallbackName = null)
    {
        ValidateRawSave(rawSave);

        var worldName = string.IsNullOrWhiteSpace(fallbackName) ? "Imported World" : fallbackName.Trim();
        string? savedAt = null;
        bool isDedicatedServer = false;
        bool hasPassword = false;
        int sessionPrivacy = 0;

        if (rawSave.Length >= 16 && rawSave.Slice(8, 4).SequenceEqual("INFO"u8))
        {
            var infoLength = BinaryPrimitives.ReadInt32LittleEndian(rawSave[12..]);
            if (infoLength > 0 && infoLength <= rawSave.Length - 16)
            {
                var infoText = Encoding.Latin1.GetString(rawSave.Slice(16, infoLength));
                savedAt = TimestampRegex().Match(infoText) is { Success: true } timestamp
                    ? timestamp.Value
                    : null;

                var revisionOffset = infoText.IndexOf("Meta_SaveFileRevision", StringComparison.Ordinal);
                if (revisionOffset >= 0)
                {
                    var values = PrintableTextRegex().Matches(infoText[(revisionOffset + 21)..]);
                    if (values.Count > 0)
                    {
                        var candidate = values[0].Value.Trim();
                        if (candidate.Length is > 0 and <= 100)
                        {
                            worldName = candidate;
                        }
                    }
                }
                TryReadCinfSessionInfo(rawSave.Slice(16, infoLength), out isDedicatedServer, out hasPassword, out sessionPrivacy);
            }
        }

        return new DragonwildsSaveMetadata(worldName, savedAt, rawSave.Length, isDedicatedServer, hasPassword, sessionPrivacy);
    }

    public static string MakeSafeSlotName(string worldName)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(worldName
            .Where(character => !invalid.Contains(character) && !char.IsControl(character))
            .ToArray())
            .Trim()
            .TrimEnd('.');

        return string.IsNullOrWhiteSpace(cleaned) ? "Imported World" : cleaned[..Math.Min(cleaned.Length, 96)];
    }

    private static byte[] ValidateRawSave(byte[] rawSave)
    {
        ValidateRawSave(rawSave.AsSpan());
        return rawSave;
    }

    private static void ValidateRawSave(ReadOnlySpan<byte> rawSave)
    {
        if (!IsRawSave(rawSave))
        {
            throw new InvalidDataException("The selected file is not a Dragonwilds world save (missing SAVE signature).");
        }

        if (rawSave.Length > int.MaxValue)
        {
            throw new InvalidDataException("The selected save is too large to convert.");
        }
    }

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"[\x20-\x7E]{3,100}", RegexOptions.CultureInvariant)]
    private static partial Regex PrintableTextRegex();
    public static byte[] ConvertToLocalSession(ReadOnlySpan<byte> rawSave)
    {
        ValidateRawSave(rawSave);

        // 1. Repair truncation if declared payload length exceeds provided bytes (e.g. FTP download EOF cutoff)
        byte[] workingData;
        int declaredPayload = BinaryPrimitives.ReadInt32LittleEndian(rawSave[4..]);
        if (declaredPayload > rawSave.Length - 8 && declaredPayload <= rawSave.Length - 8 + 32)
        {
            workingData = new byte[8 + declaredPayload];
            rawSave.CopyTo(workingData);
        }
        else
        {
            workingData = rawSave.ToArray();
        }

        // 2. Find INFO chunk
        if (workingData.Length < 16 || !workingData.AsSpan(8, 4).SequenceEqual("INFO"u8))
        {
            return workingData;
        }

        int infoLength = BinaryPrimitives.ReadInt32LittleEndian(workingData.AsSpan(12, 4));
        if (infoLength <= 0 || infoLength > workingData.Length - 16)
        {
            return workingData;
        }

        var infoSpan = workingData.AsSpan(16, infoLength);
        int cinfOffsetInInfo = infoSpan.IndexOf("CINF"u8);
        if (cinfOffsetInInfo < 0 || cinfOffsetInInfo + 8 > infoSpan.Length)
        {
            return workingData;
        }

        int cinfLength = BinaryPrimitives.ReadInt32LittleEndian(infoSpan.Slice(cinfOffsetInInfo + 4, 4));
        if (cinfLength <= 0 || cinfOffsetInInfo + 8 + cinfLength > infoSpan.Length)
        {
            return workingData;
        }

        var cinfBody = infoSpan.Slice(cinfOffsetInInfo + 8, cinfLength);
        if (cinfBody.Length < 4)
        {
            return workingData;
        }

        int numKeys = BinaryPrimitives.ReadInt32LittleEndian(cinfBody[..4]);
        int offset = 4;
        var keys = new List<(string Name, int Length, byte[] RawBytes)>(numKeys);

        for (int i = 0; i < numKeys; i++)
        {
            if (offset + 4 > cinfBody.Length) return workingData;
            int klen = BinaryPrimitives.ReadInt32LittleEndian(cinfBody.Slice(offset, 4));
            offset += 4;
            if (offset + klen > cinfBody.Length) return workingData;
            var knameBytes = cinfBody.Slice(offset, klen).ToArray();
            var kname = Encoding.Latin1.GetString(knameBytes).TrimEnd('\0');
            keys.Add((kname, klen, knameBytes));
            offset += klen;
        }

        if (offset + 4 > cinfBody.Length) return workingData;
        int numOffsets = BinaryPrimitives.ReadInt32LittleEndian(cinfBody.Slice(offset, 4));
        offset += 4;

        if (offset + numOffsets * 4 + 4 > cinfBody.Length) return workingData;
        var oldOffsets = new int[numOffsets];
        for (int i = 0; i < numOffsets; i++)
        {
            oldOffsets[i] = BinaryPrimitives.ReadInt32LittleEndian(cinfBody.Slice(offset, 4));
            offset += 4;
        }

        int valuesBlobLength = BinaryPrimitives.ReadInt32LittleEndian(cinfBody.Slice(offset, 4));
        offset += 4;
        var valuesBlob = cinfBody.Slice(offset);

        // Extract values
        var values = new List<byte[]>(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            int s = oldOffsets[i];
            int e = i + 1 < oldOffsets.Length ? oldOffsets[i + 1] : valuesBlob.Length;
            if (s < 0 || e < s || e > valuesBlob.Length)
            {
                return workingData;
            }
            values.Add(valuesBlob.Slice(s, e - s).ToArray());
        }

        // Modify keys: SessionPrivacy -> 0, SessionPasswd -> empty string (0 length)
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i].Name.Equals("SessionPrivacy", StringComparison.OrdinalIgnoreCase))
            {
                values[i] = new byte[4];
            }
            else if (keys[i].Name.Equals("SessionPasswd", StringComparison.OrdinalIgnoreCase))
            {
                values[i] = new byte[4];
            }
        }

        // Rebuild values blob and offsets
        var newOffsets = new int[keys.Count];
        using var newValuesStream = new MemoryStream();
        for (int i = 0; i < keys.Count; i++)
        {
            newOffsets[i] = (int)newValuesStream.Position;
            newValuesStream.Write(values[i]);
        }
        var newValuesBytes = newValuesStream.ToArray();

        // Rebuild CINF body
        using var newCinfStream = new MemoryStream();
        Span<byte> intBuffer = stackalloc byte[4];

        BinaryPrimitives.WriteInt32LittleEndian(intBuffer, keys.Count);
        newCinfStream.Write(intBuffer);

        foreach (var key in keys)
        {
            BinaryPrimitives.WriteInt32LittleEndian(intBuffer, key.Length);
            newCinfStream.Write(intBuffer);
            newCinfStream.Write(key.RawBytes);
        }

        BinaryPrimitives.WriteInt32LittleEndian(intBuffer, newOffsets.Length);
        newCinfStream.Write(intBuffer);

        foreach (var off in newOffsets)
        {
            BinaryPrimitives.WriteInt32LittleEndian(intBuffer, off);
            newCinfStream.Write(intBuffer);
        }

        BinaryPrimitives.WriteInt32LittleEndian(intBuffer, newValuesBytes.Length);
        newCinfStream.Write(intBuffer);
        newCinfStream.Write(newValuesBytes);

        var newCinfBytes = newCinfStream.ToArray();

        // Rebuild INFO body
        var preCinf = infoSpan[..cinfOffsetInInfo];
        using var newInfoBodyStream = new MemoryStream();
        newInfoBodyStream.Write(preCinf);
        newInfoBodyStream.Write("CINF"u8);
        BinaryPrimitives.WriteInt32LittleEndian(intBuffer, newCinfBytes.Length);
        newInfoBodyStream.Write(intBuffer);
        newInfoBodyStream.Write(newCinfBytes);

        var newInfoBodyBytes = newInfoBodyStream.ToArray();

        // Rebuild entire file: "SAVE" + payloadLen + "INFO" + infoBodyLen + infoBody + rest
        int restOffset = 16 + infoLength;
        var restOfFile = workingData.AsSpan(restOffset);

        using var newFileStream = new MemoryStream();
        newFileStream.Write("SAVE"u8);

        int newPayloadLength = 8 + newInfoBodyBytes.Length + restOfFile.Length;
        BinaryPrimitives.WriteInt32LittleEndian(intBuffer, newPayloadLength);
        newFileStream.Write(intBuffer);

        newFileStream.Write("INFO"u8);
        BinaryPrimitives.WriteInt32LittleEndian(intBuffer, newInfoBodyBytes.Length);
        newFileStream.Write(intBuffer);
        newFileStream.Write(newInfoBodyBytes);

        newFileStream.Write(restOfFile);

        return newFileStream.ToArray();
    }

    private static void TryReadCinfSessionInfo(
        ReadOnlySpan<byte> infoSpan,
        out bool isDedicatedServer,
        out bool hasPassword,
        out int sessionPrivacy)
    {
        isDedicatedServer = false;
        hasPassword = false;
        sessionPrivacy = 0;

        try
        {
            int cinfOffset = infoSpan.IndexOf("CINF"u8);
            if (cinfOffset < 0 || cinfOffset + 8 > infoSpan.Length) return;

            int cinfLength = BinaryPrimitives.ReadInt32LittleEndian(infoSpan.Slice(cinfOffset + 4, 4));
            if (cinfLength <= 0 || cinfOffset + 8 + cinfLength > infoSpan.Length) return;

            var cinfBody = infoSpan.Slice(cinfOffset + 8, cinfLength);
            if (cinfBody.Length < 4) return;

            int numKeys = BinaryPrimitives.ReadInt32LittleEndian(cinfBody[..4]);
            int offset = 4;
            var keyNames = new List<string>(numKeys);
            for (int i = 0; i < numKeys; i++)
            {
                if (offset + 4 > cinfBody.Length) return;
                int klen = BinaryPrimitives.ReadInt32LittleEndian(cinfBody.Slice(offset, 4));
                offset += 4;
                if (offset + klen > cinfBody.Length) return;
                var kname = Encoding.Latin1.GetString(cinfBody.Slice(offset, klen)).TrimEnd('\0');
                keyNames.Add(kname);
                offset += klen;
            }

            if (offset + 4 > cinfBody.Length) return;
            int numOffsets = BinaryPrimitives.ReadInt32LittleEndian(cinfBody.Slice(offset, 4));
            offset += 4;

            if (offset + numOffsets * 4 + 4 > cinfBody.Length) return;
            var offsets = new int[numOffsets];
            for (int i = 0; i < numOffsets; i++)
            {
                offsets[i] = BinaryPrimitives.ReadInt32LittleEndian(cinfBody.Slice(offset, 4));
                offset += 4;
            }

            int valuesBlobLength = BinaryPrimitives.ReadInt32LittleEndian(cinfBody.Slice(offset, 4));
            offset += 4;
            var valuesBlob = cinfBody.Slice(offset);

            for (int i = 0; i < keyNames.Count && i < offsets.Length; i++)
            {
                int s = offsets[i];
                int e = i + 1 < offsets.Length ? offsets[i + 1] : valuesBlob.Length;
                if (s < 0 || e < s || e > valuesBlob.Length) continue;
                var val = valuesBlob.Slice(s, e - s);

                if (keyNames[i].Equals("SessionPrivacy", StringComparison.OrdinalIgnoreCase) && val.Length >= 4)
                {
                    sessionPrivacy = BinaryPrimitives.ReadInt32LittleEndian(val[..4]);
                    if (sessionPrivacy != 0)
                    {
                        isDedicatedServer = true;
                    }
                }
                else if (keyNames[i].Equals("SessionPasswd", StringComparison.OrdinalIgnoreCase) && val.Length >= 4)
                {
                    int passLen = BinaryPrimitives.ReadInt32LittleEndian(val[..4]);
                    if (passLen > 1)
                    {
                        hasPassword = true;
                    }
                }
            }
        }
        catch
        {
            // Best effort detection
        }
    }
}

