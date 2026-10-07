# CD vào Kubernetes local — F07

Contract và ranh giới: [CD](../architecture/CD.md). Chỉ owner repo deploy thủ công từ main.
Runner nhận một job, tự gỡ đăng ký, không service. Máy cần đang bật, Docker Linux,
kubectl, PowerShell 7, Node 20+, git và phiên gh đã đăng nhập đúng owner.

1. `main` đã push và CI `ci.yml` success trên đúng SHA (CI cũ cùng tree nhưng SHA khác không đủ).
2. Cluster và secrets đã khởi tạo bằng `scripts/Start-K8s.ps1`. Node dừng sẽ tự start lại,
   giữ SQL/Redis/PVC. Không đổi mật khẩu hoặc xóa database. Dùng CA local hiện có.
3. Từ repo root, chạy:

```powershell
pwsh -NoProfile -File scripts/Start-CdRunner.ps1
```

Script tải runner Windows x64 v2.337.0 chính thức, kiểm SHA-256, đăng ký với label riêng,
khởi động process ẩn, dispatch `cd.yml` trên main, đợi run (tối đa 65 phút) và in URL.
GitHub-hosted Ubuntu build web/migrate, chuyển artifact vào job self-hosted.
Runner chỉ import image đã kiểm manifest/checksum/OCI revision, không build app tại local.

Mặc định state là `secrets/` của repo nơi bootstrap được chạy, không phải checkout của Actions.
Máy khác có thể dùng `-StateRoot <thư-mục-secrets-đã-có>`. Không ghi path cá nhân/secrets vào workflow.

Thành công: `https://localhost:9443`, 2 web Ready, migration Job Complete,
report trên run GitHub và `artifacts/cd/runner-<hex>.json` tại repo gốc.
Runner được deregister và credential config của lần này bị xóa cả khi fail.
Binary, work và log local ở `artifacts/cd-runners/<hex>` (ignore), không upload secret/log runner.

Nếu build/CI gate fail: xem run GitHub, sửa nguyên nhân, chạy lại bootstrap để có runner mới.
Không giữ runner ephemeral để chạy lại; không tự bấm re-run deploy với label cũ đã biến mất.
Nếu deploy fail: PVC/database được giữ. Kiểm tra namespace bằng:

```powershell
kubectl --context kind-ebaygroup5 -n ebaygroup5 get pods,jobs,pvc
```

Không dùng `-Delete`, volume prune hoặc reset database để khắc phục lỗi thường.
F08 sẽ kiểm rolling update/rollback dưới tải; CD success chưa chứng minh zero downtime.
