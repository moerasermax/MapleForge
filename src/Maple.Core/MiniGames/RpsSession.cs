namespace Maple.Core.MiniGames;

public sealed class RpsSession
{
    public const int EntryFee = 1000;
    public const int MaxWins = 10;

    private readonly Func<RpsChoice> _opponentChoiceProvider;

    public int PlayerId { get; }

    public int Wins { get; private set; }

    public bool IsActive { get; private set; }

    public bool AwaitingContinue { get; private set; }

    public RpsChoice? LastOpponentChoice { get; private set; }

    public RpsResult? LastResult { get; private set; }

    public RpsSession(int playerId, Func<RpsChoice>? opponentChoiceProvider = null)
    {
        PlayerId = playerId;
        _opponentChoiceProvider = opponentChoiceProvider ?? RandomOpponentChoice;
    }

    public void Start()
    {
        Wins = 0;
        IsActive = true;
        AwaitingContinue = false;
        LastOpponentChoice = null;
        LastResult = null;
    }

    public RpsResult Play(RpsChoice choice)
    {
        if (!Enum.IsDefined(choice))
        {
            throw new ArgumentOutOfRangeException(nameof(choice), choice, "RPS choice must be rock, scissors, or paper.");
        }

        if (!IsActive)
        {
            throw new InvalidOperationException("RPS session is not active.");
        }

        if (AwaitingContinue)
        {
            throw new InvalidOperationException("RPS session is waiting for continue or cash out.");
        }

        var opponent = _opponentChoiceProvider();
        if (!Enum.IsDefined(opponent))
        {
            throw new InvalidOperationException("Opponent choice provider returned an invalid RPS choice.");
        }

        LastOpponentChoice = opponent;

        var result = Compare(choice, opponent);
        LastResult = result;

        switch (result)
        {
            case RpsResult.Win:
                Wins++;
                AwaitingContinue = true;
                if (Wins >= MaxWins)
                {
                    IsActive = false;
                }
                break;
            case RpsResult.Lose:
                IsActive = false;
                AwaitingContinue = false;
                break;
            case RpsResult.Tie:
                break;
        }

        return result;
    }

    public bool Continue()
    {
        if (!IsActive || !AwaitingContinue || Wins >= MaxWins)
        {
            return false;
        }

        AwaitingContinue = false;
        LastOpponentChoice = null;
        LastResult = null;
        return true;
    }

    public int CashOut()
    {
        var reward = PreviewCashOutReward();
        End();
        return reward;
    }

    public int PreviewCashOutReward() => Wins <= 0 ? 0 : EntryFee * (Wins + 1);

    public void End()
    {
        IsActive = false;
        AwaitingContinue = false;
    }

    private static RpsResult Compare(RpsChoice player, RpsChoice opponent)
    {
        if (player == opponent)
        {
            return RpsResult.Tie;
        }

        return player switch
        {
            RpsChoice.Rock when opponent == RpsChoice.Scissors => RpsResult.Win,
            RpsChoice.Scissors when opponent == RpsChoice.Paper => RpsResult.Win,
            RpsChoice.Paper when opponent == RpsChoice.Rock => RpsResult.Win,
            _ => RpsResult.Lose,
        };
    }

    private static RpsChoice RandomOpponentChoice() => (RpsChoice)Random.Shared.Next(3);
}
