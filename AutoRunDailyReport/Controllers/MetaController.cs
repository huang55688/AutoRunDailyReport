using AutoRunDailyReport.Models;
using AutoRunDailyReport.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AutoRunDailyReport.Controllers
{
    public class MetaController : Controller
    {
        private readonly MetaRepository _metaRepo;

        public MetaController(MetaRepository metaRepo)
        {
            _metaRepo = metaRepo;
        }

        public async Task<IActionResult> Index()
        {
            IEnumerable<MesMachinesMetaDto> list;
            try
            {
                list = await _metaRepo.GetAllLinesWithMetaAsync();
            }
            catch
            {
                list = Enumerable.Empty<MesMachinesMetaDto>();
                ViewBag.Warning = "資料載入失敗，可能是同步資料表尚未建立或目前資料庫無法連線。";
            }

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
    }
}