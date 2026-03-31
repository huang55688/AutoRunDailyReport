using Microsoft.AspNetCore.Mvc;
using AutoRunDailyReport.Models;
using AutoRunDailyReport.Repositories;

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
                // MesMachinesSync 尚未建立（同步尚未執行過）
                list = Enumerable.Empty<MesMachinesMetaDto>();
                ViewBag.Warning = "尚無同步資料，請先執行一次同步。";
            }
            return View(list);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest();

            var meta = await _metaRepo.GetByLineAsync(id)
                       ?? new MesMachinesMetaDto { Line = id };
            return View(meta);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(MesMachinesMetaDto model)
        {
            if (string.IsNullOrWhiteSpace(model.Line))
                return BadRequest();

            await _metaRepo.UpsertManualFieldsAsync(model);
            TempData["Success"] = $"Line「{model.Line}」已儲存。";
            return RedirectToAction(nameof(Index));
        }
    }
}
