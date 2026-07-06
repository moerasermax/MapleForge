using Maple.Adapters.V113.Channel;
using Maple.Application.Parties;
using Maple.Application.PlayerShops;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.PlayerShops;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelHiredMerchantHandlerTests
{
    private readonly DateTimeOffset _now = DateTimeOffset.UnixEpoch.AddDays(2);

    [Fact]
    public async Task RemoteControl_Action3WithRemoteItem_OpensOwnerMerchant()
    {
        var repo = new InMemoryHiredMerchantRepository();
        var handler = Handler(repo);
        var owner = PlayerWithItems(
            id: 1,
            accountId: 10,
            name: "Owner",
            mapId: 910000000,
            meso: 0,
            new ItemRecord { Type = (byte)InventoryType.Cash, ItemId = 5470000, Slot = 1, Quantity = 1 });
        var merchant = NewOpenMerchant();
        await repo.AddAsync(merchant);

        var result = await handler.HandleRemoteControlAsync(Reader(w => w.WriteByte(3)), owner, _now);

        Assert.True(result.Handled);
        var packet = Assert.Single(result.SelfPackets);
        var reader = new PacketReader(packet);
        Assert.Equal(V113ChannelSendOp.PlayerInteraction, reader.ReadShort());
        Assert.Equal(5, reader.ReadByte());
        Assert.Equal(5, reader.ReadByte());
        Assert.Equal(4, reader.ReadByte());
        Assert.Equal(PlayerShopStatus.Maintenance, (await repo.FindByStoreIdAsync(merchant.StoreId))!.Status);
    }

    [Fact]
    public async Task UseHiredMerchant_WithPermitInMerchantRoom_SendsTitleBox()
    {
        var handler = Handler(new InMemoryHiredMerchantRepository());
        var player = PlayerWithItems(
            id: 1,
            accountId: 10,
            name: "Owner",
            mapId: 910000001,
            meso: 0,
            new ItemRecord { Type = (byte)InventoryType.Cash, ItemId = 5030000, Slot = 1, Quantity = 1 });

        var result = await handler.HandleUseHiredMerchantAsync(
            Reader(w => w.WriteInt(5030000)),
            player,
            channel: 1,
            _now);

        var packet = Assert.Single(result.SelfPackets);
        Assert.Equal(new byte[] { 0x2F, 0x00, 0x07 }, packet);
    }

    [Fact]
    public async Task UseHiredMerchant_CreateFragment_CreatesOpenMerchantAndSpawnPacket()
    {
        var repo = new InMemoryHiredMerchantRepository();
        var handler = Handler(repo);
        var player = PlayerWithItems(
            id: 1,
            accountId: 10,
            name: "Owner",
            mapId: 910000001,
            meso: 0,
            new ItemRecord { Type = (byte)InventoryType.Cash, ItemId = 5030000, Slot = 1, Quantity = 1 });
        player.MoveTo(new Position(123, 456, 0, 7));

        var result = await handler.HandleUseHiredMerchantAsync(
            Reader(w => w.WriteMapleString("Potions").WriteByte(0).WriteShort(1).WriteInt(5030000)),
            player,
            channel: 1,
            _now);

        Assert.True(result.Handled);
        Assert.Equal(V113ChannelSendOp.PlayerInteraction, new PacketReader(Assert.Single(result.SelfPackets)).ReadShort());
        var spawn = new PacketReader(Assert.Single(result.MapPackets));
        Assert.Equal(V113ChannelSendOp.SpawnHiredMerchant, spawn.ReadShort());
        Assert.Equal(player.Character.Id, spawn.ReadInt());
        Assert.Equal(5030000, spawn.ReadInt());
        Assert.Equal((short)123, spawn.ReadShort());
        Assert.Equal((short)456, spawn.ReadShort());

        var merchant = await repo.FindOpenByOwnerAsync(player.Character.AccountId, player.Character.Id);
        Assert.NotNull(merchant);
        Assert.Equal(PlayerShopStatus.Open, merchant!.Status);
        Assert.Equal("Potions", merchant.Title);
    }

    [Fact]
    public async Task MerchItemStore_Operation20WithPendingPackage_SendsFredrickItemData()
    {
        var repo = new InMemoryHiredMerchantRepository();
        var handler = Handler(repo);
        var merchant = NewOpenMerchant();
        merchant.TryAddListing(InventoryType.Use, new Item { ItemId = 2000000, Quantity = 4 }, 2, 2, 100);
        merchant.State.Mesos = 300;
        merchant.CloseForClaim(_now);
        await repo.AddAsync(merchant);
        var owner = PlayerWithItems(1, 10, "Owner", 100000000, 0);

        var result = await handler.HandleMerchItemStoreAsync(
            Reader(w => w.WriteByte(20).WriteMapleString(string.Empty)),
            owner);

        var packet = Assert.Single(result.SelfPackets);
        var reader = new PacketReader(packet);
        Assert.Equal(V113ChannelSendOp.MerchItemStore, reader.ReadShort());
        Assert.Equal(V113HiredMerchantPackets.MerchItemStoreOpenPackage, reader.ReadByte());
        Assert.Equal(9030000, reader.ReadInt());
        Assert.Equal(merchant.StoreId, reader.ReadInt());
        reader.Skip(5);
        Assert.Equal(300, reader.ReadInt());
    }

    [Fact]
    public async Task MerchItemStore_Operation26_ClaimsItemsMesosAndDeletesPackage()
    {
        var repo = new InMemoryHiredMerchantRepository();
        var handler = Handler(repo);
        var merchant = NewOpenMerchant();
        merchant.TryAddListing(InventoryType.Etc, new Item { ItemId = 4000000, Quantity = 3 }, 3, 1, 50);
        merchant.State.Mesos = 500;
        merchant.CloseForClaim(_now);
        await repo.AddAsync(merchant);
        var owner = PlayerWithItems(1, 10, "Owner", 100000000, 100);

        var result = await handler.HandleMerchItemStoreAsync(Reader(w => w.WriteByte(26)), owner);

        Assert.True(result.CharacterMutated);
        Assert.Contains(result.SelfPackets, packet => new PacketReader(packet).ReadShort() == V113ChannelSendOp.ModifyInventoryItem);
        Assert.Contains(result.SelfPackets, packet => new PacketReader(packet).ReadShort() == V113ChannelSendOp.UpdateStats);
        var message = result.SelfPackets.Single(packet => new PacketReader(packet).ReadShort() == V113ChannelSendOp.MerchItemMessage);
        var messageReader = new PacketReader(message);
        Assert.Equal(V113ChannelSendOp.MerchItemMessage, messageReader.ReadShort());
        Assert.Equal(V113HiredMerchantPackets.MerchItemClaimSuccess, messageReader.ReadByte());
        Assert.Equal(600, owner.Character.Meso);
        Assert.Equal(3, owner.Inventory.By(InventoryType.Etc).CountById(4000000));
        Assert.Null(await repo.FindByStoreIdAsync(merchant.StoreId));
    }

    [Fact]
    public void SpawnHiredMerchant_WritesJavaSourceOpcodeAndInteractionTail()
    {
        var merchant = NewOpenMerchant();

        var packet = V113HiredMerchantPackets.SpawnHiredMerchant(merchant, new Position(12, 34, 0, 5));

        var reader = new PacketReader(packet);
        Assert.Equal(V113ChannelSendOp.SpawnHiredMerchant, reader.ReadShort());
        Assert.Equal(merchant.OwnerId, reader.ReadInt());
        Assert.Equal(merchant.ItemId, reader.ReadInt());
        Assert.Equal((short)12, reader.ReadShort());
        Assert.Equal((short)34, reader.ReadShort());
        Assert.Equal((short)0, reader.ReadShort());
        Assert.Equal(merchant.OwnerName, reader.ReadMapleString());
        Assert.Equal(V113HiredMerchantPackets.HiredMerchantShopType, reader.ReadByte());
        Assert.Equal(merchant.Title, reader.ReadMapleString());
    }

    private V113HiredMerchantHandler Handler(InMemoryHiredMerchantRepository repo)
        => new(new PlayerShopService(repo), repo, new InMemoryPartyRegistry());

    private HiredMerchant NewOpenMerchant()
    {
        var merchant = HiredMerchant.Create(1, 10, "Owner", 5030000, "Shop", 910000001, 1, _now, TimeSpan.FromDays(1));
        merchant.Open(_now);
        return merchant;
    }

    private static PacketReader Reader(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return new PacketReader(writer.ToArray());
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
                .Where(m => m.Status == PlayerShopStatus.Open && m.IsExpired(now))
                .ToList());

        public Task<bool> DeleteAsync(int storeId, CancellationToken cancellationToken = default)
            => Task.FromResult(_merchants.Remove(storeId));
    }
}
