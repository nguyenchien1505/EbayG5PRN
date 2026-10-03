using EbayGroup5.Database;

try
{
    if (args.Length > 1 || (args.Length == 1 && args[0] is not ("--seed-demo" or "--enable-demo-auth")))
        throw new ArgumentException("Supported options: --seed-demo, --enable-demo-auth.");
    var connection = Environment.GetEnvironmentVariable("ConnectionStrings__MigrationConnection")
        ?? throw new InvalidOperationException("MigrationConnection is required.");
    if (args.Length == 1 && args[0] == "--enable-demo-auth")
    {
        var passwords = new[] { "Buyer", "Seller", "Admin" }.ToDictionary(role => role,
            role => Environment.GetEnvironmentVariable("Database__DemoPassword" + role) ?? "");
        var enabled = DemoCredentials.Enable(connection, passwords);
        Console.WriteLine($"Enabled {enabled} demo credential(s); existing passwords preserved.");
        return 0;
    }
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
