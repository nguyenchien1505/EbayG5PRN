# Thư viện ảnh mẫu UI

Quy định: [ADR-003](../../decisions/ADR-003-ui-fidelity-references.md). Mô tả chi tiết từng mẫu và phần clone giản lược nằm ở [UI_CANDIDATES](../../architecture/UI_CANDIDATES.md); file này là mục lục ảnh. Chỉ mở ảnh của màn đang làm.

## Quy tắc

- Tên file: `<ID>-<màn>-<nguồn/năm hoặc live-YYYYMMDD>[-mobile][-n].<ext>`. ID trùng với ID trong UI_CANDIDATES; `-n` là đoạn cuộn thứ n của cùng trang.
- Ảnh live chụp bằng trình duyệt in-app: viewport desktop 1050 px (ảnh thu về 800 px), mobile 375 px. Ảnh không phải pixel-perfect; dùng để đối chiếu bố cục, câu chữ và trạng thái.
- Trước khi commit: che tên tài khoản, mã bưu chính, địa chỉ, email, số đơn của người dùng bằng ô xám. Ghi vùng đã che ở cột "Che".
- Ảnh bị thay thế: đổi trạng thái thành Superseded, không xóa.

## Mục lục

| ID | File | Màn hình | Nguồn | Ngày nguồn | Trạng thái | Dùng cho | Che |
|---|---|---|---|---|---|---|---|
| HD | [header](HD-header-signed-in-user-20261008.png) | Header đã đăng nhập | Người dùng gửi | 08/10/2026 | Chốt | H03 | Tên, mã bưu chính |
| HM | [home](HM-home-signed-out-user-20261008.jpg) | Trang chủ chưa đăng nhập, footer | Người dùng gửi | 08/10/2026 | Chốt | H03 | Mã bưu chính |
| SA | [about seller](SA-about-this-seller-user-20261008.png) | Khối About this seller + Seller feedback trên trang sản phẩm | Người dùng gửi | 08/10/2026 | Chốt | H02 | — |
| IT | [1](IT-item-live-20261008-1.jpg), [2](IT-item-live-20261008-2.jpg), [3](IT-item-live-20261008-3.jpg), [4](IT-item-live-20261008-4.jpg) | Trang sản phẩm desktop (đầu trang, shipping/returns, item specifics/description, about seller) | [eBay live, item 820101344339](https://www.ebay.com/itm/820101344339) | 08/10/2026 | Chốt | H02, H05 | Mã bưu chính (header, Delivery) |
| SR | [1](SR-search-results-live-20261008-1.jpg), [2](SR-search-results-live-20261008-2.jpg), [3](SR-search-results-live-20261008-3.jpg) | Kết quả tìm kiếm: cột lọc, pill, sort, list, phân trang, footer | [eBay live](https://www.ebay.com/sch/i.html?_nkw=wireless+headphones) | 08/10/2026 | Chốt | H04 | Mã bưu chính |
| F2 | [1](F2-feedback-profile-live-20261008-1.jpg), [2](F2-feedback-profile-live-20261008-2.jpg) | Feedback profile classic: thẻ seller, Feedback ratings, Detailed seller ratings, tab, tìm/lọc/sort, danh sách, phân trang, Quick Links | [eBay live, littleniceyaya-9](https://www.ebay.com/fdbk/feedback_profile/littleniceyaya-9?filter=feedback_page%3ARECEIVED_AS_SELLER&sort=RELEVANCEV2) | 08/10/2026 | Chốt | H02 | Mã bưu chính |
| F2m | [mobile](F2m-feedback-profile-mobile-live-20261008.jpg) | Seller feedback bản mobile (mweb) | eBay live mweb_profile | 08/10/2026 | Chốt | H02 | — |
| F1 | [top](F1-feedback-profile-top-live-20261006.jpg), [list](F1-feedback-profile-list-live-20261006.jpg) | Feedback profile seller ít DSR (thông báo "at least 10 detailed seller ratings") | [eBay live, bestbuy](https://www.ebay.com/fdbk/feedback_profile/bestbuy) | 06/10/2026 | Chốt | D04, H02 | — |
| R1 | [form](R1-leave-feedback-desktop-2024.png) | Leave feedback: sentiment, sao item, comment 500, arrived on time, 3 hàng DSR, ảnh | [eBay Community](https://community.ebay.com/forum/selling-57920/topic/more-changes-to-feedback-127861/) | 06/2024 | Chốt | D02–D03, H01 | Nguồn đã che item |
| M1 | [inbox](M1-messages-inbox-desktop-2024.png) | Messages desktop ba cột | [Seller Center](https://www.ebay.com/sellercenter/resources/seller-updates/2024-summer/tools) | 06/2024 | Chốt | C04 | — |
| P1 | [card](P1-purchase-history-item-2023.png) | Purchase history item | [eBay Community](https://community.ebay.com/t5/Buying/Tracking-links-stopped-showing-in-Purchase-History/td-p/33825818) | 2023 | Chốt | B06, H06 | Nguồn đã che |
| P2 | [actions](P2-order-more-actions-2025.png) | Nhóm nút đơn hàng, More actions | [eBay Community](https://community.ebay.com/forum/ask-a-mentor-57913/topic/report-item-not-delivered-i-didnt-receive-it-is-not-in-the-more-actions-option-318671/) | 01/2025 | Chốt | D06, E02 | — |
| C1 | [gif](C1-checkout-pay-with-card.gif) | Checkout Pay with card | [eBay pages](https://pages.ebay.com/pay-with-card/index.html) | không ghi năm | Chốt | B05, H06 | — |
| E1 | [return](E1-return-item-desktop-2022.png) | Return item 2 bước | [eBay Community](https://community.ebay.com/forum/returns-57918/topic/photos-for-a-return-443208/) | 11/2022 | Chốt | E02 | — |
| E1m | [reasons](E1m-return-reason-2026.jpg) | Danh sách lý do return hiện hành | [eBay Community](https://community.ebay.com/forum/buying-57914/topic/fake-tracking-scammers-are-winning-173392/) | 03/2026 | Chốt | E02 | — |
| D2 | [seller](D2-seller-return-response-2024.jpg) | Seller xử lý return | [eBay Community](https://community.ebay.com/t5/Returns/how-to-return-to-send-partial-refund-from-the-create-return/td-p/34699999) | 09/2024 | Chốt | E03 | — |
| E0 | [picker](E0-inr-item-picker-live-20261006.jpg) | Report an item you didn't receive — chọn item | eBay Help live | 06/10/2026 | Chốt | E02 | Tên, mã bưu chính |
| H1 | [help](H1-help-inr-steps-live-20261006.jpg) | Help 4042: timeline INR, escalation | [eBay Help 4042](https://www.ebay.com/help/buying/returns-refunds/get-help-item-hasnt-arrived?id=4042) | 06/10/2026 | Chốt | E02–E04 | — |
| L0 | [sign in](L0-signin-live-20261006.jpg), [register](L0-register-live-20261006.jpg) | Đăng nhập, tạo tài khoản | eBay live | 06/10/2026 | Chốt | H06 | — |

## Chưa có file

- **U1** (panel Message seller) và **L1** (Welcome back): người dùng gửi trong chat 02–03/10/2026, ảnh gốc có thông tin tài khoản và không còn trong repo. Gửi lại bản đã che nếu cần đối chiếu.
- **D1**: Superseded bởi E1, không lưu.
- Màn cần đơn mua thật (feedback sau khi gửi, Purchases/Order details hiện hành, return/INR thật, checkout, email): xem [Q-UI-011](../../questions/INDEX.md).
