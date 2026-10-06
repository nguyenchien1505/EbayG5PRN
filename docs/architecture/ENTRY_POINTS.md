# D06 — Điểm vào nhóm 5 theo eBay

Contract do agent soạn ngày 06/10/2026 trên main `6dee061`, trước khi sửa source. Nguồn quyết định:

- [ADR-002](../decisions/ADR-002-group5-fidelity.md): điểm vào nhóm 5 nằm trên trang của nhóm khác vẫn phải giống eBay.
- [UI_REVIEW](UI_REVIEW.md#điểm-vào-nhóm-5-cần-đồng-bộ-theo-ebay-chưa-triển-khai) mục A1–A5.
- Người dùng chọn phương án 1 của Q-UI-008 ("chọn 1, triển khai task đồng bộ A1-A5").

Không đổi schema (không có migration mới). Không đổi luật nghiệp vụ D01–D05 hay C01–C05. Phần còn lại của các trang vẫn giản lược.

## A1 — Nút Message trong seller card (trang sản phẩm)

- eBay live 2026: seller card gồm tên, (score), % positive, nút **Message**.
- Clone: nút `Message` nằm trong `.seller-section`, ngay cạnh tên seller và badge uy tín. Bỏ link "Contact seller" riêng dưới Buy It Now.
- Hành vi giữ nguyên như C04:
  - Buyer: mở panel U1 (`data-chat-product-id`).
  - Khách chưa đăng nhập: link Sign in kèm `returnUrl` có `message=true`.
  - Seller/Admin: không có nút, vì chat chỉ là buyer–seller.
- Panel vẫn có tiêu đề "Message seller".

## A2 — Sao sản phẩm và % positive của seller trên card catalog

- eBay live: card kết quả có sao và số product ratings, cùng dòng seller dạng `tên 99.5% positive (1.2K)`.
- Clone giữ lưới (B04); mỗi card thêm:
  - Sao + `(N)` khi sản phẩm có ít nhất 1 rating hiển thị. Dùng **đúng quy tắc** của D02/D05: chỉ review gắn đơn, chưa bị ẩn. Có `aria-label` ghi điểm trung bình và số rating. Không có rating thì không hiện gì.
  - Dòng seller: `tên · X% positive (score)`.
    - X% là tỷ lệ 12 tháng, score tính toàn thời gian (Q-BIZ-004), feedback đã ẩn không tính (D05).
    - Không có Positive/Negative trong 12 tháng thì bỏ phần %, chỉ ghi `tên (score)`.
    - Score từ 1000 trở lên viết gọn như eBay: 1.2K, 12K, 1.5M.
- Dữ liệu lấy theo lô cho trang hiện tại (tối đa 12 sản phẩm), không truy vấn N+1:
  - `IReviewService.SummariesAsync(productIds)`.
  - `IFeedbackService.ReputationsAsync(sellerIds)`.
  - Mỗi lần gọi tối đa 100 id; vượt quá thì `ArgumentException`.
- Code tổng hợp dùng chung với `ProductReviewsAsync` / `ReputationAsync`, nên trang chi tiết và card luôn ra cùng con số.

## A3 — Leave feedback trên card Purchases và bộ lọc Ready for feedback

- eBay: card đơn có nút **Leave feedback**; dải chip có **Ready for feedback**.
- **Một quy tắc duy nhất "đơn chờ feedback":** đơn của Buyer có ít nhất một dòng mà nút Leave feedback ở trang chi tiết đơn sẽ hiện, tức `review.CanReview || feedback.CanLeave`:
  - **Review mở:** đơn `Delivered`, dòng có `productId`, chưa có Review cho dòng đó (D01).
  - **Feedback mở:** đơn `Delivered`, `sellerId` khác NULL, có đúng một Payment `Paid` với `paidAt`, hiện tại chưa quá `paidAt + 60 ngày`, chưa có SellerFeedback cho dòng đó (D03).
  - Quy tắc này nằm trong `Infrastructure/Orders/FeedbackDue` và có test đối chiếu với `ListForOrderAsync` của Review/Feedback.
- Card đơn của Buyer:
  - Có dòng chờ: nút `Leave feedback`. Chỉ một dòng chờ thì link thẳng `/Reviews/Create?orderItemId=…` (form R1); nhiều dòng thì link về trang chi tiết đơn.
  - Không có dòng chờ: không hiện nút.
- Bộ lọc dạng chip thay cho select + Apply:
  - Buyer: All purchases · Ready for feedback · Pending · Shipped · Delivered · Cancelled.
  - Seller (Sales): All sales · Pending · Shipped · Delivered · Cancelled.
  - Chip đang chọn có `aria-current="page"`.
- `status=ReadyForFeedback` chỉ hợp lệ cho Buyer. Seller dùng giá trị này thì nhận 400; service ném `ArgumentException`.
- Giới hạn: đơn legacy có hơn 50 dòng chỉ hiển thị 50 dòng đầu ở trang chi tiết. Nếu dòng chờ nằm ngoài 50 dòng đầu, card có thể dẫn tới trang chi tiết mà không thấy nút. Dữ liệu demo không có đơn như vậy.

## A4 — More actions ▾ trên card Purchases

- eBay: menu More actions (Contact seller, Return this item, I didn't receive it, …).
- Clone: `<details class="more-actions">`, dùng được bằng bàn phím và không cần JS để mở menu. Hiện có:
  - **Contact seller**: mở panel U1 theo dòng đầu tiên có sản phẩm (`data-chat-order-item-id`), cùng endpoint C04 với trang chi tiết đơn.
- Đơn không có dòng nào còn sản phẩm thì không hiện menu.
- E02 sẽ thêm "I didn't receive it", "Return this item" và "See request details" vào menu này.
- Trang Sales của Seller không có menu (ngoài phạm vi nhóm 5).

## A5 — Thu cột thư mục Messages

- eBay live 2026: hàng tiêu đề gồm icon mở/thu panel thư mục và chữ "Messages". Mặc định panel thu gọn.
- Clone:
  - Thanh tiêu đề phía trên workspace có nút icon `data-folder-toggle` (`aria-expanded`, `aria-controls="chat-folders"`) và `h1` Messages.
  - Cột thư mục (Inbox) mặc định ẩn. Bấm nút thì hiện: desktop là cột trái, mobile là một hàng phía trên danh sách.
  - Hành vi inbox, thread, đọc và realtime không đổi.

## Kiểm tra

- `dotnet test` (Test-CI): quy tắc due đối chiếu với eligibility của từng dòng; lọc ReadyForFeedback; Seller dùng ReadyForFeedback bị 400; số liệu batch bằng số liệu từng sản phẩm/seller; giới hạn 100 id; HTML có Message/stars/% positive/chip/More actions; định dạng score.
- Node test (nếu có logic JS mới tách được); browser desktop + 390/320px cho catalog, item, purchases, messages.
