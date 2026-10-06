# Tham chiếu giao diện — A02

B04 06/10/2026 hoàn tất: Q-UI-005 đã có ảnh product detail người dùng gửi thay PD1; dùng bố cục ảnh lớn trái, seller/title/price/stock phải và Contact seller entry. Desktop/mobile390 đã kiểm chứng. Không đưa thông tin tài khoản/ảnh gốc vào Git. Catalog grid giản lược theo shell, images seed NULL hiển thị placeholder; không giả rating/shipping/condition/cart/watchlist. Checkout/chat ở B05/C04; UI-01 toàn dự án vẫn Pending.

**Cập nhật sau A02, ngày 02/10/2026:** người dùng cung cấp U1 (panel Message seller) và chốt M1/R1/D1/D2 tìm trên Internet. Xem [gallery và quyết định](UI_CANDIDATES.md) cho C04/D02/D03/E02/E03. P1 Purchase history đã được người dùng chốt ở phiên A04; mobile/trạng thái đầy đủ chưa xác minh. Các ghi chép A02 bên dưới là lịch sử khảo sát, không phủ nhận quyết định mới này.

Ngày: 02/10/2026. eBay.com tiếng Anh, trình duyệt in-app ở trạng thái chưa đăng nhập. Không coi tài liệu Help là ảnh chụp UI hiện hành.

## Phần đã quan sát trực tiếp

- Trang [eBay Home](https://www.ebay.com/) truy cập được. Viewport ảnh quan sát khoảng 870×1290; đây không phải kiểm chứng responsive trên điện thoại.
- Header có hàng link tài khoản/trợ giúp/My eBay và icon thông báo/cart. Bên dưới là logo, category, ô search bo tròn và nút Search màu xanh; có thanh category riêng.
- Nội dung home có banner khuyến mãi lớn và các dải category/product. Nội dung banner thay đổi trong phiên; không cố định copy hoặc giá để làm yêu cầu dự án.
- Menu My eBay mở được; thấy Purchase History, Selling, Messages, Issue Resolution Center cùng các mục khác ngoài scope.
- Có modal xác nhận địa chỉ vận chuyển do trang tự hiện. Đã dismiss, không nhập/xác nhận địa chỉ; modal này không nằm trong scope cần tái tạo.
- Điều hướng tới URL Messages quan sát từ menu: `https://mesg.ebay.com/mesgweb/ViewMessages/0` trả “Pardon Our Interruption” với thông báo nhận diện bot. Dừng khảo sát private UI tại đây; không vượt chặn hoặc tạo giao dịch để mở quyền.

Ảnh được xem trong công cụ khi khảo sát nhưng không được lưu thành artifact trong repo. Chỉ có ghi chép quan sát; không dẫn tới file ảnh không tồn tại. Không lưu URL challenge chứa tham số phiên. Product detail/feedback/return private chưa được mở xác minh trong phiên này.

## Ma trận màn hình

| Màn hình / task | Desktop | Mobile | Nguồn và hành động tiếp |
|---|---|---|---|
| Header/home — B03 | Quan sát một phần ở viewport nêu trên | Chưa quan sát | UI public; dùng bố cục chung, không sao chép toàn bộ banner/feature ngoài scope |
| Catalog/detail — B04 | Chưa quan sát trực quan trang chi tiết | Chưa quan sát | Help xác minh điểm Contact seller; khảo sát public trang liên quan khi làm B04, không tự coi A02 đã chứng minh layout |
| Purchase history — B06 | Thấy entry menu, chưa thấy nội dung tài khoản | Chưa quan sát | Q-UI-002; cần item đã nhận và các action |
| Inbox/conversation — C04 | Bị chặn, chưa quan sát | Chưa quan sát | Q-UI-001; cần list, thread và composer |
| Review/feedback form — D02/D03 | Help xác minh bước, chưa thấy form | Chưa quan sát | Q-UI-002; cần biết các phần trên cùng form hay khác màn hình |
| Seller feedback profile — D04 | Chưa quan sát | Chưa quan sát | Có thể khảo sát trang public khi làm D04; chưa yêu cầu thêm ảnh hàng loạt |
| Create dispute/draft — E02 | Help mô tả bước, chưa thấy form trạng thái thực | Chưa quan sát | Q-UI-003; cần chọn reason, detail và confirmation |
| Buyer/seller case timeline — E03 | Chưa quan sát | Chưa quan sát | Q-UI-003; cần một case mở có phản hồi nếu sẵn có |
| Admin resolution — E05 | Không có bằng chứng UI nội bộ eBay | Không có | Thiết kế riêng theo requirement và contract, ghi rõ UI của bản clone |

## Hướng thiết kế cho bản clone

Đây là định hướng triển khai, không phải xác nhận pixel chính xác của eBay:

- B03 xây shell trắng, typography dễ đọc, search nổi bật, CTA xanh; chỉ hiển thị mục điều hướng đã hoạt động trong scope.
- Mobile cần search và action chính dùng được, không tràn ngang. Các trạng thái desktop/mobile phải test trên ứng dụng khi triển khai; chưa được nghiệm thu ở A02.
- C04 có vùng danh sách hội thoại và nội dung/composer; D02 có item context và validation; E02 có draft/saved/error và kiểm tra trước gửi. Chi tiết private layout chờ nguồn Q-UI tương ứng.
- Admin có queue, filter, case detail/timeline, action có quyền; không gắn nhãn là giao diện admin thật của eBay.
- Không bổ sung folder, offers, image upload hoặc hoàn tiền thật chỉ để giống tính năng ngoài scope.

Task UI phải cập nhật file này bằng nguồn/ảnh thật hoặc ghi rõ thiết kế giản lược đã được người dùng chấp nhận. Không tự đánh dấu UI-01 Done khi chỉ có skeleton đẹp.

Xem [hành trình](../requirements/EBAY_JOURNEYS.md), [bản đồ code cũ](REFERENCE_MAP.md) và [câu hỏi cần bổ sung](../questions/UI-REFERENCES.md).

B01 03/10/2026: Q-UI-004 user chốt S1 từ [Shopify 28/05/2026](https://www.shopify.com/blog/sell-on-ebay), bản giản lược display name/email/password và Buyer only. L1 đã chốt ảnh eBay Welcome back user gửi 03/10/2026 thay ảnh Agent Router gửi nhầm. Form hẹp, nền trắng; clone dùng email/password, không social/reset/persistent session. Không đưa ảnh/email cá nhân vào Git.

B03 03/10/2026: shell triển khai theo bố cục public A02, bản giản lược responsive. Desktop/home và mobile login/404 390px đã xem; home không tràn ngang ở 390/320px. My eBay keyboard và search navigation pass. [Evidence B03](../evidence/B03.md); UI-01 vẫn Pending toàn dự án.

B06 06/10/2026: dùng P1 đã chốt cho card history (status/order/date/item/total/detail action), Seller sales dùng cùng card với context/action Seller. Detail reuse C1/shell: items/address/demo tracking/COD + Mark as shipped hoặc Confirm received theo quyền/trạng thái. Mobile là bố cục responsive giản lược của mẫu đã duyệt, không tuyên bố tái tạo ảnh mobile eBay. Không hiển thị review/chat/dispute actions giả. UI-01 toàn dự án vẫn Pending.
