using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;

namespace HawalaExchange.Web.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/exports")]
    public class ExportController : ControllerBase
    {
        private readonly IExportService _exportService;

        public ExportController(IExportService exportService)
        {
            _exportService = exportService;
        }

        [HttpPost("customer-activities")]
        public async Task<IActionResult> ExportCustomerActivities([FromBody] ExportFilterDto filter, [FromQuery] ExportFormat format = ExportFormat.Excel)
        {
            try
            {
                var (content, contentType, fileName) = await _exportService.ExportCustomerActivitiesAsync(filter, format);
                return File(content, contentType, fileName);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
