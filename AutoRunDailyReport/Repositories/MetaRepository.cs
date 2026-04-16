using Dapper;
using Microsoft.Data.SqlClient;
using AutoRunDailyReport.Models;

namespace AutoRunDailyReport.Repositories
{
    public class MetaRepository
    {
        private readonly string _connectionString;

        public MetaRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("TargetConnection")
                ?? throw new InvalidOperationException("TargetConnection 未設定。");
        }

        public async Task EnsureTableExistsAsync()
        {
            const string sql = @"
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.MesMachinesMeta'))
CREATE TABLE dbo.MesMachinesMeta (
    MESMachineName    NVARCHAR(500) NOT NULL PRIMARY KEY,
    Line              NVARCHAR(200) NULL,
    State             NVARCHAR(100) NULL,
    AiotOwner         NVARCHAR(200) NULL,
    Owner             NVARCHAR(200) NULL,
    Schedule          NVARCHAR(200) NULL,
    TestStatus        NVARCHAR(100) NULL,
    FirstADeadline    DATETIME      NULL,
    Illustrate        NVARCHAR(MAX) NULL,
    UpdatedAt         DATETIME      NOT NULL DEFAULT GETDATE()
);";
            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql);
        }

        public async Task<IEnumerable<MesMachinesMetaDto>> GetAllAsync()
        {
            const string sql = "SELECT * FROM dbo.MesMachinesMeta ORDER BY MESMachineName;";
            using var conn = new SqlConnection(_connectionString);
            return await conn.QueryAsync<MesMachinesMetaDto>(sql);
        }

        /// <summary>
        /// 從 MesMachinesSync 取所有 MESMachineName，LEFT JOIN MesMachinesMeta。
        /// 即使尚未手動編輯過的機台也會出現。
        /// </summary>
        public async Task<IEnumerable<MesMachinesMetaDto>> GetAllLinesWithMetaAsync()
        {
            const string sql = @"
SELECT
    s.MESMachineName,
    s.Line,
    m.State,
    m.AiotOwner,
    m.Owner,
    m.Schedule,
    m.TestStatus,
    m.FirstADeadline,
    m.Illustrate,
    m.UpdatedAt
FROM (
    SELECT DISTINCT MESMachineName, Line
    FROM dbo.MesMachinesSync
    WHERE MESMachineName IS NOT NULL
) s
LEFT JOIN dbo.MesMachinesMeta m ON s.MESMachineName = m.MESMachineName
ORDER BY s.MESMachineName;";
            using var conn = new SqlConnection(_connectionString);
            return await conn.QueryAsync<MesMachinesMetaDto>(sql);
        }

        public async Task<MesMachinesMetaDto?> GetByMachineNameAsync(string machineName)
        {
            const string sql = "SELECT * FROM dbo.MesMachinesMeta WHERE MESMachineName = @MESMachineName;";
            using var conn = new SqlConnection(_connectionString);
            return await conn.QueryFirstOrDefaultAsync<MesMachinesMetaDto>(sql, new { MESMachineName = machineName });
        }

        public async Task<IEnumerable<ReminderItemDto>> GetUpcomingRemindersAsync(int days)
        {
            await EnsureReminderHiddenTableExistsAsync();

            const string sql = @"
SELECT
    meta.MESMachineName AS MachineName,
    sync.Line,
    meta.FirstADeadline AS OneADeadline
FROM dbo.MesMachinesMeta meta
LEFT JOIN dbo.MesMachinesSync sync ON sync.MESMachineName = meta.MESMachineName
LEFT JOIN dbo.OneATimeReminderHidden hidden
    ON hidden.MESMachineName = meta.MESMachineName
   AND hidden.DeadlineDate = CAST(meta.FirstADeadline AS date)
WHERE meta.FirstADeadline IS NOT NULL
  AND CAST(meta.FirstADeadline AS date) >= CAST(GETDATE() AS date)
  AND CAST(meta.FirstADeadline AS date) <= DATEADD(DAY, @Days, CAST(GETDATE() AS date))
  AND hidden.MESMachineName IS NULL
GROUP BY meta.MESMachineName, sync.Line, meta.FirstADeadline
ORDER BY meta.FirstADeadline, meta.MESMachineName;";

            using var conn = new SqlConnection(_connectionString);
            return await conn.QueryAsync<ReminderItemDto>(sql, new { Days = days });
        }

        public async Task HideReminderAsync(string machineName, DateTime deadlineDate)
        {
            await EnsureReminderHiddenTableExistsAsync();

            const string sql = @"
MERGE dbo.OneATimeReminderHidden AS target
USING (
    SELECT
        @MESMachineName AS MESMachineName,
        @DeadlineDate AS DeadlineDate
) AS source
ON target.MESMachineName = source.MESMachineName
AND target.DeadlineDate = source.DeadlineDate
WHEN MATCHED THEN
    UPDATE SET HiddenAt = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (MESMachineName, DeadlineDate, HiddenAt)
    VALUES (@MESMachineName, @DeadlineDate, GETDATE());";

            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql, new
            {
                MESMachineName = machineName,
                DeadlineDate = deadlineDate.Date
            });
        }

        /// <summary>
        /// 更新單一機台的手動欄位（不覆蓋 FirstADeadline）。
        /// </summary>
        public async Task UpsertManualFieldsAsync(MesMachinesMetaDto meta)
        {
            await EnsureTableExistsAsync();

            const string sql = @"
MERGE dbo.MesMachinesMeta AS target
USING (SELECT @MESMachineName AS MESMachineName) AS source
    ON target.MESMachineName = source.MESMachineName
WHEN MATCHED THEN
    UPDATE SET
        Line       = @Line,
        State      = @State,
        AiotOwner  = @AiotOwner,
        Owner      = @Owner,
        Schedule   = @Schedule,
        TestStatus = @TestStatus,
        Illustrate = @Illustrate,
        UpdatedAt  = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (MESMachineName, Line, State, AiotOwner, Owner, Schedule, TestStatus, Illustrate, UpdatedAt)
    VALUES (@MESMachineName, @Line, @State, @AiotOwner, @Owner, @Schedule, @TestStatus, @Illustrate, GETDATE());";

            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql, meta);
        }

        /// <summary>
        /// 同步完成後呼叫。
        /// 從 MesMachinesSync 計算每個 MESMachineName 的 FirstADeadline：
        ///   排除 Vendor = '易格'，取 MAX(EQIQDateEE_Time, EQIQDateEE_Time_Check) + 14 天。
        /// 只更新 FirstADeadline 和 Line，不覆蓋手動欄位。
        /// </summary>
        public async Task RecalculateDeadlinesAsync()
        {
            await EnsureTableExistsAsync();

            const string sql = @"
MERGE dbo.MesMachinesMeta AS target
USING (
    SELECT
        MESMachineName,
        MAX(Line) AS Line,
        DATEADD(DAY, 14,
            MAX(
                NULLIF(
                    CASE
                        WHEN ISNULL(EQIQDateEE_Time, '19000101') >= ISNULL(EQIQDateEE_Time_Check, '19000101')
                        THEN ISNULL(EQIQDateEE_Time, '19000101')
                        ELSE ISNULL(EQIQDateEE_Time_Check,        '19000101')
                    END,
                    CAST('19000101' AS DATETIME)
                )
            )
        ) AS FirstADeadline
    FROM dbo.MesMachinesSync
    WHERE (Vendor != N'易格' OR Vendor IS NOT NULL)
      AND (EQIQDateEE_Time IS NOT NULL
           OR EQIQDateEE_Time_Check IS NOT NULL)
    GROUP BY MESMachineName
) AS source ON target.MESMachineName = source.MESMachineName
WHEN MATCHED THEN
    UPDATE SET
        Line           = source.Line,
        FirstADeadline = source.FirstADeadline,
        UpdatedAt      = GETDATE()
WHEN NOT MATCHED BY TARGET THEN
    INSERT (MESMachineName, Line, FirstADeadline, UpdatedAt)
    VALUES (source.MESMachineName, source.Line, source.FirstADeadline, GETDATE());";

            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql);
        }

        private async Task EnsureReminderHiddenTableExistsAsync()
        {
            const string sql = @"
IF OBJECT_ID(N'[dbo].[OneATimeReminderHidden]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[OneATimeReminderHidden] (
        [MESMachineName] NVARCHAR(500) NOT NULL,
        [DeadlineDate]   DATE          NOT NULL,
        [HiddenAt]       DATETIME      NOT NULL DEFAULT GETDATE(),
        CONSTRAINT [PK_OneATimeReminderHidden] PRIMARY KEY ([MESMachineName], [DeadlineDate])
    );
END;";

            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql);
        }
    }
}
