# Ảnh UI và quyết định đã chốt

Ngày tìm 02/10/2026. Người dùng yêu cầu tìm ảnh thiếu trên Internet và cho xem trước khi chốt. **Đã chốt M1, R1, D1, D2** qua câu trả lời trực tiếp: “Chốt cả M1, R1, D1, D2”. U1 dùng cho panel Message seller theo câu hỏi đã được xác nhận. **P1 đã chốt** ngày 02/10/2026 qua lời nhắn “ok tôi chốt P1, hãy tiếp tục”. Chọn bố cục không tự mở rộng scope nghiệp vụ. Chỉ nạp ảnh của màn hình đang triển khai; không nạp toàn bộ gallery mỗi phiên.

## U1 — ảnh người dùng: Message seller

Đã xem ảnh đính kèm trong chat ngày 02/10/2026: panel trắng sát phải, nền product detail phủ tối, tiêu đề/đóng, seller context, mini product card, notice, vùng hội thoại, composer dưới cùng, icon ảnh, checkbox email và nút gửi disabled khi trống. Đây là nguồn người dùng cung cấp cho C04. Không suy ra luồng gửi thành công/draft/reconnect từ ảnh tĩnh. Không commit ảnh gốc vì có thông tin tài khoản; không tự thêm email copy hoặc upload ảnh vào scope.

## M1 — inbox/conversation desktop

