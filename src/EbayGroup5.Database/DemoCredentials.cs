using EbayGroup5.Infrastructure.Authentication;
using Microsoft.Data.SqlClient;

namespace EbayGroup5.Database;

public static class DemoCredentials
{
    public static int Enable(string connectionString, IReadOnlyDictionary<string, string> passwords)
    {
        MigrationRunner.ValidateDatabaseName(new SqlConnectionStringBuilder(connectionString).InitialCatalog);
        string[] roles = ["Buyer", "Seller", "Admin"];
        if (roles.Any(role => !passwords.TryGetValue(role, out var password) || !AccountService.ValidPassword(password)))
            throw new ArgumentException("Provide a valid separate password for each demo role.");
        if (passwords.Values.Distinct(StringComparer.Ordinal).Count() != 3)
            throw new ArgumentException("Demo passwords must be distinct.");
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "IF NOT EXISTS(SELECT 1 FROM dbo.DemoSeedHistory WHERE Version=N'A06-v1') THROW 51010, 'Seed A06 first.', 1;";
        command.ExecuteNonQuery();
        var count = 0;
        foreach (var role in roles)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@email", role.ToLowerInvariant() + ".a06@example.invalid");
            command.Parameters.AddWithValue("@name", "Demo " + role);
            command.Parameters.AddWithValue("@role", role);
            command.Parameters.AddWithValue("@hash", AccountService.HashPassword(passwords[role]));
            command.CommandText = """
                IF NOT EXISTS(SELECT 1 FROM dbo.[User] WITH(UPDLOCK,HOLDLOCK) WHERE email=@email AND username=@name AND role=@role)
                    THROW 51011, 'Demo identity changed or missing; review manually.', 1;
                UPDATE dbo.[User] SET password=@hash WHERE email=@email AND username=@name AND role=@role AND password IS NULL;
                """;
            count += command.ExecuteNonQuery();
        }
        transaction.Commit();
        return count;
    }
}
