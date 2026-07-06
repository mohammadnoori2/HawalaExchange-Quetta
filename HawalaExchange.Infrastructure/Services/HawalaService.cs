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

                await _context.Hawalas.AddAsync(hawala);
                await _context.SaveChangesAsync();

                // ✅ ارسال FromAccountId به متد ثبت لیجر
                await ProcessLedgerEntries(hawala, dto.FromAccountId);

                await _auditLogService.LogAsync("CREATE", "Hawalas", hawala.Id, null, $"حواله {hawala.HawalaType} ایجاد شد", GetCurrentUserId());

                await transaction.CommitAsync();

                return await GetHawalaDtoAsync(hawala.Id);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task ProcessLedgerEntries(Hawala hawala, long? fromAccountId)
        {
            if (hawala.HawalaType == "HawalaSend")
            {
                await ProcessHawalaSendLedgerAsync(hawala, fromAccountId);
            }
            else if (hawala.HawalaType == "HawalaReceive")
            {
                await ProcessHawalaReceiveLedgerAsync(hawala);
            }
            else
            {
                // حواله متفرقه
                await ProcessHawalaOtherLedgerAsync(hawala);
            }
        }
        private async Task ProcessHawalaReceiveLedgerAsync(Hawala hawala)
        {
            // ۱. دریافت حساب نماینده فرستنده (از CorrespondentId)
            if (!hawala.CorrespondentId.HasValue)
                throw new InvalidOperationException("برای حواله دریافتی، انتخاب نماینده فرستنده الزامی است.");

            var fromAccount = await _context.Accounts
                .FirstOrDefaultAsync(a => a.ReferenceType == "Correspondent" && a.ReferenceId == hawala.CorrespondentId);
            if (fromAccount == null)
                throw new InvalidOperationException("حساب نماینده فرستنده یافت نشد.");

            // ۲. دریافت حساب گیرنده (صندوق پیش‌فرض یا مشتری)
            // در صورت نیاز می‌توانید یک فیلد ToAccountId نیز اضافه کنید، اما فعلاً صندوق را در نظر می‌گیریم.
            var toAccount = await _context.Accounts
                .FirstOrDefaultAsync(a => a.AccountType == "Cash" && a.ReferenceId == null);
            if (toAccount == null)
                throw new InvalidOperationException("حساب گیرنده (صندوق) یافت نشد.");

            // ۳. دریافت حساب‌های درآمد و هزینه
            var commissionAccount = await GetOrCreateCommissionAccountAsync();
            var expenseAccount = await GetOrCreateExpenseAccountAsync();

            // ۴. ثبت ورودی‌های لیجر

            // ۴-۱. نماینده فرستنده بدهکار به مبلغ حواله (با ارز مبدأ)
            await CreateLedgerEntry(
                hawala.Id,
                fromAccount.Id,
                hawala.FromCurrencyId,
                hawala.FromAmount,
                0,
                $"حواله دریافتی {hawala.Id}: مبلغ حواله از فرستنده"
            );

            // ۴-۲. نماینده فرستنده بدهکار به مبلغ کارمزد دریافتی از نماینده (با ارز کارمزد)
            if (hawala.CommissionAmount > 0)
            {
                var commissionCurrencyId = hawala.CommissionCurrencyId ?? hawala.FromCurrencyId;
                await CreateLedgerEntry(
                    hawala.Id,
                    fromAccount.Id,
                    commissionCurrencyId,
                    hawala.CommissionAmount.Value,
                    0,
                    $"حواله دریافتی {hawala.Id}: کارمزد دریافتی از نماینده"
                );
            }

            // ۴-۳. حساب گیرنده (صندوق) بستانکار به مبلغ حواله (با ارز مقصد)
            var toAmount = hawala.ToAmount ?? hawala.FromAmount;
            await CreateLedgerEntry(
                hawala.Id,
                toAccount.Id,
                hawala.ToCurrencyId,
                0,
                toAmount,
                $"حواله دریافتی {hawala.Id}: مبلغ قابل پرداخت به گیرنده"
            );

            // ۴-۴. حساب گیرنده بستانکار به مبلغ کارمزد پرداختی به گیرنده/نماینده دیگر (با ارز کارمزد پرداختی)
            if (hawala.AgentCommissionAmount > 0)
            {
                var agentCommissionCurrencyId = hawala.AgentCommissionCurrencyId ?? hawala.ToCurrencyId;
                await CreateLedgerEntry(
                    hawala.Id,
                    toAccount.Id,
                    agentCommissionCurrencyId,
                    0,
                    hawala.AgentCommissionAmount.Value,
                    $"حواله دریافتی {hawala.Id}: کارمزد پرداختی به گیرنده"
                );
            }

            // ۴-۵. حساب درآمد کارمزد بستانکار (با ارز کارمزد دریافتی از نماینده)
            if (hawala.CommissionAmount > 0)
            {
                var commissionCurrencyId = hawala.CommissionCurrencyId ?? hawala.FromCurrencyId;
                await CreateLedgerEntry(
                    hawala.Id,
                    commissionAccount.Id,
                    commissionCurrencyId,
                    0,
                    hawala.CommissionAmount.Value,
                    $"حواله دریافتی {hawala.Id}: درآمد کارمزد"
                );
            }

            // ۴-۶. حساب هزینه کارمزد پرداختی بدهکار (با ارز کارمزد پرداختی)
            if (hawala.AgentCommissionAmount > 0)
            {
                var agentCommissionCurrencyId = hawala.AgentCommissionCurrencyId ?? hawala.ToCurrencyId;
                await CreateLedgerEntry(
                    hawala.Id,
                    expenseAccount.Id,
                    agentCommissionCurrencyId,
                    hawala.AgentCommissionAmount.Value,
                    0,
                    $"حواله دریافتی {hawala.Id}: هزینه کارمزد پرداختی"
                );
            }
        }
        private async Task ProcessHawalaOtherLedgerAsync(Hawala hawala)
        {
            // برای حواله متفرقه، یک ورودی ساده به حساب صندوق یا حساب پیش‌فرض ثبت می‌کنیم.
            var defaultAccount = await _context.Accounts
                .FirstOrDefaultAsync(a => a.AccountType == "Cash" && a.ReferenceId == null);
            if (defaultAccount == null)
                throw new InvalidOperationException("حساب پیش‌فرض برای حواله متفرقه یافت نشد.");

            await CreateLedgerEntry(
                hawala.Id,
                defaultAccount.Id,
                hawala.FromCurrencyId,
                hawala.FromAmount,
                0,
                $"حواله متفرقه {hawala.Id}: مبلغ {hawala.FromAmount} {hawala.FromCurrency?.Code}"
            );
        }
        private async Task ProcessHawalaSendLedgerAsync(Hawala hawala, long? fromAccountId)
        {
            // ۱. دریافت حساب مبدأ از پارامتر ارسال‌شده (بدون ذخیره در دیتابیس)
            if (!fromAccountId.HasValue)
                throw new InvalidOperationException("برای حواله ارسالی، انتخاب حساب مبدأ الزامی است.");

            var fromAccount = await _context.Accounts.FindAsync(fromAccountId.Value);
            if (fromAccount == null)
                throw new InvalidOperationException("حساب مبدأ انتخاب شده معتبر نیست.");

            // ۲. دریافت حساب نماینده مقصد
            if (!hawala.CorrespondentId.HasValue)
                throw new InvalidOperationException("برای حواله ارسالی، انتخاب نماینده مقصد الزامی است.");

            var correspondentAccount = await _context.Accounts
                .FirstOrDefaultAsync(a => a.ReferenceType == "Correspondent" && a.ReferenceId == hawala.CorrespondentId);
            if (correspondentAccount == null)
                throw new InvalidOperationException("حساب نماینده مقصد یافت نشد.");

            // ۳. دریافت حساب‌های درآمد و هزینه
            var commissionAccount = await GetOrCreateCommissionAccountAsync();
            var expenseAccount = await GetOrCreateExpenseAccountAsync();

            // ۴. ثبت ورودی‌های لیجر

            // ۴-۱. حساب مبدأ بدهکار به مبلغ حواله (با ارز مبدأ)
            await CreateLedgerEntry(
                hawala.Id,
                fromAccount.Id,
                hawala.FromCurrencyId,
                0,
                hawala.FromAmount,
                $"حواله ارسالی {hawala.Id}: مبلغ حواله به {hawala.FromCurrency?.Code}"
            );

            // ۴-۲. حساب مبدأ بدهکار به مبلغ کارمزد (در صورت وجود، با ارز کارمزد)
            if (hawala.CommissionAmount > 0)
            {
                var commissionCurrencyId = hawala.CommissionCurrencyId ?? hawala.FromCurrencyId;
                await CreateLedgerEntry(
                    hawala.Id,
                    fromAccount.Id,
                    commissionCurrencyId,
                    0,
                    hawala.CommissionAmount.Value,
                    $"حواله ارسالی {hawala.Id}: کارمزد دریافتی از مشتری"
                );
            }

            // ۴-۳. حساب نماینده مقصد بستانکار به مبلغ حواله (با ارز مقصد)
            var toAmount = hawala.ToAmount ?? hawala.FromAmount;
            await CreateLedgerEntry(
                hawala.Id,
                correspondentAccount.Id,
                hawala.ToCurrencyId,
                toAmount,
                0,
                $"حواله ارسالی {hawala.Id}: مبلغ قابل پرداخت به گیرنده"
            );

            // ۴-۴. حساب نماینده مقصد بستانکار به مبلغ کارمزد نمایندگی (در صورت وجود، با ارز کارمزد نمایندگی)
            if (hawala.AgentCommissionAmount > 0)
            {
                var agentCommissionCurrencyId = hawala.AgentCommissionCurrencyId ?? hawala.ToCurrencyId;
                await CreateLedgerEntry(
                    hawala.Id,
                    correspondentAccount.Id,
                    agentCommissionCurrencyId,
                    hawala.AgentCommissionAmount.Value,
                    0,
                    $"حواله ارسالی {hawala.Id}: کارمزد نمایندگی"
                );
            }

            // ۴-۵. حساب درآمد کارمزد بستانکار (با ارز کارمزد)
            if (hawala.CommissionAmount > 0)
            {
                var commissionCurrencyId = hawala.CommissionCurrencyId ?? hawala.FromCurrencyId;
                await CreateLedgerEntry(
                    hawala.Id,
                    commissionAccount.Id,
                    commissionCurrencyId,
                    hawala.CommissionAmount.Value,
                    0,
                    $"حواله ارسالی {hawala.Id}: درآمد کارمزد"
                );
            }

            // ۴-۶. حساب هزینه کارمزد نمایندگی بدهکار (با ارز کارمزد نمایندگی)
            if (hawala.AgentCommissionAmount > 0)
            {
                var agentCommissionCurrencyId = hawala.AgentCommissionCurrencyId ?? hawala.ToCurrencyId;
                await CreateLedgerEntry(
                    hawala.Id,
                   commissionAccount.Id,
                    agentCommissionCurrencyId,
                    0,
                    hawala.AgentCommissionAmount.Value,
                    $"حواله ارسالی {hawala.Id}: هزینه کارمزد نمایندگی"
                );
            }
        }

        private async Task CreateLedgerEntry(long hawalaId, long accountId, long currencyId, decimal talabKar, decimal badehKar, string description)
        {
            var ledgerEntryDto = new CreateLedgerEntryDto
            {
                TransactionId = null, // ✅ بدون ارجاع به Transaction
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

            // ✅ ابتدا بر اساس کد حساب جستجو کن
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
            if (account != null)
                return account;

            // اگر وجود نداشت، ایجاد کن
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

        private async Task<Account> GetOrCreateExpenseAccountAsync()
        {
            const string accountCode = "4100";

            // ✅ ابتدا بر اساس کد حساب جستجو کن
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
            if (account != null)
                return account;

            // اگر وجود نداشت، ایجاد کن
            var newAccount = new Account
            {
                AccountCode = accountCode,
                AccountName = "کارمزد نمایندگی",
                AccountType = "Expense",
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
        }

        private long GetCurrentUserId() => 1; // در پروژه واقعی از Claim دریافت کنید

        // ===== متدهای خواندن داده =====

        public async Task<HawalaDto?> GetHawalaByIdAsync(long id)
        {
            var hawala = await _context.Hawalas
                .Include(h => h.Correspondent)
                .Include(h => h.FromCurrency)
                .Include(h => h.ToCurrency)
                .Include(h => h.CommissionCurrency)
                .Include(h => h.AgentCommissionCurrency)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (hawala == null)
                return null;

            return await GetHawalaDtoAsync(hawala);
        }

        private async Task<HawalaDto> GetHawalaDtoAsync(long id)
        {
            var hawala = await _context.Hawalas
                .Include(h => h.Correspondent)
                .Include(h => h.FromCurrency)
                .Include(h => h.ToCurrency)
                .Include(h => h.CommissionCurrency)
                .Include(h => h.AgentCommissionCurrency)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (hawala == null)
                throw new KeyNotFoundException($"حواله با شناسه {id} یافت نشد.");

            return _mapper.Map<HawalaDto>(hawala);
        }

        private async Task<HawalaDto> GetHawalaDtoAsync(Hawala hawala)
        {
            return _mapper.Map<HawalaDto>(hawala);
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

            // فیلترها
            if (!string.IsNullOrEmpty(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim();
                query = query.Where(h =>
                    h.SenderName.Contains(term) ||
                    h.ReceiverName.Contains(term) ||
                    h.ReferenceNumber.Contains(term)
                );
            }

            if (!string.IsNullOrEmpty(filter.HawalaType))
                query = query.Where(h => h.HawalaType == filter.HawalaType);

            if (!string.IsNullOrEmpty(filter.Status))
                query = query.Where(h => h.Status == filter.Status);

            if (filter.CorrespondentId.HasValue)
                query = query.Where(h => h.CorrespondentId == filter.CorrespondentId);

            // مرتب‌سازی
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
            var dtoList = _mapper.Map<List<HawalaDto>>(items);

            return new HawalaListResultDto
            {
                Items = dtoList,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling((double)totalCount / (filter.PageSize > 0 ? filter.PageSize : totalCount))
            };
        }

        private Expression<Func<Hawala, object>> GetSortExpression(string column)
        {
            return column switch
            {
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

            // به‌روزرسانی فقط فیلدهای قابل ویرایش
            _mapper.Map(dto, hawala);

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "UPDATE",
                "Hawalas",
                hawala.Id,
                null,
                $"حواله با شناسه {hawala.Id} ویرایش شد",
                GetCurrentUserId()
            );

            return await GetHawalaDtoAsync(hawala.Id);
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

            await _auditLogService.LogAsync(
                "DELETE",
                "Hawalas",
                id,
                null,
                $"حواله با شناسه {id} حذف شد",
                GetCurrentUserId()
            );
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

            await _auditLogService.LogAsync(
                "UPDATE",
                "Hawalas",
                hawala.Id,
                "Pending",
                "Paid",
                GetCurrentUserId()
            );

            return await GetHawalaDtoAsync(hawala.Id);
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

            await _auditLogService.LogAsync(
                "CANCEL",
                "Hawalas",
                hawala.Id,
                "Pending",
                "Cancel",
                GetCurrentUserId()
            );

            return await GetHawalaDtoAsync(hawala.Id);
        }
    }
}