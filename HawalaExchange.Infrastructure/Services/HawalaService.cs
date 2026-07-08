using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace HawalaExchange.Application.Services
{
    public class HawalaService : IHawalaService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMapper _mapper;
        private readonly ILedgerService _ledgerService;
        private readonly IAccountService _accountService;
        private readonly IAuditLogService _auditLogService;

        public HawalaService(
            ApplicationDbContext context,
            IMapper mapper,
            ILedgerService ledgerService,
            IAccountService accountService,
            IAuditLogService auditLogService)
        {
            _context = context;
            _mapper = mapper;
            _ledgerService = ledgerService;
            _accountService = accountService;
            _auditLogService = auditLogService;
        }

        public async Task<HawalaDto> CreateHawalaAsync(CreateHawalaDto dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                ValidateCreateHawala(dto);

                var hawala = _mapper.Map<Hawala>(dto);
                hawala.CreatedAt = DateTime.UtcNow;
                hawala.CreatedBy = GetCurrentUserId();
                hawala.Status = dto.Status ?? "Pending";

                // منطق شماره (نمبر)
                if (dto.Number != null && dto.Number > 0)
                {
                    hawala.Number = dto.Number;
                }
                else
                {
                    //if (hawala.HawalaType == "HawalaReceive")
                    //    throw new InvalidOperationException("برای حواله آمد، شماره (نمبر) الزامی است.");

                    // تولید خودکار شماره برای حواله رفت و متفرقه
                    var lastNumber = await _context.Hawalas
                        .Where(h => h.CorrespondentId == hawala.CorrespondentId && h.HawalaType == hawala.HawalaType)
                        .OrderByDescending(h => h.Number)
                        .Select(h => (long?)h.Number)
                        .FirstOrDefaultAsync();

                    hawala.Number = (lastNumber ?? 0) + 1;
                }

                // بررسی یکتا بودن شماره
                var exists = await _context.Hawalas
                    .AnyAsync(h => h.CorrespondentId == hawala.CorrespondentId &&
                                   h.HawalaType == hawala.HawalaType &&
                                   h.Number == hawala.Number);
                if (exists)
                    throw new InvalidOperationException($"شماره {hawala.Number} برای حواله {hawala.HawalaType} این نمایندگی قبلاً ثبت شده است.");

                await _context.Hawalas.AddAsync(hawala);
                await _context.SaveChangesAsync();

                // ثبت ورودی‌های دفتر کل
                await ProcessLedgerEntries(hawala, dto.FromAccountId);

                await _auditLogService.LogAsync("CREATE", "Hawalas", hawala.Id, null, $"حواله {hawala.HawalaType} با شماره {hawala.Number} ایجاد شد", GetCurrentUserId());

                await transaction.CommitAsync();

                return _mapper.Map<HawalaDto>(hawala);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<long> GetNextNumberAsync(long correspondentId, string hawalaType)
        {
            var lastNumber = await _context.Hawalas
                .Where(h => h.CorrespondentId == correspondentId && h.HawalaType == hawalaType)
                .OrderByDescending(h => h.Number)
                .Select(h => (long?)h.Number)
                .FirstOrDefaultAsync();

            return (lastNumber ?? 0) + 1;
        }

        public async Task<HawalaDto?> GetHawalaByIdAsync(long id)
        {
            var hawala = await _context.Hawalas
                .Include(h => h.Correspondent)
                .Include(h => h.FromCurrency)
                .Include(h => h.ToCurrency)
                .Include(h => h.CommissionCurrency)
                .Include(h => h.AgentCommissionCurrency)
                .FirstOrDefaultAsync(h => h.Id == id);

            return hawala == null ? null : _mapper.Map<HawalaDto>(hawala);
        }

        public async Task<HawalaListResultDto> GetHawalasAsync(HawalaFilterDto filter)
        {
            var query = _context.Hawalas
                .Include(h => h.Correspondent)
                .Include(h => h.FromCurrency)
                .Include(h => h.ToCurrency)
                .Include(h => h.CommissionCurrency)
                .Include(h => h.AgentCommissionCurrency)
                .AsQueryable();

            if (filter.Number  > 0)
                query = query.Where(h => h.Number == filter.Number);

            if (!string.IsNullOrEmpty(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim();
                query = query.Where(h =>
                    (h.SenderName != null && h.SenderName.Contains(term)) ||
                    (h.ReceiverName != null && h.ReceiverName.Contains(term)) ||
                    (h.ReferenceNumber != null && h.ReferenceNumber.Contains(term))
                );
            }

            if (!string.IsNullOrEmpty(filter.HawalaType))
                query = query.Where(h => h.HawalaType == filter.HawalaType);

            if (!string.IsNullOrEmpty(filter.Status))
                query = query.Where(h => h.Status == filter.Status);

            if (filter.CorrespondentId.HasValue)
                query = query.Where(h => h.CorrespondentId == filter.CorrespondentId);

            query = filter.SortDirection == "asc"
                ? query.OrderBy(GetSortExpression(filter.SortColumn))
                : query.OrderByDescending(GetSortExpression(filter.SortColumn));

            var totalCount = await query.CountAsync();

            if (filter.PageSize > 0)
            {
                query = query.Skip((filter.PageNumber - 1) * filter.PageSize)
                             .Take(filter.PageSize);
            }

            var items = await query.ToListAsync();
            return new HawalaListResultDto
            {
                Items = _mapper.Map<List<HawalaDto>>(items),
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling((double)totalCount / (filter.PageSize > 0 ? filter.PageSize : totalCount))
            };
        }

        private Expression<Func<Hawala, object>> GetSortExpression(string column)
        {
            return column switch
            {
                "Number" => h => h.Number,
                "HawalaType" => h => h.HawalaType,
                "SenderName" => h => h.SenderName ?? "",
                "ReceiverName" => h => h.ReceiverName ?? "",
                "FromAmount" => h => h.FromAmount,
                "Status" => h => h.Status,
                "CreatedAt" => h => h.CreatedAt,
                _ => h => h.Id
            };
        }

        public async Task<HawalaStatisticsDto> GetStatisticsAsync()
        {
            var all = await _context.Hawalas.ToListAsync();

            return new HawalaStatisticsDto
            {
                HawalaSendCount = all.Count(h => h.HawalaType == "HawalaSend"),
                HawalaReceiveCount = all.Count(h => h.HawalaType == "HawalaReceive"),
                HawalaOtherCount = all.Count(h => h.HawalaType == "HawalaOther"),
                PendingCount = all.Count(h => h.Status == "Pending"),
                PaidCount = all.Count(h => h.Status == "Paid")
            };
        }

        public async Task<HawalaDto> UpdateHawalaAsync(long id, UpdateHawalaDto dto)
        {
            var hawala = await _context.Hawalas.FindAsync(id);
            if (hawala == null)
                throw new KeyNotFoundException($"حواله با شناسه {id} یافت نشد.");

            _mapper.Map(dto, hawala);
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync("UPDATE", "Hawalas", hawala.Id, null, $"حواله با شناسه {hawala.Id} ویرایش شد", GetCurrentUserId());

            return _mapper.Map<HawalaDto>(hawala);
        }

        public async Task DeleteHawalaAsync(long id)
        {
            var hawala = await _context.Hawalas.FindAsync(id);
            if (hawala == null)
                throw new KeyNotFoundException($"حواله با شناسه {id} یافت نشد.");

            if (hawala.Status == "Paid")
                throw new InvalidOperationException("حواله پرداخت شده قابل حذف نیست.");

            _context.Hawalas.Remove(hawala);
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync("DELETE", "Hawalas", id, null, $"حواله با شناسه {id} حذف شد", GetCurrentUserId());
        }

        public async Task<HawalaDto> MarkAsPaidAsync(long id)
        {
            var hawala = await _context.Hawalas.FindAsync(id);
            if (hawala == null)
                throw new KeyNotFoundException($"حواله با شناسه {id} یافت نشد.");

            if (hawala.Status == "Paid")
                throw new InvalidOperationException("حواله قبلاً پرداخت شده است.");

            hawala.Status = "Paid";
            hawala.PaidAt = DateTime.UtcNow;
            hawala.PaidBy = GetCurrentUserId();

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync("UPDATE", "Hawalas", hawala.Id, "Pending", "Paid", GetCurrentUserId());

            return _mapper.Map<HawalaDto>(hawala);
        }

        public async Task<HawalaDto> CancelHawalaAsync(long id, string cancelReason)
        {
            var hawala = await _context.Hawalas.FindAsync(id);
            if (hawala == null)
                throw new KeyNotFoundException($"حواله با شناسه {id} یافت نشد.");

            if (hawala.Status == "Paid")
                throw new InvalidOperationException("حواله پرداخت شده قابل لغو نیست.");

            hawala.Status = "Cancel";
            hawala.CancelledAt = DateTime.UtcNow;
            hawala.CancelledBy = GetCurrentUserId();
            hawala.CancelReason = cancelReason;

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync("CANCEL", "Hawalas", hawala.Id, "Pending", "Cancel", GetCurrentUserId());

            return _mapper.Map<HawalaDto>(hawala);
        }

        // ===== منطق دفتر کل =====

        private async Task ProcessLedgerEntries(Hawala hawala, long? fromAccountId)
        {
            if (hawala.HawalaType == "HawalaSend")
                await ProcessHawalaSendLedgerAsync(hawala, fromAccountId);
            else if (hawala.HawalaType == "HawalaReceive")
                await ProcessHawalaReceiveLedgerAsync(hawala, fromAccountId);
            else
                await ProcessHawalaOtherLedgerAsync(hawala);
        }

        private async Task ProcessHawalaSendLedgerAsync(Hawala hawala, long? fromAccountId)
        {
            if (!fromAccountId.HasValue)
                throw new InvalidOperationException("برای حواله ارسالی، انتخاب حساب مبدأ الزامی است.");

            var fromAccount = await _context.Accounts.FindAsync(fromAccountId.Value);
            if (fromAccount == null)
                throw new InvalidOperationException("حساب مبدأ انتخاب شده معتبر نیست.");

            if (!hawala.CorrespondentId.HasValue)
                throw new InvalidOperationException("برای حواله ارسالی، انتخاب نماینده مقصد الزامی است.");

            var correspondentAccount = await _context.Accounts
                .FirstOrDefaultAsync(a => a.ReferenceType == "Correspondent" && a.ReferenceId == hawala.CorrespondentId);
            if (correspondentAccount == null)
                throw new InvalidOperationException("حساب نماینده مقصد یافت نشد.");

            var commissionAccount = await GetOrCreateCommissionAccountAsync();

            // ۱. حساب مبدأ بدهکار به مبلغ حواله
            await CreateLedgerEntry(hawala.Id, fromAccount.Id, hawala.FromCurrencyId, 0, hawala.FromAmount,
                $"حواله ارسالی {hawala.Id}: مبلغ حواله");

            // ۲. حساب مبدأ بدهکار به مبلغ کارمزد
            if (hawala.CommissionAmount > 0)
            {
                var commissionCurrencyId = hawala.CommissionCurrencyId ?? hawala.FromCurrencyId;
                await CreateLedgerEntry(hawala.Id, fromAccount.Id, commissionCurrencyId, 0, hawala.CommissionAmount.Value,
                    $"حواله ارسالی {hawala.Id}: کارمزد دریافتی از مشتری");
            }

            // ۳. حساب نماینده مقصد بستانکار به مبلغ حواله (با ارز مقصد)
            var toAmount = hawala.ToAmount ?? hawala.FromAmount;
            await CreateLedgerEntry(hawala.Id, correspondentAccount.Id, hawala.ToCurrencyId, toAmount, 0,
                $"حواله ارسالی {hawala.Id}: مبلغ قابل پرداخت به گیرنده");

            // ۴. حساب نماینده مقصد بستانکار به مبلغ کارمزد نمایندگی
            if (hawala.AgentCommissionAmount > 0)
            {
                var agentCommissionCurrencyId = hawala.AgentCommissionCurrencyId ?? hawala.ToCurrencyId;
                await CreateLedgerEntry(hawala.Id, correspondentAccount.Id, agentCommissionCurrencyId, hawala.AgentCommissionAmount.Value, 0,
                    $"حواله ارسالی {hawala.Id}: کارمزد نمایندگی");

                await CreateLedgerEntry(hawala.Id, commissionAccount.Id, agentCommissionCurrencyId, 0, hawala.AgentCommissionAmount.Value,
                    $"حواله ارسالی {hawala.Id}: درآمد کارمزد");
            }

            // ۵. حساب درآمد کارمزد بستانکار
            if (hawala.CommissionAmount > 0)
            {
                var commissionCurrencyId = hawala.CommissionCurrencyId ?? hawala.FromCurrencyId;
                await CreateLedgerEntry(hawala.Id, commissionAccount.Id, commissionCurrencyId, hawala.CommissionAmount.Value, 0,
                    $"حواله ارسالی {hawala.Id}: درآمد کارمزد");
            }
        }

        private async Task ProcessHawalaReceiveLedgerAsync(Hawala hawala, long? fromAccountId)
        {
            if (!fromAccountId.HasValue)
                throw new InvalidOperationException("برای حواله دریافتی، انتخاب حساب پرداخت‌کننده الزامی است.");

            var paidFromAccount = await _context.Accounts.FindAsync(fromAccountId.Value);
            if (paidFromAccount == null)
                throw new InvalidOperationException("حساب پرداخت‌کننده انتخاب شده معتبر نیست.");

            if (!hawala.CorrespondentId.HasValue)
                throw new InvalidOperationException("برای حواله دریافتی، انتخاب نماینده فرستنده الزامی است.");

            var correspondentAccount = await _context.Accounts
                .FirstOrDefaultAsync(a => a.ReferenceType == "Correspondent" && a.ReferenceId == hawala.CorrespondentId);
            if (correspondentAccount == null)
                throw new InvalidOperationException("حساب نماینده فرستنده یافت نشد.");

            var commissionAccount = await GetOrCreateCommissionAccountAsync();

            // ۱. نماینده فرستنده بدهکار به مبلغ حواله
            await CreateLedgerEntry(hawala.Id, correspondentAccount.Id, hawala.FromCurrencyId, 0, hawala.FromAmount,
                $"حواله دریافتی {hawala.Id}: بدهکار شدن نماینده فرستنده بابت اصل حواله");

            // ۲. حساب پرداخت‌کننده بستانکار به مبلغ حواله (با ارز مقصد)
            var toAmount = hawala.ToAmount ?? hawala.FromAmount;
            await CreateLedgerEntry(hawala.Id, paidFromAccount.Id, hawala.ToCurrencyId, toAmount, 0,
                $"حواله دریافتی {hawala.Id}: پرداخت اصل حواله از حساب انتخاب‌شده");

            // ۳. کارمزد دریافتی از نماینده
            if (hawala.CommissionAmount > 0)
            {
                var commissionCurrencyId = hawala.CommissionCurrencyId ?? hawala.FromCurrencyId;
                await CreateLedgerEntry(hawala.Id, correspondentAccount.Id, commissionCurrencyId, 0, hawala.CommissionAmount.Value,
                    $"حواله دریافتی {hawala.Id}: بدهکار شدن نماینده فرستنده بابت کمیشن");

                await CreateLedgerEntry(hawala.Id, commissionAccount.Id, commissionCurrencyId, hawala.CommissionAmount.Value, 0,
                    $"حواله دریافتی {hawala.Id}: درآمد کارمزد");
            }

            // ۴. سهم نماینده پرداخت‌کننده از کمیشن
            if (hawala.AgentCommissionAmount > 0)
            {
                var agentCommissionCurrencyId = hawala.AgentCommissionCurrencyId ?? hawala.CommissionCurrencyId ?? hawala.ToCurrencyId;
                await CreateLedgerEntry(hawala.Id, commissionAccount.Id, agentCommissionCurrencyId, 0, hawala.AgentCommissionAmount.Value,
                    $"حواله دریافتی {hawala.Id}: سهم نماینده پرداخت‌کننده از کمیشن");

                await CreateLedgerEntry(hawala.Id, paidFromAccount.Id, agentCommissionCurrencyId, hawala.AgentCommissionAmount.Value, 0,
                    $"حواله دریافتی {hawala.Id}: کمیشن قابل پرداخت به حساب انتخاب‌شده");
            }
        }

        private async Task ProcessHawalaOtherLedgerAsync(Hawala hawala)
        {
            var defaultAccount = await _context.Accounts
                .FirstOrDefaultAsync(a => a.AccountType == "Cash" && a.ReferenceId == null);
            if (defaultAccount == null)
                throw new InvalidOperationException("حساب پیش‌فرض برای حواله متفرقه یافت نشد.");

            await CreateLedgerEntry(hawala.Id, defaultAccount.Id, hawala.FromCurrencyId, hawala.FromAmount, 0,
                $"حواله متفرقه {hawala.Id}: مبلغ {hawala.FromAmount} {hawala.FromCurrency?.Code}");
        }

        private async Task CreateLedgerEntry(long hawalaId, long accountId, long currencyId, decimal talabKar, decimal badehKar, string description)
        {
            var ledgerEntryDto = new CreateLedgerEntryDto
            {
                TransactionId = null,
                AccountId = accountId,
                CurrencyId = currencyId,
                TalabKar = talabKar,
                BadehKar = badehKar,
                Description = description
            };
            await _ledgerService.CreateLedgerEntryAsync(ledgerEntryDto);
        }

        private async Task<Account> GetOrCreateCommissionAccountAsync()
        {
            const string accountCode = "3001";
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
            if (account != null) return account;

            var newAccount = new Account
            {
                AccountCode = accountCode,
                AccountName = "کارمزد حواله",
                AccountType = "Income",
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            };
            await _context.Accounts.AddAsync(newAccount);
            await _context.SaveChangesAsync();
            return newAccount;
        }

        private void ValidateCreateHawala(CreateHawalaDto dto)
        {
            if (string.IsNullOrEmpty(dto.HawalaType))
                throw new InvalidOperationException("نوع حواله الزامی است.");
            if (dto.FromCurrencyId == 0)
                throw new InvalidOperationException("ارز مبدأ الزامی است.");
            if (dto.FromAmount <= 0)
                throw new InvalidOperationException("مبلغ باید بزرگتر از صفر باشد.");
            if (dto.ToCurrencyId == 0)
                throw new InvalidOperationException("ارز مقصد الزامی است.");
            if (dto.HawalaType == "HawalaSend" && dto.CorrespondentId == null)
                throw new InvalidOperationException("برای حواله ارسال، نمایندگی مقصد الزامی است.");
            if (dto.HawalaType == "HawalaReceive" && dto.CorrespondentId == null)
                throw new InvalidOperationException("برای حواله دریافت، نمایندگی فرستنده الزامی است.");
            if (dto.HawalaType == "HawalaReceive" && (dto.Number <= 0))
                throw new InvalidOperationException("برای حواله آمد، شماره (نمبر) الزامی است.");
        }

        private long GetCurrentUserId() => 1;
    }
}