namespace Mova.Domain.Enums;

public enum UserAccountStatus
{
    Active = 1,
    Restricted = 2,
    Suspended = 3,
    Deactivated = 4,
    Closed = 5
}


public enum AccountRestrictionReason
{
    None = 0,
    TermsViolation = 1,
    SuspiciousActivity = 2,
    SuspectedFraud = 3,
    SecurityReview = 4,
    ComplianceHold = 5,
    UserRequestedDeactivation = 6,
    AdministrativeAction = 7
}