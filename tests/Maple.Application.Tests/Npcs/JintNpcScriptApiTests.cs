using Maple.Application.Npcs;
using Maple.Application.Quests;
using Maple.Core.Characters;
using Maple.Core.Quests;
using Maple.Core.World;
using Maple.Scripting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple.Application.Tests.Npcs;

/// <summary>P166：Jint 實跑 OdinMS 風格腳本，確認新 cm API 與 cm.getPlayer() facade 的 overload 可被 JS 呼叫。</summary>
public sealed class JintNpcScriptApiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mf-jint-" + Guid.NewGuid().ToString("N"));

    public JintNpcScriptApiTests() => Directory.CreateDirectory(Path.Combine(_root, "npc"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Script_UsesGetPlayerHaveItemQuantityAndGainItem()
    {
        File.WriteAllText(Path.Combine(_root, "npc", "9999999.js"), """
            function start() {
                cm.gainItem(4000000, 3);
                if (cm.getPlayer().getLevel() >= 30 && cm.haveItem(4000000, 3) && cm.canHold(2000000)) {
                    cm.sendOk("lv" + cm.getPlayer().getLevel() + " qty" + cm.itemQuantity(4000000) + " " + cm.getChar().getName());
                } else {
                    cm.sendOk("no");
                }
                cm.safeDispose();
            }
            function action(mode, type, selection) { cm.dispose(); }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js", Level = 30 }, new Position(0, 0, 0, 0));
        var ctx = new NpcContext(9999999, player, new QuestService(new EmptyQuestCatalog()));
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var script = factory.TryCreate(9999999, ctx)!;
        var dialogs = new List<NpcDialog>();
        var convo = new NpcConversation(9999999, script, ctx, (d, _) => { dialogs.Add(d); return Task.CompletedTask; }, (_, _) => Task.CompletedTask);

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal("lv30 qty3 Js", Assert.Single(dialogs).Text);
        await convo.ContinueAsync(1, 0, -1, CancellationToken.None);
        Assert.False(convo.Active);
    }

    private sealed class EmptyQuestCatalog : IQuestCatalog
    {
        public QuestDefinition? GetQuest(int questId) => null;
    }
}
