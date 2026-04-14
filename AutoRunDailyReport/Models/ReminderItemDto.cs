namespace AutoRunDailyReport.Models
{
    public class ReminderItemDto
    {
        public string MachineName { get; set; } = "";
        public string? Line { get; set; }
        public DateTime? OneADeadline { get; set; }

        public int DaysRemaining
        {
            get
            {
                if (!OneADeadline.HasValue)
                    return int.MaxValue;

                return (OneADeadline.Value.Date - DateTime.Today).Days;
            }
        }
    }
}