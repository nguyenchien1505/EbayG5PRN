# Chạy Web local — .NET 8

Chạy từ repo root bằng PowerShell 7 trên Windows x64. Checkpoint C04 cần SQL Server/Redis và database đã migrate; setup Docker theo runbook dưới.

Infrastructure có [LOCAL_INFRA](LOCAL_INFRA.md); DbUp/runtime config theo [DATABASE](DATABASE.md). A03 shell độc lập là checkpoint cũ, startup hiện tại yêu cầu Redis config cho chat backplane.

## SDK

Pin SDK **8.0.427**, kèm .NET/ASP.NET Core runtime **8.0.33** (nâng 27/10/2026 theo Q-SEC-002). Phiên bản và SHA512 được đối chiếu [metadata Microsoft .NET 8](https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json) ngày 02/10/2026 (release 08/09/2026). `global.json` cho phép latestPatch cùng feature band; không tự nhảy SDK 10. Khi nâng patch phải cập nhật installer và kiểm tra lại lock/build.

```powershell
pwsh -NoProfile -File scripts/Install-Dotnet8.ps1
. ./scripts/Use-LocalDotnet.ps1
. ./scripts/Use-LocalInfrastructure.ps1
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet --version
dotnet restore --locked-mode
dotnet build --no-restore -c Release
dotnet run --project src/EbayGroup5.Web -c Release --no-build --no-launch-profile -- --urls http://127.0.0.1:5085
```

Installer kiểm tra SHA512, giải nén vào `%LOCALAPPDATA%/EbayGroup5/dotnet`, không gỡ SDK hệ thống hay đổi PATH vĩnh viễn. Dot-source `Use-LocalDotnet.ps1` ở mỗi terminal mới. Máy đã có SDK tương thích trên PATH có thể bỏ hai lệnh setup đầu; vẫn xác minh `dotnet --version` tại repo root. Installer này chỉ phục vụ Windows x64, container/Linux dùng SDK tương thích trong task hạ tầng.

Mở `http://127.0.0.1:5085/`. Dừng Ctrl+C. C03 yêu cầu SQL+Redis/schema đã setup theo [LOCAL_INFRA](LOCAL_INFRA.md); Use-LocalInfrastructure nạp config không in secrets. Local HTTP dùng Development cho auth cookie, lỗi500 vẫn generic mọi môi trường. Production mặc định cần HTTPS. Nếu đổi port, đặt Chat:AllowedOrigins đúng port; không dừng tiến trình lạ. HTTP/WS loopback và two-host tests chưa nghiệm thu deployed HTTPS/WSS/shared persistent keys/readiness/performance.

## Kiểm tra

Trong terminal thứ hai:

```powershell
(Invoke-WebRequest http://127.0.0.1:5085/).StatusCode
(Invoke-WebRequest http://127.0.0.1:5085/health/live).Content
(Invoke-WebRequest http://127.0.0.1:5085/css/site.css).StatusCode
pwsh -NoProfile -File scripts/Verify-Planning.ps1
git diff --check
```

Kỳ vọng `200`, `Healthy`, `200`. Liveness chỉ xác nhận process nhận request, không chứng minh DB/Redis sẵn sàng. A05 đã có SQL integration tests: chạy `pwsh -NoProfile -File scripts/Test-Database.ps1`. Không dùng health smoke thay test database/nghiệp vụ.

Commit `packages.lock.json` cho từng project. Khi chủ động thêm/nâng NuGet package: restore bình thường, review diff lock, rồi restore locked và build lại. Không sửa schema hoặc sinh EF migration trong setup.

C04 Buyer/Seller mở My eBay →Messages (`/Messages`); Buyer listing/order item Contact seller mở dialog. Hai browser profiles hoặc localhost/127.0.0.1 dùng cookies riêng khi test hai tài khoản; cả origin phải exact. SignalR JS8.0.29 local pinned checksum/license, không CDN/npm install khi chạy app. C05 tự reconnect/rejoin/backoff và SQL catch-up15s/continuation; Refresh đánh thức sớm. Draft/pending memory-only mất khi reload; giữ trang đến Sent. [Recovery/trace runbook](CHAT_RECOVERY.md).

`./scripts/Test-CI.ps1` cần **Node20+** trên PATH bên cạnh SDK/PowerShell7/Docker. Chạy Verify-ClientAssets rồi `node --test tests/chat-client.test.mjs` (14 cases), locked build và190 SQL/HTTP/socket tests trên containers riêng. Test-Database chỉ .NET checks, không thay Node/browser verification. Không dùng user DB để seed/giả peer-role failure; visual helper/test DB artifacts ignored.
