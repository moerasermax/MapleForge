## Task: Port Change Channel — CHANGE_CHANNEL (0x1F)

MapleForge is a MapleStory v113 private server in C#/.NET 10. Solution at `MapleForge/MapleForge.slnx`. Working directory is `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\MapleForge`.

### Java Reference
- Opcode: `CHANGE_CHANNEL = 0x1F` (recv.properties)
- Packet: `readByte()` — target channel (0-based)
- Send opcode: `CHANGE_CHANNEL = 0x08` — `writeShort(0x08) + writeByte(1) + write(ip[4]) + writeShort(port)`

### Architecture: Single-Process MVP
MapleForge runs as a SINGLE process (login+channel combined). There are no separate channel servers. The MVP approach:
1. Save player character to DB
2. Send CHANGE_CHANNEL packet with SAME server IP/port — client disconnects and reconnects
3. The existing finally block in V113ChannelConnectionHandler handles all cleanup (deregister map/registry/trade)
4. On reconnect, normal PLAYER_LOGGEDIN flow loads from DB

### What To Build

**1. Channel Change Packets** — create `src/Maple.Adapters.V113/Channel/V113ChannelChangePackets.cs`:
```csharp
internal static class V113ChannelChangePackets
{
    public static byte ParseChangeChannel(PacketReader reader) => reader.ReadByte();
    
    public static byte[] ChangeChannel(byte[] ip, short port)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.ChangeChannel); // 0x08
        w.WriteByte(1); // success flag
        w.WriteBytes(ip); // 4 bytes
        w.WriteShort(port);
        return w.ToArray();
    }
}
```

**2. Wire into handler** — add case in `V113ChannelConnectionHandler.cs`:
```csharp
case V113ChannelRecvOp.ChangeChannel:
    if (player is null) break;
    reader.ReadByte(); // target channel (ignored in single-process)
    player.FlushInventory();
    await _charService.UpdateAsync(player.Character, token);
    await s.SendAsync(V113ChannelChangePackets.ChangeChannel(
        new byte[] { 127, 0, 0, 1 }, /* port: find actual channel port */), token);
    break;
```
IMPORTANT: Search the codebase for the actual channel port. Look in `MapleServerHost.cs` or config files for `7575` or port bindings. The login server is 8484.

**3. Opcodes:**
- Add to V113ChannelRecvOp: `ChangeChannel = 0x1F`
- Add to V113ChannelSendOp: `ChangeChannel = 0x08`

**4. Tests** — create `tests/Maple.Adapters.V113.Tests/ChannelChangeChannelTests.cs`:
- ChangeChannel packet has correct opcode (0x08), flag, IP bytes, port
- Opcode constants match Java values
- Packet round-trip test

### Key Points
- This is the SIMPLEST system. Keep it minimal.
- No separate service class needed — inline in handler
- Don't duplicate cleanup logic — the finally block handles it
- No buff transfer for MVP — player reconnects clean
- Search for the actual channel port, don't hardcode 7575 without checking

### Testing commands
```
dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo
```
Current: Adapters 250+1skip.
