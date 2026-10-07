// F05 load tools (docs/architecture/LOAD.md), driven by scripts/Test-AppStack.ps1 -Load.
//   seed --scale <0..1> --out <dir>   SQL from EBAY_LOAD_SQL, user password from EBAY_LOAD_PASSWORD (never arguments)
//   chat --base https://localhost:N --ca <ca.crt> --pairs <csv> --count N --seconds S --out <file.json>
using EbayGroup5.Load;

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var i = 1; i + 1 < args.Length; i += 2) options[args[i]] = args[i + 1];
string Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new InvalidOperationException($"{name} is required.");
try
{
    switch (args.FirstOrDefault())
    {
        case "seed":
            await LoadSeed.RunAsync(Env("EBAY_LOAD_SQL"), Env("EBAY_LOAD_PASSWORD"), double.Parse(options["--scale"], System.Globalization.CultureInfo.InvariantCulture), options["--out"]);
            return 0;
        case "chat":
            return await ChatLoad.RunAsync(options["--base"], options["--ca"], Env("EBAY_LOAD_PASSWORD"), options["--pairs"],
                int.Parse(options["--count"]), int.Parse(options["--seconds"]), options["--out"]);
        default:
            Console.Error.WriteLine("Usage: seed --scale <0..1> --out <dir> | chat --base <url> --ca <file> --pairs <csv> --count N --seconds S --out <file>");
            return 2;
    }
}
catch (Exception ex)
{
    // No SQL text, data or credentials: only the failure type and message of our own checks.
    Console.Error.WriteLine($"Load tool failed: {ex.GetType().Name}: {(ex is InvalidOperationException or ArgumentException ? ex.Message : "see the stack logs")}");
    // SQL error numbers only (no SQL text or data).
    for (var inner = ex; inner != null; inner = inner.InnerException)
        if (inner is Microsoft.Data.SqlClient.SqlException sql)
            Console.Error.WriteLine("SQL error numbers: " + string.Join(",", sql.Errors.Cast<Microsoft.Data.SqlClient.SqlError>().Select(e => e.Number)));
    return 1;
}
