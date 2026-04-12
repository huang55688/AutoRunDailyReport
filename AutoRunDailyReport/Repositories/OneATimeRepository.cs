using AutoRunDailyReport.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AutoRunDailyReport.Repositories
{
    public class OneATimeRepository
    {
        private readonly string _connectionString;

        public OneATimeRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("TargetConnection")
                ?? throw new InvalidOperationException("TargetConnection is missing.");
        }

        public async Task EnsureTableExistsAsync()
        {
            const string sql = @"
IF OBJECT_ID(N'[dbo].[1ATime]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[1ATime] (
        [Id]            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [ScheduleDate]  DATE           NOT NULL,
        [DisplayOrder]  INT            NOT NULL DEFAULT 0,
        [TestItem]      NVARCHAR(500)  NOT NULL,
        [ScheduledTime] NVARCHAR(100)  NULL,
        [Progress]      INT            NOT NULL DEFAULT 0,
        [CreatedAt]     DATETIME       NOT NULL DEFAULT GETDATE(),
        [UpdatedAt]     DATETIME       NOT NULL DEFAULT GETDATE()
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_1ATime_ScheduleDate'
      AND object_id = OBJECT_ID(N'[dbo].[1ATime]')
)
BEGIN
    CREATE INDEX [IX_1ATime_ScheduleDate]
        ON [dbo].[1ATime] ([ScheduleDate]);
END;";

            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql);
        }

        public async Task<IEnumerable<OneATimeEntryDto>> GetEntriesInRangeAsync(DateTime startDate, DateTime endDate)
        {
            await EnsureTableExistsAsync();

            const string sql = @"
SELECT
    [Id],
    [ScheduleDate],
    [DisplayOrder],
    [TestItem],
    [ScheduledTime],
    [Progress]
FROM [dbo].[1ATime]
WHERE [ScheduleDate] >= @StartDate
  AND [ScheduleDate] <= @EndDate
ORDER BY [ScheduleDate], [DisplayOrder], [Id];";

            using var conn = new SqlConnection(_connectionString);
            return await conn.QueryAsync<OneATimeEntryDto>(sql, new
            {
                StartDate = startDate.Date,
                EndDate = endDate.Date
            });
        }

        public async Task ReplaceEntriesInRangeAsync(
            DateTime startDate,
            DateTime endDate,
            IEnumerable<OneATimeEntryDto> entries)
        {
            await EnsureTableExistsAsync();

            const string deleteSql = @"
DELETE FROM [dbo].[1ATime]
WHERE [ScheduleDate] >= @StartDate
  AND [ScheduleDate] <= @EndDate;";

            const string insertSql = @"
INSERT INTO [dbo].[1ATime] (
    [ScheduleDate],
    [DisplayOrder],
    [TestItem],
    [ScheduledTime],
    [Progress],
    [CreatedAt],
    [UpdatedAt]
)
VALUES (
    @ScheduleDate,
    @DisplayOrder,
    @TestItem,
    @ScheduledTime,
    @Progress,
    GETDATE(),
    GETDATE()
);";

            var entryList = entries.ToList();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();

            await conn.ExecuteAsync(deleteSql, new
            {
                StartDate = startDate.Date,
                EndDate = endDate.Date
            }, transaction);

            if (entryList.Count > 0)
                await conn.ExecuteAsync(insertSql, entryList, transaction);

            transaction.Commit();
        }
    }
}
