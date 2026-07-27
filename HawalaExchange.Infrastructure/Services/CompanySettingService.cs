using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public class CompanySettingService : ICompanySettingService
{
    private readonly ApplicationDbContext _context;

    public CompanySettingService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CompanySettingDto> GetAsync()
    {
        var setting = await _context.CompanySettings
            .AsNoTracking()
            .Include(x => x.DefaultProfitCurrency)
            .FirstOrDefaultAsync();

        var fallbackCurrency = setting?.DefaultProfitCurrencyId == null
            ? await GetFallbackProfitCurrencyAsync()
            : null;

        if (setting == null)
        {
            return new CompanySettingDto
            {
                CompanyName = "نام شرکت",
                FooterNote = "تشکر از اعتماد شما",
                DefaultProfitCurrencyId = fallbackCurrency?.Id,
                DefaultProfitCurrencyCode = fallbackCurrency?.Code ?? string.Empty
            };
        }

        return new CompanySettingDto
        {
            Id = setting.Id,
            CompanyName = setting.CompanyName,
            LogoPath = setting.LogoPath,
            PhoneNumber = setting.PhoneNumber,
            WhatsAppNumber = setting.WhatsAppNumber,
            TelegramUserName = setting.TelegramUserName,
            Address = setting.Address,
            FooterNote = setting.FooterNote,
            DefaultProfitCurrencyId = setting.DefaultProfitCurrencyId ?? fallbackCurrency?.Id,
            DefaultProfitCurrencyCode =
                setting.DefaultProfitCurrency?.Code ??
                fallbackCurrency?.Code ??
                string.Empty
        };
    }

    public async Task<CompanySettingDto> SaveAsync(CompanySettingDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.CompanyName))
            throw new InvalidOperationException("نام شرکت الزامی است.");

        if (!dto.DefaultProfitCurrencyId.HasValue || dto.DefaultProfitCurrencyId.Value <= 0)
            throw new InvalidOperationException("انتخاب ارز اصلی محاسبه مفاد و ضرر الزامی است.");

        var profitCurrency = await _context.Currencies
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == dto.DefaultProfitCurrencyId.Value &&
                x.IsActive);

        if (profitCurrency == null)
            throw new InvalidOperationException("ارز اصلی محاسبه مفاد و ضرر معتبر یا فعال نیست.");

        var setting = await _context.CompanySettings
            .FirstOrDefaultAsync();

        if (setting == null)
        {
            setting = new CompanySetting
            {
                CreatedAt = DateTime.UtcNow
            };

            await _context.CompanySettings.AddAsync(setting);
        }

        setting.CompanyName = dto.CompanyName.Trim();
        setting.LogoPath = dto.LogoPath;
        setting.PhoneNumber = dto.PhoneNumber;
        setting.WhatsAppNumber = dto.WhatsAppNumber;
        setting.TelegramUserName = dto.TelegramUserName;
        setting.Address = dto.Address;
        setting.FooterNote = dto.FooterNote;
        setting.DefaultProfitCurrencyId = profitCurrency.Id;
        setting.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new CompanySettingDto
        {
            Id = setting.Id,
            CompanyName = setting.CompanyName,
            LogoPath = setting.LogoPath,
            PhoneNumber = setting.PhoneNumber,
            WhatsAppNumber = setting.WhatsAppNumber,
            TelegramUserName = setting.TelegramUserName,
            Address = setting.Address,
            FooterNote = setting.FooterNote,
            DefaultProfitCurrencyId = profitCurrency.Id,
            DefaultProfitCurrencyCode = profitCurrency.Code
        };
    }

    private async Task<Currency?> GetFallbackProfitCurrencyAsync()
    {
        return await _context.Currencies
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.Code == "AFN")
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync();
    }
}
