using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace Maple.Content.Wz;

public enum WzObjectType
{
    Directory,
    Image,
    Property
}

public abstract class WzObject
{
    protected WzObject(string name, WzObjectType objectType)
    {
        Name = name;
        ObjectType = objectType;
    }

    public string Name { get; }

    public WzObjectType ObjectType { get; }
}

public sealed class WzDirectory : WzObject
{
    private readonly Dictionary<string, WzObject> _children = new(StringComparer.Ordinal);

    internal WzDirectory(string name)
        : base(name, WzObjectType.Directory)
    {
    }

    public IReadOnlyDictionary<string, WzObject> Children => new ReadOnlyDictionary<string, WzObject>(_children);

    internal void AddChild(WzObject child)
    {
        _children[child.Name] = child;
    }
}

public sealed class WzImage : WzObject
{
    private readonly Lazy<IReadOnlyDictionary<string, WzProperty>> _properties;

    internal WzImage(string name, int size, int checksum, int offset, Func<int, IReadOnlyDictionary<string, WzProperty>> propertyLoader)
        : base(name, WzObjectType.Image)
    {
        Size = size;
        Checksum = checksum;
        Offset = offset;
        _properties = new Lazy<IReadOnlyDictionary<string, WzProperty>>(() => propertyLoader(Offset), isThreadSafe: true);
    }

    public int Size { get; }

    public int Checksum { get; }

    public int Offset { get; }

    public IReadOnlyDictionary<string, WzProperty> Properties => _properties.Value;
}

public sealed class WzProperty : WzObject
{
    internal WzProperty(string name, object? value)
        : base(name, WzObjectType.Property)
    {
        Value = value;
    }

    public object? Value { get; }
}

public sealed record WzVector(int X, int Y);

public sealed record WzCanvasRef(int DataOffset, int DataLength);

public sealed record WzSoundRef(int DataOffset, int DataLength);

public sealed class WzFile : IDisposable
{
    private const int FixedHeaderSize = 0x3C;
    private const int VersionFieldSize = sizeof(ushort);
    private const uint WzOffsetConstant = 0x581C3F6D;
    private static readonly byte[] DefaultWzIv = { 0x4D, 0x23, 0xC7, 0x2B };
    private static readonly byte[] DefaultWzAesKey =
    {
        0x13, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x00, 0x00, 0xB4, 0x00, 0x00, 0x00,
        0x1B, 0x00, 0x00, 0x00, 0x0F, 0x00, 0x00, 0x00,
        0x33, 0x00, 0x00, 0x00, 0x52, 0x00, 0x00, 0x00,
    };
    private const int ZlzIvOffset = 0x10040;
    private const int ZlzAesKeyOffset = 0x10060;

    private readonly FileStream _stream;
    private readonly BinaryReader _reader;
    private readonly object _sync = new();
    private readonly uint _versionHash;
    private readonly byte[] _wzIv;
    private readonly bool _zeroWzKey;
    private readonly Aes _wzAes;
    private readonly ICryptoTransform _wzKeyTransform;
    private readonly List<byte> _wzKey = [];
    private byte[]? _lastWzKeyBlock;
    private int _currentStringBaseOffset = FixedHeaderSize;

    private WzFile(string path, FileStream stream, BinaryReader reader, int version, bool isPackage)
    {
        Path = path;
        _stream = stream;
        _reader = reader;
        Version = version;
        _versionHash = GetVersionHash(version);
        (_wzIv, var wzAesKey) = ResolveWzCrypto(path);
        _zeroWzKey = BitConverter.ToInt32(_wzIv, 0) == 0;
        (_wzAes, _wzKeyTransform) = CreateWzKeyTransform(wzAesKey);
        Root = isPackage ? ParseRootDirectory() : new WzDirectory(System.IO.Path.GetFileNameWithoutExtension(path));
    }

    public string Path { get; }

    public int Version { get; }

    public WzDirectory Root { get; }

