using System.Text.Json;
using AutoRunDailyReport.Models;
using AutoRunDailyReport.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AutoRunDailyReport.Controllers
{
    public class IpController : Controller
    {
        private const string HighlightedKeysTempDataKey = "Ip.HighlightedKeys";

        private readonly IpRepository _ipRepository;
        private readonly ILogger<IpController> _logger;

        public IpController(IpRepository ipRepository, ILogger<IpController> logger)
        {
            _ipRepository = ipRepository;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? lineId = null)
        {
            var model = new IpPageViewModel
            {
                CurrentLineId = lineId?.Trim() ?? string.Empty
            };

            try
            {
                model.HighlightedKeys = ReadHighlightedKeysFromTempData();

                if (!string.IsNullOrWhiteSpace(model.CurrentLineId))
                {
                    model.SearchCandidates = (await _ipRepository.SearchCandidatesAsync(model.CurrentLineId)).ToList();
                }

                model.Items = (await _ipRepository.GetAllAsync())
                    .OrderBy(item => model.HighlightedKeys.Contains(item.GetRowKey()) ? 0 : 1)
                    .ThenBy(item => item.LineId)
                    .ThenBy(item => item.EquipmentId)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load IP page.");
                model.Warnings.Add($"IP 設置資料載入失敗。{ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SearchCandidates(string lineId)
        {
            if (string.IsNullOrWhiteSpace(lineId))
            {
                TempData["Error"] = "請先輸入 LINEID。";
                return RedirectToAction(nameof(Index));
            }

            return RedirectToAction(nameof(Index), new
            {
                lineId = lineId.Trim()
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportByLineId(string lineId)
        {
            if (string.IsNullOrWhiteSpace(lineId))
            {
                TempData["Error"] = "請先選擇要匯入的 LINEID。";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var normalizedLineId = lineId.Trim();
                var result = await _ipRepository.ImportByLineIdAsync(normalizedLineId);

                TempData[HighlightedKeysTempDataKey] = JsonSerializer.Serialize(result.ImportedKeys);
                TempData["Success"] = result.ImportedCount == 0
                    ? $"在 {result.SourceDatabase}.dbo.{result.SourceTable} 找不到 LINEID = {result.LineId} 且 MESMachineNo_String 為 SKL% 的資料。"
                    : $"已從 {result.SourceDatabase}.dbo.{result.SourceTable} 匯入 {result.ImportedCount} 筆資料到 dbo.ip，並將這次匯入的設備排到表格最上方。";

                return RedirectToAction(nameof(Index), new
                {
                    lineId = normalizedLineId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import equipment by LINEID {LineId}.", lineId);
                TempData["Error"] = $"匯入 dbo.ip 失敗。{ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}";
                return RedirectToAction(nameof(Index), new
                {
                    lineId = lineId.Trim()
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveIp(SaveIpRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.LineId) ||
                string.IsNullOrWhiteSpace(request.EquipmentId))
            {
                TempData["Error"] = "LINEID 與 EQUIPMENTID 不可為空。";
                return RedirectToAction(nameof(Index), new
                {
                    lineId = request.SearchLineId
                });
            }

            try
            {
                await _ipRepository.UpdateIpAsync(request);
                TempData["Success"] = $"已儲存 {request.LineId} / {request.EquipmentId} 的 IP。";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save IP for {LineId}/{EquipmentId}.", request.LineId, request.EquipmentId);
                TempData["Error"] = $"儲存 IP 失敗。{ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}";
            }

            return RedirectToAction(nameof(Index), new
            {
                lineId = request.SearchLineId
            });
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
