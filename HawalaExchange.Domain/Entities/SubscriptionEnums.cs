namespace HawalaExchange.Domain.Entities;

public enum SubscriptionStatus
{
    Trial = 1,
    Active = 2,
    ExpiringSoon = 3,
    Expired = 4,
    Suspended = 5,
    Cancelled = 6
}

public enum BillingCycle
{
    Monthly = 1,
    Quarterly = 3,
    SemiAnnual = 6,
    Annual = 12
}

public enum SubscriptionPaymentStatus
{
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Refunded = 4,
    Cancelled = 5,
    Overdue = 6
}

public enum SubscriptionInvoiceStatus
{
    Draft = 1,
    Issued = 2,
    PartiallyPaid = 3,
    Paid = 4,
    Overdue = 5,
    Cancelled = 6,
    Refunded = 7
}
