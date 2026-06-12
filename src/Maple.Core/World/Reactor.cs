using Maple.Core.Maps;

namespace Maple.Core.World;

/// <summary>
/// Reactor field-object type compatibility constant.
/// </summary>
public static class ReactorFieldObjectTypes
{
    public const FieldObjectType Reactor = FieldObjectType.Reactor;
}

public sealed record ReactorStateData(
    byte State,
    int Type,
    int? ReactItemId,
    int ReactItemQuantity,
    int NextState,
    int TimeOutMs);

public sealed class ReactorStats
{
    private readonly Dictionary<byte, ReactorStateData> _states;

    public ReactorStats(IEnumerable<ReactorStateData> states)
    {
        _states = states.ToDictionary(static s => s.State);
    }

    public IReadOnlyCollection<ReactorStateData> States => _states.Values;

    public int GetType(byte state) => _states.TryGetValue(state, out var data) ? data.Type : -1;

    public int GetNextState(byte state) => _states.TryGetValue(state, out var data) ? data.NextState : -1;

    public int GetTimeOutMs(byte state) => _states.TryGetValue(state, out var data) ? data.TimeOutMs : -1;

    public ReactorStateData? GetState(byte state) => _states.TryGetValue(state, out var data) ? data : null;
}

public enum ReactorPacketAction
{
    None,
    Hit,
    Destroy,
}

public sealed record ReactorHitResult(
    Reactor Reactor,
    bool Applied,
    byte OldState,
    byte NewState,
    short Stance,
    ReactorPacketAction PacketAction,
    bool ShouldInvokeScript,
    bool TimeoutRestorePending);

/// <summary>
/// 執行期 Reactor（地圖物件）。對照舊 Java <c>MapleReactor</c>，保留 state 推進、
/// alive 與 v113 spawn 所需位置/名稱；封包 byte layout 留在 Adapter。
/// </summary>
public sealed class Reactor : IFieldObject
{
    public MapReactor Definition { get; }

    public ReactorStats Stats { get; }

    public int ObjectId { get; }

    public Position Position { get; }

    public FieldObjectType Type => FieldObjectType.Reactor;

    public int ReactorId => Definition.ReactorId;

    public byte State { get; private set; }

    public bool IsAlive { get; private set; } = true;

    public int FacingDirection => Definition.F;

    public string Name => Definition.Name;

    public int DelayMs => Math.Max(0, Definition.ReactorTimeMs);

    public Reactor(MapReactor definition, ReactorStats stats, int objectId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(stats);

        Definition = definition;
        Stats = stats;
        ObjectId = objectId;
        Position = new Position(
            (short)Math.Clamp(definition.X, short.MinValue, short.MaxValue),
            (short)Math.Clamp(definition.Y, short.MinValue, short.MaxValue),
            0,
            0);
    }

    public void ForceState(byte state)
    {
        State = state;
        IsAlive = true;
    }

    public void Destroy() => IsAlive = false;

    /// <summary>
    /// 套用 Java MapleReactor.hitReactor 的 MVP 狀態機：方向限制、nextState、final/script trigger。
    /// timeout/delayed respawn 只回傳 pending 訊號，由上層 scheduler 後續補齊。
    /// </summary>
    public ReactorHitResult Hit(int charPosition, short stance)
    {
        var oldState = State;
        if (!IsAlive)
        {
            return Ignored(oldState, stance);
        }

        var type = Stats.GetType(State);
        if (type >= 999 || type == -1)
        {
            return Ignored(oldState, stance);
        }

        // Java：type 2 只能從右方打；charPos 0/2 時不推進。
        if (type == 2 && (charPosition == 0 || charPosition == 2))
        {
            return Ignored(oldState, stance);
        }

        var nextState = Stats.GetNextState(State);
        State = unchecked((byte)nextState);

        var newType = Stats.GetType(State);
        var newNextState = Stats.GetNextState(State);
        var isFinal = newNextState == -1 || newType == 999;
        var timeoutPending = Stats.GetTimeOutMs(State) > 0;
        var shouldInvokeScript = isFinal
            || newNextState == State
            || ReactorId is 2618000 or 2309000
            || timeoutPending;

        var action = ReactorPacketAction.Hit;
        if (isFinal && DelayMs > 0 && (newType < 100 || newType == 999))
        {
            IsAlive = false;
            action = ReactorPacketAction.Destroy;
        }

        return new ReactorHitResult(
            this,
            Applied: true,
            OldState: oldState,
            NewState: State,
            Stance: stance,
            PacketAction: action,
            ShouldInvokeScript: shouldInvokeScript,
            TimeoutRestorePending: timeoutPending);
    }

    public bool CanTouchTrigger(bool touched)
        => touched && IsAlive && ReactorId is >= 6109013 and <= 6109027;

    private ReactorHitResult Ignored(byte oldState, short stance) => new(
        this,
        Applied: false,
        OldState: oldState,
        NewState: State,
        Stance: stance,
        PacketAction: ReactorPacketAction.None,
        ShouldInvokeScript: false,
        TimeoutRestorePending: false);
}
