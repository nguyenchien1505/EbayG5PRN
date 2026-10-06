# Rà soát UI/nghiệp vụ trước D01 so với eBay thật — 06/10/2026

Phạm vi: B01, B03–B06, C04 (và các điểm vào của D/E). Nguyên tắc áp dụng: [ADR-002](../decisions/ADR-002-group5-fidelity.md) — nhóm 5 phải giống eBay; phần hỗ trợ được giản lược nhưng không được làm hỏng nhóm 5.

Phương pháp:

- Xem eBay.com bằng trình duyệt in-app, đăng nhập tài khoản của người dùng (người dùng tự đăng nhập; agent chỉ xem, không mua/nhắn/khiếu nại). Tài khoản **không có đơn mua**, nên các trang private sau khi mua (order details, form feedback/return/INR thật) dùng nguồn công khai thay thế.
- Đối chiếu với eBay Help chính thức và ảnh eBay Community có ghi năm.
- Ảnh live chỉ lưu ignored trong `artifacts/ui-review/` (có tên người dùng/thông tin tài khoản), không commit.

## Kết quả theo màn hình

| Màn hình | eBay quan sát được | Clone hiện tại | Kết luận |
|---|---|---|---|
| Sign in / Register (B01, nhóm 1) | Live: sign in **2 bước** (Email or username → Continue), Google/Apple/Facebook, Stay signed in. Register có Personal/Business, First + Last name, Email, Password | Một trang email+password; register có display name, chỉ Buyer | **Giữ giản lược.** Seller có từ seed / Enable-DemoAuth; demo nhóm 5 không bị ảnh hưởng |
| Header (B03) | Live: Hi user · Deals · Brand Outlet · Help & Contact · Sell · Watchlist · My eBay · chuông · giỏ; logo, Shop by category, search + camera + All Categories, Search, Advanced; thanh category | Utility row, My eBay, logo, search pill + Search | **Giữ giản lược.** My eBay có Messages như eBay |
| Search / catalog (B04, nhóm 1) | Live: **list**, lọc bên trái, "N results for …", Sort: Best Match, tab All/Auction/Buy It Now; card có tình trạng, giá, "Buy It Now", phí giao, **seller % positive (count)** | Lưới 3 cột, chọn category, card title/giá/seller | **Giữ lưới**, nhưng card phải hiện dữ liệu nhóm 5 (xem A2) |
| Item page (B04) | Live: gallery trái; tiêu đề; **seller card: tên (score), % positive, Seller's other items, nút Message**; giá; specifics; Quantity; Buy It Now / Add to cart / Make offer / Watchlist | Bố cục tương tự; seller + badge uy tín (D03/D04); "Contact seller" là link riêng dưới Buy It Now | **Khớp khung.** Điểm vào chat cần đổi (A1) |
| Checkout (B05, nhóm 1/4) | C1 chính thức. [Payment methods policy](https://www.ebay.com/help/policies/payment-policies/accepted-payments-policy?id=4269): eBay.com **không có COD** (có "Payment upon pickup") | Ship to / Review item / Pay with COD demo / Order summary | **Giữ.** COD là yêu cầu nhóm 4 của đề ("PayPal, COD"), không phải nhóm 5 |
| Purchases (B06, nhóm 1) | Live (không có đơn): nav My eBay trái, "Search your orders", chip All Purchases / Processing / Unpaid items / Returns & Canceled / Show hidden / **Ready for feedback** / Shipped / Payment Failed, "See orders". Card đơn ([2023](https://community.ebay.com/forum/ask-a-mentor-57913/topic/help-cant-find-the-link-asking-ebay-to-step-in-for-a-dishonest-seller-71293/), [01/2025](https://community.ebay.com/forum/ask-a-mentor-57913/topic/report-item-not-delivered-i-didnt-receive-it-is-not-in-the-more-actions-option-318671/), [03/2026](https://community.ebay.com/forum/buying-57914/topic/fake-tracking-scammers-are-winning-173392/)): trạng thái + ngày/tổng/mã đơn; View order details, **Leave feedback ▾**, Buy again, View seller's other items, **More actions ▾** (Contact seller, I didn't receive it / Return this item, Add note, Hide order) | Select trạng thái, card + View order details; Leave feedback chỉ trong order detail | **Giữ khung giản lược**; điểm vào nhóm 5 cần thêm (A3, A4) |
| Giao/nhận (B06, nhóm 4) | Delivered do tracking của hãng; buyer không có nút xác nhận (có thể tích "arrived on time" khi để feedback). Seller: Mark as shipped / Add tracking ([Help](https://www.ebay.com/help/selling/posting-items/tracking-items-youve-sold?id=4088)) | Seller Mark as shipped (tracking demo), Buyer Confirm received | **Giữ** (nhóm 4). Phụ thuộc cho E: INR theo eBay cần **ngày giao dự kiến** — E01 phải chốt nguồn ngày này (ShippingInfo.estimatedArrival đang NULL) |
| Messages (C04, nhóm 5) | Live 2026: "Messages" + icon mở/thu panel thư mục, "Search all member messages", icon thao tác hàng loạt, dòng tin (avatar, người gửi, tiêu đề/preview, thời gian, chấm chưa đọc), khung đọc "Select a message to read" khi màn rộng | M1: cột Messages/Inbox luôn hiện + inbox + thread | **Khớp cơ bản**; lệch nhỏ ở cột thư mục (A5) |
| Review/feedback/uy tín/kiểm duyệt (D01–D05, nhóm 5) | Đã đối chiếu eBay khi làm task (R1, F1, Help) | Theo các contract REVIEWS/FEEDBACK/MODERATION | Khớp; điểm vào trên Purchases/catalog xem A2/A3 |

## Khiếu nại (Q-UI-003, nhóm 5)

- **D1 bị thay.** Ảnh D1 (đăng năm 2023) thực ra là giao diện eBay đời cũ: header cũ, "Request a return" dạng dropdown.
- **Mẫu mới (E1):** [Return item desktop 2 bước, 11/2022](https://community.ebay.com/forum/returns-57918/topic/photos-for-a-return-443208/).
  - Bước 1: "Why are you returning this item?" (radio) → Next.
  - Bước 2: Add details, Add photos, Confirm return.
  - Danh sách lý do hiện hành lấy theo [Return request mobile 03/2026](https://community.ebay.com/forum/buying-57914/topic/fake-tracking-scammers-are-winning-173392/).
- **D2 giữ:** [seller xử lý return 09/2024](https://community.ebay.com/t5/Returns/how-to-return-to-send-partial-refund-from-the-create-return/td-p/34699999).
- **Item not received:**
  - Bước đầu đã thấy live: trang "Report an item you didn't receive — Select an item to get started" (eBay Help, item picker).
  - Form và các bước lấy đúng câu chữ [eBay Help 4042](https://www.ebay.com/help/buying/returns-refunds/get-help-item-hasnt-arrived?id=4042): More actions → I didn't receive it → "I'd like a refund" / "I'd still like the item" → thông tin thêm → Send request.
- **Request details / timeline / escalation:**
  - Dùng 4 bước chính thức "What happens from here": Estimated delivery date passes → Report it → Seller has 3 business days → Ask eBay to step in (thường phản hồi trong 48 giờ).
  - Thao tác leo thang: See request details → Ask eBay to step in and help → chọn lý do → Confirm; có thể appeal trong 30 ngày.
  - Không tìm được ảnh thật của trang request details, nên trang này là **thiết kế của clone** theo câu chữ Help, cùng phong cách E1/D2. Nếu có ảnh thật (từ tài khoản có đơn) sẽ thay.
- Chi tiết và ảnh nằm trong [UI_CANDIDATES](UI_CANDIDATES.md#rà-soát-06102026--khiếu-nại-q-ui-003).

## Điểm vào nhóm 5 cần đồng bộ theo eBay (chưa triển khai)

| ID | Thay đổi | Requirement | Ghi chú |
|---|---|---|---|
| A1 | Trang sản phẩm: chuyển điểm vào chat thành nút **Message** trong seller card (cạnh tên, % positive) | G5-01 | Hành vi panel U1 giữ nguyên |
| A2 | Card catalog: hiện **sao trung bình + số rating** của sản phẩm và **seller % positive** | G5-04, G5-05 | Lưới giữ nguyên; dữ liệu D02/D04 đã có |
| A3 | Purchases: nút **Leave feedback** trên card đơn đủ điều kiện và chip/lọc **Ready for feedback** | G5-03 | Form gộp R1 giữ nguyên |
| A4 | Purchases: menu **More actions ▾** (Contact seller; E02 thêm I didn't receive it / Return this item / See request details) | G5-01, G5-06 | Có thể làm chung E02 |
| A5 | Messages: thu cột thư mục thành icon toggle như eBay 2026 | G5-01 | Thẩm mỹ nhỏ |

Thời điểm triển khai chờ người dùng chọn (Q-UI-008).
