using Maple.Core.Events;
using Maple.Core.MiniGames;

namespace Maple.Core.World;

public sealed partial class Player
{
    private BeansGameSession? _beansGameSession;

    public RpsSession? RpsSession { get; private set; }

    public BeansGameSession BeansGameSession => _beansGameSession ??= new BeansGameSession(Character.Id);

    public CoconutTeam CoconutTeam { get; private set; } = CoconutTeam.Maple;

    public void SetRpsSession(RpsSession? session) => RpsSession = session;

    public void ClearRpsSession() => RpsSession = null;

    public void SetCoconutTeam(CoconutTeam team)
    {
        if (!Enum.IsDefined(team))
        {
            throw new ArgumentOutOfRangeException(nameof(team), team, "Coconut team must be Maple or Story.");
        }

        CoconutTeam = team;
    }
}
