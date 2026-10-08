# Chạy dự án từ clone sạch

## Chuẩn bị

Windows x64, Git, PowerShell 7 (`pwsh`), Docker Desktop **Linux containers**,
Docker Compose, Chrome, Node 20+, kubectl (Docker Desktop). Dành tối thiểu 8 GB RAM
cho Docker khi chạy kind/test; không chạy các bài SQL/load nặng song song.
.NET SDK **8.0.425** theo `global.json`; Java chỉ cần cho JMeter.
Repo private yêu cầu tài khoản GitHub được cấp quyền, không đưa token vào URL/lệnh.

```powershell
git clone https://github.com/Twiash/EbayGroup5.git
Set-Location EbayGroup5
pwsh -NoProfile -File scripts/Install-Dotnet8.ps1
. ./scripts/Use-LocalDotnet.ps1
dotnet restore --locked-mode
dotnet build -c Release --no-restore
pwsh -NoProfile -File scripts/Verify-Planning.ps1
```

Không cần `../ProjectCode` hoặc file đề bài bên ngoài: nguồn/schema/contract đã ở repo.
Không copy `.env`, `secrets`, `bin/obj`, `artifacts` từ máy người khác.
Installer SDK/kind dùng checksum ghim, PATH chỉ đổi trong phiên PowerShell.

## Cách chính: demo Kubernetes local

```powershell
pwsh -NoProfile -File scripts/Start-K8s.ps1 -Demo -DemoBusinessDaySeconds 30
kubectl --context kind-ebaygroup5 -n ebaygroup5 get pods,pvc,jobs
```

Lệnh tạo/khởi động kind `ebaygroup5`, build web/migrate, tạo credentials/TLS nếu thiếu,
apply14 migration → seed A06-v1 → cấp auth riêng →web/nginx2 replica.
Seed/auth chạy lại giữ dữ liệu và mật khẩu cũ. Không xoá database/volume.
Mở **https://localhost:9443** (HTTP9080 redirect). SQL/Redis readiness phải đạt;
migration jobs Complete, web2 Ready, PVC SQL/Redis Bound.

Ba email:

| Role | Email | Mật khẩu local |
|---|---|---|
| Buyer | buyer.a06@example.invalid | key `demo-buyer-password` |
| Seller | seller.a06@example.invalid | key `demo-seller-password` |
| Admin | admin.a06@example.invalid | key `demo-admin-password` |

Mật khẩu ở **`secrets/k8s/secrets.json`**, mở trên máy demo khi cần,
không copy lên chat/Git/slide/log/screenshot. Mỗi người dùng một browser profile/context.
Admin chỉ từ mạng demo đã allowlist, cần role/credential như người dùng khác.
Nếu identity/email đã bị sửa, auth provisioning sẽ từ chối; không reset password để lách.

TLS: CA ở `secrets/tls/ca.crt`. Khi browser cảnh báo, kiểm đúng localhost/certificate;
có thể import CA bằng Certificate Manager vào Current User Trusted Root theo quyết định của bạn.
Không tắt kiểm TLS toàn bộ hoặc dùng chứng chỉ local cho production.
Script/test tự xác minh CA/hostname hoặc SPKI của đúng chứng chỉ run.

`-Demo` rút ngắn **một ngày làm việc thành30s**, không bỏ điều kiện owner/thời hạn.
Tạo **đơn mới** trong mỗi buổi. Ngày seed tháng9 chỉ là fixture lịch sử;
không dùng đơn đó làm căn cứ demo INR/Return/feedback lâu dài.

```powershell
# Dừng node nhưng giữ dữ liệu
pwsh -NoProfile -File scripts/Start-K8s.ps1 -Stop
# Bật lại demo, giữ credentials/PVC
pwsh -NoProfile -File scripts/Start-K8s.ps1 -Demo
# Triển khai chế độ thời gian thật (DemoBusinessDaySeconds=0)
pwsh -NoProfile -File scripts/Start-K8s.ps1
```

