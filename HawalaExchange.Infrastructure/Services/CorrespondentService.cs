using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using YourNamespace.Data;
using YourNamespace.Entities;

namespace HawalaExchange.Infrastructure.Services
{
    public class CorrespondentService : ICorrespondentService
    {
        private readonly ApplicationDbContext _context;
        public CorrespondentService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<CorrespondentDtos>> GetAllAsync()
        {
            return await _context.Correspondents
                .AsNoTracking()
                .Where(c => !c.IsArchived)
                .Select(c => new CorrespondentDtos
                {
                    Id = c.Id,
                    Code = c.Code,
                    Name = c.Name,
                    Country = c.Country,
                    City = c.City,
                    PhoneNumber = c.PhoneNumber,
                    Address = c.Address,
                    IsArchived = c.IsArchived,
                    Remarks = c.Remarks,
                    CreatedAt = c.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<CorrespondentDtos?> GetByIdAsync(long id)
        {
            return await _context.Correspondents
                .AsNoTracking()
                .Where(c => c.Id == id && !c.IsArchived)
                .Select(c => new CorrespondentDtos
                {
                    Id = c.Id,
                    Code = c.Code,
                    Name = c.Name,
                    Country = c.Country,
                    City = c.City,
                    PhoneNumber = c.PhoneNumber,
                    Address = c.Address,
                    IsArchived = c.IsArchived,
                    Remarks = c.Remarks,
                    CreatedAt = c.CreatedAt
                })
                .FirstOrDefaultAsync();
        }
        public async Task<long> CreateAsync(CreateCorrespondentRequest request)
        {
            var existing = await _context.Correspondents
                .AnyAsync(c => c.Code == request.Code && !c.IsArchived);
            if (existing) 
            {
                throw new InvalidOperationException($"A correspondent with code '{request.Code}' already exists.");
            }
            var correspondent = new Correspondent
            {
                Code = request.Code,
                Name = request.Name,
                Country = request.Country,
                City = request.City,
                PhoneNumber = request.PhoneNumber,
                Address = request.Address,
                Remarks = request.Remarks,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.Correspondents.Add(correspondent);
            await _context.SaveChangesAsync();

            return correspondent.Id;
        }

        public async Task UpdateAsync(long id, UpdateCorrespondentRequest request)
        {
            var existing = await _context.Correspondents
                .FindAsync(id);
            if (existing == null || existing.IsArchived)
            {
                throw new InvalidOperationException($"Correspondent with ID '{id}' not found.");
            }

            existing.Name = request.Name;
            existing.Country = request.Country;
            existing.City = request.City;
            existing.PhoneNumber = request.PhoneNumber;
            existing.Address = request.Address;
            existing.Remarks = request.Remarks;

            await _context.SaveChangesAsync();
        }
        public async Task DeleteAsync(long id)
        {
            var existing = await _context.Correspondents
                .FindAsync(id);
            if (existing == null || existing.IsArchived)
            {
                throw new InvalidOperationException($"Correspondent with ID '{id}' not found.");
            }

            existing.IsArchived = true;
            await _context.SaveChangesAsync();
        }

        

        
    }
}
