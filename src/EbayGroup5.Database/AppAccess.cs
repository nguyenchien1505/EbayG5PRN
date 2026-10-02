using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace EbayGroup5.Database;

public static class AppAccess
{
    public static void Provision(string migrationConnection, string login, string password)
    {
        var builder = new SqlConnectionStringBuilder(migrationConnection);
        MigrationRunner.ValidateDatabaseName(builder.InitialCatalog);
        if (login != "EbayGroup5App" && !Regex.IsMatch(login, @"\AEbayGroup5_TestApp_[a-zA-Z0-9]{1,64}\z"))
            throw new ArgumentException("Unsupported app login name.");
        if (!Regex.IsMatch(password, @"\A[A-Za-z0-9!_-]{24,128}\z"))
            throw new ArgumentException("Invalid app password format.");
        // Identifiers and literal password are tightly constrained; do not log this command.
        using var connection = new SqlConnection(migrationConnection);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF SUSER_ID(@login) IS NULL
                CREATE LOGIN [{login}] WITH PASSWORD=N'{password}', CHECK_POLICY=ON;
            IF DATABASE_PRINCIPAL_ID(@login) IS NULL
                CREATE USER [{login}] FOR LOGIN [{login}];
            IF IS_ROLEMEMBER('ebay_app', @login) <> 1
                ALTER ROLE [ebay_app] ADD MEMBER [{login}];
            """;
        command.Parameters.AddWithValue("@login", login);
        command.ExecuteNonQuery();
        // Do not rotate an existing login silently. A mismatch is detected through this connection.
        builder.UserID = login;
        builder.Password = password;
        builder.IntegratedSecurity = false;
        using var verify = new SqlConnection(builder.ConnectionString);
        verify.Open();
    }
}
