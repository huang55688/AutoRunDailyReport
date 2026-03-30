using AutoRunDailyReport.Repositories;

namespace AutoRunDailyReport.Services
{
    public class SyncService
    {
        private readonly MesRepository _source;
        private readonly TargetRepository _target;
        private readonly SyncStatusTracker _tracker;
        private readonly ILogger<SyncService> _logger;

        public SyncService(MesRepository source, TargetRepository target,
            SyncStatusTracker tracker, ILogger<SyncService> logger)
        {
            _source = source;
            _target = target;
            _tracker = tracker;
            _logger = logger;
        }

        public async Task<bool> RunAsync()
        {
            if (_tracker.IsRunning)
            {
                _logger.LogWarning("同步已在執行中，略過此次呼叫。");
                return false;
            }

            _tracker.SetRunning(true);
            _logger.LogInformation("開始同步資料...");

            try
            {
                var data = await _source.GetAllMesMachinesAsync();
                var list = data.ToList();
                await _target.UpsertMesMachinesAsync(list);

                var message = $"成功同步 {list.Count} 筆資料";
                _logger.LogInformation(message);
                _tracker.RecordResult(true, message, list.Count);
                return true;
            }
            catch (Exception ex)
            {
                var message = $"同步失敗：{ex.Message}";
                _logger.LogError(ex, "資料同步發生錯誤");
                _tracker.RecordResult(false, message, 0);
                return false;
            }
        }
    }
}
