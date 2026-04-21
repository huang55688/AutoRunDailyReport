using AutoRunDailyReport.Models;
using AutoRunDailyReport.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AutoRunDailyReport.Controllers
{
    public class MetaController : Controller
    {
        private readonly MetaRepository _metaRepo;
        private readonly IpRepository _ipRepository;

        public MetaController(MetaRepository metaRepo, IpRepository ipRepository)
        {
            _metaRepo = metaRepo;
            _ipRepository = ipRepository;
        }

        public async Task<IActionResult> Index(string? search = null, bool futureDeadline = false, string? expandedMachineName = null)
        {
            IEnumerable<MesMachinesMetaDto> list;
            try
            {
                list = await _metaRepo.GetAllLinesWithMetaAsync(search, futureDeadline);
            }
            catch
            {
                list = Enumerable.Empty<MesMachinesMetaDto>();
                ViewBag.Warning = "資料載入失敗，可能是同步資料表尚未建立或目前資料庫無法連線。";
            }

            ViewBag.Search = search?.Trim();
            ViewBag.FutureDeadline = futureDeadline;
            ViewBag.ExpandedMachineName = expandedMachineName?.Trim();
            return View(list);
        }

        [HttpGet]
        public async Task<IActionResult> Edit([FromQuery] string machineName)
        {
            if (string.IsNullOrWhiteSpace(machineName))
            {
                return BadRequest();
            }

            var meta = await _metaRepo.GetByMachineNameAsync(machineName)
                       ?? new MesMachinesMetaDto { MESMachineName = machineName };
            return View(meta);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(MesMachinesMetaDto model)
        {
            if (string.IsNullOrWhiteSpace(model.MESMachineName))
            {
                return BadRequest();
            }

            await _metaRepo.UpsertManualFieldsAsync(model);
            TempData["Success"] = $"機台 {model.MESMachineName} 已成功儲存。";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSyncDetail(SaveMetaSyncDetailRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.LineId) ||
                string.IsNullOrWhiteSpace(request.EquipmentId))
            {
                TempData["Error"] = "MESMachineNo_String 與 MESSubEQNo_String 不可為空。";
                return RedirectToAction(nameof(Index), new
                {
                    search = request.Search,
                    futureDeadline = request.FutureDeadline,
                    expandedMachineName = request.MachineName
                });
            }

            try
            {
                await _ipRepository.UpdateIpAsync(new SaveIpRequest
                {
                    LineId = request.LineId.Trim(),
                    EquipmentId = request.EquipmentId.Trim(),
                    Ip = request.Ip,
                    Device = request.Device
                });

                TempData["Success"] = $"已儲存 {request.LineId} / {request.EquipmentId} 的 IP 與 Device。";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"儲存同步資訊失敗。{ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}";
            }

            return RedirectToAction(nameof(Index), new
            {
                search = request.Search,
                futureDeadline = request.FutureDeadline,
                expandedMachineName = request.MachineName
            });
        }
    }
}
