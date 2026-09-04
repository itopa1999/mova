namespace Mova.Shared.Constants;

public static class Roles
{
    public const string Customer = "Customer";

    public const string Admin = "Admin";

    public const string SuperAdmin = "SuperAdmin";

    public const string SupportAgent = "SupportAgent";

}

public static class OtpPurpose
{
    public const string AccountVerification = "ACCOUNT_VERIF-CATION";

    public const string PasswordReset =
        "PASSWORD_RESET";

    public const string Login =
        "LOGIN";

    public const string TransactionPin =
        "TRANSACTION_PIN";
}


public static class Platforms
{
    public const string Web = "Web";
    public const string Mobile = "Mobile";
    public const string Swagger = "Swagger";
}

public static class SignUpMethods
{
    public const string Email = "Email";
    public const string PhoneNumber = "PhoneNumber";

}