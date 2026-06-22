using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Domain.Enums
{
    public enum TransactionType
    {
        Exchange,
        HawalaSend,
        HawalaReceive,
        Transfer,
        Expense,
        Adjustment
    }
}
