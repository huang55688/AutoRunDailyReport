using System.Text.Json;
using AutoRunDailyReport.Models;
using AutoRunDailyReport.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AutoRunDailyReport.Controllers
{
    public class IpController : Controller
    {
        private const string HighlightedKeysTempDataKey = "Ip.HighlightedKeys";
        private const string CurrentLineIdTempDataKey = "Ip.CurrentLineId";

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
                model.HighlightedKeys = ReadHighlightedKeysFromTempData();
                model.CurrentLineId = TempData[CurrentLineIdTempDataKey]?.ToString() ?? string.Empty;

                model.Items = (await _ipRepository.GetAllAsync())
                    .OrderBy(item => model.HighlightedKeys.Contains(item.GetRowKey()) ? 0 : 1)
                    .ThenBy(item => item.LineId)
                    .ThenBy(item => item.EquipmentNo)
                    .ThenBy(item => item.EquipmentId)
                    .ToList();
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
        public async Task<IActionResult> ImportByLineId(string lineId)
        {
            if (string.IsNullOrWhiteSpace(lineId))
            {
                TempData["Error"] = "請先輸入 LineID。";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var normalizedLineId = lineId.Trim();
                var result = await _ipRepository.ImportByLineIdAsync(normalizedLineId);

                TempData[CurrentLineIdTempDataKey] = normalizedLineId;
                TempData[HighlightedKeysTempDataKey] = JsonSerializer.Serialize(result.ImportedKeys);

                TempData["Success"] = result.ImportedCount == 0
                    ? $"在 {result.SourceDatabase}.dbo.{result.SourceTable} 找不到 LineID = {result.LineId} 的資料。"
                    : $"已從 {result.SourceDatabase}.dbo.{result.SourceTable} 匯入 LineID = {result.LineId} 的 {result.ImportedCount} 筆資料到 dbo.ip，並已移到表格上方高亮顯示。";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import equipment by LineID {LineId}.", lineId);
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

        private HashSet<string> ReadHighlightedKeysFromTempData()
        {
            var json = TempData[HighlightedKeysTempDataKey]?.ToString();
            if (string.IsNullOrWhiteSpace(json))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var keys = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                return new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
