CREATE TABLE dbo.DemoSeedHistory (
    Version nvarchar(100) NOT NULL PRIMARY KEY,
    AppliedAtUtc datetime2 NOT NULL
);
