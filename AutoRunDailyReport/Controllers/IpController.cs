using AutoRunDailyReport.Models;
using AutoRunDailyReport.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AutoRunDailyReport.Controllers
{
    public class IpController : Controller
    {
        private readonly IpRepository _ipRepository;
        private readonly ILogger<IpController> _logger;

        public IpController(IpRepository ipRepository, ILogger<IpController> logger)
        {
            _ipRepository = ipRepository;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = new IpPageViewModel();

            try
            {
                model.Items = (await _ipRepository.GetAllAsync()).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load IP page.");
                model.Warnings.Add($"IP 頁面載入失敗。{ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportRecent()
        {
            try
            {
                var result = await _ipRepository.ImportRecentEquipmentAsync(14);
                TempData["Success"] = $"已從 {result.SourceDatabase}.dbo.{result.SourceTable} 匯入 {result.ImportedCount} 筆資料到 dbo.ip。";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import recent equipment into dbo.ip.");
                TempData["Error"] = $"匯入 dbo.ip 失敗。{ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveIp(SaveIpRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.LineId) ||
                string.IsNullOrWhiteSpace(request.EquipmentId) ||
                string.IsNullOrWhiteSpace(request.EquipmentNo))
            {
                TempData["Error"] = "LINEID、EQUIPMENTID、EQUIPMENTNO 不可為空白。";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                await _ipRepository.UpdateIpAsync(request);
                TempData["Success"] = $"已更新設備 {request.EquipmentNo} 的 IP。";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save IP for equipment {EquipmentNo}.", request.EquipmentNo);
                TempData["Error"] = $"儲存 IP 失敗。{ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
