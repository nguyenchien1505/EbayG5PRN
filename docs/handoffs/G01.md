# Handoff — G01: nghiệm thu UI-01 và rà ma trận yêu cầu

08/10/2026. Baseline main `013f7f4`; branch `task/G01-ui-acceptance`; checkpoint `git log --oneline --grep=G01`. Runtime `409e05d`, fast-forward vào main sau khi [CI37689369159](https://github.com/Twiash/EbayGroup5/actions/runs/37689369159) pass.
Handoff trước: [F08](F08.md).

## Quyết định người dùng (08/10/2026)

- Rà soát trước, không sửa; sau đó chốt **P1**: nghiệm thu UI + truy vết, sửa hiển thị nhỏ, không đổi nghiệp vụ, không làm badge tin chưa đọc (Q-UI-010 Deferred).
- Ảnh và seed cluster: agent quyết định → ảnh commit ở `docs/evidence/ui/`; cluster `ebaygroup5` **không bị ghi**, seed demo để G02.
- Commit kèm bản trích đề bài có khoảng trắng của người dùng (`requirements-extracted.md`); nội dung khớp ma trận.
- Nhắc lại quy tắc: nhóm 5 giống eBay nhất có thể; phần hỗ trợ demo giản lược, không ảnh hưởng nhóm 5.

## Hoàn tất

- Contract [UI_ACCEPTANCE](../architecture/UI_ACCEPTANCE.md) (phương pháp, 19 màn, nguồn, kết quả, khác biệt).
- `Test-AppStack.ps1 -Screens <dir>` → Journeys `--screens`: dữ liệu sạch qua UI rồi chụp desktop 1280 / mobile 390, kiểm tràn ngang + CSP. 38 ảnh, 0 vấn đề.
- Sửa nhỏ: `chat.js` (không hiện "Connected"), `Sellers/Feedback.cshtml` (bỏ dòng công thức), `_DisputeDraftStatus.cshtml` + `site.css` (khoảng trống form khiếu nại), `DemoSeedRunner` (ngày giao dự kiến cho đơn seed → INR demo được) + `DemoSeedTests`.
- Tài liệu: evidence INDEX đủ 22 mục còn thiếu + G01; MATRIX UI-01 Done + ghi chú cũ sửa; UI_CANDIDATES; Q-UI-005 đóng; Q-DEMO-001, Q-UI-010 thêm; START_HERE rút gọn (~300 từ).

## Verification

- Test lọc 51 + 40 pass; **Test-CI local 263/263 .NET + 20/20 Node**, 0 warning; Test-AppStack compose pass; GitHub [CI37689369159](https://github.com/Twiash/EbayGroup5/actions/runs/37689369159) success (compose, load, K8s, rolling).

## Requirement

- **UI-01 Done.** Còn Pending: **OPS-04** (Jira live) và **DOC-01** (báo cáo/demo) → G02.

## Cho G02

- **Q-DEMO-001 (Open):** seed demo gắn ngày 20–25/09/2026 → return hết 25/10, INR hết 28/10, feedback hết 24/11/2026; đơn mới cần qua ngày giao dự kiến trừ khi bật `Disputes:DemoBusinessDaySeconds`. Cần chốt cách demo trước khi seed cluster `ebaygroup5`.
- Q-TEAM-002 (hạn nộp) vẫn Open. Jira KAN chưa sync live (OPS-04).
- Khác biệt với eBay đã ghi (badge tin chưa đọc, ảnh đính kèm, nút ↻/nhãn Read) — nêu trong báo cáo.

## Giữ nguyên

- Cluster `ebaygroup5` (web `f0b65b0`, catalog trống), untracked `src/EbayGroup5.Web/Properties/launchSettings.json`.
