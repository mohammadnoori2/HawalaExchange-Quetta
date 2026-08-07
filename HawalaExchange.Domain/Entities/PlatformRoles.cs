namespace HawalaExchange.Domain.Entities;

public static class PlatformRoles
{
    public const string Owner = "PlatformOwner";
    public const string Admin = "PlatformAdmin";
    public const string Support = "PlatformSupport";

    public static readonly string[] All = [Owner, Admin, Support];
    public static readonly string[] Writers = [Owner, Admin];
}

public enum SubscriptionAccessLevel
{
    Blocked = 0,
    ReadOnly = 1,
    Full = 2
}
