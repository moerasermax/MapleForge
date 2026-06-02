using Maple.Core.Characters;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Application.Tests;

/// <summary>
/// Core/World 執行期領域實體單元測試（Player/FieldInstance/Position）。
/// 純領域、不碰 socket/DB —— 這正是「執行期狀態放 Core 富領域」的可測性勝場。
/// </summary>
public class FieldWorldTests
{
    private static Player MakePlayer(int id, short x, short y)
        => new(new Character { Id = id, Name = "P" + id }, new Position(x, y, 0, 0));

    [Fact]
    public void Player_MoveTo_UpdatesPosition_ObjectIdIsCharId()
    {
        var p = MakePlayer(1, 0, 0);
        Assert.Equal(1, p.ObjectId);
        Assert.Equal(FieldObjectType.Player, p.Type);

        p.MoveTo(new Position(100, 200, 3, 7));

        Assert.Equal((short)100, p.Position.X);
        Assert.Equal((short)200, p.Position.Y);
        Assert.Equal((byte)3, p.Position.Stance);
        Assert.Equal((short)7, p.Position.Foothold);
    }

    [Fact]
    public void FieldInstance_Add_Get_Remove_Players()
    {
        var f = new FieldInstance(100000000);
        var a = MakePlayer(1, 0, 0);
        var b = MakePlayer(2, 50, 0);
        f.Add(a);
        f.Add(b);

        Assert.Equal(2, f.Players.Count());
        Assert.Same(a, f.Get(1));
        Assert.True(f.Remove(2));
        Assert.Null(f.Get(2));
        Assert.Single(f.Players);
    }

    [Fact]
    public void ObjectsInRange_FiltersByDistance()
    {
        var f = new FieldInstance(1);
        f.Add(MakePlayer(1, 0, 0));      // center, dist 0
        f.Add(MakePlayer(2, 30, 40));    // dist 50
        f.Add(MakePlayer(3, 300, 0));    // dist 300

        var inRange = f.ObjectsInRange(new Position(0, 0, 0, 0), 60)
            .Select(o => o.ObjectId).OrderBy(i => i).ToArray();

        Assert.Equal(new[] { 1, 2 }, inRange); // 自身 + (30,40) 在 60 內；(300,0) 不在
    }

    [Fact]
    public void Npc_DerivesPositionFromDefinition_AndIsFieldObject()
    {
        var npc = new Npc(new MapNpc { NpcId = 9000000, X = 100, Cy = 200, Fh = 5 }, objectId: 1000);

        Assert.Equal(1000, npc.ObjectId);
        Assert.Equal(FieldObjectType.Npc, npc.Type);
        Assert.Equal((short)100, npc.Position.X);
        Assert.Equal((short)200, npc.Position.Y);   // 站立 y 取 WZ cy
        Assert.Equal((short)5, npc.Position.Foothold);
        Assert.Equal(9000000, npc.Definition.NpcId);
    }

    [Fact]
    public void FieldInstance_HoldsPlayersAndNpcsTogether()
    {
        var f = new FieldInstance(100000000);
        f.Add(MakePlayer(1, 0, 0));
        f.Add(new Npc(new MapNpc { NpcId = 9000000, X = 10, Cy = 0 }, objectId: 1000));

        Assert.Single(f.Players);                                  // NPC 不算 player
        Assert.Equal(2, f.Objects.Count);                          // 但同在場上
        Assert.Equal(FieldObjectType.Npc, f.Get(1000)!.Type);
    }
}
