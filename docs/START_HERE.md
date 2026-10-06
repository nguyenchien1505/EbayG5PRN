# Bắt đầu ở đây

**Checkpoint:** A00–A07, B01–B06, C01–C05 và D01 hoàn tất (D01: DbUp0007 review gắn OrderItem + service + POST /Reviews/Create, 157 .NET +14 Node pass, UI thuộc D02, G5-03 Pending; Q-BIZ-001/002 Open; GitHub [CI37418785742](https://github.com/Twiash/EbayGroup5/actions/runs/37418785742) success, main FF); sourceSQL17 và DbUp0001–0006 bất biến (D01 thêm 0007), chat SQL/REST +cookie WS/Redis +inbox/contact/read +automatic recovery/metadata trace.140 .NET +14 Node pass; actual browser restart1050-message catch-up/lost event/lost ack verified. G5-01/G5-02 Done; TLS/deployment/perf/operational logs còn F. GitHub CI/runtime xem handoff.

1. Đọc [quy định agent](../AGENTS.md) và [handoff mới nhất](handoffs/LATEST.md).
2. Xem [task index](tasks/INDEX.md), chọn đúng task được yêu cầu.
3. Chat C01–C05 đã xong; [C05 evidence](evidence/C05.md)/[recovery runbook](runbooks/CHAT_RECOVERY.md)/[CHAT](architecture/CHAT.md) dẫn scope và giới hạn. SQL durable, không outbox, pending/draft memory-only; chưa deployed TLS/shared keys/latency/dataset/log collection acceptance. Chờ task người dùng chọn; không tự chuyển D02/D03/E/F. D01 contract: [REVIEWS](architecture/REVIEWS.md).

Các quyết định cố định: .NET 8 MVC/Razor; SQL Server; schema chỉ mở rộng; viết mới; UI tiếng Anh; repo riêng; GitHub private; Jira KAN; demo local Docker/K8s. Xem [ADR-001](decisions/ADR-001-foundation.md) khi cần lý do.

- [Ma trận yêu cầu](requirements/MATRIX.md): chỉ đọc những ID liên quan task.
- [Nguồn và checksum](requirements/SOURCES.md): dùng khi đối chiếu yêu cầu/schema.
- [Câu hỏi đang mở](questions/INDEX.md): deadline và UI phần chưa triển khai; tài khoản thành viên đã chốt; UI đã chốt U1/M1/R1/P1/D1/D2. GitHub private repo [Twiash/EbayGroup5](https://github.com/Twiash/EbayGroup5) đã nối origin.
- [Phân công](team/OWNERSHIP.md): đã tạo/gán 5 Epic +20 issue; [Jira mapping](team/JIRA_MAPPING.md).
- User chốt đồng bộ Jira theo đợt, chưa ấn định số task. B05+B06/KAN-11, C01/KAN-13, C02/KAN-14, C03+C04/KAN-15, C05/KAN-16 complete locally; D01 hoàn tất local một phần KAN-17 (còn D02), chờ gom đợt sync; chưa ghi Jira từng task.

Kiểm tra tài liệu: `pwsh -NoProfile -File scripts/Verify-Planning.ps1` từ repo root. Build/run: [LOCAL_SETUP](runbooks/LOCAL_SETUP.md); SQL/Redis: [LOCAL_INFRA](runbooks/LOCAL_INFRA.md). Tra source: [SOURCE_MAP](architecture/SOURCE_MAP.md).

Không đọc toàn bộ [PLAN](PLAN.md) hoặc ProjectCode mỗi phiên. Task card dẫn tới tài liệu/source cần thiết.

Tham khảo module khi cần: [code cũ](architecture/REFERENCE_MAP.md), [hành trình eBay](requirements/EBAY_JOURNEYS.md), [UI](architecture/UI_REFERENCE.md), [ảnh và quyết định](architecture/UI_CANDIDATES.md). Không cần nạp nguồn UI để làm A07.
