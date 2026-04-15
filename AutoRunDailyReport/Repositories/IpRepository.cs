using AutoRunDailyReport.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AutoRunDailyReport.Repositories
{
    public class IpRepository
    {
        private const string SourceDatabaseName = "SM_AIOT_KF";
        private const string SourceTableName = "MesMachinesSync";

        private readonly string _targetConnectionString;

        public IpRepository(IConfiguration configuration)
        {
            _targetConnectionString = configuration.GetConnectionString("TargetConnection")
                ?? throw new InvalidOperationException("TargetConnection is missing.");
        }

        public async Task EnsureTableExistsAsync()
        {
            const string sql = @"
IF OBJECT_ID(N'[dbo].[ip]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ip] (
        [LINEID]      NVARCHAR(100) NOT NULL,
        [EQUIPMENTID] NVARCHAR(100) NOT NULL,
        [ip]          NVARCHAR(100) NULL,
        CONSTRAINT [PK_ip] PRIMARY KEY ([LINEID], [EQUIPMENTID])
    );
END
ELSE IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE [object_id] = OBJECT_ID(N'[dbo].[ip]')
      AND [name] = N'EQUIPMENTNO'
)
BEGIN
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'[dbo].[ip_migrating]', N'U') IS NOT NULL
    BEGIN
        DROP TABLE [dbo].[ip_migrating];
    END;

    CREATE TABLE [dbo].[ip_migrating] (
        [LINEID]      NVARCHAR(100) NOT NULL,
        [EQUIPMENTID] NVARCHAR(100) NOT NULL,
        [ip]          NVARCHAR(100) NULL,
        CONSTRAINT [PK_ip_migrating] PRIMARY KEY ([LINEID], [EQUIPMENTID])
    );

    INSERT INTO [dbo].[ip_migrating] ([LINEID], [EQUIPMENTID], [ip])
    SELECT
        CAST([LINEID] AS NVARCHAR(100)) AS [LINEID],
        CAST([EQUIPMENTID] AS NVARCHAR(100)) AS [EQUIPMENTID],
        MAX(NULLIF(LTRIM(RTRIM([ip])), N'')) AS [ip]
    FROM [dbo].[ip]
    GROUP BY [LINEID], [EQUIPMENTID];

    DROP TABLE [dbo].[ip];
    EXEC sp_rename N'[dbo].[ip_migrating]', N'ip';
    EXEC sp_rename N'[PK_ip_migrating]', N'PK_ip', N'OBJECT';

    COMMIT TRANSACTION;
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
    [ip] AS Ip
FROM [dbo].[ip]
WHERE UPPER(LEFT(LTRIM(RTRIM([LINEID])), 3)) = N'SKL'
ORDER BY [LINEID], [EQUIPMENTID];";

            using var conn = new SqlConnection(_targetConnectionString);
            var rows = await conn.QueryAsync<IpRowViewModel>(sql);
            return rows.ToList();
        }

        public async Task<IReadOnlyList<IpSearchCandidateViewModel>> SearchCandidatesAsync(string lineId)
        {
            if (string.IsNullOrWhiteSpace(lineId))
            {
                return Array.Empty<IpSearchCandidateViewModel>();
            }

            const string sql = @"
SELECT
    CAST(LTRIM(RTRIM([MESMachineNo_String])) AS NVARCHAR(100)) AS LineId,
    COUNT(DISTINCT CAST(LTRIM(RTRIM([MESSubEQNo_String])) AS NVARCHAR(100))) AS EquipmentCount
FROM [dbo].[MesMachinesSync]
WHERE NULLIF(LTRIM(RTRIM([MESMachineNo_String])), N'') IS NOT NULL
  AND NULLIF(LTRIM(RTRIM([MESSubEQNo_String])), N'') IS NOT NULL
  AND UPPER(LEFT(LTRIM(RTRIM([MESMachineNo_String])), 3)) = N'SKL'
  AND UPPER(LTRIM(RTRIM([MESMachineNo_String]))) LIKE UPPER(N'%' + @LineId + N'%')
GROUP BY CAST(LTRIM(RTRIM([MESMachineNo_String])) AS NVARCHAR(100))
ORDER BY LineId;";

            using var conn = new SqlConnection(_targetConnectionString);
            var rows = await conn.QueryAsync<IpSearchCandidateViewModel>(sql, new
            {
                LineId = lineId.Trim()
            });
            return rows.ToList();
        }

        public async Task<EquipmentImportResult> ImportByLineIdAsync(string lineId)
        {
            if (string.IsNullOrWhiteSpace(lineId))
            {
                throw new InvalidOperationException("LINEID 不可為空。");
            }

            await EnsureTableExistsAsync();

            const string sourceSql = @"
SELECT DISTINCT
    CAST(LTRIM(RTRIM([MESMachineNo_String])) AS NVARCHAR(100)) AS LineId,
    CAST(LTRIM(RTRIM([MESSubEQNo_String])) AS NVARCHAR(100)) AS EquipmentId,
    CAST(NULL AS NVARCHAR(100)) AS Ip
FROM [dbo].[MesMachinesSync]
WHERE NULLIF(LTRIM(RTRIM([MESMachineNo_String])), N'') IS NOT NULL
  AND NULLIF(LTRIM(RTRIM([MESSubEQNo_String])), N'') IS NOT NULL
  AND UPPER(LEFT(LTRIM(RTRIM([MESMachineNo_String])), 3)) = N'SKL'
  AND UPPER(LTRIM(RTRIM([MESMachineNo_String]))) = UPPER(@LineId)
ORDER BY [MESSubEQNo_String];";

            using var conn = new SqlConnection(_targetConnectionString);
            var normalizedLineId = lineId.Trim();
            var sourceRows = (await conn.QueryAsync<IpRowViewModel>(sourceSql, new
            {
                LineId = normalizedLineId
            })).ToList();

            if (sourceRows.Count == 0)
            {
                return new EquipmentImportResult
                {
                    SourceDatabase = SourceDatabaseName,
                    SourceTable = SourceTableName,
                    LineId = normalizedLineId,
                    ImportedCount = 0
                };
            }

            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();

            const string importSql = @"
IF NOT EXISTS (
    SELECT 1
    FROM [dbo].[ip]
    WHERE [LINEID] = @LineId
      AND [EQUIPMENTID] = @EquipmentId
)
BEGIN
    INSERT INTO [dbo].[ip] ([LINEID], [EQUIPMENTID], [ip])
    VALUES (@LineId, @EquipmentId, NULL);
END;";

            foreach (var row in sourceRows)
            {
                await conn.ExecuteAsync(importSql, new
                {
                    row.LineId,
                    row.EquipmentId
                }, transaction);
            }

            transaction.Commit();

            return new EquipmentImportResult
            {
                SourceDatabase = SourceDatabaseName,
                SourceTable = SourceTableName,
                LineId = normalizedLineId,
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
  AND [EQUIPMENTID] = @EquipmentId;";

            using var conn = new SqlConnection(_targetConnectionString);
            var affectedRows = await conn.ExecuteAsync(sql, new
            {
                request.LineId,
                request.EquipmentId,
                Ip = string.IsNullOrWhiteSpace(request.Ip) ? null : request.Ip.Trim()
            });

            if (affectedRows == 0)
            {
                throw new InvalidOperationException("在 dbo.ip 找不到要更新的資料。");
            }
        }
    }
}
