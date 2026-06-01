using System.Text.Json;

namespace Maple.Tools.HeadlessClient;

/// <summary>
/// s2c 封包編目：自動記錄所有不同 opcode 的 count / 首次出現 hex / ASCII。
/// 用途：讓 headless client 在不了解協定的情況下也能快速枚舉 server 送出的 opcode。
/// </summary>
public sealed class PacketCatalog
{
    private sealed class Entry
    {
        public required ushort Opcode    { get; init; }
        public int             Count     { get; set; } = 1;
        public required string FirstHex  { get; init; }
        public required string FirstAscii { get; init; }
    }

    private readonly Dictionary<ushort, Entry> _entries = [];

    /// <summary>記錄一個解密後的 s2c payload（前 2 bytes = opcode LE）。</summary>
    public void Record(byte[] payload)
    {
        if (payload.Length < 2) return;
        ushort opcode = (ushort)(payload[0] | (payload[1] << 8));

        if (!_entries.TryGetValue(opcode, out var e))
            _entries[opcode] = new Entry
            {
                Opcode     = opcode,
                FirstHex   = Convert.ToHexString(payload),
                FirstAscii = ToAscii(payload),
            };
        else
            e.Count++;
    }

    public void PrintSummary(TextWriter writer)
    {
        writer.WriteLine($"\n{'─',40}");
        writer.WriteLine($" s2c Packet Catalog — {_entries.Count} 個不同 opcode");
        writer.WriteLine($"{'─',40}");
        foreach (var e in _entries.Values.OrderBy(e => e.Opcode))
        {
            string hex   = e.FirstHex.Length > 80 ? e.FirstHex[..80] + "…" : e.FirstHex;
            string ascii = e.FirstAscii.Length > 40 ? e.FirstAscii[..40] + "…" : e.FirstAscii;
            writer.WriteLine($" 0x{e.Opcode:X4} ×{e.Count,-4}  {hex}");
            writer.WriteLine($"           asc  {ascii}");
        }
        writer.WriteLine($"{'─',40}");
    }

    public string ToJson()
        => JsonSerializer.Serialize(
            _entries.Values.OrderBy(e => e.Opcode).Select(e => new
            {
                opcode   = $"0x{e.Opcode:X4}",
                count    = e.Count,
                firstHex = e.FirstHex,
                ascii    = e.FirstAscii,
            }),
            new JsonSerializerOptions { WriteIndented = true });

    private static string ToAscii(byte[] data)
        => string.Concat(data.Select(b => b is >= 0x20 and < 0x7F ? (char)b : '.'));
}