[Nguồn chính thức eBay Seller Center, Summer 2024](https://www.ebay.com/sellercenter/resources/seller-updates/2024-summer/tools). Mẫu nhiều cột cho C04; không khẳng định giống tuyệt đối UI 2026.

![M1 — desktop messaging](https://ucpstatic.ebay.com/static-assets/images/2024/06/us-desktop-messaging.png?crafterSite=sellercenter-us)

## R1 — feedback form

[Nguồn eBay Community, thảo luận tháng 6/2024](https://community.ebay.com/forum/selling-57920/topic/more-changes-to-feedback-127861/). Ảnh thành viên đăng, không phải đặc tả nghiệp vụ chính thức; dùng tham khảo sentiment/stars/comment cho D02–D03. Review sản phẩm và feedback seller vẫn theo contract của clone.

![R1 — feedback](https://res.cloudinary.com/startup-grind/image/upload/c_fill,dpr_2,f_auto,g_center,q_auto:good/v1/gcs/platform-data-ebay/discussions/large_KpIC0iC)

## P1 — purchase history item

[Nguồn eBay Community, thảo luận năm 2023](https://community.ebay.com/t5/Buying/Tracking-links-stopped-showing-in-Purchase-History/td-p/33825818). Mẫu item/order context và nhóm action cho B06; ảnh không chứng minh mọi action trong trạng thái Delivered.

![P1 — purchase history](https://res.cloudinary.com/startup-grind/image/upload/c_fill,dpr_2,f_auto,g_center,q_auto:good/v1/gcs/platform-data-ebay/discussions/medium_zKDl6WM)

## D1 — buyer return request (đã thay bằng E1 ngày 06/10/2026)

**Không dùng nữa:** rà soát 06/10/2026 cho thấy ảnh là giao diện eBay đời cũ (xem mục Rà soát bên dưới). Giữ lại để lưu lịch sử.

[Nguồn eBay Community, thảo luận năm 2023](https://community.ebay.com/forum/returns-57918/topic/i-ordered-two-items-but-only-got-one-104714/). Mẫu reason/detail/action cho E02. Không dùng lời khuyên trong bình luận làm quy tắc xử lý dispute.

![D1 — buyer return request](https://res.cloudinary.com/startup-grind/image/upload/c_fill,dpr_2,f_auto,g_center,q_auto:good/v1/gcs/platform-data-ebay/discussions/large_WkKELnz)

## D2 — seller xử lý return (đã chốt)

[Nguồn eBay Community, tháng 9/2024](https://community.ebay.com/t5/Returns/how-to-return-to-send-partial-refund-from-the-create-return/td-p/34699999). Mẫu xử lý return/partial refund cho E03. Đây là mẫu D2 trong câu hỏi người dùng đã chốt; URL ảnh cũ đã chuyển sang CDN. Không mặc định áp dụng hoàn tiền thật hoặc mọi lựa chọn hiển thị.

![D2 — seller return response](https://res.cloudinary.com/startup-grind/image/upload/c_fill,dpr_2,f_auto,g_center,q_auto:good/v1/gcs/platform-data-ebay/discussions/large_w0ZKvm5)

## Cách chốt và giới hạn

- M1/R1/P1/D1/D2 đã được chốt ngày 02/10/2026; không hỏi lại quyền dùng bố cục này. Ngày 06/10/2026 D1 được thay bằng E1 theo ADR-002 (xem mục Rà soát). Task B06 dùng P1, C04/D02/D03/E02/E03 dùng các mẫu tương ứng cùng contract và U1; không yêu cầu mở tất cả ảnh.
- Link ảnh M1/R1/P1/D1/D2 đã kiểm tra HTTP HEAD 200, content-type ảnh; dùng URL hiện tại vì URL Community cũ trả 410. Ảnh chỉ nhúng từ nguồn, không copy tài khoản/order/comment làm dữ liệu demo.
- (Cập nhật G01, 08/10/2026: ảnh desktop/mobile của mọi màn nhóm 5 ở [evidence/ui](../evidence/ui/), đối chiếu tại [UI_ACCEPTANCE](UI_ACCEPTANCE.md).) Ghi chú gốc: chưa đủ bằng chứng UI mobile và trạng thái lỗi/draft. Seller feedback profile đã có F1; timeline/escalation lấy câu chữ từ Help 4042 (mục Rà soát). Khi đến task liên quan, tiếp tục tìm nguồn, trình mẫu hoặc đề xuất trạng thái clone để người dùng chốt. Admin thiết kế riêng, không mạo nhận UI admin eBay.

## C1 — checkout (B05, đã chốt 06/10/2026)

[eBay Pay with card chính thức](https://pages.ebay.com/pay-with-card/index.html), trang không ghi năm của ảnh; URL kiểm tra ngày 06/10/2026. User chốt “Chốt C1 và bố cục đề xuất” qua Q-UI-006. Nguồn bổ sung [eBay Connect Payments 2023, slide 3](https://developer.ebay.com/cms/files/connect-2023/payments.pdf) và [Checkout Help](https://www.ebay.com/help/buying/paying/checkout?id=4036).

![C1 checkout](https://ir.ebaystatic.com/pictures/aw/pics/EUO-240/W43_PayWithCard_Gif_ROW.gif)

Clone giản lược: Ship to → item/quantity → COD mô phỏng → total/Place order, desktop hai cột/mobile một cột. Không triển khai card/PayPal/coupon/vault/tax/shipping options từ mẫu. COD là quyết định demo của dự án, không suy ra từ ảnh hoặc chính sách eBay. Không copy thông tin cá nhân trong nguồn.

C04 06/10/2026 triển khai U1/M1 đã chốt: right white dialog/backdrop/item context/plaintext composer và Messages navigation/inbox/thread ba cột. Mobile full-width drawer hoặc list/thread Back là adaptation clone, không nguồn eBay mobile mới. Không attachments/offer/email/archive. [C04 evidence](../evidence/C04.md) ghi thực chứng desktop/mobile/error/paging/read/ownership; không hỏi lại phê duyệt U1/M1.

## F1 — seller feedback profile

Nguồn: [eBay Feedback profile thật](https://www.ebay.com/fdbk/feedback_profile/bestbuy), trang live xem ngày 06/10/2026 (không phải ảnh có năm). Ảnh chụp lưu ignored trong `artifacts/d04/F1-ebay-feedback-profile-top.jpg` và `artifacts/d04/F1-ebay-feedback-profile-list.jpg`; không commit vì là nội dung của eBay và có username thật đã được che một phần. Cách reply lấy từ [eBay Help](https://www.ebay.com/help/selling/leaving-feedback-buyers/replying-feedback-received-item-sold?id=4102). Người dùng chốt **F1 giản lược** qua Q-UI-007.

Bản giản lược cho clone gồm:

- Thẻ đầu trang: tên seller, `(score)`, "Positive Feedback (last 12 months): x%", thời điểm bắt đầu (lấy dữ liệu có thật; schema không có ngày tạo user thì bỏ dòng "Member since"), link "View items for sale" nếu có.
- Bảng "Feedback ratings": các dòng Positive/Neutral/Negative, các cột 1 tháng / 6 tháng / 12 tháng, thêm cột "All time" (đúng mô tả của phương án người dùng đã chọn; điểm P−N tính trên cột này).
- Danh sách 3 cột FEEDBACK / FROM / WHEN:
  - FEEDBACK: icon sentiment, comment, tên item.
  - FROM: buyer che tên kiểu `b***r`, giá, "Verified purchase".
  - WHEN: thời gian tương đối.
  - Reply của seller hiện ngay dưới comment; Seller chủ hồ sơ thấy nút Reply bên phải.
- Lọc theo sentiment và phân trang.
- Bỏ: Detailed seller ratings, tab buyer/left for others, ô tìm item, ảnh, Member Quick Links. Mobile xếp thành một cột theo shell của clone, không có ảnh eBay mobile làm mẫu.

## Rà soát 06/10/2026 — khiếu nại (Q-UI-003)

Áp dụng [ADR-002](../decisions/ADR-002-group5-fidelity.md): khiếu nại là yêu cầu nhóm 5, nên phải giống eBay. Tổng hợp rà soát nằm ở [UI_REVIEW](UI_REVIEW.md). Ảnh dưới đây là ảnh thành viên đăng trên eBay Community, có ghi năm; dùng để tham khảo bố cục, không coi là đặc tả nghiệp vụ. Thông tin cá nhân trong ảnh nguồn không được chép.

### E1 — buyer return, desktop 2 bước (11/2022)

[Nguồn](https://community.ebay.com/forum/returns-57918/topic/photos-for-a-return-443208/).

- Bước 1: "Why are you returning this item?" với radio lý do → Next.
- Bước 2: Add details, Add photos (0/10), Confirm return.
- Banner thông tin ở đầu trang.

Mẫu này thay D1.

![E1 — return item](https://res.cloudinary.com/startup-grind/image/upload/c_fill,dpr_2,f_auto,g_center,q_auto:good/v1/gcs/platform-data-ebay/discussions/large_RkeQbTJ)

### E1m — danh sách lý do hiện hành (03/2026)

[Nguồn](https://community.ebay.com/forum/buying-57914/topic/fake-tracking-scammers-are-winning-173392/). Dropdown "Return reason" gồm: Ordered the wrong item, Changed my mind, No longer needed, Found a better price, Doesn't fit, Item damaged, Item defective, Missing parts or pieces, Item didn't match description, Received the wrong item.

Cùng thread có card đơn 2026 ("Shipped—Carrier scan", View order details, Leave feedback ▾, View tracking). Ô "Report a problem" trong thread là của PayPal, **không dùng**.

![E1m — return reason](https://res.cloudinary.com/startup-grind/image/upload/c_fill,dpr_2,f_auto,g_center,q_auto:good/v1/gcs/platform-data-ebay/discussions/large_JGLXbXz)

### E0 — Report an item you didn't receive (live 06/10/2026)

Trang eBay Help "Report an item you didn't receive — Select an item to get started": ô "Search by item or order number" và danh sách đơn gần đây. Agent đã xem live; ảnh lưu ignored tại `artifacts/ui-review/5b-ebay-inr-item-picker-live.jpg`.

Form INR theo [Help 4042](https://www.ebay.com/help/buying/returns-refunds/get-help-item-hasnt-arrived?id=4042): More actions → I didn't receive it → "I'd like a refund" / "I'd still like the item" → Add any additional information → Send request.

### H1 — timeline và escalation chính thức (Help 4042, live 06/10/2026)

"What happens from here":

1. Estimated delivery date passes.
2. Report it.
3. Seller has 3 business days.
4. Ask eBay to step in — "We review and usually respond within 48 hours".

Leo thang: Purchases → More actions → See request details → Ask eBay to step in and help → chọn lý do → Confirm. Có thể appeal trong 30 ngày. Ảnh ignored: `artifacts/ui-review/5a-ebay-help-inr-steps-live.jpg`.

Không có ảnh thật của trang request details. Clone thiết kế trang này theo câu chữ H1/E0, cùng phong cách E1/D2, và ghi rõ là thiết kế của clone.

### P2 — điểm vào khiếu nại trên đơn (01/2025)

[Nguồn](https://community.ebay.com/forum/ask-a-mentor-57913/topic/report-item-not-delivered-i-didnt-receive-it-is-not-in-the-more-actions-option-318671/). Nhóm nút View order details / Buy again / View seller's other items / **More actions ▾**.

![P2 — order actions](https://res.cloudinary.com/startup-grind/image/upload/c_fill,dpr_2,f_auto,g_center,q_auto:good/v1/gcs/platform-data-ebay/discussions/medium_zITPrPs)

### D2

Giữ như đã chốt (09/2024, giao diện hiện hành).
