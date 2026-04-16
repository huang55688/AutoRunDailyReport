namespace AutoRunDailyReport.Models
{
    public class IpPageViewModel
    {
        public List<IpRowViewModel> Items { get; set; } = new();
        public List<IpSearchCandidateViewModel> SearchCandidates { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public HashSet<string> HighlightedKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string CurrentLineId { get; set; } = string.Empty;
    }

    public class IpSearchCandidateViewModel
    {
        public string LineId { get; set; } = string.Empty;
        public int EquipmentCount { get; set; }
    }

    public class IpRowViewModel
    {
        public string LineId { get; set; } = string.Empty;
        public string EquipmentId { get; set; } = string.Empty;
        public string? Ip { get; set; }

        public string GetRowKey()
        {
            return $"{LineId}||{EquipmentId}";
        }
    }

    public class SaveIpRequest
    {
        public string LineId { get; set; } = string.Empty;
        public string EquipmentId { get; set; } = string.Empty;
        public string? SearchLineId { get; set; }
        public string? Ip { get; set; }
    }

    public class EquipmentImportResult
    {
        public string SourceDatabase { get; set; } = string.Empty;
        public string SourceTable { get; set; } = string.Empty;
        public string LineId { get; set; } = string.Empty;
        public int ImportedCount { get; set; }
        public List<string> ImportedKeys { get; set; } = new();
    }
}
