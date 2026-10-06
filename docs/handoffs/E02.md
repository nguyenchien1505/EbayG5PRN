# Handoff — E02: form khiếu nại và nháp trên server

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `e61e3ec`. Runtime `fbc1f33` trên `task/E02-dispute-form-draft`, fast-forward vào main sau khi [CI37484965524](https://github.com/Twiash/EbayGroup5/actions/runs/37484965524) pass.
- Checkpoint: `git log --oneline --grep=E02`. Handoff trước: [E01](E01.md).

## Hoàn tất

- Contract: [DISPUTE_FORMS](../architecture/DISPUTE_FORMS.md).
- **DbUp `0012_DisputeDraft.sql`**: bảng `DisputeDraft`, mỗi bộ (buyer, dòng hàng, loại) một nháp; có CHECK theo loại và grant CRUD. Manifest có 13 assets.
- **Application/Infrastructure:**
  - `EligibilityAsync` theo lô, tối đa 50 dòng; dùng chung `Build` với bản cho một dòng.
  - `DisputeEligibility` có thêm `ItemTitle`, `LineTotal`, `Requests`.
  - `FindDraftAsync` / `SaveDraftAsync`: khóa đơn như `OpenAsync`, lưu bằng MERGE.
  - `OpenAsync` xóa nháp trong cùng transaction.
- **Web:**
  - `DisputesController`: NotReceived, Return, Open, Draft.
  - Views `Disputes/*`: INR theo Help 4042, Return 2 bước theo E1, trang Unavailable.
  - `Models/DisputeForms.cs`: nhãn lý do E1m và nhãn trạng thái.
  - Purchases More actions: I didn't receive it / Return this item / See request details.
  - Trang chi tiết đơn: link mở request và trạng thái theo từng dòng (`#line-{id}`).
  - JS: `dispute-draft.mjs` (thuần, có Node test) và `dispute-form.js`. CSS dispute trong `site.css`.
  - `Test-CI.ps1` chạy thêm `tests/dispute-draft.test.mjs`.

## Verification

- [Evidence](../evidence/E02.md):
  - Node 6/6; SQL tập trung 86/86; mutation 2/2 bị bắt.
  - Browser: luồng Return đầy đủ, desktop/390/320px.
- **Full Test-CI local bị chặn vì ổ C: còn 0,2 GB.** Docker Desktop dừng và SQL báo mất log. Đã báo người dùng; không tự xóa dữ liệu.
- **GitHub [CI37484965524](https://github.com/Twiash/EbayGroup5/actions/runs/37484965524) exact runtime `fbc1f33` success**: job build-test 5m13s, **230/230 .NET + 20/20 Node** (14 chat + 6 draft), build 0 warnings. Đây là bằng chứng full suite. Docs-only `[skip ci]` sau CI.
- Không đụng `launchSettings.json` (untracked).

## Cho task sau

- **E03:**
  - Trang `/Disputes/Details/{id}` dùng `FindAsync` (timeline, `Actions`); đổi link "See request details" ở `History.cshtml` / `Details.cshtml` sang trang này.
  - Thêm danh sách request cho buyer và seller (G5-07).
  - Mẫu UI: D2/H1.
- **E04/E05:** như [E01](E01.md).
- **Môi trường:** trước khi chạy Test-CI local cần giải phóng ổ C: (Docker Desktop VM + DB test `EbayGroup5_Test_*` trong SQL local).

## Tiếp theo / Jira

- E01 + E02 → **KAN-21 / TV4 Nguyên Nguyễn: hoàn tất local**.
- Chưa sync live; không tạo worklog/review/issue.
