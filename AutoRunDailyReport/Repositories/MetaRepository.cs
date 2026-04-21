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
        public async Task<IEnumerable<MesMachinesMetaDto>> GetAllLinesWithMetaAsync(string? search = null, bool futureDeadline = false)
        {
            const string baseSql = @"
WITH SyncBase AS (
    SELECT DISTINCT
        LTRIM(RTRIM(s.MESMachineName)) AS MESMachineName,
        NULLIF(LTRIM(RTRIM(s.Line)), N'') AS SyncLine,
        NULLIF(LTRIM(RTRIM(s.MESMachineNo_String)), N'') AS MESMachineNoString,
        NULLIF(LTRIM(RTRIM(s.MESSubEQNo_String)), N'') AS MESSubEQNoString,
        NULLIF(LTRIM(RTRIM(s.Vendor)), N'') AS Vendor
    FROM dbo.MesMachinesSync s
    WHERE s.MESMachineName IS NOT NULL
      AND LTRIM(RTRIM(s.MESMachineName)) != N''
),
FilteredMachine AS (
SELECT
    b.MESMachineName,
    MAX(b.SyncLine) AS SyncLine
FROM SyncBase b
LEFT JOIN dbo.MesMachinesMeta m ON b.MESMachineName = m.MESMachineName
WHERE (
        @Search IS NULL
     OR @Search = N''
     OR UPPER(REPLACE(b.MESMachineName, N' ', N'')) LIKE UPPER(N'%' + REPLACE(@Search, N' ', N'') + N'%')
     OR UPPER(REPLACE(ISNULL(NULLIF(LTRIM(RTRIM(m.Line)), N''), ISNULL(b.SyncLine, N'')), N' ', N'')) LIKE UPPER(N'%' + REPLACE(@Search, N' ', N'') + N'%')
)
GROUP BY b.MESMachineName
)
";

            const string listSql = @"
SELECT
    f.MESMachineName,
    ISNULL(NULLIF(LTRIM(RTRIM(m.Line)), N''), f.SyncLine) AS Line,
    m.State,
    m.AiotOwner,
    m.Owner,
    m.Schedule,
    m.TestStatus,
    m.FirstADeadline,
    m.Illustrate,
    m.UpdatedAt
FROM FilteredMachine f
LEFT JOIN dbo.MesMachinesMeta m ON f.MESMachineName = m.MESMachineName
ORDER BY
    CASE
        WHEN @FutureDeadline = 1
         AND m.FirstADeadline IS NOT NULL
         AND CAST(m.FirstADeadline AS date) >= CAST(GETDATE() AS date)
        THEN 0
        ELSE 1
    END,
    CASE
        WHEN @FutureDeadline = 1
         AND m.FirstADeadline IS NOT NULL
         AND CAST(m.FirstADeadline AS date) >= CAST(GETDATE() AS date)
        THEN CAST(m.FirstADeadline AS date)
    END ASC,
    f.MESMachineName;";

            const string detailSqlWithIp = @"
SELECT
    b.MESMachineName,
    b.MESMachineNoString AS MESMachineNoString,
    b.MESSubEQNoString AS MESSubEQNoString,
    b.Vendor,
    MAX(NULLIF(LTRIM(RTRIM(ip.[ip])), N'')) AS Ip,
    MAX(NULLIF(LTRIM(RTRIM(ip.[Device])), N'')) AS Device
FROM SyncBase b
INNER JOIN FilteredMachine f ON b.MESMachineName = f.MESMachineName
LEFT JOIN dbo.[ip] ip
    ON NULLIF(LTRIM(RTRIM(ip.[LINEID])), N'') = b.MESMachineNoString
   AND NULLIF(LTRIM(RTRIM(ip.[EQUIPMENTID])), N'') = b.MESSubEQNoString
WHERE b.MESMachineNoString IS NOT NULL
   OR b.MESSubEQNoString IS NOT NULL
   OR b.Vendor IS NOT NULL
GROUP BY
    b.MESMachineName,
    b.MESMachineNoString,
    b.MESSubEQNoString,
    b.Vendor
ORDER BY
    b.MESMachineName,
    b.MESMachineNoString,
    b.MESSubEQNoString;";

            const string detailSqlWithoutIp = @"
SELECT
    b.MESMachineName,
    b.MESMachineNoString AS MESMachineNoString,
    b.MESSubEQNoString AS MESSubEQNoString,
    b.Vendor,
    CAST(NULL AS NVARCHAR(100)) AS Ip,
    CAST(NULL AS NVARCHAR(100)) AS Device
FROM SyncBase b
INNER JOIN FilteredMachine f ON b.MESMachineName = f.MESMachineName
WHERE b.MESMachineNoString IS NOT NULL
   OR b.MESSubEQNoString IS NOT NULL
   OR b.Vendor IS NOT NULL
ORDER BY
    b.MESMachineName,
    b.MESMachineNoString,
    b.MESSubEQNoString;";

            using var conn = new SqlConnection(_connectionString);
            var hasIpTable = await HasIpTableAsync(conn);
            var sql = baseSql
                + listSql
                + Environment.NewLine
                + baseSql
                + (hasIpTable ? detailSqlWithIp : detailSqlWithoutIp);
            var parameters = new
            {
                Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                FutureDeadline = futureDeadline
            };

            using var multi = await conn.QueryMultipleAsync(sql, parameters);
            var items = (await multi.ReadAsync<MesMachinesMetaDto>()).ToList();
            var details = (await multi.ReadAsync<MesMachineMetaDetailDto>()).ToList();

            var detailLookup = details
                .GroupBy(detail => detail.MESMachineName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .GroupBy(detail => new
                        {
                            MESMachineNoString = detail.MESMachineNoString?.Trim(),
                            MESSubEQNoString = detail.MESSubEQNoString?.Trim(),
                            Vendor = detail.Vendor?.Trim(),
                            Ip = detail.Ip?.Trim(),
                            Device = detail.Device?.Trim()
                        })
                        .Select(grouped => new MesMachineMetaDetailDto
                        {
                            MESMachineName = group.Key,
                            MESMachineNoString = grouped.Key.MESMachineNoString,
                            MESSubEQNoString = grouped.Key.MESSubEQNoString,
                            Vendor = grouped.Key.Vendor,
                            Ip = grouped.Key.Ip,
                            Device = grouped.Key.Device
                        })
                        .OrderBy(detail => detail.MESMachineNoString)
                        .ThenBy(detail => detail.MESSubEQNoString)
                        .ThenBy(detail => detail.Vendor)
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                if (detailLookup.TryGetValue(item.MESMachineName, out var machineDetails))
                {
                    item.SyncDetails = machineDetails;
                }
            }

            return items;
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

            const string machineNameSql = @"
SELECT
    meta.MESMachineName AS MachineName,
    NULLIF(LTRIM(RTRIM(meta.Line)), N'') AS Line,
    meta.FirstADeadline AS OneADeadline
FROM dbo.MesMachinesMeta meta
LEFT JOIN dbo.OneATimeReminderHidden hidden
    ON hidden.MESMachineName = meta.MESMachineName
   AND hidden.DeadlineDate = CAST(meta.FirstADeadline AS date)
WHERE meta.FirstADeadline IS NOT NULL
  AND CAST(meta.FirstADeadline AS date) >= CAST(GETDATE() AS date)
  AND CAST(meta.FirstADeadline AS date) <= DATEADD(DAY, @Days, CAST(GETDATE() AS date))
  AND hidden.MESMachineName IS NULL
ORDER BY meta.FirstADeadline, meta.MESMachineName;";

            const string lineSql = @"
SELECT
    meta.MESMachineName AS MachineName,
    NULLIF(LTRIM(RTRIM(meta.Line)), N'') AS Line,
    meta.FirstADeadline AS OneADeadline
FROM dbo.MesMachinesMeta meta
LEFT JOIN dbo.OneATimeReminderHidden hidden
    ON hidden.Line = meta.Line
   AND hidden.DeadlineDate = CAST(meta.FirstADeadline AS date)
WHERE meta.FirstADeadline IS NOT NULL
  AND CAST(meta.FirstADeadline AS date) >= CAST(GETDATE() AS date)
  AND CAST(meta.FirstADeadline AS date) <= DATEADD(DAY, @Days, CAST(GETDATE() AS date))
  AND hidden.Line IS NULL
ORDER BY meta.FirstADeadline, meta.MESMachineName;";

            using var conn = new SqlConnection(_connectionString);
            var keyMode = await GetReminderHiddenKeyModeAsync(conn);
            var sql = keyMode == "Line" ? lineSql : machineNameSql;
            return await conn.QueryAsync<ReminderItemDto>(sql, new { Days = days });
        }

        public async Task HideReminderAsync(string? machineName, string? line, DateTime deadlineDate)
        {
            await EnsureReminderHiddenTableExistsAsync();

            using var conn = new SqlConnection(_connectionString);
            var keyMode = await GetReminderHiddenKeyModeAsync(conn);

            if (keyMode == "Line")
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    throw new InvalidOperationException("OneATimeReminderHidden 目前使用 Line 結構，但提醒項目沒有 Line。");
                }

                const string lineSql = @"
