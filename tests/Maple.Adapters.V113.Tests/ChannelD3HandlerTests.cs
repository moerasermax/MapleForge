using Maple.Adapters.V113.Channel;
using Maple.Application.CashShop;
using Maple.Application.Combat;
using Maple.Application.Events;
using Maple.Application.Maps;
using Maple.Core.Accounts;
using Maple.Core.CashShop;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.IO;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelD3HandlerTests
{
    [Fact]
    public async Task CouponCode_ParseAndRedeemNxRefreshesCashBalance()
    {
        var repo = new InMemoryCouponRepository(new CashCoupon
        {
            Code = "D3NX",
            Type = CashCouponRewardType.CashPoints,
            Item = 123,
        });
        var handler = new V113CashShopOperationHandler(new CashShopService(new EmptyCashItemCatalog(), repo));
        var account = new Account { Id = 7, CashPoints = 1 };
        var player = Player(job: 100);
        var requestBytes = Writer(w => w.WriteShort(0x1234).WriteMapleString("d3nx"));

        var request = V113CashShopPackets.ParseCouponCode(new PacketReader(requestBytes));
        var result = await handler.HandleCouponCodeAsync(
            new PacketReader(requestBytes),
            account,
            player,
            FixedNow);

        Assert.Equal(0x1234, request.Unknown);
        Assert.Equal("d3nx", request.Code);
        Assert.True(result.AccountMutated);
        Assert.False(result.CharacterMutated);
        Assert.Equal(124, account.CashPoints);

        var packet = Assert.Single(result.Packets);
        var r = new PacketReader(packet);
        Assert.Equal(V113ChannelSendOp.CashShopUpdate, r.ReadShort());
        Assert.Equal(124, r.ReadInt());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public async Task CouponCode_InvalidCodeWritesJavaFailPacket()
    {
        var handler = new V113CashShopOperationHandler(
            new CashShopService(new EmptyCashItemCatalog(), new InMemoryCouponRepository()));

        var result = await handler.HandleCouponCodeAsync(
            new PacketReader(Writer(w => w.WriteShort(0).WriteMapleString("missing"))),
            new Account(),
            Player(job: 100),
            FixedNow);

        Assert.False(result.AccountMutated);
        var packet = Assert.Single(result.Packets);
        var r = new PacketReader(packet);
        Assert.Equal(V113ChannelSendOp.CashShopOperation, r.ReadShort());
        Assert.Equal(V113CashShopPackets.ServerBoughtCashItemFailed, r.ReadByte());
        Assert.Equal(179, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void GamePoll_ParseReadsTickSelectionAndDisabledPollEnablesActions()
    {
        var requestBytes = Writer(w => w.WriteInt(123456).WriteInt(2));

        var request = V113UserInterfaceHandler.ParseGamePoll(new PacketReader(requestBytes));
        var response = V113UserInterfaceHandler.HandleGamePoll(new PacketReader(requestBytes));

        Assert.True(request.Complete);
        Assert.Equal(123456, request.Tick);
        Assert.Equal(2, request.Selection);
        Assert.Equal(V113StatsPackets.EnableActions(), response);
    }

    [Fact]
    public void MapleTv_ParsesPayloadAndEnablesActionsAsJavaParityStub()
    {
        var requestBytes = Writer(w => w.WriteByte(1).WriteMapleString("msg"));

        var request = V113UserInterfaceHandler.ParseMapleTv(new PacketReader(requestBytes));
        var response = V113UserInterfaceHandler.HandleMapleTv(new PacketReader(requestBytes));

        Assert.Equal(requestBytes, request.Payload);
        Assert.Equal(V113StatsPackets.EnableActions(), response);
    }

    [Fact]
    public void BeansUpdate_ResetsSessionAndWritesUnverifiedExitBeansFixture()
    {
        var handler = new V113EventMiniGameHandler(new CoconutEventService());
        var player = Player(job: 100);
        player.Character.Beans = 5;
        player.BeansGameSession.Start(player.Character.Beans);
        Assert.True(player.BeansGameSession.IsActive);

        var result = handler.HandleBeansUpdate(new PacketReader(Writer(w => w.WriteByte(0x99))), player);

        Assert.False(player.BeansGameSession.IsActive);
        Assert.Equal(2, result.SelfPackets.Count);
        // server-to-client fixture is Java-source candidate/unverified until live client smoke.
        Assert.Equal(new byte[] { 0x54, 0x01, 0x06 }, result.SelfPackets[0]);
        Assert.Equal(V113StatsPackets.EnableActions(), result.SelfPackets[1]);
    }

    [Fact]
    public void MonsterBomb_KillsSelfDestructMobWithoutRewardsAndBroadcastsUnverifiedKillAnimation()
    {
        var field = new FieldInstance(100000000);
        var mob = new Mob(
            new MapMonster { MonsterId = 9300166, X = 10, Y = 20, Fh = 1 },
            new MobStats(9300166, MaxHp: 50, MaxMp: 10, Level: 1, Exp: 999, SelfDestructAnimation: 3),
            objectId: 100001);
        field.Add(mob);
        var player = Player(job: 421);
        var combat = new CombatService(new MapService(new EmptyDataProvider()));
        var requestBytes = Writer(w => w.WriteInt(100001));

        var request = V113MonsterBombHandler.Parse(new PacketReader(requestBytes));
        var result = V113MonsterBombHandler.Handle(new PacketReader(requestBytes), player, field, combat);

        Assert.Equal(100001, request.MobObjectId);
        Assert.True(result.Killed);
        Assert.Empty(result.SelfPackets);
        Assert.Null(field.GetMob(100001));

        // server-to-client fixture is Java-source candidate/unverified until live client smoke.
        var packet = Assert.Single(result.MapPackets);
        var r = new PacketReader(packet);
        Assert.Equal(V113ChannelSendOp.KillMonster, r.ReadShort());
        Assert.Equal(100001, r.ReadInt());
        Assert.Equal(3, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    private static readonly DateTimeOffset FixedNow = new(2026, 7, 6, 0, 0, 0, TimeSpan.Zero);

    private static byte[] Writer(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return writer.ToArray();
    }

    private static Player Player(short job)
        => new(
            new Character
            {
                Id = 77,
                Name = "D3",
                Job = job,
                Stats = new CharacterStats { Hp = 100, MaxHp = 100, Mp = 10, MaxMp = 10 },
            },
            new Position(0, 0, 0, 0));

    private sealed class EmptyCashItemCatalog : ICashItemCatalog
    {
        public CashItemDefinition? GetBySerialNumber(int serialNumber) => null;
    }

    private sealed class InMemoryCouponRepository : ICashCouponRepository
    {
        private readonly Dictionary<string, CashCoupon> _coupons;

        public InMemoryCouponRepository(params CashCoupon[] coupons)
        {
            _coupons = coupons.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
        }

        public Task<CashCoupon?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult(_coupons.GetValueOrDefault(code));

        public Task<bool> TryMarkUsedAsync(
            string code,
            string usedBy,
            DateTimeOffset usedAt,
            CancellationToken cancellationToken = default)
        {
            if (!_coupons.TryGetValue(code, out var coupon) || !coupon.Valid)
            {
                return Task.FromResult(false);
            }

            coupon.Valid = false;
            coupon.UsedBy = usedBy;
            coupon.UsedAt = usedAt;
            return Task.FromResult(true);
        }

        public Task UpsertAsync(CashCoupon coupon, CancellationToken cancellationToken = default)
        {
            _coupons[coupon.Code] = coupon;
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyDataProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => throw new NotSupportedException();

        public IDataNode? GetAt(string fileName, string path) => null;
    }
}
