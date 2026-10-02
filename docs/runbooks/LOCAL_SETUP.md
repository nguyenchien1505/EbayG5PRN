# Chạy scaffold local — A03

Chạy từ repo root bằng PowerShell 7 trên Windows x64. Chưa cần SQL Server/Redis/Docker ở checkpoint này.

Sau A04, infrastructure có [runbook riêng](LOCAL_INFRA.md). Web shell vẫn chạy độc lập; A05 đã bổ sung DbContext/runtime config và [runbook database](DATABASE.md).

## SDK

Pin SDK **8.0.425**, kèm .NET/ASP.NET Core runtime **8.0.31**. Phiên bản và SHA512 được đối chiếu [metadata Microsoft .NET 8](https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json) ngày 02/10/2026 (release 08/09/2026). `global.json` cho phép latestPatch cùng feature band; không tự nhảy SDK 10. Khi nâng patch phải cập nhật installer và kiểm tra lại lock/build.

```powershell
pwsh -NoProfile -File scripts/Install-Dotnet8.ps1
. ./scripts/Use-LocalDotnet.ps1
dotnet --version
dotnet restore --locked-mode
dotnet build --no-restore -c Release
dotnet run --project src/EbayGroup5.Web -c Release --no-build --no-launch-profile -- --urls http://127.0.0.1:5085
```

Installer kiểm tra SHA512, giải nén vào `%LOCALAPPDATA%/EbayGroup5/dotnet`, không gỡ SDK hệ thống hay đổi PATH vĩnh viễn. Dot-source `Use-LocalDotnet.ps1` ở mỗi terminal mới. Máy đã có SDK tương thích trên PATH có thể bỏ hai lệnh setup đầu; vẫn xác minh `dotnet --version` tại repo root. Installer này chỉ phục vụ Windows x64, container/Linux dùng SDK tương thích trong task hạ tầng.

Mở `http://127.0.0.1:5085/`. Dừng bằng Ctrl+C. Nếu cổng bận, đổi cổng trong lệnh; không dừng tiến trình lạ. App chạy Production mặc định; middleware trả lỗi 500 tổng quát. HTTP loopback này chỉ là scaffold; chưa nghiệm thu HTTPS/WSS, auth hoặc readiness của các dịch vụ.

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
