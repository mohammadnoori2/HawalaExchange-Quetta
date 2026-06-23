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
    public class CustomerService : ICustomerService
    {
        private readonly ApplicationDbContext _context;

        public CustomerService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<CustomerDto>> GetAllAsync()
        {
            return await _context.Customers
                .AsNoTracking()
                .OrderBy(x => x.FullName)
                .Select(x => new CustomerDto
                {
                    Id = x.Id,
                    CustomerCode = x.CustomerCode,
                    FullName = x.FullName,
                    PhoneNumber = x.PhoneNumber,
                    TazkiraNumber = x.TazkiraNumber,
                    Address = x.Address,
                    Remarks = x.Remarks,
                    IsArchived = x.IsArchived
                })
                .ToListAsync();
        }

        public async Task<CustomerDto?> GetByIdAsync(long id)
        {
            return await _context.Customers
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new CustomerDto
                {
                    Id = x.Id,
                    CustomerCode = x.CustomerCode,
                    FullName = x.FullName,
                    PhoneNumber = x.PhoneNumber,
                    TazkiraNumber = x.TazkiraNumber,
                    Address = x.Address,
                    Remarks = x.Remarks,
                    IsArchived = x.IsArchived
                })
                .FirstOrDefaultAsync();
        }

        public async Task<long> CreateAsync(CreateCustomerRequest request)
        {
            var exists = await _context.Customers
                .AnyAsync(x => x.CustomerCode == request.CustomerCode);

            if (exists)
            {
                throw new InvalidOperationException("Customer code already exists.");
            }

            var customer = new Customer
            {
                CustomerCode = request.CustomerCode.Trim(),
                FullName = request.FullName.Trim(),
                PhoneNumber = request.PhoneNumber,
                TazkiraNumber = request.TazkiraNumber,
                Address = request.Address,
                Remarks = request.Remarks,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();

            return customer.Id;
        }

        public async Task UpdateAsync(long id, UpdateCustomerRequest request)
        {
            var customer = await _context.Customers.FindAsync(id);

            if (customer == null)
            {
                throw new InvalidOperationException("Customer not found.");
            }

            customer.FullName = request.FullName.Trim();
            customer.PhoneNumber = request.PhoneNumber;
            customer.TazkiraNumber = request.TazkiraNumber;
            customer.Address = request.Address;
            customer.Remarks = request.Remarks;

            await _context.SaveChangesAsync();
        }

        public async Task ArchiveAsync(long id)
        {
            var customer = await _context.Customers.FindAsync(id);

            if (customer == null)
            {
                throw new InvalidOperationException("Customer not found.");
            }

            customer.IsArchived = true;

            await _context.SaveChangesAsync();
        }
    }
    }
