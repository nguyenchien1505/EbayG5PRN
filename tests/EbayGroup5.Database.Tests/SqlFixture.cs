using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace EbayGroup5.Database.Tests;

internal static class SqlFixture
{
    public static string NewTarget()
    {
        var raw = Environment.GetEnvironmentVariable("EBAY_TEST_SQL")
            ?? throw new InvalidOperationException("Run scripts/Test-Database.ps1; SQL tests require an explicit local admin connection.");
        var builder = new SqlConnectionStringBuilder(raw)
        {
            InitialCatalog = "EbayGroup5_Test_" + Guid.NewGuid().ToString("N")
        };
        return builder.ConnectionString;
    }

    public static void CreateEmpty(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!Regex.IsMatch(builder.InitialCatalog, @"\AEbayGroup5_Test_[a-zA-Z0-9]+\z")) throw new InvalidOperationException("Test target guard.");
        var name = builder.InitialCatalog;
        builder.InitialCatalog = "master";
        Execute(builder.ConnectionString, $"CREATE DATABASE [{name}]");
    }

    public static void Execute(string connectionString, string sql)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        command.ExecuteNonQuery();
    }

    public static string Scalar(string connectionString, string sql)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        return Convert.ToString(command.ExecuteScalar()) ?? "";
    }

    public static HashSet<string> Rows(string connectionString, string sql)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();
        var rows = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) rows.Add(reader.GetString(0));
        return rows;
    }

    // Applies the baseline plus every DbUp script ordered before `version` (later scripts may depend on it); returns how many remain.
    public static int MigrateBefore(string connectionString, string version)
    {
        CreateEmpty(connectionString);
        var scripts = new List<DbUp.Engine.SqlScript> { new("0001_Baseline.sql", Baseline.ReadTableDdl()) };
        var assembly = typeof(MigrationRunner).Assembly;
        var names = assembly.GetManifestResourceNames().Where(x => x.Contains(".Scripts.") && x.EndsWith(".sql")).Order(StringComparer.Ordinal).ToList();
        var remaining = 0;
        foreach (var resource in names)
        {
            var name = resource[(resource.IndexOf(".Scripts.", StringComparison.Ordinal) + 9)..];
            if (string.CompareOrdinal(name, version) >= 0) { remaining++; continue; }
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            scripts.Add(new(name, reader.ReadToEnd()));
        }
        if (!MigrationRunner.BuildEngine(connectionString, scripts).PerformUpgrade().Successful) throw new InvalidOperationException("Partial upgrade failed.");
        return remaining;
    }

    // Compare structural metadata, not SQL-generated constraint names or object IDs.
    public const string Metadata = """
        SELECT CONCAT('C|',SCHEMA_NAME(t.schema_id),'|',t.name,'|',c.name,'|',ty.name,'|',c.max_length,'|',c.precision,'|',c.scale,'|',c.is_nullable,'|',c.is_identity,'|',CONVERT(nvarchar(30),ic.seed_value),'|',CONVERT(nvarchar(30),ic.increment_value),'|',c.collation_name)
        FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
        LEFT JOIN sys.identity_columns ic ON ic.object_id=c.object_id AND ic.column_id=c.column_id
        WHERE t.is_ms_shipped=0
        UNION ALL
        SELECT CONCAT('K|',SCHEMA_NAME(t.schema_id),'|',t.name,'|',c.name,'|',i.type,'|',i.is_unique,'|',i.is_primary_key,'|',i.is_unique_constraint,'|',k.key_ordinal,'|',k.is_descending_key,'|',i.has_filter,'|',i.filter_definition,'|',i.is_disabled)
        FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id JOIN sys.index_columns k ON k.object_id=i.object_id AND k.index_id=i.index_id JOIN sys.columns c ON c.object_id=k.object_id AND c.column_id=k.column_id
        WHERE t.is_ms_shipped=0
        UNION ALL
        SELECT CONCAT('F|',SCHEMA_NAME(t.schema_id),'|',t.name,'|',c.name,'|',SCHEMA_NAME(rt.schema_id),'|',rt.name,'|',rc.name,'|',f.delete_referential_action,'|',f.update_referential_action,'|',f.is_disabled,'|',f.is_not_trusted)
        FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
        JOIN sys.tables t ON t.object_id=fc.parent_object_id JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=fc.parent_column_id
        JOIN sys.tables rt ON rt.object_id=fc.referenced_object_id JOIN sys.columns rc ON rc.object_id=rt.object_id AND rc.column_id=fc.referenced_column_id
        UNION ALL
        SELECT CONCAT('D|',SCHEMA_NAME(t.schema_id),'|',t.name,'|',c.name,'|',d.definition) FROM sys.default_constraints d JOIN sys.tables t ON t.object_id=d.parent_object_id JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=d.parent_column_id
        UNION ALL
        SELECT CONCAT('X|',SCHEMA_NAME(t.schema_id),'|',t.name,'|',c.definition,'|',c.is_disabled,'|',c.is_not_trusted) FROM sys.check_constraints c JOIN sys.tables t ON t.object_id=c.parent_object_id
        """;
}
