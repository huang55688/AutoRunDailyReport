namespace AutoRunDailyReport.Models
{
    public class PortSettingPageViewModel
    {
        public int ReminderDays { get; set; }
        public string? PortDatabaseName { get; set; }
        public List<PortSettingRowViewModel> Items { get; set; } = new();
        public List<string> Warnings { get; set; } = new();

        public int TotalRows => Items.Count;
        public int MatchedRows => Items.Count(item => !string.IsNullOrWhiteSpace(item.PortId));
    }

    public class PortSettingRowViewModel
    {
        public string Line { get; set; } = "";
        public DateTime? OneADeadline { get; set; }
        public string? MESMachineNo_String { get; set; }
        public string? MESSubEQNo_String { get; set; }
        public string? MachineDtIID { get; set; }
        public string? PortId { get; set; }
    }

    public class PortSettingConnectionTestResult
    {
        public DateTime CheckedAt { get; set; } = DateTime.Now;
        public int ReminderDays { get; set; }
        public string PortServer { get; set; } = "10.26.66.151";
        public string TargetServer { get; set; } = "10.26.166.109";
        public ConnectionCheckResult TargetConnection { get; set; } = new();
        public ConnectionCheckResult PortSourceConnection { get; set; } = new();
        public ConnectionCheckResult PortDatabaseLookup { get; set; } = new();
        public ConnectionCheckResult PortTableQuery { get; set; } = new();

        public bool OverallSuccess =>
            TargetConnection.Success &&
            PortSourceConnection.Success &&
            PortDatabaseLookup.Success &&
            PortTableQuery.Success;
    }

    public class ConnectionCheckResult
    {
        public bool Success { get; set; }
        public string Step { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Detail { get; set; }
    }
}
