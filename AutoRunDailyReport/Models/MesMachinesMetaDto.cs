namespace AutoRunDailyReport.Models
{
    /// <summary>
    /// 手動維護欄位，對應 dbo.MesMachinesMeta
    /// 以 pk_SheetLink 與 MesMachinesSync 做 JOIN
    /// </summary>
    public class MesMachinesMetaDto
    {
        public int pk_SheetLink { get; set; }
        public string? State { get; set; }
        public string? AiotOwner { get; set; }
        public string? Owner { get; set; }
        public string? Schedule { get; set; }
        public string? TestStatus { get; set; }
        public DateTime? FirstADeadline { get; set; }
        public string? Illustrate { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