    public static WzFile Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        try
        {
            var isPackage = ValidateHeader(reader, stream, out var detectedVersion);
            stream.Position = 0;
            return new WzFile(path, stream, reader, detectedVersion, isPackage);
        }
        catch
        {
            reader.Dispose();
            stream.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _wzKeyTransform.Dispose();
        _wzAes.Dispose();
        _reader.Dispose();
        _stream.Dispose();
    }

    private static bool ValidateHeader(BinaryReader reader, FileStream stream, out int detectedVersion)
    {
        detectedVersion = 113;
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (!string.Equals(magic, "PKG1", StringComparison.Ordinal))
        {
            return false;
        }

        var fileSize = reader.ReadInt64();
        var headerSize = reader.ReadInt32();
        if (headerSize != FixedHeaderSize)
        {
            throw new InvalidDataException($"Unsupported header size: 0x{headerSize:X}.");
        }

        var fileSizeWithHeader = fileSize + headerSize;
        var fileSizeWithVersion = fileSize + headerSize + VersionFieldSize;
        if (fileSize != stream.Length && fileSizeWithHeader != stream.Length && fileSizeWithVersion != stream.Length)
        {
            throw new InvalidDataException($"Header file size mismatch: header={fileSize}, header+size={fileSizeWithHeader}, header+size+ver={fileSizeWithVersion}, actual={stream.Length}.");
        }

        stream.Position = headerSize;
        var encryptedVersion = reader.ReadUInt16();
        var expected = EncryptVersion(113);
        if (encryptedVersion == expected)
        {
            detectedVersion = 113;
            return true;
        }

        for (var version = 0; version <= 999; version++)
        {
            if (version == 113)
            {
                continue;
            }

            if (EncryptVersion(version) == encryptedVersion)
            {
                detectedVersion = version;
                return true;
            }
        }

        throw new InvalidDataException($"Unexpected encrypted version: 0x{encryptedVersion:X4}; no matching version found in [0..999].");

    }

    private WzDirectory ParseRootDirectory()
    {
        lock (_sync)
        {
            _stream.Position = FixedHeaderSize;
            _stream.Position += VersionFieldSize;
            var root = new WzDirectory(System.IO.Path.GetFileNameWithoutExtension(Path));
            ReadDirectoryEntries(root);
            return root;
        }
    }

    private void ReadDirectoryEntries(WzDirectory directory)
    {
        var count = ReadCompressedInt(_reader);
        for (var i = 0; i < count; i++)
        {
            var type = _reader.ReadByte();
            string name;

            switch (type)
            {
                case 1:
                    _reader.ReadInt32();
                    _reader.ReadInt16();
                    _ = ReadWzOffset();
                    continue;
                case 2:
                    var stringOffset = _reader.ReadInt32();
                    var returnPosition = _stream.Position;
                    _stream.Position = FixedHeaderSize + stringOffset;
                    type = _reader.ReadByte();
                    name = ReadWzString();
                    _stream.Position = returnPosition;
                    break;
                case 3:
                case 4:
                    name = ReadWzString();
                    break;
                default:
                    throw new InvalidDataException($"Unknown directory entry type: {type}.");
            }

            var size = ReadCompressedInt(_reader);
            var checksum = ReadCompressedInt(_reader);
            var offset = (int)ReadWzOffset();

            if (type == 4)
            {
                var image = new WzImage(name, size, checksum, offset, LoadImageProperties);
                directory.AddChild(image);
            }
            else if (type == 3)
            {
                var child = new WzDirectory(name);
                directory.AddChild(child);
            }
            else
            {
                throw new InvalidDataException($"Unknown resolved directory entry type: {type}.");
            }
        }
    }

