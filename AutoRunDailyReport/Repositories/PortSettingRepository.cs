using AutoRunDailyReport.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AutoRunDailyReport.Repositories
{
    public class PortSettingRepository
    {
        private const string PortTableName = "PDL_MachineDtl_Port";

        private readonly string _targetConnectionString;
        private readonly string _portMasterConnectionString;

        public PortSettingRepository(IConfiguration configuration)
        {
            _targetConnectionString = configuration.GetConnectionString("TargetConnection")
                ?? throw new InvalidOperationException("TargetConnection is missing.");
            _portMasterConnectionString = configuration.GetConnectionString("PortSourceConnection")
                ?? throw new InvalidOperationException("PortSourceConnection is missing.");
        }

        public async Task<(IReadOnlyList<PortSettingRowViewModel> Items, string? PortDatabaseName)> GetUpcomingPortSettingsAsync(int days)
        {
            var deadlineRows = (await GetUpcomingDeadlineRowsAsync(days)).ToList();
            if (deadlineRows.Count == 0)
            {
                return (Array.Empty<PortSettingRowViewModel>(), null);
            }

            var portDatabaseName = await FindPortDatabaseNameAsync();
            if (string.IsNullOrWhiteSpace(portDatabaseName))
            {
                var fallbackRows = deadlineRows.Select(row => new PortSettingRowViewModel
                {
                    Line = row.Line,
                    OneADeadline = row.OneADeadline,
                    MESMachineNo_String = row.MESMachineNo_String,
                    MESSubEQNo_String = row.MESSubEQNo_String
                }).ToList();

                return (fallbackRows, null);
            }

            var machineDtIIds = deadlineRows
                .Select(row => row.MESSubEQNo_String?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var portLookup = machineDtIIds.Length == 0
                ? new Dictionary<string, List<PortSourceRow>>(StringComparer.OrdinalIgnoreCase)
                : await GetPortLookupAsync(portDatabaseName, machineDtIIds);

            var results = new List<PortSettingRowViewModel>();

            foreach (var row in deadlineRows)
            {
                var machineDtIId = row.MESSubEQNo_String?.Trim();
                if (string.IsNullOrWhiteSpace(machineDtIId) ||
                    !portLookup.TryGetValue(machineDtIId, out var portRows) ||
                    portRows.Count == 0)
                {
                    results.Add(new PortSettingRowViewModel
                    {
                        Line = row.Line,
                        OneADeadline = row.OneADeadline,
                        MESMachineNo_String = row.MESMachineNo_String,
                        MESSubEQNo_String = row.MESSubEQNo_String
                    });
                    continue;
                }

                foreach (var portRow in portRows)
                {
                    results.Add(new PortSettingRowViewModel
                    {
                        Line = row.Line,
                        OneADeadline = row.OneADeadline,
                        MESMachineNo_String = row.MESMachineNo_String,
                        MESSubEQNo_String = row.MESSubEQNo_String,
                        MachineDtIID = portRow.MachineDtIID,
                        PortId = portRow.PortId
                    });
                }
            }

            return (results, portDatabaseName);
        }

        public async Task<PortSettingConnectionTestResult> TestConnectionsAsync(int days)
        {
            var result = new PortSettingConnectionTestResult
            {
                ReminderDays = days
            };

            result.TargetConnection = await TestTargetConnectionAsync(days);
            if (!result.TargetConnection.Success)
            {
                return result;
            }

            result.PortSourceConnection = await TestPortSourceConnectionAsync();
            if (!result.PortSourceConnection.Success)
            {
                return result;
            }

            try
            {
                var portDatabaseName = await FindPortDatabaseNameAsync();
                if (string.IsNullOrWhiteSpace(portDatabaseName))
                {
                    result.PortDatabaseLookup = new ConnectionCheckResult
                    {
                        Success = false,
                        Step = "PortDatabaseLookup",
                        Message = "找不到包含 dbo.PDL_MachineDtl_Port 的資料庫。",
                        Detail = "已成功連到 10.26.66.151，但在可存取的資料庫中沒有找到目標資料表。"
                    };
                    return result;
                }

                result.PortDatabaseLookup = new ConnectionCheckResult
                {
                    Success = true,
                    Step = "PortDatabaseLookup",
                    Message = $"已找到資料表所在資料庫：{portDatabaseName}"
                };

                result.PortTableQuery = await TestPortTableQueryAsync(portDatabaseName);
                return result;
            }
            catch (Exception ex)
            {
                result.PortDatabaseLookup = BuildFailureResult(
                    "PortDatabaseLookup",
                    "搜尋 dbo.PDL_MachineDtl_Port 所在資料庫時失敗。",
                    ex);
                return result;
            }
        }

        private async Task<IEnumerable<UpcomingDeadlineRow>> GetUpcomingDeadlineRowsAsync(int days)
        {
            const string sql = @"
SELECT DISTINCT
    meta.Line,
    meta.FirstADeadline AS OneADeadline,
    sync.MESMachineNo_String,
    sync.MESSubEQNo_String
FROM dbo.MesMachinesMeta meta
INNER JOIN dbo.MesMachinesSync sync ON sync.Line = meta.Line
WHERE meta.FirstADeadline IS NOT NULL
  AND CAST(meta.FirstADeadline AS date) >= CAST(GETDATE() AS date)
  AND CAST(meta.FirstADeadline AS date) <= DATEADD(DAY, @Days, CAST(GETDATE() AS date))
ORDER BY meta.FirstADeadline, meta.Line, sync.MESMachineNo_String, sync.MESSubEQNo_String;";

            using var conn = new SqlConnection(_targetConnectionString);
            return await conn.QueryAsync<UpcomingDeadlineRow>(sql, new { Days = days });
        }

        private async Task<ConnectionCheckResult> TestTargetConnectionAsync(int days)
        {
            try
            {
                const string sql = @"
SELECT COUNT(1)
FROM dbo.MesMachinesMeta
WHERE FirstADeadline IS NOT NULL
  AND CAST(FirstADeadline AS date) >= CAST(GETDATE() AS date)
  AND CAST(FirstADeadline AS date) <= DATEADD(DAY, @Days, CAST(GETDATE() AS date));";

                using var conn = new SqlConnection(_targetConnectionString);
                var count = await conn.ExecuteScalarAsync<int>(sql, new { Days = days });

                return new ConnectionCheckResult
                {
                    Success = true,
                    Step = "TargetConnection",
                    Message = $"成功連線到目標資料庫，{days} 天內到期資料共 {count} 筆。"
                };
            }
            catch (Exception ex)
            {
                return BuildFailureResult(
                    "TargetConnection",
                    "連線目標資料庫或查詢 MesMachinesMeta 失敗。",
                    ex);
            }
        }

        private async Task<ConnectionCheckResult> TestPortSourceConnectionAsync()
        {
            try
            {
                const string sql = "SELECT DB_NAME();";

                using var conn = new SqlConnection(_portMasterConnectionString);
                var currentDb = await conn.ExecuteScalarAsync<string>(sql);

                return new ConnectionCheckResult
                {
                    Success = true,
                    Step = "PortSourceConnection",
                    Message = $"成功連線到 Port 來源 SQL Server，目前資料庫：{currentDb ?? "未知"}。"
                };
            }
            catch (Exception ex)
            {
                return BuildFailureResult(
                    "PortSourceConnection",
                    "連線到 10.26.66.151 失敗。",
                    ex);
            }
        }

        private async Task<string?> FindPortDatabaseNameAsync()
        {
            const string sql = @"
CREATE TABLE #Matches (DatabaseName sysname NOT NULL);

DECLARE @sql nvarchar(max) = N'';

SELECT @sql = @sql + N'
IF EXISTS (
    SELECT 1
    FROM ' + QUOTENAME(name) + N'.sys.tables
    WHERE name = N''' + @TableName + N'''
)
    INSERT INTO #Matches(DatabaseName) VALUES (N''' + REPLACE(name, '''', '''''') + N''');'
FROM sys.databases
WHERE state_desc = N'ONLINE'
  AND HAS_DBACCESS(name) = 1
  AND name NOT IN (N'master', N'model', N'msdb', N'tempdb');

EXEC sp_executesql @sql;

SELECT TOP (1) DatabaseName
FROM #Matches
ORDER BY DatabaseName;";

            using var conn = new SqlConnection(_portMasterConnectionString);
            return await conn.QueryFirstOrDefaultAsync<string>(sql, new { TableName = PortTableName });
        }

        private async Task<ConnectionCheckResult> TestPortTableQueryAsync(string databaseName)
        {
            try
            {
                var safeDatabaseName = QuoteIdentifier(databaseName);
                var sql = $@"
SELECT COUNT(1)
FROM {safeDatabaseName}.dbo.{QuoteIdentifier(PortTableName)};";

                using var conn = new SqlConnection(_portMasterConnectionString);
                var count = await conn.ExecuteScalarAsync<int>(sql);

                return new ConnectionCheckResult
                {
                    Success = true,
                    Step = "PortTableQuery",
                    Message = $"成功讀取 {databaseName}.dbo.{PortTableName}，目前共有 {count} 筆資料。"
                };
            }
            catch (Exception ex)
            {
                return BuildFailureResult(
                    "PortTableQuery",
                    $"已找到資料庫 {databaseName}，但查詢 {PortTableName} 失敗。",
                    ex);
            }
        }

        private async Task<Dictionary<string, List<PortSourceRow>>> GetPortLookupAsync(
            string databaseName,
            IEnumerable<string> machineDtIIds)
        {
            var safeDatabaseName = QuoteIdentifier(databaseName);
            var sql = $@"
SELECT
    CAST([MachineDtIID] AS NVARCHAR(200)) AS MachineDtIID,
    CAST([ID] AS NVARCHAR(100)) AS PortId
FROM {safeDatabaseName}.dbo.{QuoteIdentifier(PortTableName)}
WHERE CAST([MachineDtIID] AS NVARCHAR(200)) IN @MachineDtIIds
ORDER BY CAST([MachineDtIID] AS NVARCHAR(200)), [ID];";

            using var conn = new SqlConnection(_portMasterConnectionString);
            var rows = await conn.QueryAsync<PortSourceRow>(sql, new
            {
                MachineDtIIds = machineDtIIds.ToArray()
            });

            return rows
                .GroupBy(row => row.MachineDtIID, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static string QuoteIdentifier(string value)
        {
            return $"[{value.Replace("]", "]]")}]";
        }

        private static ConnectionCheckResult BuildFailureResult(string step, string message, Exception ex)
        {
            var baseException = ex.GetBaseException();

            return new ConnectionCheckResult
            {
                Success = false,
                Step = step,
                Message = message,
                Detail = $"{baseException.GetType().Name}: {baseException.Message}"
            };
        }

        private class UpcomingDeadlineRow
        {
            public string Line { get; set; } = "";
            public DateTime? OneADeadline { get; set; }
            public string? MESMachineNo_String { get; set; }
            public string? MESSubEQNo_String { get; set; }
        }

        private class PortSourceRow
        {
            public string MachineDtIID { get; set; } = "";
            public string PortId { get; set; } = "";
        }
    }
}
