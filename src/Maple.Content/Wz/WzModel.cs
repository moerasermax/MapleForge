using System.Collections.ObjectModel;
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

    internal WzImage(string name, int size, int checksum, int offset, Func<IReadOnlyDictionary<string, WzProperty>> propertyLoader)
        : base(name, WzObjectType.Image)
    {
        Size = size;
        Checksum = checksum;
        Offset = offset;
        _properties = new Lazy<IReadOnlyDictionary<string, WzProperty>>(propertyLoader, isThreadSafe: true);
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
    private readonly FileStream _stream;
    private readonly BinaryReader _reader;
    private readonly object _sync = new();

    private WzFile(string path, FileStream stream, BinaryReader reader, int version, bool isPackage)
    {
        Path = path;
        _stream = stream;
        _reader = reader;
        Version = version;
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
            switch (type)
            {
                case 1:
                    _reader.ReadInt32();
                    _reader.ReadInt32();
                    break;
                case 2:
                    _reader.ReadInt32();
                    break;
                case 3:
                case 4:
                    var name = ReadWzStringBlock();
                    var size = ReadCompressedInt(_reader);
                    var checksum = ReadCompressedInt(_reader);
                    var offset = _reader.ReadInt32();

                    if (type == 3)
                    {
                        var image = new WzImage(name, size, checksum, offset, () => LoadImageProperties(offset));
                        directory.AddChild(image);
                    }
                    else
                    {
                        var child = new WzDirectory(name);
                        directory.AddChild(child);
                    }

                    break;
                default:
                    throw new InvalidDataException($"Unknown directory entry type: {type}.");
            }
        }
    }

    private IReadOnlyDictionary<string, WzProperty> LoadImageProperties(int imageOffset)
    {
        lock (_sync)
        {
            _stream.Position = imageOffset;
            _ = ReadWzStringBlock();
            _reader.ReadUInt16();
            _reader.ReadUInt16();
            _reader.ReadUInt16();
            _reader.ReadUInt16();
            return ReadPropertyList();
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
            0x02 => _reader.ReadInt16(),
            0x03 => ReadCompressedInt(_reader),
            0x14 => ReadCompressedLong(_reader),
            0x04 => ReadCompressedFloat(_reader),
            0x05 => _reader.ReadDouble(),
            0x08 => ReadWzStringBlock(),
            0x09 => ReadSubProperty(),
            0x0F => ReadCanvasReference(),
            0x10 => new WzVector(ReadCompressedInt(_reader), ReadCompressedInt(_reader)),
            0x11 => ReadSoundReference(),
            0x1A => ReadWzStringBlock(),
            _ => throw new InvalidDataException($"Unsupported property type: 0x{type:X2}.")
        };
    }

    private Dictionary<string, WzProperty> ReadSubProperty()
    {
        _reader.ReadInt32();
        return ReadPropertyList();
    }

    private WzCanvasRef ReadCanvasReference()
    {
        var marker = _reader.ReadByte();
        if (marker == 1)
        {
            _ = ReadPropertyList();
        }

        _reader.ReadInt32();
        _reader.ReadInt32();
        _reader.ReadInt32();
        _reader.ReadInt32();
        _reader.ReadInt32();
        var dataLength = _reader.ReadInt32();
        var dataOffset = (int)_stream.Position;
        _stream.Position += dataLength;
        return new WzCanvasRef(dataOffset, dataLength);
    }

    private WzSoundRef ReadSoundReference()
    {
        var dataLength = _reader.ReadInt32();
        _reader.ReadInt32();
        var dataOffset = (int)_stream.Position;
        _stream.Position += dataLength;
        return new WzSoundRef(dataOffset, dataLength);
    }

    private string ReadWzStringBlock()
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
            mask = (mask + 1) & 0xFF;
        }

        return Encoding.ASCII.GetString(data);
    }

    private string DecodeUnicodeString(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)(_reader.ReadUInt16() ^ 0xAAAA);
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

    private static float ReadCompressedFloat(BinaryReader reader)
    {
        var value = reader.ReadSByte();
        return value == -128 ? reader.ReadSingle() : value / 1024f;
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
