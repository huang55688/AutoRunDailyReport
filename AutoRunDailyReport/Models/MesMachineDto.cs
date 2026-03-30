namespace AutoRunDailyReport.Models
{

    public class MesMachineDto
    {
        public int pk_SheetLink { get; set; }
        public string MESMachineName { get; set; }
        public DateTime? EQIQDateEE_Time { get; set; }
        public DateTime? InLineTestDate_Time { get; set; }
        public string MESSubEQName_String { get; set; }
        public string Layout { get; set; }
        public string Line { get; set; }
        public string Vendor { get; set; }
        public string Section { get; set; }
        public string Process { get; set; }
        public string MESSubEQNo_String { get; set; }
        public string MESMachineNo_String { get; set; }
    }

}
