namespace HawalaExchange.Application.DTOs;

public class CompanySettingDto
{
    public long Id { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string? LogoPath { get; set; }

    public string? PhoneNumber { get; set; }

    public string? WhatsAppNumber { get; set; }

    public string? TelegramUserName { get; set; }

    public string? Address { get; set; }

    public string? FooterNote { get; set; }
}