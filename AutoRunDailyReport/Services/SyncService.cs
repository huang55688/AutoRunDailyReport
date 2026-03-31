using AutoRunDailyReport.Repositories;

namespace AutoRunDailyReport.Services
{
    public class SyncService
    {
        private readonly MesRepository _source;
        private readonly TargetRepository _target;
        private readonly MetaRepository _meta;
        private readonly SyncStatusTracker _tracker;
        private readonly ILogger<SyncService> _logger;

        public SyncService(MesRepository source, TargetRepository target,
            MetaRepository meta, SyncStatusTracker tracker, ILogger<SyncService> logger)
        {
            _source = source;
            _target = target;
            _meta = meta;
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
                // Step 1：同步機台資料到目標 DB
                var data = await _source.GetAllMesMachinesAsync();
                var list = data.ToList();
                await _target.UpsertMesMachinesAsync(list);
                _logger.LogInformation("機台資料同步完成，共 {Count} 筆。", list.Count);

                // Step 2：重新計算各 Line 的 FirstADeadline
                await _meta.RecalculateDeadlinesAsync();
                _logger.LogInformation("FirstADeadline 重新計算完成。");

                var message = $"成功同步 {list.Count} 筆資料，已重算各 Line Deadline";
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
