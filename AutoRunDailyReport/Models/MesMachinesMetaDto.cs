namespace AutoRunDailyReport.Models
{
    /// <summary>
    /// 以 MESMachineName 為單位的手動維護欄位，對應 dbo.MesMachinesMeta。
    /// FirstADeadline 由系統在每次 Sync 後自動計算（不開放手動修改）。
    /// </summary>
    public class MesMachinesMetaDto
    {
        public string MESMachineName { get; set; } = "";
        public string? Line { get; set; }
        public string? State { get; set; }
        public string? AiotOwner { get; set; }
        public string? Owner { get; set; }
        public string? Schedule { get; set; }
        public string? TestStatus { get; set; }
        public DateTime? FirstADeadline { get; set; }
        public string? Illustrate { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<MesMachineMetaDetailDto> SyncDetails { get; set; } = new();
    }

    public class MesMachineMetaDetailDto
    {
        public string MESMachineName { get; set; } = "";
        public string? MESMachineNoString { get; set; }
        public string? MESSubEQNoString { get; set; }
        public string? Vendor { get; set; }
        public string? Ip { get; set; }
        public string? Device { get; set; }
    }

    public class SaveMetaSyncDetailRequest
    {
        public string MachineName { get; set; } = string.Empty;
        public string LineId { get; set; } = string.Empty;
        public string EquipmentId { get; set; } = string.Empty;
        public string? Search { get; set; }
        public bool FutureDeadline { get; set; }
        public string? Ip { get; set; }
        public string? Device { get; set; }
    }
}
