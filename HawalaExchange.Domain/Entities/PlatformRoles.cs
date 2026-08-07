namespace HawalaExchange.Domain.Entities;

public static class PlatformRoles
{
    public const string SuperAdmin = "PlatformSuperAdmin";
    public const string Owner = "PlatformOwner";
    public const string Admin = "PlatformAdmin";
    public const string Support = "PlatformSupport";
    public const string Finance = "PlatformFinance";
    public const string Viewer = "PlatformViewer";

    public static readonly string[] All = [SuperAdmin, Owner, Admin, Support, Finance, Viewer];
    public static readonly string[] Writers = [SuperAdmin, Owner, Admin];
    public static readonly string[] Billing = [SuperAdmin, Owner, Admin, Finance];
    public static readonly string[] Auditors = [SuperAdmin, Owner, Admin, Viewer];
    public static readonly string[] Privileged = [SuperAdmin, Owner];
}

public enum SubscriptionAccessLevel
{
    Blocked = 0,
    ReadOnly = 1,
    Full = 2
}
