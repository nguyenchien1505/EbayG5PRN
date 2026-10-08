# eBay Clone — Nhóm 5

Dự án môn C#/.NET: Chat, đánh giá, uy tín người bán và khiếu nại, kèm luồng mua hàng tối thiểu để demo độc lập.

**Hiện trạng:** A00–G01 hoàn tất; G02 đang bàn giao. Chat, review/uy tín, khiếu nại,
Docker/Kubernetes, CI/CD và load/rolling đã có bằng chứng. Jira live chờ đồng bộ theo yêu cầu user.
[Tiến độ](docs/START_HERE.md), [ma trận](docs/requirements/MATRIX.md), [evidence](docs/evidence/INDEX.md).

Bộ bàn giao: [Báo cáo](docs/delivery/REPORT.md), [setup từ clone sạch](docs/delivery/SETUP.md),
[kịch bản demo](docs/delivery/DEMO.md), [phân công/checklist](docs/delivery/HANDOVER.md).
Ngày nộp chưa xác định. UI tiếng Anh, tài liệu tiếng Việt; refund/shipping mô phỏng.

Demo kind (PowerShell7 +Docker Linux +kubectl):

```powershell
pwsh -NoProfile -File scripts/Start-K8s.ps1 -Demo
```

Mở https://localhost:9443; credential riêng ở `secrets/k8s/secrets.json` (ignore, không chia sẻ công khai).
Demo dùng **đơn mới**,30s/ngày làm việc, không đổi ngày seed lịch sử. Chi tiết và prerequisites ở setup trên.

- [Bắt đầu/tiếp tục công việc](docs/START_HERE.md)
- [Kế hoạch đã chốt](docs/PLAN.md)
- [Backlog coding](docs/tasks/INDEX.md)
- [Yêu cầu và nghiệm thu](docs/requirements/MATRIX.md)
- [Phân công nhóm/Jira](docs/team/OWNERSHIP.md)

Stack đã chọn: .NET 8, ASP.NET Core MVC/Razor, EF Core, SignalR, SQL Server, Redis, Nginx, Docker và Kubernetes local. Schema được giao chỉ được mở rộng.

Kiểm tra tài liệu nền bằng PowerShell 7:

```powershell
pwsh -NoProfile -File scripts/Verify-Planning.ps1
```

Tài liệu nguồn đã được trích vào repo để có thể bàn giao độc lập. Mã kỳ trước vẫn ở thư mục ngoài repo `../ProjectCode`, chỉ đọc khi task chỉ rõ cần tham khảo.
