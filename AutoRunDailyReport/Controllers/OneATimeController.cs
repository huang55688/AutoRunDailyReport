using AutoRunDailyReport.Models;
using AutoRunDailyReport.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AutoRunDailyReport.Controllers
{
    public class OneATimeController : Controller
    {
        private readonly MetaRepository _metaRepository;
        private readonly OneATimeRepository _oneATimeRepository;
        private readonly NoticeRepository _noticeRepository;
        private readonly ILogger<OneATimeController> _logger;

        public OneATimeController(
            MetaRepository metaRepository,
            OneATimeRepository oneATimeRepository,
            NoticeRepository noticeRepository,
            ILogger<OneATimeController> logger)
        {
            _metaRepository = metaRepository;
            _oneATimeRepository = oneATimeRepository;
            _noticeRepository = noticeRepository;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(bool edit = false, bool progressOnly = false)
        {
            var model = await BuildPageViewModelAsync(edit, progressOnly);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(OneATimeSaveRequest request)
        {
            if (request.Days.Count == 0)
            {
                TempData["Error"] = "沒有可儲存的時程資料。";
                return RedirectToAction(nameof(Index), new { edit = true });
            }

            var rangeStart = request.Days.Min(x => x.ScheduleDate.Date);
            var rangeEnd = request.Days.Max(x => x.ScheduleDate.Date);

            var entries = request.Days
                .SelectMany(day => day.Entries.Select((entry, index) => new OneATimeEntryDto
                {
                    ScheduleDate = day.ScheduleDate.Date,
                    DisplayOrder = entry.DisplayOrder > 0 ? entry.DisplayOrder : index + 1,
                    TestItem = (entry.TestItem ?? string.Empty).Trim(),
                    ScheduledTime = null,
                    Progress = Math.Clamp(entry.Progress, 0, 100)
                }))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.TestItem))
                .OrderBy(entry => entry.ScheduleDate)
                .ThenBy(entry => entry.DisplayOrder)
                .ToList();

            try
            {
                await _oneATimeRepository.ReplaceEntriesInRangeAsync(rangeStart, rangeEnd, entries);
                TempData["Success"] = $"已儲存到 [dbo].[1ATime]，共 {entries.Count} 筆資料。";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save 1A schedule.");
                TempData["Error"] = "儲存時程失敗，請稍後再試。";
            }

            return RedirectToAction(nameof(Index), new { edit = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HideReminder(string line, DateTime oneADeadline)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                TempData["Error"] = "提醒項目的 Line 不可為空白。";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                await _metaRepository.HideReminderAsync(line.Trim(), oneADeadline.Date);
                TempData["Success"] = $"已將 {line} 的提醒隱藏。";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to hide reminder for line {Line}.", line);
                TempData["Error"] = "更新提醒狀態失敗，請稍後再試。";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddNotice(string noticeText)
        {
            if (string.IsNullOrWhiteSpace(noticeText))
            {
                TempData["Error"] = "Notice 內容不可為空白。";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                await _noticeRepository.AddAsync(noticeText.Trim());
                TempData["Success"] = "Notice 已新增。";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add notice.");
                TempData["Error"] = "新增 Notice 失敗，請稍後再試。";
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<OneATimePageViewModel> BuildPageViewModelAsync(bool isEditMode, bool isProgressMode)
        {
            var model = new OneATimePageViewModel
            {
                IsEditMode = isEditMode,
                IsProgressMode = isProgressMode
            };

            var today = DateTime.Today;
            var rangeStart = GetMonday(today.AddDays(-5));
            var rangeEnd = GetFriday(today.AddDays(40));

            try
            {
                model.Reminders = (await _metaRepository.GetUpcomingRemindersAsync(14)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load 1A reminders.");
                model.Warnings.Add("提醒區載入失敗，暫時無法顯示 1A Deadline 資料。");
            }

            IReadOnlyDictionary<DateTime, List<OneATimeEntryDto>> entriesByDate =
                new Dictionary<DateTime, List<OneATimeEntryDto>>();

            try
            {
                entriesByDate = (await _oneATimeRepository.GetEntriesInRangeAsync(rangeStart, rangeEnd))
                    .GroupBy(entry => entry.ScheduleDate.Date)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .OrderBy(entry => entry.DisplayOrder)
                            .ThenBy(entry => entry.Id)
                            .ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load 1A schedule.");
                model.Warnings.Add("時程表載入失敗，目前先顯示空白內容。");
            }

            try
            {
                var notices = await _noticeRepository.GetAllAsync();
                model.Notices = notices
                    .Select((notice, index) => new NoticeBoardItemViewModel
                    {
                        Tag = $"Notice.{index + 1}",
                        Text = notice
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load notice board.");
                model.Warnings.Add("Notice 載入失敗，目前無法顯示注意事項。");
            }

            for (var date = rangeStart; date <= rangeEnd; date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                entriesByDate.TryGetValue(date.Date, out var entries);

                model.Days.Add(new OneATimeDayViewModel
                {
                    Date = date,
                    IsToday = date.Date == today,
                    Entries = entries ?? new List<OneATimeEntryDto>()
                });
            }

            return model;
        }

        private static DateTime GetMonday(DateTime date)
        {
            var day = (int)date.DayOfWeek;
            var diff = day == 0 ? -6 : 1 - day;
            return date.Date.AddDays(diff);
        }

        private static DateTime GetFriday(DateTime date)
        {
            return GetMonday(date).AddDays(4);
        }
    }
}
