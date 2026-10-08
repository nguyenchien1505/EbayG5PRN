# ADR-003 — Giao diện giống eBay ở mọi màn, thư viện ảnh mẫu trong repo

Trạng thái: Accepted, 08/10/2026. Bổ sung [ADR-002](ADR-002-group5-fidelity.md); thay câu "phần còn lại của trang đó vẫn giản lược" của ADR-002 và các ghi chú "không commit ảnh eBay" trong [UI_CANDIDATES](../architecture/UI_CANDIDATES.md)/[UI_REFERENCE](../architecture/UI_REFERENCE.md).

## Nguồn

Người dùng, 08/10/2026, sau khi rà soát G01/G02:

- "Tuy nhiều phần không thuộc nhóm 5, giản lược là đúng nhưng chỉ nên giản lược về mặt backend và tính năng đừng giản lược về mặt UI để cho trải nghiệm giống ebay nhất."
- "Các ảnh UI đã chốt nên được lưu chuẩn dự án để sau này có thể đối chiếu."
- Yêu cầu làm trước các phần không cần đơn mua thật; ảnh còn thiếu thì hỏi người dùng.

## Quyết định

1. **Nhóm 5** giữ nguyên ADR-002: UI và nghiệp vụ giống eBay nhất có thể.
2. **Màn của nhóm khác** (header, trang chủ, tìm kiếm, sản phẩm, checkout, đơn hàng, tài khoản): bố cục, thành phần và trạng thái giống eBay; **nghiệp vụ/backend được giản lược**. Mỗi điều khiển hiển thị phải thuộc một trong hai loại:
   - chạy được ở mức giản lược, phạm vi ghi trong contract của task;
   - mở trang thông báo chung "This feature isn't available in this demo" (status 200, có link quay lại). Không để link chết hoặc nút không phản hồi.
3. **Không bịa số liệu.** Số lượng đã bán, watcher, giảm giá, phí ship, huy hiệu (Top Rated…) chỉ hiện khi tính được từ dữ liệu thật của clone; không có dữ liệu thì bỏ dòng đó. Ảnh sản phẩm demo là ảnh minh họa của dự án, không dùng ảnh listing eBay.
4. **Nguồn mẫu UI đã chốt:** trang eBay.com live (public) và ảnh người dùng gửi. Ảnh Community/Seller Center chỉ dùng khi đã được chốt như trước. Khi live và ảnh cũ khác nhau, ưu tiên live mới nhất.
5. **Thư viện ảnh:** ảnh đã chốt lưu trong [docs/references/ui](../references/ui/INDEX.md), có mục lục (ID, nguồn, ngày, viewport, task dùng). Trước khi commit phải che thông tin cá nhân của người dùng (tên tài khoản, mã bưu chính/địa chỉ, email, số đơn). Username người khác eBay đã che sẵn (`1***h`) thì giữ. Repo private; ảnh chỉ để đối chiếu nội bộ, không dùng nội dung ảnh làm dữ liệu demo.
6. **Không vượt cơ chế chặn bot của eBay.** Màn cần đăng nhập, giao dịch hoặc bị chặn thì yêu cầu người dùng chụp; ghi vào [questions](../questions/INDEX.md).

## Hệ quả

- Các quyết định giản lược trước đây của B03/B04/B05/B06 (shell rút gọn, catalog lưới, không cart/watchlist hiển thị) được xem lại theo từng task H; nghiệp vụ COD, đăng ký Buyer-only… vẫn giữ.
- Task H thay đổi UI dùng chung (layout, CSS) phải chạy lại hành trình J1–J7 và bộ chụp UI để chứng minh nhóm 5 không bị ảnh hưởng.
- Đổi quyết định này phải có ADR mới.
