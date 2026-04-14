using AutoRunDailyReport.Models;
using AutoRunDailyReport.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AutoRunDailyReport.Controllers
{
    public class PortSettingController : Controller
    {
        private readonly PortSettingRepository _repository;
        private readonly ILogger<PortSettingController> _logger;

        public PortSettingController(
            PortSettingRepository repository,
            ILogger<PortSettingController> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            const int reminderDays = 14;

            var model = new PortSettingPageViewModel
            {
                ReminderDays = reminderDays
            };

            try
            {
                var result = await _repository.GetUpcomingPortSettingsAsync(reminderDays);
                model.Items = result.Items.ToList();
                model.PortDatabaseName = result.PortDatabaseName;

                if (string.IsNullOrWhiteSpace(model.PortDatabaseName))
                {
                    model.Warnings.Add("找不到包含 dbo.PDL_MachineDtl_Port 的資料庫，因此目前只顯示 Deadline 與機台資料。你可以先測試 /api/portsetting/test-connection 來確認是哪一步失敗。");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load port settings page.");
                model.Warnings.Add(BuildDetailedErrorMessage("port設置資料載入失敗。", ex));
            }

            return View(model);
        }

        [HttpGet("/api/portsetting/test-connection")]
        public async Task<IActionResult> TestConnection()
        {
            const int reminderDays = 14;

            try
            {
                var result = await _repository.TestConnectionsAsync(reminderDays);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to test port setting connections.");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    success = false,
                    message = "測試 PortSetting 連線時發生未預期錯誤。",
                    detail = BuildDetailedErrorMessage("例外詳情：", ex)
                });
            }
        }

        private static string BuildDetailedErrorMessage(string prefix, Exception ex)
        {
            var baseException = ex.GetBaseException();
            return $"{prefix}{baseException.GetType().Name}: {baseException.Message}";
        }
    }
}
