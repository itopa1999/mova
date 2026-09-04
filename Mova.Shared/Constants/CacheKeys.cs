namespace Mova.Shared.Constants;

public static class CacheKeys
{
    public static string Profile(long userId) => $"profile:{userId}";
}
