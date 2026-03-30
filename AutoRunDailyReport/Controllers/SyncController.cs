using Microsoft.AspNetCore.Mvc;
using AutoRunDailyReport.Services;

namespace AutoRunDailyReport.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SyncController : ControllerBase
    {
        private readonly SyncService _syncService;
        private readonly SyncStatusTracker _tracker;

        public SyncController(SyncService syncService, SyncStatusTracker tracker)
        {
            _syncService = syncService;
            _tracker = tracker;
        }

        /// <summary>取得目前同步狀態</summary>
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            return Ok(new
            {
                isRunning       = _tracker.IsRunning,
                lastRunTime     = _tracker.LastRunTime,
                lastRunSuccess  = _tracker.LastRunSuccess,
                lastRunMessage  = _tracker.LastRunMessage,
                lastRowsSynced  = _tracker.LastRowsSynced,
                intervalMinutes = _tracker.IntervalMinutes,
                nextRunTime     = _tracker.NextRunTime,
                recentLogs      = _tracker.RecentLogs
            });
        }

        /// <summary>手動觸發同步</summary>
        [HttpPost("run")]
        public IActionResult RunSync()
        {
            if (_tracker.IsRunning)
                return Conflict(new { message = "同步正在執行中，請稍後再試。" });

            // 非阻塞方式執行，立即回應
            _ = Task.Run(() => _syncService.RunAsync());

            return Accepted(new { message = "同步已開始執行，請稍後查看狀態。" });
        }

        /// <summary>設定自動同步間隔</summary>
        [HttpPost("interval")]
        public IActionResult SetInterval([FromBody] SetIntervalRequest request)
        {
            if (request.Minutes < 1 || request.Minutes > 1440)
                return BadRequest(new { message = "間隔必須介於 1 到 1440 分鐘之間。" });

            _tracker.IntervalMinutes = request.Minutes;
            _tracker.NextRunTime = DateTime.Now.AddMinutes(request.Minutes);

            return Ok(new
            {
                message         = $"已設定為每 {request.Minutes} 分鐘自動同步一次。",
                nextRunTime     = _tracker.NextRunTime,
                intervalMinutes = _tracker.IntervalMinutes
            });
        }
    }

    public record SetIntervalRequest(int Minutes);
}
