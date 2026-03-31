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
    pk_SheetLink      INT           NOT NULL PRIMARY KEY,
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
            const string sql = "SELECT * FROM dbo.MesMachinesMeta;";
            using var conn = new SqlConnection(_connectionString);
            return await conn.QueryAsync<MesMachinesMetaDto>(sql);
        }

        public async Task<MesMachinesMetaDto?> GetByIdAsync(int pkSheetLink)
        {
            const string sql = "SELECT * FROM dbo.MesMachinesMeta WHERE pk_SheetLink = @pk_SheetLink;";
            using var conn = new SqlConnection(_connectionString);
            return await conn.QueryFirstOrDefaultAsync<MesMachinesMetaDto>(sql, new { pk_SheetLink = pkSheetLink });
        }

        public async Task UpsertAsync(MesMachinesMetaDto meta)
        {
            await EnsureTableExistsAsync();

            const string sql = @"
MERGE dbo.MesMachinesMeta AS target
USING (SELECT @pk_SheetLink AS pk_SheetLink) AS source
    ON target.pk_SheetLink = source.pk_SheetLink
WHEN MATCHED THEN
    UPDATE SET
        State          = @State,
        AiotOwner      = @AiotOwner,
        Owner          = @Owner,
        Schedule       = @Schedule,
        TestStatus     = @TestStatus,
        FirstADeadline = @FirstADeadline,
        Illustrate     = @Illustrate,
        UpdatedAt      = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (pk_SheetLink, State, AiotOwner, Owner, Schedule,
            TestStatus, FirstADeadline, Illustrate, UpdatedAt)
    VALUES (@pk_SheetLink, @State, @AiotOwner, @Owner, @Schedule,
            @TestStatus, @FirstADeadline, @Illustrate, GETDATE());";

            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql, meta);
        }
    }
}
