using HawalaExchange.Application.DTOs;
using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.Interfaces
{
    public interface ICapitalInvestmentService
    {
        Task CreateAsync(CreateCapitalInvestmentDto dto);
    }
}
