# Bản đồ source hiện tại

Checkpoint A04. Chỉ mở phần tương ứng task; chưa có nghiệp vụ/schema ứng dụng/auth.

| Đường dẫn từ repo root | Trách nhiệm |
|---|---|
| `global.json`, `Directory.Build.props`, `EbayGroup5.sln` | Pin SDK, net8.0, nullable, implicit usings và NuGet lock |
| `src/EbayGroup5.Core/` | Domain; hiện chỉ có project, không phụ thuộc web/database |
| `src/EbayGroup5.Application/` | Use cases; hiện tham chiếu Core |
| `src/EbayGroup5.Infrastructure/` | Adapter persistence/integration tương lai; tham chiếu Application (Core bắc cầu) |
| `src/EbayGroup5.Web/Program.cs` | Composition root MVC, static files, route, lỗi chung và `/health/live` |
| `src/EbayGroup5.Web/Controllers/HomeController.cs` | Trang scaffold |
| `src/EbayGroup5.Web/Views/`, `wwwroot/css/site.css` | Razor shell tối thiểu; chưa là UI eBay B03 |
| `src/EbayGroup5.Web/appsettings.json` | Logging/AllowedHosts local; không connection string/secret |
| `scripts/Install-Dotnet8.ps1`, `scripts/Use-LocalDotnet.ps1` | SDK riêng user Windows; giữ SDK hệ thống |
| `compose.yaml`, `.env.example` | SQL/Redis pin digest, loopback, volumes, healthcheck; .env thật ignored |
| `scripts/Initialize-LocalEnvironment.ps1`, `scripts/Use-LocalInfrastructure.ps1` | Sinh secrets không overwrite, nạp connection strings vào terminal |
| `scripts/Verify-LocalInfrastructure.ps1` | Health/auth smoke; tùy chọn recreate/persistence riêng stack |

Web tham chiếu Application + Infrastructure. Chưa thêm package EF Core/SignalR Redis/DbUp hoặc lớp giả để lấp thư mục. NuGet lock hiện chỉ phản ánh project graph; sẽ mở rộng khi thêm dependency thật.

Lệnh chạy: [LOCAL_SETUP](../runbooks/LOCAL_SETUP.md). Thêm đường dẫn test thực tế vào đây khi tạo test project.

SQL/Redis: [runbook](../runbooks/LOCAL_INFRA.md), [contract](LOCAL_INFRA.md); A05 đọc hai file này và compose trước khi thêm baseline/mapping.
