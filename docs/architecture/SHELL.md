# B03 — shell và điều hướng

B04 supersedes home/search-unavailable behavior below: Home redirects Catalog/Index, header search submits to live catalog; old Home/Search preserves q in redirect. See [CATALOG](CATALOG.md). Remaining B03 decisions still apply.

Nguồn: bố cục public đã quan sát tại A02 trong [UI_REFERENCE](UI_REFERENCE.md); A03 scaffold, S1/L1 giữ nguyên form. Responsive là bản giản lược của clone, không khẳng định pixel parity với eBay mobile.

- Header trắng, wordmark màu, utility account row, search pill + nút xanh; My eBay dùng native details/summary hỗ trợ bàn phím, chỉ link tới Account/Me đã hoạt động. Không link giả tới cart/messages/orders/admin chưa triển khai.
- Home thay project setup bằng trạng thái marketplace chưa mở. Search GET Home/Search, query trim/tối đa 100 ký tự/HTML encoded; trang thông báo search chưa khả dụng, không giả kết quả rỗng từ SQL. B04 thay bằng catalog thật.
- Auth dùng cùng brand/utility nhưng không search để giữ form S1/L1 gọn. Footer ghi rõ educational demo; không copy legal claims của eBay.
- Empty/search-unavailable và 404 có tiêu đề/hướng dẫn/đường về Home; giữ nguyên status HTTP. Error 500 generic JSON và security statuses của B02 không thay đổi. 404 page chỉ xử lý GET không có body, không đổi API/POST response.
- Skip link, focus visible, label search, native menu, wrap long names/query và mobile không overflow. Không cần JS, ảnh hay package mới.
- Files: shared layout, CSS, Home controller/views, Program 404 handler; kiểm thử HTTP routing/query encoding/status và browser desktop/mobile/menu/auth navigation.
