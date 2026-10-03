# Bản đồ source hiện tại

Checkpoint A06. Baseline/mapping và demo seed có; chưa có nghiệp vụ/auth.

| Đường dẫn từ repo root | Trách nhiệm |
|---|---|
| `global.json`, `Directory.Build.props`, `EbayGroup5.sln` | Pin SDK, net8.0, nullable, implicit usings và NuGet lock |
| `src/EbayGroup5.Core/Entities/` | 17 POCO giữ nullable legacy, không phụ thuộc EF/web |
| `src/EbayGroup5.Application/` | Use cases; hiện tham chiếu Core |
| `src/EbayGroup5.Infrastructure/Persistence/EbayDbContext.cs` | Fluent mapping explicit 17 bảng, types/null/identity/FK NO ACTION; dùng Set<T> |
| `src/EbayGroup5.Web/Program.cs` | Composition root MVC, static files, route, lỗi chung và `/health/live` |
| `src/EbayGroup5.Web/Controllers/HomeController.cs` | Trang scaffold |
| `src/EbayGroup5.Web/Views/`, `wwwroot/css/site.css` | Razor shell tối thiểu; chưa là UI eBay B03 |
| `src/EbayGroup5.Web/appsettings.json` | Logging/AllowedHosts local; không connection string/secret |
| `scripts/Install-Dotnet8.ps1`, `scripts/Use-LocalDotnet.ps1` | SDK riêng user Windows; giữ SDK hệ thống |
| `compose.yaml`, `.env.example` | SQL/Redis pin digest, loopback, volumes, healthcheck; .env thật ignored |
| `scripts/Initialize-LocalEnvironment.ps1`, `scripts/Use-LocalInfrastructure.ps1` | Sinh secrets không overwrite, nạp connection strings vào terminal |
| `scripts/Verify-LocalInfrastructure.ps1` | Health/auth smoke; tùy chọn recreate/persistence riêng stack |
| `src/EbayGroup5.Database/` | Console DbUp: Baseline hash/preamble, MigrationRunner lock/journal/transaction, AppAccess login; Scripts SQL mở rộng |
| `scripts/Initialize-Database.ps1`, `scripts/Test-Database.ps1` | Bootstrap app credentials/migrations và kiểm thử SQL cô lập |
| `tests/EbayGroup5.Database.Tests/` | Metadata nguồn độc lập, model/roundtrip/constraints/permissions, upgrade/rerun/rollback/concurrency |

Web tham chiếu Application + Infrastructure, đăng ký DbContext nhưng không tự migrate/seed. EF SQL Server 8.0.31; console DbUp SQL Server 7.2.0 riêng. Không có EF migrations, package Redis backplane hoặc repository lớp bọc dư thừa. NuGet lock gồm dependencies thật.

Lệnh chạy: [LOCAL_SETUP](../runbooks/LOCAL_SETUP.md). Thêm đường dẫn test thực tế vào đây khi tạo test project.

SQL/Redis: [runbook](../runbooks/LOCAL_INFRA.md). Database: [contract](DATABASE.md), [lệnh migrate/test](../runbooks/DATABASE.md). A06 đọc đúng phần seed/DB này; không đọc lại cả dự án.

Seed: `src/EbayGroup5.Database/DemoSeedRunner.cs`, `Seed/v1.sql`, `scripts/Seed-Demo.ps1`; tests `DemoSeedTests.cs`. Contract [DEMO_SEED](DEMO_SEED.md).

CI A07: `.github/workflows/ci.yml`, `scripts/Test-CI.ps1`, `scripts/Verify-Migrations.ps1`, `scripts/migration-checksums.json`. [Contract CI](CI.md).

B01: Infrastructure/Authentication/AccountService; Web/Controllers/AccountController, Models/AccountForms, Views/Account (Register/Login/Me); Database/DemoCredentials; scripts/Enable-DemoAuth.ps1; AccountServiceTests/AccountHttpTests. [AUTH](AUTH.md).

B04 backend checkpoint: Application/Catalog DTO + ICatalogService; Infrastructure/Catalog EF queries; Web/Controllers/CatalogApiController; CatalogTests. UI pending Q-UI-005. [So sánh project cũ/mới](PROJECT_COMPARISON.md).
