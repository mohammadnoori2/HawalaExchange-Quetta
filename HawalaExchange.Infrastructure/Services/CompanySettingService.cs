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
            .FirstOrDefaultAsync();

        if (setting == null)
        {
            return new CompanySettingDto
            {
                CompanyName = "نام شرکت",
                FooterNote = "تشکر از اعتماد شما"
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
            FooterNote = setting.FooterNote
        };
    }

    public async Task<CompanySettingDto> SaveAsync(CompanySettingDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.CompanyName))
            throw new InvalidOperationException("نام شرکت الزامی است.");

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
            FooterNote = setting.FooterNote
        };
    }
}