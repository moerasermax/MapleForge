using LiteDB;
using Maple.Adapters.V113.Channel;
using Maple.Application.OnlinePlayers;
using Maple.Application.Parties;
using Maple.Application.PlayerShops;
using Maple.Application.Trades;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.PlayerShops;
using Maple.Core.World;
using Maple.Persistence.PlayerShops;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelPlayerInteractionHiredMerchantTests
{
    private readonly DateTimeOffset _now = DateTimeOffset.UnixEpoch.AddDays(3);

    [Fact]
    public async Task PlayerInteraction_HiredMerchantCreateAddOpenVisitBuyChatAndClose()
    {
        var repo = new InMemoryHiredMerchantRepository();
        var router = Router(repo);
        var owner = PlayerWithItems(
            id: 1,
            accountId: 10,
            name: "Owner",
            mapId: 910000001,
            meso: 0,
            new ItemRecord { Type = (byte)InventoryType.Cash, ItemId = 5030000, Slot = 1, Quantity = 1 },
            new ItemRecord { Type = (byte)InventoryType.Use, ItemId = 2000000, Slot = 2, Quantity = 10 });
        owner.MoveTo(new Position(111, 222, 0, 7));
        var buyer = PlayerWithItems(2, 20, "Buyer", 910000001, meso: 1_000);
        var visitor = PlayerWithItems(3, 30, "Visitor", 910000001, meso: 1_000);
        var ownerSelf = new List<byte[]>();
        var ownerMap = new List<byte[]>();
        var buyerSelf = new List<byte[]>();
        var buyerMap = new List<byte[]>();
        var visitorSelf = new List<byte[]>();
        var visitorMap = new List<byte[]>();
        var nonVisitorMap = new List<byte[]>();

        await router.HandleAsync(CreateMerchant("Potions"), owner, Capture(ownerSelf), Capture(ownerMap), 1, _now, CancellationToken.None);
        var merchant = await repo.FindOpenByOwnerAsync(owner.Character.AccountId, owner.Character.Id);

        Assert.NotNull(merchant);
        Assert.Equal(PlayerShopStatus.Draft, merchant!.Status);
        Assert.Equal(merchant.StoreId, owner.ActiveShopId);
        Assert.Equal(new Position(111, 222, 0, 7), merchant.Position);
        Assert.Contains(ownerSelf, p => new PacketReader(p).ReadShort() == V113ChannelSendOp.PlayerInteraction);

        var addChanged = await router.HandleAsync(
            AddItem(InventoryType.Use, slot: 2, bundles: 2, perBundle: 3, price: 100),
            owner,
            Capture(ownerSelf),
            Capture(ownerMap),
            1,
            _now,
            CancellationToken.None);

        merchant = await repo.FindByStoreIdAsync(merchant.StoreId);
        Assert.True(addChanged);
        Assert.Equal((short)4, owner.Inventory.By(InventoryType.Use).Get(2)!.Quantity);
        Assert.Equal((short)2, merchant!.Items[0].Bundles);
        Assert.Contains(ownerSelf, p => PacketAction(p) == 0x16);

        await router.HandleAsync(Interaction(0x0B), owner, Capture(ownerSelf), Capture(ownerMap), 1, _now, CancellationToken.None);

        merchant = await repo.FindByStoreIdAsync(merchant.StoreId);
        Assert.Equal(PlayerShopStatus.Open, merchant!.Status);
        Assert.Null(owner.ActiveShopId);
        // Java-source candidate/unverified: SPAWN_HIRED_MERCHANT layout still needs true v113 client capture.
        var spawn = new PacketReader(ownerMap.Single(p => new PacketReader(p).ReadShort() == V113ChannelSendOp.SpawnHiredMerchant));
        Assert.Equal(V113ChannelSendOp.SpawnHiredMerchant, spawn.ReadShort());
        Assert.Equal(owner.Character.Id, spawn.ReadInt());
        Assert.Equal(5030000, spawn.ReadInt());
        Assert.Equal((short)111, spawn.ReadShort());
        Assert.Equal((short)222, spawn.ReadShort());

        await router.HandleAsync(Visit(owner.Character.Id), buyer, Capture(buyerSelf), Capture(buyerMap), 1, _now, CancellationToken.None);

        merchant = await repo.FindByStoreIdAsync(merchant.StoreId);
        Assert.NotNull(merchant);
        Assert.Equal(merchant!.StoreId, buyer.ActiveShopId);
        Assert.Contains(merchant!.State.Visitors, v => v.CharacterId == buyer.Character.Id && v.Slot == 1);
        Assert.DoesNotContain(buyerMap, p => PacketAction(p) == 0x04);

        await router.HandleAsync(Visit(owner.Character.Id), visitor, Capture(visitorSelf), Capture(visitorMap), 1, _now, CancellationToken.None);

        merchant = await repo.FindByStoreIdAsync(merchant.StoreId);
        Assert.NotNull(merchant);
        Assert.Equal(merchant!.StoreId, visitor.ActiveShopId);
        Assert.Contains(merchant.State.Visitors, v => v.CharacterId == visitor.Character.Id && v.Slot == 2);
        Assert.Contains(buyerSelf, p => PacketAction(p) == 0x04);
        Assert.DoesNotContain(visitorMap, p => PacketAction(p) == 0x04);

        var visitorPacketsBeforeBuy = visitorSelf.Count;
        var buyChanged = await router.HandleAsync(
            Buy(index: 0, bundles: 1),
            buyer,
            Capture(buyerSelf),
            Capture(nonVisitorMap),
            1,
            _now,
            CancellationToken.None);

        merchant = await repo.FindByStoreIdAsync(merchant.StoreId);
        Assert.NotNull(merchant);
        Assert.True(buyChanged);
        Assert.Equal(900, buyer.Character.Meso);
        Assert.Equal(3, buyer.Inventory.By(InventoryType.Use).CountById(2000000));
        Assert.Equal((short)1, merchant!.Items[0].Bundles);
        Assert.Equal(100, merchant.Mesos);
        Assert.Contains(visitorSelf.Skip(visitorPacketsBeforeBuy), p => PacketAction(p) == 0x16);
        Assert.DoesNotContain(nonVisitorMap, p => PacketAction(p) == 0x16);

        await router.HandleAsync(Chat("hi"), buyer, Capture(buyerSelf), Capture(buyerMap), 1, _now, CancellationToken.None);
        Assert.Contains(buyerSelf, p => PacketAction(p) == 0x06);
        Assert.Contains(visitorSelf, p => PacketAction(p) == 0x06);

        await router.HandleAsync(Visit(owner.Character.Id), owner, Capture(ownerSelf), Capture(ownerMap), 1, _now, CancellationToken.None);
        await router.HandleAsync(Interaction(0x26), owner, Capture(ownerSelf), Capture(ownerMap), 1, _now, CancellationToken.None);

        merchant = await repo.FindByStoreIdAsync(merchant.StoreId);
        Assert.Equal(PlayerShopStatus.PendingClaim, merchant!.Status);
        Assert.Null(owner.ActiveShopId);
        Assert.Contains(ownerSelf, IsCloseMerchantShopError);
        Assert.Contains(ownerMap, p => new PacketReader(p).ReadShort() == V113ChannelSendOp.DestroyHiredMerchant);
    }

    [Fact]
    public async Task HiredMerchant_LiteDbEndToEnd_ReplaysPositionAfterRestartAndClaimsFredrickPackage()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"maple-hired-merchant-e2e-{Guid.NewGuid():N}.db");
        try
        {
            var owner = PlayerWithItems(
                id: 1,
                accountId: 10,
                name: "Owner",
                mapId: 910000001,
                meso: 0,
                new ItemRecord { Type = (byte)InventoryType.Cash, ItemId = 5030000, Slot = 1, Quantity = 1 },
                new ItemRecord { Type = (byte)InventoryType.Use, ItemId = 2000000, Slot = 2, Quantity = 10 });
            owner.MoveTo(new Position(321, 654, 0, 8));
            var buyer = PlayerWithItems(2, 20, "Buyer", 910000001, meso: 1_000);
            int storeId;

            using (var db = new LiteDatabase(dbPath))
            {
                var repo = new LiteDbHiredMerchantRepository(db);
                var router = Router(repo);
                await router.HandleAsync(CreateMerchant("Restart shop"), owner, Noop, Noop, 1, _now, CancellationToken.None);
                storeId = owner.ActiveShopId!.Value;
                await router.HandleAsync(AddItem(InventoryType.Use, 2, 2, 3, 100), owner, Noop, Noop, 1, _now, CancellationToken.None);
                await router.HandleAsync(Interaction(0x0B), owner, Noop, Noop, 1, _now, CancellationToken.None);
            }

            using (var db = new LiteDatabase(dbPath))
            {
                var repo = new LiteDbHiredMerchantRepository(db);
                var service = new PlayerShopService(repo);
                var hired = new V113HiredMerchantHandler(service, repo, new InMemoryPartyRegistry());

                var expired = await service.ExpireOpenMerchantsAsync(_now.AddHours(1));
                var replay = await hired.SpawnOpenMerchantPacketsAsync(1, 910000001, new Position(0, 0, 0, 0));

                Assert.Equal(0, expired);
                // Java-source candidate/unverified: replay SPAWN_HIRED_MERCHANT fixture uses source-derived layout only.
                var spawn = new PacketReader(Assert.Single(replay));
                Assert.Equal(V113ChannelSendOp.SpawnHiredMerchant, spawn.ReadShort());
                Assert.Equal(owner.Character.Id, spawn.ReadInt());
                Assert.Equal(5030000, spawn.ReadInt());
                Assert.Equal((short)321, spawn.ReadShort());
                Assert.Equal((short)654, spawn.ReadShort());

                var router = Router(repo);
                await router.HandleAsync(Visit(owner.Character.Id), buyer, Noop, Noop, 1, _now, CancellationToken.None);
                await router.HandleAsync(Buy(0, 1), buyer, Noop, Noop, 1, _now, CancellationToken.None);
                await router.HandleAsync(Visit(owner.Character.Id), owner, Noop, Noop, 1, _now, CancellationToken.None);
                await router.HandleAsync(Interaction(0x26), owner, Noop, Noop, 1, _now, CancellationToken.None);

                var package = await repo.FindClaimableByOwnerAsync(owner.Character.AccountId, owner.Character.Id);
                Assert.NotNull(package);
                Assert.Equal(PlayerShopStatus.PendingClaim, package!.Status);

                var claim = await hired.HandleMerchItemStoreAsync(Reader(w => w.WriteByte(26)), owner);

                Assert.True(claim.CharacterMutated);
                Assert.Null(await repo.FindByStoreIdAsync(storeId));
                Assert.Equal(100, owner.Character.Meso);
                Assert.Equal(7, owner.Inventory.By(InventoryType.Use).CountById(2000000));
                Assert.Contains(claim.SelfPackets, p => new PacketReader(p).ReadShort() == V113ChannelSendOp.MerchItemMessage);
            }
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task NotifyDisconnectAsync_VisitorDisconnects_RemovesVisitorAndBroadcastsToRemainingParticipants()
    {
        var repo = new InMemoryHiredMerchantRepository();
        var router = Router(repo);
        var owner = PlayerWithItems(
            id: 1,
            accountId: 10,
            name: "Owner",
            mapId: 910000001,
            meso: 0,
            new ItemRecord { Type = (byte)InventoryType.Cash, ItemId = 5030000, Slot = 1, Quantity = 1 },
            new ItemRecord { Type = (byte)InventoryType.Use, ItemId = 2000000, Slot = 2, Quantity = 10 });
        var buyer = PlayerWithItems(2, 20, "Buyer", 910000001, meso: 1_000);
        var visitor = PlayerWithItems(3, 30, "Visitor", 910000001, meso: 1_000);
        var ownerSelf = new List<byte[]>();
        var buyerSelf = new List<byte[]>();

        await router.HandleAsync(CreateMerchant("Potions"), owner, Capture(ownerSelf), Noop, 1, _now, CancellationToken.None);
        await router.HandleAsync(AddItem(InventoryType.Use, 2, 2, 3, 100), owner, Capture(ownerSelf), Noop, 1, _now, CancellationToken.None);
        await router.HandleAsync(Interaction(0x0B), owner, Capture(ownerSelf), Noop, 1, _now, CancellationToken.None);
        await router.HandleAsync(Visit(owner.Character.Id), buyer, Capture(buyerSelf), Noop, 1, _now, CancellationToken.None);
        await router.HandleAsync(Visit(owner.Character.Id), visitor, Noop, Noop, 1, _now, CancellationToken.None);

        var storeId = buyer.ActiveShopId!.Value;
        var merchant = await repo.FindByStoreIdAsync(storeId);
        Assert.Contains(merchant!.State.Visitors, v => v.CharacterId == visitor.Character.Id);

        // 對照 Java MapleClient 斷線流程 removalTask 的 shop.removeVisitor(player)：訪客斷線要被清出，
        // 仍留在 shop 內的 buyer 收到 ShopVisitorLeave 通知，訪客自己（已斷線）不會被送包。owner 開店
        // 後（HandleOpenMerchantAsync 會 `_merchantSessions.Clear`）已不在 live session 名單內，
        // 除非重新 Visit 自己的攤位，故不斷言 owner 收包。
        await router.NotifyDisconnectAsync(visitor, CancellationToken.None);

        merchant = await repo.FindByStoreIdAsync(storeId);
        Assert.DoesNotContain(merchant!.State.Visitors, v => v.CharacterId == visitor.Character.Id);
        Assert.Null(visitor.ActiveShopId);
        Assert.Contains(buyerSelf, p => PacketAction(p) == 0x0A);
    }

    [Fact]
    public async Task NotifyDisconnectAsync_OwnerDisconnectsWhileEditingDraft_ClearsActiveShopIdWithoutClosingMerchant()
    {
        var repo = new InMemoryHiredMerchantRepository();
        var router = Router(repo);
        var owner = PlayerWithItems(
            id: 1,
            accountId: 10,
            name: "Owner",
            mapId: 910000001,
            meso: 0,
            new ItemRecord { Type = (byte)InventoryType.Cash, ItemId = 5030000, Slot = 1, Quantity = 1 });
        var ownerSelf = new List<byte[]>();

        await router.HandleAsync(CreateMerchant("Potions"), owner, Capture(ownerSelf), Noop, 1, _now, CancellationToken.None);

        var storeId = owner.ActiveShopId!.Value;
        var beforeCount = ownerSelf.Count;

        // owner 在 MapleForge 的 Visitors 清單中查無 slot（owner 本身不是 visitor），LeaveMerchantAsync
        // 對 owner 是 no-op，不觸發任何廣播——對照 Java 的 owner 分支邏輯（HIRED_MERCHANT 保持開放，
        // 而非關閉）；MapleForge 現行「過期轉 claimable」背景服務設計不依賴 owner 是否在線，本次不動。
        await router.NotifyDisconnectAsync(owner, CancellationToken.None);

        Assert.Null(owner.ActiveShopId);
        var merchant = await repo.FindByStoreIdAsync(storeId);
        Assert.Equal(PlayerShopStatus.Draft, merchant!.Status);
        Assert.Equal(beforeCount, ownerSelf.Count);
    }

    [Fact]
    public async Task NotifyDisconnectAsync_PlayerNotInAnyShop_DoesNothing()
    {
        var repo = new InMemoryHiredMerchantRepository();
        var router = Router(repo);
        var player = PlayerWithItems(1, 10, "Solo", 910000001, meso: 0);

        await router.NotifyDisconnectAsync(player, CancellationToken.None);

        Assert.Null(player.ActiveShopId);
    }

    private static V113PlayerInteractionRouter Router(IHiredMerchantRepository repo)
        => new(
            new TradeService(new InMemoryOnlinePlayerRegistry()),
            new PlayerShopService(repo),
            repo,
            new InMemoryHiredMerchantSessionDispatcher());

    private static Func<byte[], CancellationToken, Task> Capture(List<byte[]> packets)
        => (packet, _) =>
        {
            packets.Add(packet);
            return Task.CompletedTask;
        };

    private static Task Noop(byte[] packet, CancellationToken cancellationToken) => Task.CompletedTask;

    private static PacketReader CreateMerchant(string title)
        => Reader(w => w
            .WriteByte(0x00)
            .WriteByte(5)
            .WriteMapleString(title)
            .WriteByte(0)
            .WriteShort(1)
            .WriteInt(5030000));

    private static PacketReader AddItem(InventoryType type, short slot, short bundles, short perBundle, int price)
        => Reader(w => w
            .WriteByte(0x1E)
            .WriteByte((byte)type)
            .WriteShort(slot)
            .WriteShort(bundles)
            .WriteShort(perBundle)
            .WriteInt(price));

    private static PacketReader Visit(int ownerId)
        => Reader(w => w.WriteByte(0x04).WriteInt(ownerId));

    private static PacketReader Buy(byte index, short bundles)
        => Reader(w => w.WriteByte(0x21).WriteByte(index).WriteShort(bundles));

    private static PacketReader Chat(string message)
        => Reader(w => w.WriteByte(0x06).WriteMapleString(message));

    private static PacketReader Interaction(byte action)
        => Reader(w => w.WriteByte(action));

    private static PacketReader Reader(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return new PacketReader(writer.ToArray());
    }

    private static byte PacketAction(byte[] packet)
    {
        var reader = new PacketReader(packet);
        if (reader.ReadShort() != V113ChannelSendOp.PlayerInteraction)
        {
            return 0xFF;
        }

        return reader.ReadByte();
    }

    private static bool IsCloseMerchantShopError(byte[] packet)
    {
        try
        {
            var reader = new PacketReader(packet);
            return reader.ReadShort() == V113ChannelSendOp.PlayerInteraction &&
                reader.ReadByte() == 0x0A &&
                reader.ReadByte() == 0 &&
                reader.ReadByte() == 0x15 &&
                reader.Remaining == 0;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static Player PlayerWithItems(
        int id,
        int accountId,
        string name,
        int mapId,
        int meso,
        params ItemRecord[] items)
        => new(
            new Character
            {
                Id = id,
                AccountId = accountId,
                Name = name,
                MapId = mapId,
                Meso = meso,
                Items = items.ToList(),
            },
            new Position(0, 0, 0, 0));

    private sealed class InMemoryHiredMerchantRepository : IHiredMerchantRepository
    {
        private readonly Dictionary<int, HiredMerchant> _merchants = new();
        private int _nextStoreId = 1;

        public Task<int> AddAsync(HiredMerchant merchant, CancellationToken cancellationToken = default)
        {
            if (merchant.StoreId <= 0)
            {
                merchant.StoreId = _nextStoreId++;
            }

            _merchants[merchant.StoreId] = merchant;
            return Task.FromResult(merchant.StoreId);
        }

        public Task UpsertAsync(HiredMerchant merchant, CancellationToken cancellationToken = default)
        {
            _merchants[merchant.StoreId] = merchant;
            return Task.CompletedTask;
        }

        public Task<HiredMerchant?> FindByStoreIdAsync(int storeId, CancellationToken cancellationToken = default)
            => Task.FromResult(_merchants.GetValueOrDefault(storeId));

        public Task<HiredMerchant?> FindOpenByOwnerAsync(int ownerAccountId, int ownerId, CancellationToken cancellationToken = default)
            => Task.FromResult(_merchants.Values.FirstOrDefault(m =>
                m.OwnerAccountId == ownerAccountId &&
                m.OwnerId == ownerId &&
                m.Status is PlayerShopStatus.Draft or PlayerShopStatus.Open or PlayerShopStatus.Maintenance));

        public Task<HiredMerchant?> FindClaimableByOwnerAsync(int ownerAccountId, int ownerId, CancellationToken cancellationToken = default)
            => Task.FromResult(_merchants.Values.FirstOrDefault(m =>
                m.OwnerAccountId == ownerAccountId &&
                m.OwnerId == ownerId &&
                m.Status is PlayerShopStatus.PendingClaim or PlayerShopStatus.Expired));

        public Task<IReadOnlyList<HiredMerchant>> FindOpenByMapAsync(byte channel, int mapId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<HiredMerchant>>(_merchants.Values
                .Where(m => m.Channel == channel && m.MapId == mapId && m.Status == PlayerShopStatus.Open)
                .ToList());

        public Task<IReadOnlyList<HiredMerchant>> FindExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<HiredMerchant>>(_merchants.Values
                .Where(m => m.Status is PlayerShopStatus.Open or PlayerShopStatus.Maintenance && m.IsExpired(now))
                .ToList());

        public Task<bool> DeleteAsync(int storeId, CancellationToken cancellationToken = default)
            => Task.FromResult(_merchants.Remove(storeId));
    }
}
