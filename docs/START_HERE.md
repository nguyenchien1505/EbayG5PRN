# Bắt đầu ở đây

**Checkpoint:** A00–A07 hoàn tất; SQL baseline 17 bảng, EF mapping, DbUp và 12 test SQL pass. CI GitHub đã pass. B01–B04 auth/security/catalog hoàn tất, 32 SQL/HTTP tests pass; demo credential local đã enable riêng.

1. Đọc [quy định agent](../AGENTS.md) và [handoff mới nhất](handoffs/LATEST.md).
2. Xem [task index](tasks/INDEX.md), chọn đúng task được yêu cầu.
3. Task tiếp theo **[B05](tasks/B05.md)** — Buy Now, địa chỉ và COD mô phỏng. Đọc [CATALOG](architecture/CATALOG.md) và [SECURITY](architecture/SECURITY.md) để nối checkout đúng quyền/tồn kho.

Các quyết định cố định: .NET 8 MVC/Razor; SQL Server; schema chỉ mở rộng; viết mới; UI tiếng Anh; repo riêng; GitHub private; Jira KAN; demo local Docker/K8s. Xem [ADR-001](decisions/ADR-001-foundation.md) khi cần lý do.

- [Ma trận yêu cầu](requirements/MATRIX.md): chỉ đọc những ID liên quan task.
- [Nguồn và checksum](requirements/SOURCES.md): dùng khi đối chiếu yêu cầu/schema.
- [Câu hỏi đang mở](questions/INDEX.md): deadline và UI phần chưa triển khai; tài khoản thành viên đã chốt; UI đã chốt U1/M1/R1/P1/D1/D2. GitHub private repo [Twiash/EbayGroup5](https://github.com/Twiash/EbayGroup5) đã nối origin.
- [Phân công](team/OWNERSHIP.md): đã tạo/gán 5 Epic +20 issue; [Jira mapping](team/JIRA_MAPPING.md).

Kiểm tra tài liệu: `pwsh -NoProfile -File scripts/Verify-Planning.ps1` từ repo root. Build/run: [LOCAL_SETUP](runbooks/LOCAL_SETUP.md); SQL/Redis: [LOCAL_INFRA](runbooks/LOCAL_INFRA.md). Tra source: [SOURCE_MAP](architecture/SOURCE_MAP.md).

Không đọc toàn bộ [PLAN](PLAN.md) hoặc ProjectCode mỗi phiên. Task card dẫn tới tài liệu/source cần thiết.

Tham khảo module khi cần: [code cũ](architecture/REFERENCE_MAP.md), [hành trình eBay](requirements/EBAY_JOURNEYS.md), [UI](architecture/UI_REFERENCE.md), [ảnh và quyết định](architecture/UI_CANDIDATES.md). Không cần nạp nguồn UI để làm A07.