MERGE dbo.OneATimeReminderHidden AS target
USING (
    SELECT
        @Line AS Line,
        @DeadlineDate AS DeadlineDate
) AS source
ON target.Line = source.Line
AND target.DeadlineDate = source.DeadlineDate
WHEN MATCHED THEN
    UPDATE SET HiddenAt = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (Line, DeadlineDate, HiddenAt)
    VALUES (@Line, @DeadlineDate, GETDATE());";

                await conn.ExecuteAsync(lineSql, new
                {
                    Line = line.Trim(),
                    DeadlineDate = deadlineDate.Date
                });

                return;
            }

            if (string.IsNullOrWhiteSpace(machineName))
            {
                throw new InvalidOperationException("OneATimeReminderHidden 目前使用 MESMachineName 結構，但提醒項目沒有 MESMachineName。");
            }

            const string machineNameSql = @"
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

            await conn.ExecuteAsync(machineNameSql, new
            {
                MESMachineName = machineName.Trim(),
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

        private async Task<string> GetReminderHiddenKeyModeAsync(SqlConnection conn)
        {
            const string sql = @"
SELECT CASE
    WHEN EXISTS (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'[dbo].[OneATimeReminderHidden]')
          AND name = N'Line'
    ) THEN N'Line'
    WHEN EXISTS (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'[dbo].[OneATimeReminderHidden]')
          AND name = N'MESMachineName'
    ) THEN N'MESMachineName'
    ELSE N'MESMachineName'
END;";

            return await conn.ExecuteScalarAsync<string>(sql) ?? "MESMachineName";
        }

        private async Task<bool> HasIpTableAsync(SqlConnection conn)
        {
            const string sql = @"
IF OBJECT_ID(N'[dbo].[ip]', N'U') IS NULL
BEGIN
    SELECT CAST(0 AS bit);
    RETURN;
END;

IF COL_LENGTH(N'dbo.ip', N'Device') IS NULL
BEGIN
    ALTER TABLE [dbo].[ip]
    ADD [Device] NVARCHAR(100) NULL;
END;

SELECT CAST(1 AS bit);";

            return await conn.ExecuteScalarAsync<bool>(sql);
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
