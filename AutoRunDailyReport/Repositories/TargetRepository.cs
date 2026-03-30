using Dapper;
using Microsoft.Data.SqlClient;
using AutoRunDailyReport.Models;

namespace AutoRunDailyReport.Repositories
{
    public class TargetRepository
    {
        private readonly string _connectionString;

        public TargetRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("TargetConnection")
                ?? throw new InvalidOperationException("TargetConnection 未設定，請在 appsettings.json 中填入目標資料庫連線字串。");
        }

        public async Task EnsureTableExistsAsync()
        {
            const string sql = @"
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.MesMachinesSync'))
CREATE TABLE dbo.MesMachinesSync (
    pk_SheetLink          INT           NOT NULL PRIMARY KEY,
    MESMachineName        NVARCHAR(500) NULL,
    EQIQDateEE_Time       DATETIME      NULL,
    InLineTestDate_Time   DATETIME      NULL,
    MESSubEQName_String   NVARCHAR(500) NULL,
    Layout                NVARCHAR(200) NULL,
    Line                  NVARCHAR(200) NULL,
    Vendor                NVARCHAR(200) NULL,
    Section               NVARCHAR(200) NULL,
    Process               NVARCHAR(200) NULL,
    MESSubEQNo_String     NVARCHAR(200) NULL,
    MESMachineNo_String   NVARCHAR(200) NULL,
    SyncedAt              DATETIME      NOT NULL DEFAULT GETDATE()
);";
            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql);
        }

        public async Task UpsertMesMachinesAsync(IEnumerable<MesMachineDto> machines)
        {
            await EnsureTableExistsAsync();

            const string sql = @"
MERGE dbo.MesMachinesSync AS target
USING (SELECT @pk_SheetLink AS pk_SheetLink) AS source
    ON target.pk_SheetLink = source.pk_SheetLink
WHEN MATCHED THEN
    UPDATE SET
        MESMachineName      = @MESMachineName,
        EQIQDateEE_Time     = @EQIQDateEE_Time,
        InLineTestDate_Time = @InLineTestDate_Time,
        MESSubEQName_String = @MESSubEQName_String,
        Layout              = @Layout,
        Line                = @Line,
        Vendor              = @Vendor,
        Section             = @Section,
        Process             = @Process,
        MESSubEQNo_String   = @MESSubEQNo_String,
        MESMachineNo_String = @MESMachineNo_String,
        SyncedAt            = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (pk_SheetLink, MESMachineName, EQIQDateEE_Time, InLineTestDate_Time,
            MESSubEQName_String, Layout, Line, Vendor, Section, Process,
            MESSubEQNo_String, MESMachineNo_String, SyncedAt)
    VALUES (@pk_SheetLink, @MESMachineName, @EQIQDateEE_Time, @InLineTestDate_Time,
            @MESSubEQName_String, @Layout, @Line, @Vendor, @Section, @Process,
            @MESSubEQNo_String, @MESMachineNo_String, GETDATE());";

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            foreach (var machine in machines)
                await conn.ExecuteAsync(sql, machine);
        }
    }
}
