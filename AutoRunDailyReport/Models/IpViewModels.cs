namespace AutoRunDailyReport.Models
{
    public class IpPageViewModel
    {
        public List<IpRowViewModel> Items { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public HashSet<string> HighlightedKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string CurrentLineId { get; set; } = "";
    }

    public class IpRowViewModel
    {
        public string LineId { get; set; } = "";
        public string EquipmentId { get; set; } = "";
        public string EquipmentNo { get; set; } = "";
        public string? Ip { get; set; }

        public string GetRowKey()
        {
            return $"{LineId}||{EquipmentId}||{EquipmentNo}";
        }
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
        public string LineId { get; set; } = "";
        public int ImportedCount { get; set; }
        public List<string> ImportedKeys { get; set; } = new();
    }
}
