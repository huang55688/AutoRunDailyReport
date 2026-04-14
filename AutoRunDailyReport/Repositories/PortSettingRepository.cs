using AutoRunDailyReport.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AutoRunDailyReport.Repositories
{
    public class PortSettingRepository
    {
        private const string PortTableName = "PDL_MachineDtl_Port";

        private static readonly string[] MachineDetailColumnCandidates =
        {
            "MachineDtlID",
            "MachineDtlId",
            "MachineDtIID"
        };

        private static readonly string[] PortIdColumnCandidates =
        {
            "ID",
            "Id"
        };

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
                return (BuildFallbackRows(deadlineRows), null);
            }

            var columnNames = await ResolvePortColumnNamesAsync(portDatabaseName);

            var machineDetailIds = deadlineRows
                .Select(row => row.MESSubEQNo_String?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var portLookup = machineDetailIds.Length == 0
                ? new Dictionary<string, List<PortSourceRow>>(StringComparer.OrdinalIgnoreCase)
                : await GetPortLookupAsync(portDatabaseName, columnNames, machineDetailIds);

            var results = new List<PortSettingRowViewModel>();

            foreach (var row in deadlineRows)
            {
                var machineDetailId = row.MESSubEQNo_String?.Trim();
                if (string.IsNullOrWhiteSpace(machineDetailId) ||
                    !portLookup.TryGetValue(machineDetailId, out var portRows) ||
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
                        MachineDtIID = portRow.MachineDetailId,
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
                        Detail = "請確認 10.26.66.151 上是否真的存在這張表，或目前帳號是否有讀取該資料庫的權限。"
                    };
                    return result;
                }

                result.PortDatabaseLookup = new ConnectionCheckResult
                {
                    Success = true,
                    Step = "PortDatabaseLookup",
                    Message = $"成功找到 Port 資料庫：{portDatabaseName}"
                };

                var columnNames = await ResolvePortColumnNamesAsync(portDatabaseName);
                result.PortTableQuery = await TestPortTableQueryAsync(portDatabaseName, columnNames);
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
    meta.MESMachineName,
    meta.Line,
    meta.FirstADeadline AS OneADeadline,
    sync.MESMachineNo_String,
    sync.MESSubEQNo_String
FROM dbo.MesMachinesMeta meta
INNER JOIN dbo.MesMachinesSync sync ON sync.MESMachineName = meta.MESMachineName
WHERE meta.FirstADeadline IS NOT NULL
  AND CAST(meta.FirstADeadline AS date) >= CAST(GETDATE() AS date)
  AND CAST(meta.FirstADeadline AS date) <= DATEADD(DAY, @Days, CAST(GETDATE() AS date))
ORDER BY meta.FirstADeadline, meta.MESMachineName, sync.MESMachineNo_String, sync.MESSubEQNo_String;";

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
                    Message = $"成功連到目標資料庫，{days} 天內到期資料共 {count} 筆。"
                };
            }
            catch (Exception ex)
            {
                return BuildFailureResult(
                    "TargetConnection",
                    "讀取目標資料庫的 MesMachinesMeta 失敗。",
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
                    Message = $"成功連到 Port 來源 SQL Server，目前資料庫：{currentDb ?? "未知"}。"
                };
            }
            catch (Exception ex)
            {
                return BuildFailureResult(
                    "PortSourceConnection",
                    "無法連線到 10.26.66.151。",
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

        private async Task<PortColumnNames> ResolvePortColumnNamesAsync(string databaseName)
        {
            var safeDatabaseName = QuoteIdentifier(databaseName);
            var sql = $@"
SELECT c.name
FROM {safeDatabaseName}.sys.columns c
INNER JOIN {safeDatabaseName}.sys.tables t ON c.object_id = t.object_id
INNER JOIN {safeDatabaseName}.sys.schemas s ON t.schema_id = s.schema_id
WHERE s.name = N'dbo'
  AND t.name = @TableName;";

            using var conn = new SqlConnection(_portMasterConnectionString);
            var columns = (await conn.QueryAsync<string>(sql, new { TableName = PortTableName })).ToList();

            if (columns.Count == 0)
            {
                throw new InvalidOperationException(
                    $"在 {databaseName}.dbo.{PortTableName} 找不到任何欄位，請確認資料表是否存在且帳號有欄位讀取權限。");
            }

            var machineDetailColumn = FindFirstMatchingColumn(columns, MachineDetailColumnCandidates);
            var portIdColumn = FindFirstMatchingColumn(columns, PortIdColumnCandidates);

            if (machineDetailColumn is null)
            {
                throw new InvalidOperationException(
                    $"在 {databaseName}.dbo.{PortTableName} 找不到機台對應欄位。可用欄位：{string.Join(", ", columns)}");
            }

            if (portIdColumn is null)
            {
                throw new InvalidOperationException(
                    $"在 {databaseName}.dbo.{PortTableName} 找不到 Port ID 欄位。可用欄位：{string.Join(", ", columns)}");
            }

            return new PortColumnNames
            {
                MachineDetailColumn = machineDetailColumn,
                PortIdColumn = portIdColumn
            };
        }

        private async Task<ConnectionCheckResult> TestPortTableQueryAsync(string databaseName, PortColumnNames columnNames)
        {
            try
            {
                var safeDatabaseName = QuoteIdentifier(databaseName);
                var machineDetailColumn = QuoteIdentifier(columnNames.MachineDetailColumn);
                var portIdColumn = QuoteIdentifier(columnNames.PortIdColumn);

                var sql = $@"
SELECT TOP (1)
    CAST({machineDetailColumn} AS NVARCHAR(200)) AS MachineDetailId,
    CAST({portIdColumn} AS NVARCHAR(100)) AS PortId
FROM {safeDatabaseName}.dbo.{QuoteIdentifier(PortTableName)};";

                using var conn = new SqlConnection(_portMasterConnectionString);
                await conn.QueryFirstOrDefaultAsync(sql);

                return new ConnectionCheckResult
                {
                    Success = true,
                    Step = "PortTableQuery",
                    Message = $"成功讀取 {databaseName}.dbo.{PortTableName}，比對欄位使用 {columnNames.MachineDetailColumn} / {columnNames.PortIdColumn}。"
                };
            }
            catch (Exception ex)
            {
                return BuildFailureResult(
                    "PortTableQuery",
                    $"讀取 {databaseName}.dbo.{PortTableName} 的欄位資料失敗。",
                    ex);
            }
        }

        private async Task<Dictionary<string, List<PortSourceRow>>> GetPortLookupAsync(
            string databaseName,
            PortColumnNames columnNames,
            IEnumerable<string> machineDetailIds)
        {
            var safeDatabaseName = QuoteIdentifier(databaseName);
            var machineDetailColumn = QuoteIdentifier(columnNames.MachineDetailColumn);
            var portIdColumn = QuoteIdentifier(columnNames.PortIdColumn);

            var sql = $@"
SELECT
    CAST({machineDetailColumn} AS NVARCHAR(200)) AS MachineDetailId,
    CAST({portIdColumn} AS NVARCHAR(100)) AS PortId
FROM {safeDatabaseName}.dbo.{QuoteIdentifier(PortTableName)}
WHERE CAST({machineDetailColumn} AS NVARCHAR(200)) IN @MachineDetailIds
ORDER BY CAST({machineDetailColumn} AS NVARCHAR(200)), {portIdColumn};";

            using var conn = new SqlConnection(_portMasterConnectionString);
            var rows = await conn.QueryAsync<PortSourceRow>(sql, new
            {
                MachineDetailIds = machineDetailIds.ToArray()
            });

            return rows
                .GroupBy(row => row.MachineDetailId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<PortSettingRowViewModel> BuildFallbackRows(IEnumerable<UpcomingDeadlineRow> deadlineRows)
        {
            return deadlineRows
                .Select(row => new PortSettingRowViewModel
                {
                    Line = row.Line,
                    OneADeadline = row.OneADeadline,
                    MESMachineNo_String = row.MESMachineNo_String,
                    MESSubEQNo_String = row.MESSubEQNo_String
                })
                .ToList();
        }

        private static string? FindFirstMatchingColumn(IEnumerable<string> actualColumns, IEnumerable<string> candidates)
        {
            var actualColumnList = actualColumns.ToList();

            foreach (var candidate in candidates)
            {
                var match = actualColumnList.FirstOrDefault(column =>
                    string.Equals(column, candidate, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(match))
                {
                    return match;
                }
            }

            return null;
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

        private sealed class PortColumnNames
        {
            public string MachineDetailColumn { get; set; } = "";
            public string PortIdColumn { get; set; } = "";
        }

        private sealed class UpcomingDeadlineRow
        {
            public string MESMachineName { get; set; } = "";
            public string? Line { get; set; }
            public DateTime? OneADeadline { get; set; }
            public string? MESMachineNo_String { get; set; }
            public string? MESSubEQNo_String { get; set; }
        }

        private sealed class PortSourceRow
        {
            public string MachineDetailId { get; set; } = "";
            public string PortId { get; set; } = "";
        }
    }
}