    private IReadOnlyDictionary<string, WzProperty> LoadImageProperties(int imageOffset)
    {
        lock (_sync)
        {
            _stream.Position = imageOffset;
            var header = _reader.ReadByte();
            if (header != 0x73)
            {
                throw new InvalidDataException($"Unsupported image header byte: 0x{header:X2}.");
            }

            var imageType = ReadWzString();
            var reserved = _reader.ReadUInt16();
            if (!string.Equals(imageType, "Property", StringComparison.Ordinal) || reserved != 0)
            {
                throw new InvalidDataException($"Unexpected image preamble: type={imageType}, reserved={reserved}.");
            }

            var previousBaseOffset = _currentStringBaseOffset;
            _currentStringBaseOffset = imageOffset;
            try
            {
                return ReadPropertyList();
            }
            finally
            {
                _currentStringBaseOffset = previousBaseOffset;
            }
        }
    }

    private Dictionary<string, WzProperty> ReadPropertyList()
    {
        var count = ReadCompressedInt(_reader);
        var properties = new Dictionary<string, WzProperty>(count, StringComparer.Ordinal);

        for (var i = 0; i < count; i++)
        {
            var name = ReadWzStringBlock();
            var type = _reader.ReadByte();
            var value = ReadPropertyValue(type);
            properties[name] = new WzProperty(name, value);
        }

        return properties;
    }

    private object? ReadPropertyValue(byte type)
    {
        return type switch
        {
            0x00 => null,
            0x0B => _reader.ReadInt16(),
            0x02 => _reader.ReadInt16(),
            0x13 => ReadCompressedInt(_reader),
            0x03 => ReadCompressedInt(_reader),
            0x14 => ReadCompressedLong(_reader),
            0x04 => ReadCompressedFloat(),
            0x05 => _reader.ReadDouble(),
            0x08 => ReadWzStringBlock(),
            0x09 => ReadExtendedProperty(),
            _ => throw new InvalidDataException($"Unsupported property type: 0x{type:X2}.")
        };
    }

    private object? ReadExtendedProperty()
    {
        var endOfBlock = _reader.ReadUInt32() + _stream.Position;
        var extType = _reader.ReadByte();

        string iname = extType switch
        {
            0x00 or 0x73 => string.Empty,
            0x01 or 0x1B => ReadWzStringAtOffset(_currentStringBaseOffset + _reader.ReadInt32()),
            _ => throw new InvalidDataException($"Unsupported extended property marker: 0x{extType:X2}.")
        };

        var value = ReadExtendedPropertyCore(iname);
        if (_stream.Position != endOfBlock)
        {
            _stream.Position = endOfBlock;
        }

        return value;
    }

    private object? ReadExtendedPropertyCore(string iname)
    {
        if (string.IsNullOrEmpty(iname))
        {
            iname = ReadWzString();
        }

        return iname switch
        {
            "Property" => ReadSubProperty(),
            "Shape2D#Vector2D" => new WzVector(ReadCompressedInt(_reader), ReadCompressedInt(_reader)),
            "Shape2D#Convex2D" => ReadConvexProperty(),
            "Canvas" => ReadCanvasReference(),
            "Sound_DX8" => ReadSoundReference(),
            "UOL" => ReadUolReference(),
            _ => throw new InvalidDataException($"Unsupported extended property type: {iname}.")
        };
    }

    private Dictionary<string, WzProperty> ReadSubProperty()
    {
        _reader.ReadUInt16();
        return ReadPropertyList();
    }

    private List<object?> ReadConvexProperty()
    {
        var count = ReadCompressedInt(_reader);
        var values = new List<object?>(count);
        for (var i = 0; i < count; i++)
        {
            values.Add(ReadExtendedProperty());
        }

        return values;
    }

    private object ReadCanvasReference()
    {
        _reader.ReadByte();
        var hasProperties = _reader.ReadByte();
        if (hasProperties == 1)
        {
            _reader.ReadUInt16();
            _ = ReadPropertyList();
        }

        var width = ReadCompressedInt(_reader);
        var height = ReadCompressedInt(_reader);
        var format1 = ReadCompressedInt(_reader);
        _reader.ReadByte();
        _reader.ReadInt32();

