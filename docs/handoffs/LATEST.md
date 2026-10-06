# Handoff — D06: đồng bộ điểm vào nhóm 5 theo eBay (A1–A5)

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `6dee061`. Runtime nằm trên `task/D06-group5-entry-points` và chỉ fast-forward vào main sau khi GitHub CI chạy đúng hash pass; số run ghi bằng commit docs-only `[skip ci]`.
- Checkpoint: `git log --oneline --grep=D06`.
- Handoff trước: [UI-REVIEW-G5](UI-REVIEW-G5.md) (rà soát, ADR-002) và [D05](D05.md) (runtime gần nhất).

## Hoàn tất

Người dùng chọn phương án 1 của Q-UI-008. Contract: [ENTRY_POINTS](../architecture/ENTRY_POINTS.md). Không có migration và không đổi luật nghiệp vụ.

- **A1** — `Views/Catalog/Details.cshtml`: nút `Message` nằm trong seller card, bỏ link "Contact seller" riêng. Hành vi giữ như C04.
- **A2** — Card catalog có sao + `(N)` và dòng `seller X% positive (score)`.
  - Dữ liệu lấy theo lô qua `IReviewService.SummariesAsync` và `IFeedbackService.ReputationsAsync`.
  - Cả hai dùng chung code tổng hợp với trang chi tiết (`Summarize`, `ReputationsAsync`).
  - Định dạng score: `SellerReputation.CompactScore`.
- **A3** — Purchases:
  - Chip lọc (thay cho select + Apply); có `Ready for feedback`, chỉ dành cho Buyer.
  - Nút `Leave feedback` trên card.
  - Quy tắc duy nhất nằm ở `Infrastructure/Orders/FeedbackDue.cs`; `OrderService` nhận thêm `TimeProvider` tùy chọn.
- **A4** — Menu `More actions ▾` (`<details class="more-actions">`) → Contact seller (`data-chat-order-item-id`). Trang Purchases nạp panel chat.
- **A5** — Messages: thanh tiêu đề có nút `data-folder-toggle`; cột Inbox mặc định ẩn. Toggle trong `chat.js`, CSS trong `chat.css`.
- Thêm D06 vào PLAN/INDEX; `Verify-Planning.ps1` kiểm 40 task.

## Verification

- [Evidence](../evidence/D06.md): Test-CI local **203/203 .NET + 14/14 Node**, build 0 warnings.
- Mutation điều kiện 60 ngày bị test bắt.
- Browser desktop, 390px và 320px trên preview cô lập; sửa lỗi menu bị cắt.
- GitHub CI: xem commit docs `[skip ci]` của D06.
- Không đụng `launchSettings.json` (untracked).

## Lưu ý cho task sau

- **E02:** thêm I didn't receive it / Return this item / See request details vào menu More actions trong `Views/Orders/History.cshtml`. Mẫu UI: E1/E1m/E0/H1 ([UI_CANDIDATES](../architecture/UI_CANDIDATES.md#rà-soát-06102026--khiếu-nại-q-ui-003)).
- **E01:** phải chốt nguồn ngày giao dự kiến (`ShippingInfo.estimatedArrival` đang NULL).
- Nếu đổi điều kiện review (D01) hoặc feedback (D03), phải sửa `FeedbackDue` theo; test `Ready_for_feedback_matches_the_order_details_button_for_every_order` sẽ báo lệch.

## Tiếp theo / Jira

- Chờ người dùng chọn task tiếp theo; không tự làm E/F.
- D06 chưa có issue Jira; khi gom đợt, người dùng quyết định gắn vào KAN-20 hay tạo issue mới.
- Ledger giữ như [D05](D05.md). Chưa sync live; không tạo worklog/review/issue.
