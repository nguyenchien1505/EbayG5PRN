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
        // Ancillary B06 snapshot only for the newly created v1 Buyer. Original seed SQL/columns remain immutable.
        // Dynamic SQL keeps the runner compatible with a pre-B06 schema in upgrade tests.
        command.CommandText = """
            IF COL_LENGTH(N'dbo.OrderTable', N'SellerId') IS NOT NULL
                EXEC(N'UPDATE o SET SellerId=s.id FROM dbo.OrderTable o
                    JOIN dbo.[User] b ON b.id=o.buyerId AND b.email=N''buyer.a06@example.invalid''
                    CROSS JOIN dbo.[User] s WHERE s.email=N''seller.a06@example.invalid'' AND o.SellerId IS NULL;');
            """;
        command.ExecuteNonQuery();
        // G01 (UI_ACCEPTANCE.md): DbUp 0011 backfilled the estimated delivery date only for orders that existed when it ran, so on a
        // fresh database the seeded orders had none and item-not-received could never open. Same formula as 0011, new v1 orders only.
        command.CommandText = """
            IF COL_LENGTH(N'dbo.OrderTable', N'EstimatedDeliveryUtc') IS NOT NULL
                EXEC(N'UPDATE o SET EstimatedDeliveryUtc = DATEADD(day,
                        CASE ((DATEDIFF(day, ''19000101'', o.orderDate) % 7) + 7) % 7 WHEN 5 THEN 6 WHEN 6 THEN 5 ELSE 7 END, CAST(o.orderDate AS datetime2(7)))
                    FROM dbo.OrderTable o JOIN dbo.[User] b ON b.id=o.buyerId AND b.email=N''buyer.a06@example.invalid''
                    WHERE o.orderDate IS NOT NULL AND o.EstimatedDeliveryUtc IS NULL;');
            """;
        command.ExecuteNonQuery();
        command.CommandText = "INSERT dbo.DemoSeedHistory(Version,AppliedAtUtc) VALUES(N'A06-v1',SYSUTCDATETIME());";
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }
}
