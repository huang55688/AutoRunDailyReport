namespace AutoRunDailyReport.Models
{
    public class IpPageViewModel
    {
        public List<IpRowViewModel> Items { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class IpRowViewModel
    {
        public string LineId { get; set; } = "";
        public string EquipmentId { get; set; } = "";
        public string EquipmentNo { get; set; } = "";
        public string? Ip { get; set; }
    }

    public class SaveIpRequest
    {
        public string LineId { get; set; } = "";
        public string EquipmentId { get; set; } = "";
        public string EquipmentNo { get; set; } = "";
        public string? Ip { get; set; }
    }

    public class EquipmentImportResult
    {
        public string SourceDatabase { get; set; } = "";
        public string SourceTable { get; set; } = "";
        public int ImportedCount { get; set; }
    }
}
