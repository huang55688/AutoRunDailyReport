using AutoRunDailyReport.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AutoRunDailyReport.Repositories
{
    public class IpRepository
    {
        private static readonly string[] SourceTableCandidates =
        {
            "SERM_EQUIPMENT",
            "SBRM_EQUIPMENT"
        };

        private static readonly string[] LineIdColumnCandidates =
        {
            "LINEID",
            "LineId"
        };

        private static readonly string[] EquipmentIdColumnCandidates =
        {
            "EQUIPMENTID",
            "EquipmentId"
        };

        private static readonly string[] EquipmentNoColumnCandidates =
        {
            "EQUIPMENTNO",
            "EquipmentNo"
        };

        private static readonly string[] CreatedDateColumnCandidates =
        {
            "CREATEDATE",
            "CreateDate",
            "CREATETIME",
            "CreateTime",
            "INSERTTIME",
            "InsertTime",
            "ADDTIME",
            "AddTime",
            "ADDDATE",
            "AddDate",
            "UPDATETIME",
            "UpdateTime",
            "MODIFYTIME",
            "ModifyTime"
        };

        private readonly string _targetConnectionString;
        private readonly string _serverMasterConnectionString;

        public IpRepository(IConfiguration configuration)
        {
            _targetConnectionString = configuration.GetConnectionString("TargetConnection")
                ?? throw new InvalidOperationException("TargetConnection is missing.");

            var builder = new SqlConnectionStringBuilder(_targetConnectionString)
            {
                InitialCatalog = "master"
            };

            _serverMasterConnectionString = builder.ConnectionString;
        }

        public async Task EnsureTableExistsAsync()
        {
            const string sql = @"
IF OBJECT_ID(N'[dbo].[ip]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ip] (
        [LINEID]      NVARCHAR(100) NOT NULL,
        [EQUIPMENTID] NVARCHAR(100) NOT NULL,
        [EQUIPMENTNO] NVARCHAR(100) NOT NULL,
        [ip]          NVARCHAR(100) NULL,
        CONSTRAINT [PK_ip] PRIMARY KEY ([LINEID], [EQUIPMENTID], [EQUIPMENTNO])
    );
END;";

            using var conn = new SqlConnection(_targetConnectionString);
            await conn.ExecuteAsync(sql);
        }

        public async Task<IReadOnlyList<IpRowViewModel>> GetAllAsync()
        {
            await EnsureTableExistsAsync();

            const string sql = @"
SELECT
    [LINEID] AS LineId,
    [EQUIPMENTID] AS EquipmentId,
    [EQUIPMENTNO] AS EquipmentNo,
    [ip] AS Ip
FROM [dbo].[ip]
ORDER BY [LINEID], [EQUIPMENTNO], [EQUIPMENTID];";

            using var conn = new SqlConnection(_targetConnectionString);
            var rows = await conn.QueryAsync<IpRowViewModel>(sql);
            return rows.ToList();
        }

        public async Task<EquipmentImportResult> ImportRecentEquipmentAsync(int daysBack)
        {
            await EnsureTableExistsAsync();

            var sourceLocation = await FindSourceTableLocationAsync();
            var sourceColumns = await ResolveSourceColumnsAsync(sourceLocation);
            var sourceRows = (await GetRecentEquipmentRowsAsync(sourceLocation, sourceColumns, daysBack)).ToList();

            if (sourceRows.Count == 0)
            {
                return new EquipmentImportResult
                {
                    SourceDatabase = sourceLocation.DatabaseName,
                    SourceTable = sourceLocation.TableName,
                    ImportedCount = 0
                };
            }

            const string mergeSql = @"
MERGE [dbo].[ip] AS target
USING (
    SELECT
        @LineId AS [LINEID],
        @EquipmentId AS [EQUIPMENTID],
        @EquipmentNo AS [EQUIPMENTNO]
) AS source
ON target.[LINEID] = source.[LINEID]
AND target.[EQUIPMENTID] = source.[EQUIPMENTID]
AND target.[EQUIPMENTNO] = source.[EQUIPMENTNO]
WHEN NOT MATCHED THEN
    INSERT ([LINEID], [EQUIPMENTID], [EQUIPMENTNO], [ip])
    VALUES (source.[LINEID], source.[EQUIPMENTID], source.[EQUIPMENTNO], NULL);";

            using var conn = new SqlConnection(_targetConnectionString);
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();

            foreach (var row in sourceRows)
            {
                await conn.ExecuteAsync(mergeSql, new
                {
                    row.LineId,
                    row.EquipmentId,
                    row.EquipmentNo
                }, transaction);
            }

            transaction.Commit();

            return new EquipmentImportResult
            {
                SourceDatabase = sourceLocation.DatabaseName,
                SourceTable = sourceLocation.TableName,
                ImportedCount = sourceRows.Count
            };
        }

        public async Task UpdateIpAsync(SaveIpRequest request)
        {
            await EnsureTableExistsAsync();

            const string sql = @"
UPDATE [dbo].[ip]
SET [ip] = @Ip
WHERE [LINEID] = @LineId
  AND [EQUIPMENTID] = @EquipmentId
  AND [EQUIPMENTNO] = @EquipmentNo;";

            using var conn = new SqlConnection(_targetConnectionString);
            var affectedRows = await conn.ExecuteAsync(sql, new
            {
                request.LineId,
                request.EquipmentId,
                request.EquipmentNo,
                Ip = string.IsNullOrWhiteSpace(request.Ip) ? null : request.Ip.Trim()
            });

            if (affectedRows == 0)
            {
                throw new InvalidOperationException("找不到要更新的 dbo.ip 資料列。");
            }
        }

        private async Task<SourceTableLocation> FindSourceTableLocationAsync()
        {
            var tableNamesSql = string.Join(", ", SourceTableCandidates.Select(name => $"N'{name}'"));

            var sql = $@"
CREATE TABLE #Matches (
    [DatabaseName] sysname NOT NULL,
    [TableName]    sysname NOT NULL
);

DECLARE @sql nvarchar(max) = N'';

SELECT @sql = @sql + N'
INSERT INTO #Matches ([DatabaseName], [TableName])
SELECT N''' + REPLACE(name, '''', '''''') + N''', t.name
FROM ' + QUOTENAME(name) + N'.sys.tables t
INNER JOIN ' + QUOTENAME(name) + N'.sys.schemas s ON t.schema_id = s.schema_id
WHERE s.name = N''dbo''
  AND t.name IN ({tableNamesSql});'
FROM sys.databases
WHERE state_desc = N'ONLINE'
  AND HAS_DBACCESS(name) = 1
  AND name NOT IN (N'master', N'model', N'msdb', N'tempdb');

EXEC sp_executesql @sql;

SELECT TOP (1)
    [DatabaseName],
    [TableName]
FROM #Matches
ORDER BY
    CASE [TableName]
        WHEN N'SERM_EQUIPMENT' THEN 0
        WHEN N'SBRM_EQUIPMENT' THEN 1
        ELSE 9
    END,
    [DatabaseName];";

            using var conn = new SqlConnection(_serverMasterConnectionString);
            var result = await conn.QueryFirstOrDefaultAsync<SourceTableLocation>(sql);

            if (result is null)
            {
                throw new InvalidOperationException("找不到 dbo.SERM_EQUIPMENT 或 dbo.SBRM_EQUIPMENT。");
            }

            return result;
        }

        private async Task<SourceColumnNames> ResolveSourceColumnsAsync(SourceTableLocation sourceLocation)
        {
            var safeDatabaseName = QuoteIdentifier(sourceLocation.DatabaseName);
            var sql = $@"
SELECT c.name
FROM {safeDatabaseName}.sys.columns c
INNER JOIN {safeDatabaseName}.sys.tables t ON c.object_id = t.object_id
INNER JOIN {safeDatabaseName}.sys.schemas s ON t.schema_id = s.schema_id
WHERE s.name = N'dbo'
  AND t.name = @TableName;";

            using var conn = new SqlConnection(_serverMasterConnectionString);
            var columns = (await conn.QueryAsync<string>(sql, new { sourceLocation.TableName })).ToList();

            if (columns.Count == 0)
            {
                throw new InvalidOperationException(
                    $"在 {sourceLocation.DatabaseName}.dbo.{sourceLocation.TableName} 找不到任何欄位。");
            }

            var lineIdColumn = FindFirstMatchingColumn(columns, LineIdColumnCandidates);
            var equipmentIdColumn = FindFirstMatchingColumn(columns, EquipmentIdColumnCandidates);
            var equipmentNoColumn = FindFirstMatchingColumn(columns, EquipmentNoColumnCandidates);
            var createdDateColumn = FindFirstMatchingColumn(columns, CreatedDateColumnCandidates);

            if (lineIdColumn is null || equipmentIdColumn is null || equipmentNoColumn is null)
            {
                throw new InvalidOperationException(
                    $"在 {sourceLocation.DatabaseName}.dbo.{sourceLocation.TableName} 找不到 LINEID / EQUIPMENTID / EQUIPMENTNO 對應欄位。可用欄位：{string.Join(", ", columns)}");
            }

            if (createdDateColumn is null)
            {
                throw new InvalidOperationException(
                    $"在 {sourceLocation.DatabaseName}.dbo.{sourceLocation.TableName} 找不到可用的新增日期欄位。可用欄位：{string.Join(", ", columns)}");
            }

            return new SourceColumnNames
            {
                LineIdColumn = lineIdColumn,
                EquipmentIdColumn = equipmentIdColumn,
                EquipmentNoColumn = equipmentNoColumn,
                CreatedDateColumn = createdDateColumn
            };
        }

        private async Task<IEnumerable<IpRowViewModel>> GetRecentEquipmentRowsAsync(
            SourceTableLocation sourceLocation,
            SourceColumnNames columnNames,
            int daysBack)
        {
            var safeDatabaseName = QuoteIdentifier(sourceLocation.DatabaseName);
            var safeTableName = QuoteIdentifier(sourceLocation.TableName);
            var safeLineIdColumn = QuoteIdentifier(columnNames.LineIdColumn);
            var safeEquipmentIdColumn = QuoteIdentifier(columnNames.EquipmentIdColumn);
            var safeEquipmentNoColumn = QuoteIdentifier(columnNames.EquipmentNoColumn);
            var safeCreatedDateColumn = QuoteIdentifier(columnNames.CreatedDateColumn);

            var sql = $@"
SELECT DISTINCT
    CAST({safeLineIdColumn} AS NVARCHAR(100)) AS LineId,
    CAST({safeEquipmentIdColumn} AS NVARCHAR(100)) AS EquipmentId,
    CAST({safeEquipmentNoColumn} AS NVARCHAR(100)) AS EquipmentNo,
    CAST(NULL AS NVARCHAR(100)) AS Ip
FROM {safeDatabaseName}.dbo.{safeTableName}
WHERE {safeLineIdColumn} IS NOT NULL
  AND {safeEquipmentIdColumn} IS NOT NULL
  AND {safeEquipmentNoColumn} IS NOT NULL
  AND CAST({safeCreatedDateColumn} AS date) >= DATEADD(DAY, -@DaysBack, CAST(GETDATE() AS date))
  AND CAST({safeCreatedDateColumn} AS date) <= CAST(GETDATE() AS date)
ORDER BY
    CAST({safeLineIdColumn} AS NVARCHAR(100)),
    CAST({safeEquipmentNoColumn} AS NVARCHAR(100)),
    CAST({safeEquipmentIdColumn} AS NVARCHAR(100));";

            using var conn = new SqlConnection(_serverMasterConnectionString);
            return await conn.QueryAsync<IpRowViewModel>(sql, new { DaysBack = daysBack });
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

        private sealed class SourceTableLocation
        {
            public string DatabaseName { get; set; } = "";
            public string TableName { get; set; } = "";
        }

        private sealed class SourceColumnNames
        {
            public string LineIdColumn { get; set; } = "";
            public string EquipmentIdColumn { get; set; } = "";
            public string EquipmentNoColumn { get; set; } = "";
            public string CreatedDateColumn { get; set; } = "";
        }
    }
}
