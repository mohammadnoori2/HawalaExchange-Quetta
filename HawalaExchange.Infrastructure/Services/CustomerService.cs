using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services
{
    public class CustomerService : BaseService<Customer, CustomerDto, CreateCustomerDto, UpdateCustomerDto>, ICustomerService
    {
        public CustomerService(ApplicationDbContext context, IMapper mapper)
            : base(context, mapper) { }

        public async Task<CustomerDto?> GetByCustomerCodeAsync(string customerCode)
        {
            var entity = await _dbSet.FirstOrDefaultAsync(c => c.CustomerCode == customerCode);
            return entity == null ? null : _mapper.Map<CustomerDto>(entity);
        }

        public async Task<IEnumerable<CustomerDto>> SearchAsync(string searchTerm)
        {
            var term = searchTerm.ToLower();
            var customers = await _dbSet
                .Where(c =>
                    c.FullName.ToLower().Contains(term) ||
                    (c.PhoneNumber != null && c.PhoneNumber.Contains(term)) ||
                    (c.TazkiraNumber != null && c.TazkiraNumber.Contains(term)) ||
                    c.CustomerCode.ToLower().Contains(term))
                .ToListAsync();
            return _mapper.Map<IEnumerable<CustomerDto>>(customers);
        }

        public async Task<IEnumerable<CustomerDto>> GetArchivedAsync()
        {
            var entities = await _dbSet.Where(c => c.IsArchived).ToListAsync();
            return _mapper.Map<IEnumerable<CustomerDto>>(entities);
        }

        public async Task<CustomerDto> ArchiveAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"Customer with ID {id} not found.");

            entity.IsArchived = true;
            await _context.SaveChangesAsync();
            return _mapper.Map<CustomerDto>(entity);
        }

        public async Task<CustomerDto> UnarchiveAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"Customer with ID {id} not found.");

            entity.IsArchived = false;
            await _context.SaveChangesAsync();
            return _mapper.Map<CustomerDto>(entity);
        }

        protected override async Task ValidateCreateAsync(Customer entity, CreateCustomerDto dto)
        {
            entity.CustomerCode = await GenerateCustomerCodeAsync();
        }

        private async Task<string> GenerateCustomerCodeAsync()
        {
            var count = await _dbSet.CountAsync() + 1;
            return $"CUST-{DateTime.Now:yyyyMMdd}-{count:D4}";
        }
    }
}