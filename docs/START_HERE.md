# Bắt đầu ở đây

**Checkpoint:** A00–A02 hoàn tất Git/tài liệu và bản đồ tham khảo; ứng dụng chưa được scaffold.

1. Đọc [quy định agent](../AGENTS.md) và [handoff mới nhất](handoffs/LATEST.md).
2. Xem [task index](tasks/INDEX.md), chọn đúng task được yêu cầu.
3. Task tiếp theo: **[A03](tasks/A03.md)** — scaffold .NET 8. Câu hỏi private UI không chặn task này.

Các quyết định cố định: .NET 8 MVC/Razor; SQL Server; schema chỉ mở rộng; viết mới; UI tiếng Anh; repo riêng; GitHub private; Jira KAN; demo local Docker/K8s. Xem [ADR-001](decisions/ADR-001-foundation.md) khi cần lý do.

- [Ma trận yêu cầu](requirements/MATRIX.md): chỉ đọc những ID liên quan task.
- [Nguồn và checksum](requirements/SOURCES.md): dùng khi đối chiếu yêu cầu/schema.
- [Câu hỏi đang mở](questions/INDEX.md): GitHub remote, thành viên, deadline và Q-UI-001–003 chưa có; không chặn A03.
- [Phân công](team/OWNERSHIP.md): Jira chưa tạo issue.

Kiểm tra tài liệu: `pwsh -NoProfile -File scripts/Verify-Planning.ps1` từ repo root. Chưa có lệnh build/run ứng dụng; A03/A04 sẽ bổ sung.

Không đọc toàn bộ [PLAN](PLAN.md) hoặc ProjectCode mỗi phiên. Task card dẫn tới tài liệu/source cần thiết.

Tham khảo module khi cần: [code cũ](architecture/REFERENCE_MAP.md), [hành trình eBay](requirements/EBAY_JOURNEYS.md), [UI](architecture/UI_REFERENCE.md). Không cần đọc cả ba tài liệu để làm A03.
