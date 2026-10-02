# eBay Clone — Nhóm 5

Dự án môn C#/.NET: Chat, đánh giá, uy tín người bán và khiếu nại, kèm luồng mua hàng tối thiểu để demo độc lập.

**Hiện trạng:** A00–A05 hoàn tất; SQL baseline 17 bảng, DbUp, EF Core và runtime login riêng đã có. 8 test SQL thật pass; chưa seed dữ liệu/tài khoản demo hoặc nghiệp vụ. [Migration và test DB](docs/runbooks/DATABASE.md). Hạ tầng: [LOCAL_INFRA](docs/runbooks/LOCAL_INFRA.md). Xem [hướng dẫn chạy](docs/runbooks/LOCAL_SETUP.md) và [source map](docs/architecture/SOURCE_MAP.md).

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
