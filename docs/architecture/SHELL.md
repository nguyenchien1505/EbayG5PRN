# B03 — shell và điều hướng

B04 supersedes home/search-unavailable behavior below: Home redirects Catalog/Index, header search submits to live catalog; old Home/Search preserves q in redirect. See [CATALOG](CATALOG.md). Remaining B03 decisions still apply.

Nguồn: bố cục public đã quan sát tại A02 trong [UI_REFERENCE](UI_REFERENCE.md); A03 scaffold, S1/L1 giữ nguyên form. Responsive là bản giản lược của clone, không khẳng định pixel parity với eBay mobile.

- Header trắng, wordmark màu, utility account row, search pill + nút xanh; My eBay dùng native details/summary hỗ trợ bàn phím, chỉ link tới Account/Me đã hoạt động. Không link giả tới cart/messages/orders/admin chưa triển khai.
- Home thay project setup bằng trạng thái marketplace chưa mở. Search GET Home/Search, query trim/tối đa 100 ký tự/HTML encoded; trang thông báo search chưa khả dụng, không giả kết quả rỗng từ SQL. B04 thay bằng catalog thật.
- Auth dùng cùng brand/utility nhưng không search để giữ form S1/L1 gọn. Footer ghi rõ educational demo; không copy legal claims của eBay.
- Empty/search-unavailable và 404 có tiêu đề/hướng dẫn/đường về Home; giữ nguyên status HTTP. Error 500 generic JSON và security statuses của B02 không thay đổi. 404 page chỉ xử lý GET không có body, không đổi API/POST response.
- Skip link, focus visible, label search, native menu, wrap long names/query và mobile không overflow. Không cần JS, ảnh hay package mới.
- Files: shared layout, CSS, Home controller/views, Program 404 handler; kiểm thử HTTP routing/query encoding/status và browser desktop/mobile/menu/auth navigation.

## Quyết định H03 (09/10/2026, header/footer/trang chủ giống eBay)

Theo [ADR-003](../decisions/ADR-003-ui-fidelity-references.md); mẫu HD, HM, IT-1 trong [references](../references/ui/INDEX.md). Thay bố cục giản lược B03 ở trên.

- **Hàng tiện ích:** "Hi {tên}!" ▾ (Account settings, Sign out) hoặc "Hi! Sign in or register"; Deals, Brand Outlet, Help & Contact; bên phải Ship to (thành phố của địa chỉ mặc định — eBay hiện mã bưu chính, schema không có), Sell ▾, Watchlist ▾, My eBay ▾ (mục theo vai trò như trước), chuông, giỏ.
- **Hàng logo:** Shop by category ▾ (danh mục thật), ô tìm kiếm bo tròn có icon, nút camera, chọn danh mục (`categoryId` hoạt động), Search, Advanced. **Thanh danh mục:** Saved, tối đa 9 danh mục thật, Deals, Sell.
- **Footer:** cột Buy / Sell / Your account / About eBay / Help & Contact; dòng cuối giữ "Educational demo… Not affiliated with eBay Inc." (không chép tuyên bố pháp lý của eBay).
- **Trang chủ `/`** (trước chuyển hướng sang catalog): banner, Shop by category (danh mục thật), Shopping made easy, Get more with an eBay account (khi chưa đăng nhập), Recently listed (12 listing mới nhất, giá thật), Money Back Guarantee (dẫn tới Returns and requests). Không hiện giảm giá/coupon/deal bịa.
- Điều khiển chưa có tính năng (Deals, Brand Outlet, Help & Contact, Watchlist, Notifications, Cart, Search by image, Advanced, Saved, Start selling, các link footer) mở `/Help/Unavailable/{key}` với danh sách khóa cố định. Seller có thêm Sales history, Feedback profile, Your listings trong Sell ▾.
- Menu dùng `<details data-menu>` + `shell.js` (một menu mở, đóng khi click ngoài/Escape); không inline script (CSP). Mobile ≤ 700 px: ẩn link chữ phụ, ô tìm full width, thanh danh mục cuộn ngang trong chính nó (trang không tràn ngang).
- Danh mục header cache 60 giây trong bộ nhớ từng replica; lỗi đọc DB không làm hỏng trang (danh sách rỗng, log cảnh báo).
