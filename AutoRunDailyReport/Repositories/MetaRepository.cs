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
    Line              NVARCHAR(200) NOT NULL PRIMARY KEY,
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
            const string sql = "SELECT * FROM dbo.MesMachinesMeta ORDER BY Line;";
            using var conn = new SqlConnection(_connectionString);
            return await conn.QueryAsync<MesMachinesMetaDto>(sql);
        }

        /// <summary>
        /// 從 MesMachinesSync 取所有 Line，LEFT JOIN MesMachinesMeta。
        /// 即使尚未手動編輯過的 Line 也會出現。
        /// </summary>
        public async Task<IEnumerable<MesMachinesMetaDto>> GetAllLinesWithMetaAsync()
        {
            const string sql = @"
SELECT
    s.Line,
    m.State,
    m.AiotOwner,
    m.Owner,
    m.Schedule,
    m.TestStatus,
    m.FirstADeadline,
    m.Illustrate,
    m.UpdatedAt
FROM (SELECT DISTINCT Line FROM dbo.MesMachinesSync WHERE Line IS NOT NULL) s
LEFT JOIN dbo.MesMachinesMeta m ON s.Line = m.Line
ORDER BY s.Line;";
            using var conn = new SqlConnection(_connectionString);
            return await conn.QueryAsync<MesMachinesMetaDto>(sql);
        }

        public async Task<MesMachinesMetaDto?> GetByLineAsync(string line)
        {
            const string sql = "SELECT * FROM dbo.MesMachinesMeta WHERE Line = @Line;";
            using var conn = new SqlConnection(_connectionString);
            return await conn.QueryFirstOrDefaultAsync<MesMachinesMetaDto>(sql, new { Line = line });
        }

        /// <summary>
        /// 更新單一 Line 的手動欄位（不覆蓋 FirstADeadline）。
        /// </summary>
        public async Task UpsertManualFieldsAsync(MesMachinesMetaDto meta)
        {
            await EnsureTableExistsAsync();

            const string sql = @"
MERGE dbo.MesMachinesMeta AS target
USING (SELECT @Line AS Line) AS source
    ON target.Line = source.Line
WHEN MATCHED THEN
    UPDATE SET
        State      = @State,
        AiotOwner  = @AiotOwner,
        Owner      = @Owner,
        Schedule   = @Schedule,
        TestStatus = @TestStatus,
        Illustrate = @Illustrate,
        UpdatedAt  = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (Line, State, AiotOwner, Owner, Schedule, TestStatus, Illustrate, UpdatedAt)
    VALUES (@Line, @State, @AiotOwner, @Owner, @Schedule, @TestStatus, @Illustrate, GETDATE());";

            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql, meta);
        }

        /// <summary>
        /// 同步完成後呼叫。
        /// 從 MesMachinesSync 計算每個 Line 的 FirstADeadline：
        ///   排除 Vendor = '易格'，取 MAX(EQIQDateEE_Time, CheckTime) + 14 天。
        ///   （不含 InLineTestDate_Time，避免重複累加天數）
        /// 只更新 FirstADeadline，不覆蓋手動欄位。
        /// </summary>
        public async Task RecalculateDeadlinesAsync()
        {
            await EnsureTableExistsAsync();

            const string sql = @"
MERGE dbo.MesMachinesMeta AS target
USING (
    SELECT
        Line,
        DATEADD(DAY, 14,
            MAX(
                NULLIF(
                    CASE
                        WHEN ISNULL(EQIQDateEE_Time, '19000101') >= ISNULL(CheckTime, '19000101')
                        THEN ISNULL(EQIQDateEE_Time, '19000101')
                        ELSE ISNULL(CheckTime,        '19000101')
                    END,
                    CAST('19000101' AS DATETIME)
                )
            )
        ) AS FirstADeadline
    FROM dbo.MesMachinesSync
    WHERE (Vendor != N'易格' OR Vendor IS NOT NULL)
      AND (EQIQDateEE_Time IS NOT NULL
           OR CheckTime IS NOT NULL)
    GROUP BY Line
) AS source ON target.Line = source.Line
WHEN MATCHED THEN
    UPDATE SET
        FirstADeadline = source.FirstADeadline,
        UpdatedAt      = GETDATE()
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Line, FirstADeadline, UpdatedAt)
    VALUES (source.Line, source.FirstADeadline, GETDATE());";

            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql);
        }
    }
}
