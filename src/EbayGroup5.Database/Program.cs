using EbayGroup5.Database;

try
{
    if (args.Length > 1 || (args.Length == 1 && args[0] != "--seed-demo"))
        throw new ArgumentException("Supported option: --seed-demo.");
    var connection = Environment.GetEnvironmentVariable("ConnectionStrings__MigrationConnection")
        ?? throw new InvalidOperationException("MigrationConnection is required.");
    if (args.Length == 1)
    {
        Console.WriteLine(DemoSeedRunner.Run(connection)
            ? "Demo A06-v1 created. Demo users have no login credentials."
            : "Demo A06-v1 already applied; existing data preserved.");
        return 0;
    }
    var count = MigrationRunner.Run(connection);
    Console.WriteLine($"Database ready; applied {count} migration(s).");
    var password = Environment.GetEnvironmentVariable("Database__AppPassword");
    if (!string.IsNullOrEmpty(password))
    {
        AppAccess.Provision(connection, "EbayGroup5App", password);
        Console.WriteLine("Runtime login verified; table CRUD granted, no schema/journal grants.");
    }
    return 0;
}
catch (Exception exception)
{
    // SQL errors may contain SQL text/data. Keep credentials and content out of CLI logs.
    Console.Error.WriteLine($"Database setup failed ({exception.GetType().Name}). Check target, credentials and database runbook; no database was reset.");
    return 1;
}
