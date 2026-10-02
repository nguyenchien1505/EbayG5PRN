using DbUp;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("EbayGroup5.Database.Tests")]
namespace EbayGroup5.Database;

public static class MigrationRunner
{
    public static int Run(string connectionString)
    {
        var connection = new SqlConnectionStringBuilder(connectionString);
        var database = connection.InitialCatalog;
        ValidateDatabaseName(database);
        var baseline = Baseline.ReadTableDdl(); // Verify before any mutation.
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        using var gate = new SqlConnection(master.ConnectionString);
        gate.Open();
        using var mutex = gate.CreateCommand();
        mutex.CommandTimeout = 40;
        mutex.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@name, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=30000; SELECT @r;";
        mutex.Parameters.AddWithValue("@name", "EbayGroup5.Migrations:" + database);
        if (Convert.ToInt32(mutex.ExecuteScalar()) < 0) throw new InvalidOperationException("Migration lock unavailable.");
        try
        {
            // The lock connection remains open until upgrade finishes, including database creation.
            using var create = gate.CreateCommand();
            create.CommandText = $"IF DB_ID(@name) IS NULL CREATE DATABASE [{database}];";
            create.Parameters.AddWithValue("@name", database);
            create.ExecuteNonQuery();
            using var target = new SqlConnection(connectionString);
            target.Open();
            using var guard = target.CreateCommand();
            guard.CommandText = "SELECT CASE WHEN OBJECT_ID('dbo.SchemaVersions','U') IS NULL AND EXISTS(SELECT 1 FROM sys.tables WHERE is_ms_shipped=0) THEN 1 ELSE 0 END";
            if (Convert.ToInt32(guard.ExecuteScalar()) != 0)
                throw new InvalidOperationException("Unjournaled nonempty database. Review metadata before explicit adoption; nothing was reset.");

            var scripts = new List<SqlScript> { new("0001_Baseline.sql", baseline) };
            var assembly = typeof(MigrationRunner).Assembly;
            foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.Contains(".Scripts.") && n.EndsWith(".sql")).Order(StringComparer.Ordinal))
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
                scripts.Add(new SqlScript(resource[(resource.IndexOf(".Scripts.", StringComparison.Ordinal) + 9)..], reader.ReadToEnd()));
            }
            var result = BuildEngine(connectionString, scripts).PerformUpgrade();
            if (!result.Successful) throw new InvalidOperationException("Migration failed; transaction rolled back.", result.Error);
            return result.Scripts.Count();
        }
        finally
        {
            using var release = gate.CreateCommand();
            release.CommandText = "EXEC sys.sp_releaseapplock @Resource=@name, @LockOwner='Session';";
            release.Parameters.AddWithValue("@name", "EbayGroup5.Migrations:" + database);
            release.ExecuteNonQuery();
        }
    }

    internal static UpgradeEngine BuildEngine(string connectionString, IEnumerable<SqlScript> scripts) =>
        DeployChanges.To.SqlDatabase(connectionString)
            .WithScripts(scripts)
            .WithTransaction()
            .JournalToSqlTable("dbo", "SchemaVersions")
            .LogToNowhere()
            .Build();

    public static void ValidateDatabaseName(string name)
    {
        if (name != "EbayGroup5" && !Regex.IsMatch(name, @"\AEbayGroup5_Test_[a-zA-Z0-9]{1,64}\z"))
            throw new ArgumentException("Target must be EbayGroup5 or an isolated EbayGroup5_Test_<suffix> database.");
    }
}
