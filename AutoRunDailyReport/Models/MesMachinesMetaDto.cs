namespace AutoRunDailyReport.Models
{
    /// <summary>
    /// 以 Line 為單位的手動維護欄位，對應 dbo.MesMachinesMeta。
    /// FirstADeadline 由系統在每次 Sync 後自動計算（不開放手動修改）。
    /// </summary>
    public class MesMachinesMetaDto
    {
        public string Line { get; set; } = "";
        public string? State { get; set; }
        public string? AiotOwner { get; set; }
        public string? Owner { get; set; }
        public string? Schedule { get; set; }
        public string? TestStatus { get; set; }
        /// <summary>
        /// 自動計算：同 Line 排除 Vendor='易格' 後，
        /// MAX(EQIQDateEE_Time, InLineTestDate_Time, CheckTime) + 14 天。
        /// </summary>
        public DateTime? FirstADeadline { get; set; }
        public string? Illustrate { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
