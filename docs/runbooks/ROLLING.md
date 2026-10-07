# Kiểm rolling update / rollback — F08

Chạy từ repo root với PowerShell 7, SDK .NET 8, Docker Linux, kubectl.
Contract và gate: [ROLLING](../architecture/ROLLING.md).

```powershell
. ./scripts/Use-LocalDotnet.ps1
pwsh -NoProfile -File scripts/Test-RollingUpdate.ps1 -Profile Short
pwsh -NoProfile -File scripts/Test-RollingUpdate.ps1 -Profile Full
```

Không chạy cùng Test-CI hoặc load khác để tránh tranh RAM SQL. Full seed dataset
100k message/10k review/1k dispute rồi đo 10 phút; Short dùng 1/10 dataset và 2 phút.
Cluster luôn là `ebaygroup5-rolling-<hex>`, không phải cluster demo `ebaygroup5`.

Report ở `artifacts/rolling/<hex>/report.json`; SQL credentials/CSV/TLS ở cùng run
nhưng **không được upload**. CI chỉ upload report. Pass xoá đúng cluster test/image tags;
`-KeepCluster` hoặc fail giữ cluster để đọc log. `Clear-TestArtifacts.ps1 -Apply`
chỉ dọn cluster test có prefix strict, không dọn cluster người dùng.

Đọc `http5xx/networkErrors/httpErrors`, số ACK/delivery/SQL missing/duplicate,
max recovery và samples mỗi phase. Một report trống/không reconnect/không đổi pod
không phải minh chứng đạt. Rollback chỉ ứng dụng qua revision, không rollback database.
