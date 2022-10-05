public static FileScheme ReadScheme(SmartStream stream, string filePath, string fileName)
{
    if (BundleFile.IsBundleFile(stream))
    {
        return BundleFile.ReadScheme(stream, filePath, fileName);
    }
    if (ArchiveFile.IsArchiveFile(stream))
    {
        return ArchiveFile.ReadScheme(stream, filePath, fileName);
    }
    if (WebFile.IsWebFile(stream))
    {
        return WebFile.ReadScheme(stream, filePath);
    }
    if (SerializedFile.IsSerializedFile(stream))
    {
        return SerializedFile.ReadScheme(stream, filePath, fileName);
    }
    return ResourceFile.ReadScheme(stream, filePath, fileName);
}

private void ReadHeader(Stream stream)
{
    long headerPosition = stream.Position;
    using(EndianReader reader = new EndianReader(stream, EndianType.BigEndian))
    {
        Header.Read(reader);
        if (Header.Signature.IsRawWeb())
        {
            if (stream.Position - headerPosition != Header.RawWeb.HeaderSize)
            {
                throw new Exception($"Read {stream.Position - headerPosition} but expected {Header.RawWeb.HeaderSize}");
            }
        }
    }
}

public static bool TryParseSignature(string signatureString, out BundleType type)
{
    switch (signatureString)
    {
        case nameof(BundleType.UnityWeb):
            type = BundleType.UnityWeb;
            return true;

        case nameof(BundleType.UnityRaw):
            type = BundleType.UnityRaw;
            return true;

        case nameof(BundleType.UnityFS):
            type = BundleType.UnityFS;
            return true;

        default:
            type = default;
            return false;
    }
}

private void ReadScheme(Stream stream)
{
    long basePosition = stream.Position;
    ReadHeader(stream);

    switch (Header.Signature)
    {
        case BundleType.UnityRaw:
        case BundleType.UnityWeb:
            ReadRawWebMetadata(stream, out Stream dataStream, out long metadataOffset);
            ReadRawWebData(dataStream, metadataOffset);
            break;

        case BundleType.UnityFS:
            long headerSize = stream.Position - basePosition;
            ReadFileStreamMetadata(stream, basePosition);
            ReadFileStreamData(stream, basePosition, headerSize);
            break;

        default:
            throw new Exception($"Unknown bundle signature '{Header.Signature}'");
    }
}

internal static bool IsBundleHeader(EndianReader reader)
{
    const int MaxLength = 0x20;
    if (reader.BaseStream.Length >= MaxLength)
    {
        long position = reader.BaseStream.Position;
        bool isRead = reader.ReadStringZeroTerm(MaxLength, out string signature);
        reader.BaseStream.Position = position;
        if (isRead)
        {
            return TryParseSignature(signature, out BundleType _);
        }
    }
    return false;
}

protected const int BufferSize = 4096;
private readonly byte[] m_buffer = new byte[BufferSize];

public bool ReadStringZeroTerm(int maxLength, out string result)
{
    maxLength = Math.Min(maxLength, m_buffer.Length);
    for (int i = 0; i < maxLength; i++)
    {
        byte bt = ReadByte();
        if (bt == 0)
        {
            // "UnityFS"
            result = Encoding.UTF8.GetString(m_buffer, 0, i);
            return true;
        }
        m_buffer[i] = bt;
    }

    result = null;
    return false;
}

int BufferToInt32(int offset = 0)
{
    return (m_buffer[offset + 3] << 0) | (m_buffer[offset + 2] << 8)
        | (m_buffer[offset + 1] << 16) | (m_buffer[offset + 0] << 24);
}

public void Read(EndianReader reader)
{
    string signature = reader.ReadStringZeroTerm();
    // "UnityFS"
    Signature = ParseSignature(signature);
    // BF_520_x, br.BaseStream.Position = 12
    Version = (BundleVersion) reader.ReadInt32();
    // "5.x.x" Position = 18
    UnityWebBundleVersion = reader.ReadStringZeroTerm();
    // "2018.2.21f1" Position = 30
    string engineVersion = reader.ReadStringZeroTerm();
    // [2018.2.21f1]
    UnityWebMinimumRevision = uTinyRipper.Version.Parse(engineVersion);

    switch (Signature)
    {
        case BundleType.UnityRaw:
        case BundleType.UnityWeb:
            RawWeb = new BundleRawWebHeader();
            RawWeb.Read(reader, Version);
            break;

        case BundleType.UnityFS:
            FileStream = new BundleFileStreamHeader();
            FileStream.Read(reader);
            break;

        default:
            throw new Exception($"Unknown bundle signature '{Signature}'");
    }
}

public sealed class BundleFileStreamHeader
{
    public void Read(EndianReader reader)
    {
        Size = reader.ReadInt64();
        CompressedBlocksInfoSize = reader.ReadInt32();
        UncompressedBlocksInfoSize = reader.ReadInt32();
        Flags = (BundleFlags) reader.ReadInt32();
    }

    /// <summary>
    /// Equal to file size, sometimes equal to uncompressed data size without the header
    /// </summary>
    public long Size { get; set; }
    /// <summary>
    /// UnityFS length of the possibly-compressed (LZMA, LZ4) bundle data header
    /// </summary>
    public int CompressedBlocksInfoSize { get; set; }
    public int UncompressedBlocksInfoSize { get; set; }
    public BundleFlags Flags { get; set; }
}

private void ReadFileStreamMetadata(Stream stream, long basePosition)
{
    BundleFileStreamHeader header = Header.FileStream;
    if (header.Flags.IsBlocksInfoAtTheEnd())
    {
        stream.Position = basePosition + (header.Size - header.CompressedBlocksInfoSize);
    }

    CompressionType metaCompression = header.Flags.GetCompression();
    switch (metaCompression)
    {
        case CompressionType.None:
            {
                ReadMetadata(stream, header.UncompressedBlocksInfoSize);
            }
            break;

        case CompressionType.Lzma:
            {
                using(MemoryStream uncompressedStream = new MemoryStream(new byte[header.UncompressedBlocksInfoSize]))
                {
                    SevenZipHelper.DecompressLZMAStream(stream, header.CompressedBlocksInfoSize, uncompressedStream, header.UncompressedBlocksInfoSize);

                    uncompressedStream.Position = 0;
                    ReadMetadata(uncompressedStream, header.UncompressedBlocksInfoSize);
                }
            }
            break;

        case CompressionType.Lz4:
        case CompressionType.Lz4HC:
            {
                using(MemoryStream uncompressedStream = new MemoryStream(new byte[header.UncompressedBlocksInfoSize]))
                {
                    using(Lz4DecodeStream decodeStream = new Lz4DecodeStream(stream, header.CompressedBlocksInfoSize))
                    {
                        decodeStream.ReadBuffer(uncompressedStream, header.UncompressedBlocksInfoSize);
                    }

                    uncompressedStream.Position = 0;
                    ReadMetadata(uncompressedStream, header.UncompressedBlocksInfoSize);
                }
            }
            break;

        default:
            throw new NotSupportedException($"Bundle compression '{metaCompression}' isn't supported");
    }
}

public enum BundleVersion
{
    .Unknown = 0,
    .
    .BF_100_250 = 1,
    .BF_260_340 = 2,
    .BF_350_4x = 3,
    .BF_520a1 = 4,
    .BF_520aunk = 5,
    .BF_520_x = 6,
    .
}

m_buffer = new byte[4096] { 50, 48, 49, 56, 46, 50, 46, 50, 49, 102, 49 }
