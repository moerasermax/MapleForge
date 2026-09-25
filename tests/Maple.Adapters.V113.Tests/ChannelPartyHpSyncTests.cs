using Maple.Adapters.V113.Channel;
using Maple.Application.Maps;
using Maple.Application.Parties;
using Maple.Core.Characters;
using Maple.Core.Parties;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>P095：<see cref="V113PartyHpSync"/> 對照 Java <c>updatePartyMemberHP</c>（同地圖、同頻道隊友）。</summary>
public sealed class ChannelPartyHpSyncTests
{
    [Fact]
    public async Task BroadcastAsync_SendsOnlyToPartyMembersOnSameMap()
    {
        var parties = new InMemoryPartyRegistry();
        var registry = new InMemoryMapSessionRegistry();
        var hurt = NewPlayer(1, mapId: 100000000, hp: 40);
        var mateHere = NewPlayer(2, mapId: 100000000, hp: 100);
        var mateElsewhere = NewPlayer(3, mapId: 101000000, hp: 100);
        var stranger = NewPlayer(4, mapId: 100000000, hp: 100);
        var created = parties.CreateParty(PartyMember.FromCharacter(hurt.Character, channelIndex: 1));
        parties.JoinParty(created.Party!.Id, PartyMember.FromCharacter(mateHere.Character, channelIndex: 1));
        parties.JoinParty(created.Party!.Id, PartyMember.FromCharacter(mateElsewhere.Character, channelIndex: 1));
        var received = new List<(int CharId, byte[] Packet)>();
        foreach (var p in new[] { hurt, mateHere, mateElsewhere, stranger })
        {
            var id = p.Character.Id;
            registry.Register(p.Character.MapId, id, p, (pkt, _) => { received.Add((id, pkt)); return Task.CompletedTask; }, new object());
        }

        await new V113PartyHpSync(parties, registry).BroadcastAsync(hurt, CancellationToken.None);

        var (charId, packet) = Assert.Single(received);
        Assert.Equal(2, charId);
        Assert.Equal(V113PartyPackets.UpdatePartyMemberHp(1, 40, 100), packet);
    }

    [Fact]
    public async Task BroadcastAsync_NoParty_SendsNothing()
    {
        var registry = new InMemoryMapSessionRegistry();
        var solo = NewPlayer(1, mapId: 100000000, hp: 40);
        var other = NewPlayer(2, mapId: 100000000, hp: 100);
        var received = new List<byte[]>();
        registry.Register(100000000, 2, other, (pkt, _) => { received.Add(pkt); return Task.CompletedTask; }, new object());

        await new V113PartyHpSync(new InMemoryPartyRegistry(), registry).BroadcastAsync(solo, CancellationToken.None);

        Assert.Empty(received);
    }

    [Theory]
    [InlineData(40, false)] // HP 沒變（例如只回 MP 的藥水）
    [InlineData(10, true)]  // HP 有變
    public async Task BroadcastIfChangedAsync_OnlySendsWhenHpChanged(short hpBefore, bool expectSent)
    {
        // P099：道具/技能路徑只有 HP 真的變動才同步（Java setHp 才會 updatePartyMemberHP）。
        var parties = new InMemoryPartyRegistry();
        var registry = new InMemoryMapSessionRegistry();
        var drinker = NewPlayer(1, mapId: 100000000, hp: 40);
        var mate = NewPlayer(2, mapId: 100000000, hp: 100);
        var created = parties.CreateParty(PartyMember.FromCharacter(drinker.Character, channelIndex: 1));
        parties.JoinParty(created.Party!.Id, PartyMember.FromCharacter(mate.Character, channelIndex: 1));
        var received = new List<byte[]>();
        registry.Register(100000000, 2, mate, (pkt, _) => { received.Add(pkt); return Task.CompletedTask; }, new object());

        await new V113PartyHpSync(parties, registry).BroadcastIfChangedAsync(drinker, hpBefore, CancellationToken.None);

        Assert.Equal(expectSent ? 1 : 0, received.Count);
    }

    private static Player NewPlayer(int id, int mapId, short hp)
        => new(new Character { Id = id, Name = $"P{id}", MapId = mapId, Stats = new CharacterStats { Hp = hp, MaxHp = 100 } }, new Position(0, 0, 0, 0));
}
