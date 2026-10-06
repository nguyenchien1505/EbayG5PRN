# Handoff — rà soát UI/nghiệp vụ theo eBay (sau D05)

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5. Chỉ sửa tài liệu, không đổi runtime/schema.

- Baseline main `0f6af30`; nhánh `task/ui-review-group5`, commit docs-only `[skip ci]`, fast-forward vào main.
- Handoff task runtime gần nhất: [D05](D05.md) (CI37431358143 success).

## Hoàn tất

- [ADR-002](../decisions/ADR-002-group5-fidelity.md): người dùng thuộc nhóm 5. Yêu cầu nhóm 5 phải giống eBay; tính năng nhóm khác chỉ để demo thì giản lược, không được làm hỏng nhóm 5. Điểm vào nhóm 5 trên trang nhóm khác vẫn phải giống eBay.
- [UI_REVIEW](../architecture/UI_REVIEW.md): đối chiếu eBay live (người dùng tự đăng nhập; agent chỉ xem), eBay Help và ảnh Community có năm cho B01, B03–B06, C04, D01–D05.
  - Giữ giản lược: B01 (đăng nhập một trang), B04 (catalog lưới), B05 (COD là yêu cầu nhóm 4 của đề; eBay.com không có COD), B06 (Buyer "Confirm received"; eBay dùng tracking).
  - Điểm vào nhóm 5 còn lệch: A1–A5, chờ Q-UI-008.
- Q-UI-003 Answered theo ADR-002: E1/E1m thay D1 (giao diện cũ), giữ D2; INR và request details theo [Help 4042](https://www.ebay.com/help/buying/returns-refunds/get-help-item-hasnt-arrived?id=4042). Chi tiết ở [UI_CANDIDATES](../architecture/UI_CANDIDATES.md#rà-soát-06102026--khiếu-nại-q-ui-003).
- Ghi chú vào task card E01 (ngày giao dự kiến, 3 ngày làm việc, 48 giờ, appeal 30 ngày), E02 (E1/E1m/E0/H1), E03 (D2/H1).
- Ảnh live và ảnh ghép so sánh chỉ lưu ignored trong `artifacts/ui-review/`, không commit (có tên/tài khoản người dùng).

## Verification

- `pwsh -NoProfile -File scripts/Verify-Planning.ps1` pass; `git diff --check` sạch.
- Không chạy Test-CI vì không đổi runtime; main runtime vẫn là `316adac` (CI37431358143).

## Còn mở / tiếp theo

- **Q-UI-008:** thời điểm làm A1–A5 — (1) task nhỏ riêng trước E01, hoặc (2) gộp A4 vào E02, phần còn lại vào G01.
- **E01 phải chốt nguồn ngày giao dự kiến:** `ShippingInfo.estimatedArrival` đang NULL khi Mark as shipped; INR theo eBay chỉ mở sau ngày này.
- Chưa triển khai E/F; chờ người dùng chọn task.
- Jira: không có task mới; ledger giữ nguyên như [D05](D05.md); chưa sync live.