        var encodedLength = _reader.ReadInt32();
        var dataLength = encodedLength - 1;
        _reader.ReadByte();
        var dataOffset = (int)_stream.Position;
        if (dataLength > 0)
        {
            _stream.Position += dataLength;
        }

        return new
        {
            Width = width,
            Height = height,
            Format = format1,
            Data = new WzCanvasRef(dataOffset, dataLength > 0 ? dataLength : 0)
        };
    }

    private WzSoundRef ReadSoundReference()
    {
        _reader.ReadByte();
        var dataLength = ReadCompressedInt(_reader);
        _ = ReadCompressedInt(_reader);
        const int soundHeaderLength = 51;
        var headerStart = _stream.Position;
        _stream.Position = headerStart + soundHeaderLength;
        var waveFormatLength = _reader.ReadByte();
        _stream.Position = headerStart + soundHeaderLength + 1 + waveFormatLength;
        var dataOffset = (int)_stream.Position;
        if (dataLength > 0)
        {
            _stream.Position += dataLength;
        }

        return new WzSoundRef(dataOffset, dataLength > 0 ? dataLength : 0);
    }

    private string ReadUolReference()
    {
        _reader.ReadByte();
        var kind = _reader.ReadByte();
        return kind switch
        {
            0 => ReadWzString(),
            1 => ReadWzStringAtOffset(_currentStringBaseOffset + _reader.ReadInt32()),
            _ => throw new InvalidDataException($"Unsupported UOL type: 0x{kind:X2}.")
        };
    }

    private string ReadWzStringBlock()
    {
        var marker = _reader.ReadByte();
        return marker switch
        {
            0 or 0x73 => ReadWzString(),
            1 or 0x1B => ReadWzStringAtOffset(_currentStringBaseOffset + _reader.ReadInt32()),
            _ => throw new InvalidDataException($"Unsupported string block marker: 0x{marker:X2}.")
        };
    }

    private string ReadWzStringAtOffset(int offset)
    {
        var currentPosition = _stream.Position;
        _stream.Position = offset;
        var result = ReadWzString();
        _stream.Position = currentPosition;
        return result;
    }

    private string ReadWzString()
    {
        var marker = _reader.ReadSByte();
        return marker switch
        {
            0 => string.Empty,
            127 => DecodeAsciiString(_reader.ReadInt32()),
            -128 => DecodeUnicodeString(_reader.ReadInt32()),
            > 0 => DecodeUnicodeString(marker),
            < 0 and > -128 => DecodeAsciiString(-marker)
        };
    }

    private string DecodeAsciiString(int length)
    {
        var data = _reader.ReadBytes(length);
        var mask = 0xAA;
        for (var i = 0; i < data.Length; i++)
        {
            data[i] ^= (byte)mask;
            data[i] ^= GetWzKeyByte(i);
            mask = (mask + 1) & 0xFF;
        }

        return Encoding.ASCII.GetString(data);
    }

    private string DecodeUnicodeString(int length)
    {
        var chars = new char[length];
        EnsureWzKeySize(length * 2);
        for (var i = 0; i < length; i++)
        {
            var encryptedChar = _reader.ReadUInt16();
            encryptedChar ^= (ushort)(0xAAAA + i);
            encryptedChar ^= (ushort)((GetWzKeyByte((i * 2) + 1) << 8) + GetWzKeyByte(i * 2));
            chars[i] = (char)encryptedChar;
        }

        return new string(chars);
    }

    private static int ReadCompressedInt(BinaryReader reader)
    {
        var value = reader.ReadSByte();
        return value == -128 ? reader.ReadInt32() : value;
    }

    private static long ReadCompressedLong(BinaryReader reader)
    {
        var value = reader.ReadSByte();
        return value == -128 ? reader.ReadInt64() : value;
    }

