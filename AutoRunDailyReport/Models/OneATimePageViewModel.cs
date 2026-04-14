namespace AutoRunDailyReport.Models
{
    public class OneATimePageViewModel
    {
        public List<ReminderItemDto> Reminders { get; set; } = new();
        public List<OneATimeDayViewModel> Days { get; set; } = new();
        public List<NoticeBoardItemViewModel> Notices { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public bool IsEditMode { get; set; }
        public bool IsProgressMode { get; set; }
    }

    public class OneATimeDayViewModel
    {
        public DateTime Date { get; set; }
        public bool IsToday { get; set; }
        public List<OneATimeEntryDto> Entries { get; set; } = new();
        public List<OneATimeEntryInputModel> EditableEntries { get; set; } = new();
    }

    public class OneATimeEntryDto
    {
        public int Id { get; set; }
        public DateTime ScheduleDate { get; set; }
        public int DisplayOrder { get; set; }
        public string TestItem { get; set; } = "";
        public string? ScheduledTime { get; set; }
        public int Progress { get; set; }
    }

    public class OneATimeEntryInputModel
    {
        public int DisplayOrder { get; set; }
        public string TestItem { get; set; } = "";
        public string? ScheduledTime { get; set; }
        public int Progress { get; set; }
    }

    public class OneATimeDayInputModel
    {
        public DateTime ScheduleDate { get; set; }
        public List<OneATimeEntryInputModel> Entries { get; set; } = new();
    }

    public class OneATimeSaveRequest
    {
        public List<OneATimeDayInputModel> Days { get; set; } = new();
    }

    public class NoticeBoardItemViewModel
    {
        public string Tag { get; set; } = "";
        public string Text { get; set; } = "";
    }
}
