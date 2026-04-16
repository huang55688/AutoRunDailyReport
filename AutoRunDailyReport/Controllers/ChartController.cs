using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace AutoRunDailyReport.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChartController : ControllerBase
    {
        private readonly string _connectionString;

        public ChartController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("TargetConnection")
                ?? throw new InvalidOperationException("TargetConnection 未設定。");
        }

        /// <summary>
        /// 依月份彙總：每月 InLineTestDate_Time 累計數、InLineTestACDDate_Time_Check 累計數
        /// 每台機器只取最新一筆，若最新紀錄不在區間內則不計入
        /// 支援時間區間 + KFPhase + Layout 篩選
        /// </summary>
        [HttpGet("inline-test")]
        public async Task<IActionResult> GetInLineTestChart(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? kfPhase,
            [FromQuery] string? layout)
        {
            var start = from ?? DateTime.Today.AddMonths(-5);
            var end = (to ?? DateTime.Today).Date.AddDays(1).AddMilliseconds(-3);

            const string sql = @"
;WITH ExcludeNames AS (
    SELECT value AS Name FROM (VALUES
        (N'分析儀'),
        (N'鋼板'),
        (N'檢測機')
    ) AS T(value)
),
LatestPerMachine AS (
    SELECT
        MESMachineName,
        InLineTestDate_Time,
        InLineTestACDDate_Time_Check,
        KFPhase_String,
        Layout,
        ROW_NUMBER() OVER (
            PARTITION BY MESMachineName
            ORDER BY pk_SheetLink DESC
        ) AS rn
    FROM dbo.MesMachinesSync
    WHERE InLineTestDate_Time IS NOT NULL
      AND (Vendor IS NULL OR Vendor != N'易格')
      AND NOT EXISTS (
          SELECT 1 FROM ExcludeNames
          WHERE MESSubEQName_String LIKE N'%' + Name + N'%'
      )
)
SELECT
    FORMAT(InLineTestDate_Time, 'yyyy-MM') AS Month,
    COUNT(*) AS InLineTestCount,
    COUNT(InLineTestACDDate_Time_Check) AS CheckedCount
FROM LatestPerMachine
WHERE rn = 1
  AND InLineTestDate_Time >= @Start
  AND InLineTestDate_Time <= @End
  AND (@KFPhase IS NULL OR KFPhase_String = @KFPhase)
  AND (@Layout IS NULL OR Layout = @Layout)
GROUP BY FORMAT(InLineTestDate_Time, 'yyyy-MM')
ORDER BY Month;";

            using var conn = new SqlConnection(_connectionString);
            var rows = (await conn.QueryAsync(sql, new
            {
                Start = start,
                End = end,
                KFPhase = string.IsNullOrWhiteSpace(kfPhase) ? null : kfPhase,
                Layout = string.IsNullOrWhiteSpace(layout) ? null : layout
            })).ToList();

            var labels = new List<string>();
            var totalData = new List<int>();
            var okData = new List<int>();
            var ngData = new List<int>();
            var cumInline = 0;
            var cumChecked = 0;

            foreach (var row in rows)
            {
                cumInline += (int)row.InLineTestCount;
                cumChecked += (int)row.CheckedCount;

                labels.Add((string)row.Month);
                totalData.Add(cumInline);
                okData.Add(cumChecked);
                ngData.Add(cumInline - cumChecked);
            }

            return Ok(new { labels, totalData, okData, ngData });
        }

        /// <summary>
        /// 取得未完成的線別清單（InLineTestDate_Time 有值但 InLineTestACDDate_Time_Check 為 NULL）
        /// 回傳按月份分組的結構
        /// </summary>
        [HttpGet("inline-test/incomplete")]
        public async Task<IActionResult> GetIncompleteLines(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? kfPhase,
            [FromQuery] string? layout)
        {
            var start = from ?? DateTime.Today.AddMonths(-5);
            var end = (to ?? DateTime.Today).Date.AddDays(1).AddMilliseconds(-3);

            const string sql = @"
;WITH ExcludeNames AS (
    SELECT value AS Name FROM (VALUES
        (N'分析儀'),
        (N'鋼板'),
        (N'檢測機')
    ) AS T(value)
),
LatestPerMachine AS (
    SELECT
        MESMachineName,
        InLineTestDate_Time,
        InLineTestACDDate_Time_Check,
        KFPhase_String,
        Layout,
        ROW_NUMBER() OVER (
            PARTITION BY MESMachineName
            ORDER BY pk_SheetLink DESC
        ) AS rn
    FROM dbo.MesMachinesSync
    WHERE InLineTestDate_Time IS NOT NULL
      AND (Vendor IS NULL OR Vendor != N'易格')
      AND NOT EXISTS (
          SELECT 1 FROM ExcludeNames
          WHERE MESSubEQName_String LIKE N'%' + Name + N'%'
      )
)
SELECT
    MESMachineName,
    KFPhase_String AS KFPhase,
    Layout,
    InLineTestDate_Time,
    FORMAT(InLineTestDate_Time, 'yyyy-MM') AS Month
FROM LatestPerMachine
WHERE rn = 1
  AND InLineTestDate_Time >= @Start
  AND InLineTestDate_Time <= @End
  AND InLineTestACDDate_Time_Check IS NULL
  AND (@KFPhase IS NULL OR KFPhase_String = @KFPhase)
  AND (@Layout IS NULL OR Layout = @Layout)
ORDER BY Month, InLineTestDate_Time DESC;";

            using var conn = new SqlConnection(_connectionString);
            var rows = (await conn.QueryAsync(sql, new
            {
                Start = start,
                End = end,
                KFPhase = string.IsNullOrWhiteSpace(kfPhase) ? null : kfPhase,
                Layout = string.IsNullOrWhiteSpace(layout) ? null : layout
            })).ToList();

            // 按月份分組
            var grouped = rows
                .GroupBy(r => (string)r.Month)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    month = g.Key,
                    count = g.Count(),
                    items = g.Select(r => new
                    {
                        machineName = (string)r.MESMachineName,
                        kfPhase = (string?)r.KFPhase,
                        layout = (string?)r.Layout,
                        inlineTestDate = ((DateTime)r.InLineTestDate_Time).ToString("yyyy-MM-dd HH:mm")
                    })
                });

            var totalCount = rows.Count;

            return Ok(new { totalCount, groups = grouped });
        }


        /// <summary>取得篩選器的下拉選項</summary>
        [HttpGet("filters")]
        public async Task<IActionResult> GetFilters()
        {
            const string sql = @"
SELECT DISTINCT KFPhase_String FROM dbo.MesMachinesSync WHERE KFPhase_String IS NOT NULL ORDER BY KFPhase_String;
SELECT DISTINCT Layout FROM dbo.MesMachinesSync WHERE Layout IS NOT NULL ORDER BY Layout;";

            using var conn = new SqlConnection(_connectionString);
            using var multi = await conn.QueryMultipleAsync(sql);
            var phases = (await multi.ReadAsync<string>()).ToList();
            var Layouts = (await multi.ReadAsync<string>()).ToList();

            return Ok(new { phases, Layouts });
        }
    }
}
