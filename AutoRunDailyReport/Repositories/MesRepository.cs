
using Dapper;
using Microsoft.Data.SqlClient;
using AutoRunDailyReport.Models;



namespace AutoRunDailyReport.Repositories
{
    public class MesRepository
    {

        private readonly string _connectionString;

        public MesRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public async Task<IEnumerable<MesMachineDto>> GetAllMesMachinesAsync()
        {
            const string sql = @"
SELECT
    A.pk_SheetLink,
    REPLACE(A.MESMachineName_String, '<br>', '') AS MESMachineName,
    B.EQIQDateEE_Time,
    C.InLineTestDate_Time,
    D.MESSubEQName_String,
    E.Layout,
    E.Line,
    E.Vendor,
    E.Section,
    E.Process,
    F.MESSubEQNo_String,
    G.MESMachineNo_String,
    H.CheckTime
FROM dbo.C_PC_MESMachineName A
LEFT JOIN dbo.C_EE_EQIQDate B ON A.pk_SheetLink = B.pk_SheetLink
LEFT JOIN dbo.C_AIOT_InLineTestDate C ON A.pk_SheetLink = C.pk_SheetLink
LEFT JOIN dbo.C_PC_MESSubEQName D ON A.pk_SheetLink = D.pk_SheetLink
LEFT JOIN dbo.C_BasicConfig E ON A.pk_SheetLink = E.fk_SheetLink
LEFT JOIN dbo.C_PC_MESSubEQNo F ON A.pk_SheetLink = F.pk_SheetLink
LEFT JOIN dbo.C_PC_MESMachineNo G ON A.pk_SheetLink = G.pk_SheetLink
LEFT JOIN dbo.C_TimeChecked H ON A.pk_SheetLink = G.pk_SheetLink
ORDER BY C.InLineTestDate_Time DESC;";

            using var conn = new SqlConnection(_connectionString);
            return await conn.QueryAsync<MesMachineDto>(sql);
        }

        public async Task<IEnumerable<MesMachineDto>> GetLatestMesMachinesAsync()
        {
            const string sql = @"
SELECT TOP 100
    A.pk_SheetLink,
    REPLACE(A.MESMachineName_String, '<br>', '') AS MESMachineName,
    B.EQIQDateEE_Time,
    C.InLineTestDate_Time,
    D.MESSubEQName_String,
    E.Layout,
    E.Line,
    E.Vendor,
    E.Section,
    E.Process,
    F.MESSubEQNo_String,
    G.MESMachineNo_String
FROM dbo.C_PC_MESMachineName A
LEFT JOIN dbo.C_EE_EQIQDate B ON A.pk_SheetLink = B.pk_SheetLink
LEFT JOIN dbo.C_AIOT_InLineTestDate C ON A.pk_SheetLink = C.pk_SheetLink
LEFT JOIN dbo.C_PC_MESSubEQName D ON A.pk_SheetLink = D.pk_SheetLink
LEFT JOIN dbo.C_BasicConfig E ON A.pk_SheetLink = E.fk_SheetLink
LEFT JOIN dbo.C_PC_MESSubEQNo F ON A.pk_SheetLink = F.pk_SheetLink
LEFT JOIN dbo.C_PC_MESMachineNo G ON A.pk_SheetLink = G.pk_SheetLink
ORDER BY C.InLineTestDate_Time DESC;";

            using var conn = new SqlConnection(_connectionString);
            return await conn.QueryAsync<MesMachineDto>(sql);
        }
    }
}
