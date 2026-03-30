namespace AutoRunDailyReport.Services
{
    public class SyncLogEntry
    {
        public DateTime Time { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int RowsSynced { get; set; }
    }

    public class SyncStatusTracker
    {
        private readonly List<SyncLogEntry> _logs = new();
        private readonly object _lock = new();

        public bool IsRunning { get; private set; }
        public DateTime? LastRunTime { get; private set; }
        public bool? LastRunSuccess { get; private set; }
        public string LastRunMessage { get; private set; } = "尚未執行";
        public int LastRowsSynced { get; private set; }
        public int IntervalMinutes { get; set; } = 30;
        public DateTime? NextRunTime { get; set; }

        public IReadOnlyList<SyncLogEntry> RecentLogs
        {
            get { lock (_lock) { return _logs.ToList().AsReadOnly(); } }
        }

        public void SetRunning(bool running)
        {
            lock (_lock) { IsRunning = running; }
        }

        public void RecordResult(bool success, string message, int rowsSynced)
        {
            lock (_lock)
            {
                IsRunning = false;
                LastRunTime = DateTime.Now;
                LastRunSuccess = success;
                LastRunMessage = message;
                LastRowsSynced = rowsSynced;

                _logs.Insert(0, new SyncLogEntry
                {
                    Time = DateTime.Now,
                    Success = success,
                    Message = message,
                    RowsSynced = rowsSynced
                });

                if (_logs.Count > 20)
                    _logs.RemoveAt(_logs.Count - 1);
            }
        }
    }
}
