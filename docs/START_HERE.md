# Bắt đầu ở đây

**Checkpoint:** A00–A07, B01–B06, C01–C05, D01–D04 hoàn tất (review gắn OrderItem/0007, form R1 + tổng hợp review, seller feedback/0008 với 60 ngày/chờ 7 ngày, hồ sơ uy tín F1 + seller reply một lần/0009; điểm P−N tích luỹ, % 12 tháng; 183 .NET +14 Node; **G5-03, G5-05 Done**, G5-04 chờ D05; Q-BIZ-001..004, Q-UI-007 Answered; GitHub [CI37428908236](https://github.com/Twiash/EbayGroup5/actions/runs/37428908236) success, main FF); sourceSQL17 và DbUp0001–0008 bất biến (D04 thêm 0009), chat SQL/REST +cookie WS/Redis +inbox/contact/read +automatic recovery/metadata trace.140 .NET +14 Node pass; actual browser restart1050-message catch-up/lost event/lost ack verified. G5-01/G5-02 Done; TLS/deployment/perf/operational logs còn F. GitHub CI/runtime xem handoff.

1. Đọc [quy định agent](../AGENTS.md) và [handoff mới nhất](handoffs/LATEST.md).
2. Xem [task index](tasks/INDEX.md), chọn đúng task được yêu cầu.
3. Chat C01–C05 đã xong; [C05 evidence](evidence/C05.md)/[recovery runbook](runbooks/CHAT_RECOVERY.md)/[CHAT](architecture/CHAT.md) dẫn scope và giới hạn. SQL durable, không outbox, pending/draft memory-only; chưa deployed TLS/shared keys/latency/dataset/log collection acceptance. Chờ task người dùng chọn; không tự chuyển D05/E/F. Contract [REVIEWS](architecture/REVIEWS.md), [FEEDBACK](architecture/FEEDBACK.md).

Các quyết định cố định: .NET 8 MVC/Razor; SQL Server; schema chỉ mở rộng; viết mới; UI tiếng Anh; repo riêng; GitHub private; Jira KAN; demo local Docker/K8s. Xem [ADR-001](decisions/ADR-001-foundation.md) khi cần lý do.

- [Ma trận yêu cầu](requirements/MATRIX.md): chỉ đọc những ID liên quan task.
- [Nguồn và checksum](requirements/SOURCES.md): dùng khi đối chiếu yêu cầu/schema.
- [Câu hỏi đang mở](questions/INDEX.md): deadline và UI phần chưa triển khai; tài khoản thành viên đã chốt; UI đã chốt U1/M1/R1/P1/D1/D2. GitHub private repo [Twiash/EbayGroup5](https://github.com/Twiash/EbayGroup5) đã nối origin.
- [Phân công](team/OWNERSHIP.md): đã tạo/gán 5 Epic +20 issue; [Jira mapping](team/JIRA_MAPPING.md).
- User chốt đồng bộ Jira theo đợt, chưa ấn định số task. B05+B06/KAN-11, C01/KAN-13, C02/KAN-14, C03+C04/KAN-15, C05/KAN-16, D01+D02/KAN-17, D03+D04/KAN-18 complete locally, chờ gom đợt sync; chưa ghi Jira từng task.

Kiểm tra tài liệu: `pwsh -NoProfile -File scripts/Verify-Planning.ps1` từ repo root. Build/run: [LOCAL_SETUP](runbooks/LOCAL_SETUP.md); SQL/Redis: [LOCAL_INFRA](runbooks/LOCAL_INFRA.md). Tra source: [SOURCE_MAP](architecture/SOURCE_MAP.md).

Không đọc toàn bộ [PLAN](PLAN.md) hoặc ProjectCode mỗi phiên. Task card dẫn tới tài liệu/source cần thiết.

Tham khảo module khi cần: [code cũ](architecture/REFERENCE_MAP.md), [hành trình eBay](requirements/EBAY_JOURNEYS.md), [UI](architecture/UI_REFERENCE.md), [ảnh và quyết định](architecture/UI_CANDIDATES.md). Không cần nạp nguồn UI để làm A07.
