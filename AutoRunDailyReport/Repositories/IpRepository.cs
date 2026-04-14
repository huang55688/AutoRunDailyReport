using AutoRunDailyReport.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AutoRunDailyReport.Repositories
{
    public class IpRepository
    {
        private const string SourceDatabaseName = "DIAEAP_UNIMICRON";
        private const string SourceTableName = "SBRM_EQUIPMENT";

        private readonly string _targetConnectionString;
        private readonly string _sourceConnectionString;

        public IpRepository(IConfiguration configuration)
        {
            _targetConnectionString = configuration.GetConnectionString("TargetConnection")
                ?? throw new InvalidOperationException("TargetConnection is missing.");

            var sourceBuilder = new SqlConnectionStringBuilder(_targetConnectionString)
            {
                InitialCatalog = SourceDatabaseName
            };

            _sourceConnectionString = sourceBuilder.ConnectionString;
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

        public async Task<EquipmentImportResult> ImportByLineIdAsync(string lineId)
        {
            if (string.IsNullOrWhiteSpace(lineId))
            {
                throw new InvalidOperationException("LineID 不可為空白。");
            }

            await EnsureTableExistsAsync();

            const string sourceSql = @"
SELECT DISTINCT
    CAST([LINEID] AS NVARCHAR(100)) AS LineId,
    CAST([EQUIPMENTID] AS NVARCHAR(100)) AS EquipmentId,
    CAST([EQUIPMENTNO] AS NVARCHAR(100)) AS EquipmentNo,
    CAST(NULL AS NVARCHAR(100)) AS Ip
FROM [dbo].[SBRM_EQUIPMENT]
WHERE [LINEID] = @LineId
  AND [EQUIPMENTID] IS NOT NULL
  AND [EQUIPMENTNO] IS NOT NULL
ORDER BY [EQUIPMENTNO], [EQUIPMENTID];";

            using var sourceConn = new SqlConnection(_sourceConnectionString);
            var sourceRows = (await sourceConn.QueryAsync<IpRowViewModel>(sourceSql, new
            {
                LineId = lineId.Trim()
            })).ToList();

            if (sourceRows.Count == 0)
            {
                return new EquipmentImportResult
                {
                    SourceDatabase = SourceDatabaseName,
                    SourceTable = SourceTableName,
                    LineId = lineId.Trim(),
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

            using var targetConn = new SqlConnection(_targetConnectionString);
            await targetConn.OpenAsync();
            using var transaction = targetConn.BeginTransaction();

            foreach (var row in sourceRows)
            {
                await targetConn.ExecuteAsync(mergeSql, new
                {
                    row.LineId,
                    row.EquipmentId,
                    row.EquipmentNo
                }, transaction);
            }

            transaction.Commit();

            return new EquipmentImportResult
            {
                SourceDatabase = SourceDatabaseName,
                SourceTable = SourceTableName,
                LineId = lineId.Trim(),
                ImportedCount = sourceRows.Count,
                ImportedKeys = sourceRows.Select(row => row.GetRowKey()).ToList()
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
    }
}
