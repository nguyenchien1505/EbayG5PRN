# Handoff — Đợt H (giống eBay sau bàn giao)

08/10/2026. Baseline main `241ed25` (G02 Done). Handoff trước [G02](G02.md).
Checkpoint: `git log --oneline --grep="\[H0"`.

## Quyết định người dùng (08/10/2026)

- Uy tín seller (G5-05) đang giản lược so với eBay → làm lại theo eBay ([H01](../tasks/H01.md), [H02](../tasks/H02.md)).
- Màn nhóm khác: UI giống eBay, chỉ giản lược backend/tính năng ([ADR-003](../decisions/ADR-003-ui-fidelity-references.md)).
- Ảnh UI đã chốt lưu trong repo: [references/ui](../references/ui/INDEX.md). Người dùng sẽ đặt mua thật để chụp các màn cần đơn hàng (Q-UI-011); làm trước phần không cần đơn.

## Trạng thái task H

- **H00 Done**: 26 ảnh (2,9 MB) + mục lục, ADR-003, card H01–H07, Q-UI-011/012.
- H01–H05, H07 Planned; H06 Blocked chờ Q-UI-011. Q-UI-012 (ảnh kèm feedback/chat) Open, không chặn H01.

## Lưu ý vận hành

- eBay chặn Chrome headless và đôi khi challenge ở viewport mobile: không vượt chặn; dùng trình duyệt in-app (1050 px) hoặc ảnh người dùng gửi.
- Che tên/mã bưu chính trước khi lưu ảnh; script PIL tạm ở scratchpad (ô xám theo toạ độ).
- Cluster `ebaygroup5` (G02) giữ nguyên, không seed thêm đơn test. Không commit Co-Authored-By Claude.
- User untracked `src/EbayGroup5.Web/Properties/launchSettings.json` giữ nguyên.

## Bước tiếp theo

H01: chốt contract (FEEDBACK.md) trước khi code, migration 0015, rồi H02/H03… theo INDEX, mỗi task một branch + GitHub CI.
