# Bắt đầu ở đây

**Checkpoint:** **40/40 task kỹ thuật A00–G02 hoàn tất**; DOC-01 Done về báo cáo/setup/demo/handover.
[G02 evidence](evidence/G02.md), runtime `e635866`/[CI37709417510](https://github.com/Twiash/EbayGroup5/actions/runs/37709417510) success.
Còn **OPS-04 Pending**: user yêu cầu hoãn Jira live. Ngày nộp chưa xác định (Q-TEAM-002), badge unread Deferred (Q-UI-010).
Q-DEMO-001 đã chốt: **đơn mới +clock demo**, `Start-K8s -Demo`30s/day. Không sửa seed/ngày lịch sử.
**Đợt H (08/10/2026, [ADR-003](decisions/ADR-003-ui-fidelity-references.md)):** UI mọi màn giống eBay, backend nhóm khác giản lược; ảnh mẫu đã chốt ở [references/ui](references/ui/INDEX.md); task H00–H07 trong [INDEX](tasks/INDEX.md).
Bàn giao: [REPORT](delivery/REPORT.md), [SETUP](delivery/SETUP.md), [DEMO](delivery/DEMO.md),
[HANDOVER/checklist](delivery/HANDOVER.md). Task/status: [INDEX](tasks/INDEX.md); requirement: [MATRIX](requirements/MATRIX.md).

1. Đọc [quy định agent](../AGENTS.md) và [handoff mới nhất](handoffs/LATEST.md).
2. Chọn đúng task được yêu cầu trong [task index](tasks/INDEX.md); chỉ làm khi người dùng giao.
3. **Phạm vi:** [ADR-002](decisions/ADR-002-group5-fidelity.md) — nhóm 5 (chat, đánh giá, khiếu nại) giống eBay thật nhất có thể; phần hỗ trợ demo của nhóm khác giản lược nhưng không được ảnh hưởng nhóm 5. Nghiệm thu UI: [UI_ACCEPTANCE](architecture/UI_ACCEPTANCE.md).

Quyết định cố định: .NET 8 MVC/Razor; SQL Server, schema nguồn bất biến (chỉ mở rộng, DbUp 0001–0014); viết mới; UI tiếng Anh; GitHub private [Twiash/EbayGroup5](https://github.com/Twiash/EbayGroup5); Jira KAN; demo local Docker/K8s. Lý do: [ADR-001](decisions/ADR-001-foundation.md).

## Contract theo module

- Tài khoản/bảo mật: [AUTH](architecture/AUTH.md), [SECURITY](architecture/SECURITY.md), [SECURITY_TESTS](architecture/SECURITY_TESTS.md) (F04 + thu hồi phiên Q-SEC-001).
- Hỗ trợ: [CATALOG](architecture/CATALOG.md), [CHECKOUT](architecture/CHECKOUT.md), [ORDERS](architecture/ORDERS.md), [DEMO_SEED](architecture/DEMO_SEED.md).
- Nhóm 5: [CHAT](architecture/CHAT.md), [REVIEWS](architecture/REVIEWS.md), [FEEDBACK](architecture/FEEDBACK.md), [MODERATION](architecture/MODERATION.md), [ENTRY_POINTS](architecture/ENTRY_POINTS.md), [DISPUTES](architecture/DISPUTES.md), [DISPUTE_FORMS](architecture/DISPUTE_FORMS.md), [DISPUTE_DETAILS](architecture/DISPUTE_DETAILS.md), [DISPUTE_AUTOMATION](architecture/DISPUTE_AUTOMATION.md), [DISPUTE_ADMIN](architecture/DISPUTE_ADMIN.md).
- Vận hành: [DEPLOYMENT](architecture/DEPLOYMENT.md), [SCALE_OUT](architecture/SCALE_OUT.md), [JOURNEYS](architecture/JOURNEYS.md), [LOAD](architecture/LOAD.md) (F05 + sửa deadlock Q-PERF-001), [K8S](architecture/K8S.md), [CD](architecture/CD.md), [ROLLING](architecture/ROLLING.md).

## Chạy và kiểm tra

- Build/run: [LOCAL_SETUP](runbooks/LOCAL_SETUP.md); SQL/Redis: [LOCAL_INFRA](runbooks/LOCAL_INFRA.md); rolling: [runbooks/ROLLING](runbooks/ROLLING.md).
- `scripts/Test-CI.ps1` (verify + build + test SQL/Redis cô lập), `scripts/Test-AppStack.ps1` (stack HTTPS: journeys + smoke; `-Load Short|Full|Stress`; `-Screens <dir>`), `scripts/Test-K8s.ps1`, `scripts/Start-AppStack.ps1` (https://localhost:8443), `scripts/Start-K8s.ps1` (https://localhost:9443). Dọn: `scripts/Clear-TestArtifacts.ps1 [-Apply]`.
- Tài liệu: `pwsh -NoProfile -File scripts/Verify-Planning.ps1`.

## Tham khảo khi cần

[Ma trận yêu cầu](requirements/MATRIX.md) (chỉ đọc ID liên quan) · [Nguồn và checksum](requirements/SOURCES.md) · [Câu hỏi](questions/INDEX.md) · [Phân công](team/OWNERSHIP.md) / [Jira mapping](team/JIRA_MAPPING.md) (đồng bộ live theo đợt, chưa làm) · [SOURCE_MAP](architecture/SOURCE_MAP.md) · [REFERENCE_MAP](architecture/REFERENCE_MAP.md) · [EBAY_JOURNEYS](requirements/EBAY_JOURNEYS.md) · [UI_REVIEW](architecture/UI_REVIEW.md) · [UI_CANDIDATES](architecture/UI_CANDIDATES.md). Không đọc toàn bộ [PLAN](PLAN.md) mỗi phiên.
