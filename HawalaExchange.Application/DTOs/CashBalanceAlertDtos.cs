namespace HawalaExchange.Application.DTOs;

public sealed class CashBalanceAlertSettingDto
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public long CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal MinimumBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public bool IsBelowMinimum => IsActive && CurrentBalance < MinimumBalance;
    public bool IsActive { get; set; }
    public bool NotifyAllUsers { get; set; }
    public bool ShowInApp { get; set; }
    public List<long> RecipientUserIds { get; set; } = [];
    public List<string> RecipientNames { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class SaveCashBalanceAlertSettingDto
{
    public long AccountId { get; set; }
    public long CurrencyId { get; set; }
    public decimal MinimumBalance { get; set; }
    public bool IsActive { get; set; } = true;
    public bool NotifyAllUsers { get; set; } = true;
    public bool ShowInApp { get; set; } = true;
    public List<long> RecipientUserIds { get; set; } = [];
}

public sealed class CashBalanceAlertDto
{
    public long Id { get; set; }
    public long SettingId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal CurrentBalance { get; set; }
    public decimal MinimumBalance { get; set; }
    public decimal Shortage => Math.Max(MinimumBalance - CurrentBalance, 0m);
    public string Message =>
        $"موجودی {CurrencyCode} در {AccountName} به {CurrentBalance:N2} رسیده است. حداقل تعیین‌شده {MinimumBalance:N2} است.";
    public DateTime TriggeredAt { get; set; }
    public DateTime LastCheckedAt { get; set; }
}

public sealed class CashBalanceAlertUserDto
{
    public long Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}
