using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IHawalaService
    {
        Task<AgentBillDto> PreviewAgentBillAsync(AgentBillCreateDto request);
        Task<AgentBillDto> CreateAgentBillAsync(AgentBillCreateDto request);
        Task<AgentBillDto> ExecuteAndCreateAgentBillAsync(AgentBillExecuteDto request);
        Task<AgentBillDto> CreateHistoricalAgentReceiptAsync(AgentBillHistoricalDto request);
        Task<AgentBillDto> GetAgentBillAsync(long id);
        Task<IReadOnlyList<AgentBillDto>> GetAgentBillsForHawalasAsync(long correspondentId, IReadOnlyCollection<long> ids);
        Task<IReadOnlyList<AccountDto>> GetAgentBillPaymentAccountsAsync();
        Task<PaginatedResult<AgentBillDto>> GetAgentBillsAsync(long correspondentId, int page = 1, int pageSize = 20);
        Task ConfirmAgentExecutionAsync(long correspondentId, IReadOnlyCollection<long> ids);
        Task PayAgentBillAsync(AgentBillPayDto request);
        Task CancelAgentBillAsync(long id);
        Task<HawalaDto> CreateHawalaAsync(CreateHawalaDto dto);
        Task<IReadOnlyList<HawalaDto>> CreateHawalasAsync(IReadOnlyCollection<CreateHawalaDto> items);
        Task<HawalaDto> UpdateHawalaAsync(long id, UpdateHawalaDto dto);
        Task<HawalaDto> ChangePaymentLocationAsync(long id, long paymentLocationId);
        Task<long> ResolvePaymentLocationAccountAsync(long? paymentLocationId);
        Task DeleteHawalaAsync(long id);
        Task<HawalaDto?> GetHawalaByIdAsync(long id);
        Task<HawalaListResultDto> GetHawalasAsync(HawalaFilterDto filter, CancellationToken cancellationToken = default);
        Task<HawalaStatisticsDto> GetStatisticsAsync(CancellationToken cancellationToken = default);
        Task<CorrespondentHawalaRangeResultDto> GetCorrespondentRangeAsync(CorrespondentHawalaRangeFilterDto filter);
       
        Task<HawalaDto> MarkAsPaidAsync(long id, long paidFromAccountId);
        Task<HawalaDto> MarkAsPaidAsync(long id, PayHawalaDto payment);
        Task<BulkHawalaPaymentPreviewDto> PreviewBulkPaymentAsync(IReadOnlyCollection<long> ids, long paidFromAccountId);
        Task<int> PayBulkAsync(BulkHawalaPaymentRequestDto request);
        Task<PaginatedResult<HawalaPaymentBatchDto>> GetPaymentHistoryAsync(int page = 1, int pageSize = 20);
        Task<HawalaPaymentBatchDto> GetPaymentBatchAsync(long id);
        Task<HawalaDto> AddAgentCommissionAsync(long id, AddHawalaAgentCommissionDto commission);
        Task<HawalaDto> UpdateAgentCommissionAsync(long id, UpdateHawalaAgentCommissionDto commission);
        Task<HawalaDto> CancelHawalaAsync(long id, CancelHawalaDto cancellation);
        Task<HawalaCancellationPreviewDto> GetCancellationPreviewAsync(long id);
        Task<HawalaDto> CompleteCancellationAsync(long id, HawalaCompensationRequestDto request);
        Task<long> GetNextNumberAsync(long correspondentId, string hawalaType);
    }
}
