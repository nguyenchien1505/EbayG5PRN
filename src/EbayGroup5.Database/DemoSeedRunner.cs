using Microsoft.Data.SqlClient;

namespace EbayGroup5.Database;

public static class DemoSeedRunner
{
    public static bool Run(string connectionString)
    {
        MigrationRunner.ValidateDatabaseName(new SqlConnectionStringBuilder(connectionString).InitialCatalog);
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 60;
        command.CommandText = """
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=N'EbayGroup5.DemoSeed',
                @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000;
            IF @result < 0 THROW 51000, 'Demo seed lock unavailable.', 1;
            SELECT COUNT(*) FROM dbo.DemoSeedHistory WHERE Version=N'A06-v1';
            """;
        if (Convert.ToInt32(command.ExecuteScalar()) != 0)
        {
            transaction.Commit();
            return false;
        }
        using var reader = new StreamReader(typeof(DemoSeedRunner).Assembly.GetManifestResourceStream("DemoSeed.v1.sql")!);
        command.CommandText = reader.ReadToEnd();
        command.ExecuteNonQuery();
        command.CommandText = "INSERT dbo.DemoSeedHistory(Version,AppliedAtUtc) VALUES(N'A06-v1',SYSUTCDATETIME());";
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }
}
