using Dapper;
using Microsoft.Data.SqlClient;

namespace AutoRunDailyReport.Repositories
{
    public class NoticeRepository
    {
        private readonly string _connectionString;

        public NoticeRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("TargetConnection")
                ?? throw new InvalidOperationException("TargetConnection is missing.");
        }

        public async Task EnsureTableExistsAsync()
        {
            const string sql = @"
IF OBJECT_ID(N'[dbo].[notice]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[notice] (
        [notice] NVARCHAR(MAX) NOT NULL
    );
END;";

            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql);
        }

        public async Task<IReadOnlyList<string>> GetAllAsync()
        {
            await EnsureTableExistsAsync();

            const string sql = "SELECT [notice] FROM [dbo].[notice];";

            using var conn = new SqlConnection(_connectionString);
            var rows = await conn.QueryAsync<string>(sql);
            return rows.ToList();
        }

        public async Task AddAsync(string noticeText)
        {
            await EnsureTableExistsAsync();

            const string sql = @"
INSERT INTO [dbo].[notice] ([notice])
VALUES (@NoticeText);";

            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql, new { NoticeText = noticeText });
        }
    }
}