    private float ReadCompressedFloat()
    {
        var marker = _reader.ReadByte();
        return marker switch
        {
            0 => 0f,
            0x80 => _reader.ReadSingle(),
            _ => throw new InvalidDataException($"Unsupported compressed float marker: 0x{marker:X2}.")
        };
    }

    private uint ReadWzOffset()
    {
        var offset = (uint)_stream.Position;
        offset = (offset - FixedHeaderSize) ^ 0xFFFFFFFF;
        offset = unchecked(offset * _versionHash);
        offset = unchecked(offset - WzOffsetConstant);
        offset = RotateLeft(offset, (int)(offset & 0x1F));
        var encryptedOffset = _reader.ReadUInt32();
        offset ^= encryptedOffset;
        offset = unchecked(offset + (uint)(FixedHeaderSize * 2));
        return offset;
    }

    private static uint RotateLeft(uint value, int shift)
    {
        shift &= 31;
        if (shift == 0)
        {
            return value;
        }

        return (value << shift) | (value >> (32 - shift));
    }

    private static (byte[] Iv, byte[] AesKey) ResolveWzCrypto(string wzPath)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(wzPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                var zlzPath = System.IO.Path.Combine(directory, "ZLZ.dll");
                if (File.Exists(zlzPath))
                {
                    using var zlzStream = new FileStream(zlzPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var zlzReader = new BinaryReader(zlzStream, Encoding.ASCII, leaveOpen: false);

                    if (zlzStream.Length >= ZlzAesKeyOffset + 4)
                    {
                        zlzStream.Position = ZlzIvOffset;
                        var iv = zlzReader.ReadBytes(4);
                        if (iv.Length == 4)
                        {
                            zlzStream.Position = ZlzAesKeyOffset;
                            var key = new byte[32];
                            for (var i = 0; i < 8; i++)
                            {
                                var chunk = zlzReader.ReadBytes(4);
                                if (chunk.Length != 4)
                                {
                                    break;
                                }

                                Buffer.BlockCopy(chunk, 0, key, i * 4, chunk.Length);
                                zlzStream.Position += 12;
                            }

                            return (iv, key);
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore and use fallback crypto constants.
        }

        return (DefaultWzIv, DefaultWzAesKey);
    }

    private static (Aes Aes, ICryptoTransform Transform) CreateWzKeyTransform(byte[] aesKey)
    {
        var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Key = aesKey;
        return (aes, aes.CreateEncryptor());
    }

    private void EnsureWzKeySize(int requiredSize)
    {
        if (requiredSize <= _wzKey.Count)
        {
            return;
        }

        if (_zeroWzKey)
        {
            var additional = requiredSize - _wzKey.Count;
            for (var i = 0; i < additional; i++)
            {
                _wzKey.Add(0);
            }

            return;
        }

        while (_wzKey.Count < requiredSize)
        {
            var input = new byte[16];
            if (_lastWzKeyBlock is null)
            {
                for (var i = 0; i < input.Length; i++)
                {
                    input[i] = _wzIv[i % _wzIv.Length];
                }
            }
            else
            {
                Buffer.BlockCopy(_lastWzKeyBlock, 0, input, 0, input.Length);
            }

            var output = new byte[16];
            _ = _wzKeyTransform.TransformBlock(input, 0, input.Length, output, 0);
            _lastWzKeyBlock = output;
            _wzKey.AddRange(output);
        }
    }

    private byte GetWzKeyByte(int index)
    {
        EnsureWzKeySize(index + 1);
        return _wzKey[index];
    }

    private static uint GetVersionHash(int version)
    {
        uint hash = 0;
        var text = version.ToString();
        foreach (var c in text)
        {
            hash = (hash * 32) + c + 1;
        }

        return hash;
    }

    private static ushort EncryptVersion(int version)
    {
        var hash = GetVersionHash(version);
        return (ushort)(0xFF ^ ((hash >> 24) & 0xFF) ^ ((hash >> 16) & 0xFF) ^ ((hash >> 8) & 0xFF) ^ (hash & 0xFF));
    }
}
