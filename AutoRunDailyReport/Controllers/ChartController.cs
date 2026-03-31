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
        /// 支援時間區間 + KFPhase + Section 篩選
        /// </summary>
        [HttpGet("inline-test")]
        public async Task<IActionResult> GetInLineTestChart(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? kfPhase,
            [FromQuery] string? section)
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
        Section,
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
  AND (@Section IS NULL OR Section = @Section)
GROUP BY FORMAT(InLineTestDate_Time, 'yyyy-MM')
ORDER BY Month;";

            using var conn = new SqlConnection(_connectionString);
            var rows = (await conn.QueryAsync(sql, new
            {
                Start = start,
                End = end,
                KFPhase = string.IsNullOrWhiteSpace(kfPhase) ? null : kfPhase,
                Section = string.IsNullOrWhiteSpace(section) ? null : section
            })).ToList();

            // 累計計算
            var labels = new List<string>();
            var inlineData = new List<int>();
            var checkedData = new List<int>();
            var diffData = new List<int>();
            int cumInline = 0, cumChecked = 0;

            foreach (var r in rows)
            {
                cumInline += (int)r.InLineTestCount;
                cumChecked += (int)r.CheckedCount;
                labels.Add((string)r.Month);
                inlineData.Add(cumInline);
                checkedData.Add(cumChecked);
                diffData.Add(cumInline - cumChecked);
            }

            return Ok(new { labels, inlineData, checkedData, diffData });
        }

        /// <summary>取得篩選器的下拉選項</summary>
        [HttpGet("filters")]
        public async Task<IActionResult> GetFilters()
        {
            const string sql = @"
SELECT DISTINCT KFPhase_String FROM dbo.MesMachinesSync WHERE KFPhase_String IS NOT NULL ORDER BY KFPhase_String;
SELECT DISTINCT Section FROM dbo.MesMachinesSync WHERE Section IS NOT NULL ORDER BY Section;";

            using var conn = new SqlConnection(_connectionString);
            using var multi = await conn.QueryMultipleAsync(sql);
            var phases = (await multi.ReadAsync<string>()).ToList();
            var sections = (await multi.ReadAsync<string>()).ToList();

            return Ok(new { phases, sections });
        }
    }
}