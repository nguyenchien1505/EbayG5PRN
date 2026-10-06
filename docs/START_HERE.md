# Bắt đầu ở đây

**Checkpoint:** A00–A07, B01–B06 và C01–C02 hoàn tất; original SQL17 bảng giữ nguyên, DbUp additive0006 và chat service/owned REST,116 SQL/HTTP/schema tests pass. Hub/Redis/realtime/UI chưa triển khai. GitHub CI/runtime xem handoff.

1. Đọc [quy định agent](../AGENTS.md) và [handoff mới nhất](handoffs/LATEST.md).
2. Xem [task index](tasks/INDEX.md), chọn đúng task được yêu cầu.
3. Task tiếp theo **[C03](tasks/C03.md)** — SignalR có xác thực và Redis backplane, đọc [CHAT](architecture/CHAT.md), [SECURITY](architecture/SECURITY.md), C02 service/controller/tests. Dùng shared SQL service, commit-before-event/replay suppression, exact Origin và recipient/member guard; REST tests chưa là realtime evidence. Chỉ tiếp tục task user yêu cầu.

Các quyết định cố định: .NET 8 MVC/Razor; SQL Server; schema chỉ mở rộng; viết mới; UI tiếng Anh; repo riêng; GitHub private; Jira KAN; demo local Docker/K8s. Xem [ADR-001](decisions/ADR-001-foundation.md) khi cần lý do.

- [Ma trận yêu cầu](requirements/MATRIX.md): chỉ đọc những ID liên quan task.
- [Nguồn và checksum](requirements/SOURCES.md): dùng khi đối chiếu yêu cầu/schema.
- [Câu hỏi đang mở](questions/INDEX.md): deadline và UI phần chưa triển khai; tài khoản thành viên đã chốt; UI đã chốt U1/M1/R1/P1/D1/D2. GitHub private repo [Twiash/EbayGroup5](https://github.com/Twiash/EbayGroup5) đã nối origin.
- [Phân công](team/OWNERSHIP.md): đã tạo/gán 5 Epic +20 issue; [Jira mapping](team/JIRA_MAPPING.md).
- User chốt đồng bộ Jira theo đợt task để phân chia thành viên; chưa ấn định số task. B05+B06/KAN-11, C01/KAN-13 và C02/KAN-14 có evidence local, chờ gom đợt sync, chưa ghi Jira từng task.

Kiểm tra tài liệu: `pwsh -NoProfile -File scripts/Verify-Planning.ps1` từ repo root. Build/run: [LOCAL_SETUP](runbooks/LOCAL_SETUP.md); SQL/Redis: [LOCAL_INFRA](runbooks/LOCAL_INFRA.md). Tra source: [SOURCE_MAP](architecture/SOURCE_MAP.md).

Không đọc toàn bộ [PLAN](PLAN.md) hoặc ProjectCode mỗi phiên. Task card dẫn tới tài liệu/source cần thiết.

Tham khảo module khi cần: [code cũ](architecture/REFERENCE_MAP.md), [hành trình eBay](requirements/EBAY_JOURNEYS.md), [UI](architecture/UI_REFERENCE.md), [ảnh và quyết định](architecture/UI_CANDIDATES.md). Không cần nạp nguồn UI để làm A07.