Chuyển clock cần dùng đơn mới, không thay đổi hạn của case đã mở.
CD thông thường cũng dùng clock0, nên bật lại `-Demo` trước buổi trình bày nếu vừa CD.
Không dùng `-Delete`, `down -v` hoặc prune để xử lý lỗi thường.

## Cách khác: Compose HTTPS

```powershell
pwsh -NoProfile -File scripts/Initialize-LocalEnvironment.ps1
docker compose up -d --wait --wait-timeout 180
pwsh -NoProfile -File scripts/Initialize-Database.ps1
pwsh -NoProfile -File scripts/Seed-Demo.ps1
pwsh -NoProfile -File scripts/Enable-DemoAuth.ps1
pwsh -NoProfile -File scripts/New-LocalTlsCertificate.ps1
pwsh -NoProfile -File scripts/Start-AppStack.ps1
```

Mở https://localhost:8443, mật khẩu demo ở `.env.auth` (khác kind credentials).
Đây là chế độ **thời gian thật**. Kịch bản buổi học có clock rút ngắn nên dùng kind ở trên.
Dừng ứng dụng `Start-AppStack.ps1 -Stop` giữ SQL/Redis/volume.
Lệnh chi tiết/prerequisites: [LOCAL_INFRA](../runbooks/LOCAL_INFRA.md),
[DATABASE](../runbooks/DATABASE.md), [DEPLOYMENT](../architecture/DEPLOYMENT.md).

## Kiểm thử tái lập

```powershell
. ./scripts/Use-LocalDotnet.ps1
pwsh -NoProfile -File scripts/Test-CI.ps1
pwsh -NoProfile -File scripts/Test-AppStack.ps1 -DemoClockSeconds 10
```

Test-CI build/SQL/HTTP/WSS/Redis/Node, app stack tiếp theo chạy browser journeys +
INR mới/draft/escalation/admin + TLS smoke trên **project riêng**.
Pass tự dọn tài nguyên test; fail giữ logs/stack để chẩn đoán.
Các bài tải/K8s/rolling có thể dài hơn: [ROLLING](../runbooks/ROLLING.md),
[CD](../runbooks/CD.md), [LOAD](../architecture/LOAD.md).

Kiểm chứng bàn giao bằng clone GitHub mới (chỉ trên commit đã push):

```powershell
pwsh -NoProfile -File scripts/Test-Handover.ps1 -ExpectedSha (git rev-parse HEAD).Trim() -Ref main
```

Script tạo checkout riêng trong `artifacts/handover/clean-<hex>/repo`, không copy secrets,
test/build từ source đã commit; giữ report/log kiểm chứng. Không dùng thư mục nguồn để
giả lập một clone sạch. Khi kiểm nhánh task, truyền đúng `-Ref task/...`.

## Khi lỗi

| Dấu hiệu | Kiểm tra |
|---|---|
| Docker/SQL chưa ready | Docker Linux, RAM/port; `get pods`/events; không reset volume |
| Catalog trống | Đã chạy `Start-K8s -Demo` và job seed Complete? Đúng9443 hay8443? |
| Login sai | Đúng email, mật khẩu của backend kind/compose, đợi Retry-After nếu429 |
| Admin403 | Role Admin và IP allowlist; không nới network ra wildcard |
| Chưa mở được INR | Đơn mới, chưa Delivered, đã qua5 ngày demo (150s khi30s/day)? |
| Seed case hết hạn | Tạo đơn mới; không sửa ngày lịch sử/SQL để làm form mở |
| Không reload draft | Đợi “Draft saved”, đúng profile/owner/form, kiểm API/mạng |

Backup database/volume cần thực hiện trước nâng hạ tầng theo
[LOCAL_INFRA](../runbooks/LOCAL_INFRA.md). Không đưa secrets vào gói bàn giao công khai.
