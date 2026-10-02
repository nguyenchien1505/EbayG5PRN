using EbayGroup5.Database;

try
{
    var connection = Environment.GetEnvironmentVariable("ConnectionStrings__MigrationConnection")
        ?? throw new InvalidOperationException("MigrationConnection is required.");
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
