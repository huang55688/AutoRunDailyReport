namespace AutoRunDailyReport.Services
{
    public class BackgroundSyncService : BackgroundService
    {
        private readonly SyncService _syncService;
        private readonly SyncStatusTracker _tracker;
        private readonly ILogger<BackgroundSyncService> _logger;

        public BackgroundSyncService(SyncService syncService,
            SyncStatusTracker tracker, ILogger<BackgroundSyncService> logger)
        {
            _syncService = syncService;
            _tracker = tracker;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _tracker.NextRunTime = DateTime.Now.AddMinutes(_tracker.IntervalMinutes);
            _logger.LogInformation("背景同步服務已啟動，下次執行：{NextRun}", _tracker.NextRunTime);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

                    if (_tracker.NextRunTime.HasValue && DateTime.Now >= _tracker.NextRunTime.Value)
                    {
                        _logger.LogInformation("自動同步觸發");
                        await _syncService.RunAsync();
                        _tracker.NextRunTime = DateTime.Now.AddMinutes(_tracker.IntervalMinutes);
                        _logger.LogInformation("下次自動執行：{NextRun}", _tracker.NextRunTime);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "背景同步服務發生未預期錯誤");
                }
            }

            _logger.LogInformation("背景同步服務已停止。");
        }
    }
}
