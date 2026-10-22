namespace Mova.Domain.Enums;

public enum SavingsCircleStatus
{
    Draft = 1,
    Active = 2,
    Completed = 3,
    Cancelled = 4,
    Suspended = 5,
}

public enum CircleMemberStatus
{
    Invited = 1,
    Active = 2,
    Defaulted = 3,
    Exited = 4,
    Completed = 5,
}

public enum ContributionStatus
{
    Pending = 1,
    Collected = 2,
    Failed = 3,
    Waived = 4,
}

public enum ContributionFrequency
{
    Daily = 1,
    Weekly = 2,
    Monthly = 4,
}

public enum PenaltyAmountFrom
{
    Round = 1,
    Deposit = 2
}

public enum RotationStrategy
{
    FixedOrder = 1,
    RandomDraw = 2,
    BiddingBased = 3,
}

public enum CircleRoundStatus
{
    Pending = 1,
    Collecting = 2,
    Collected = 3,
    PaidOut = 4,
    Failed = 5,
}