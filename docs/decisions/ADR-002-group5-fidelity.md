# ADR-002 — Nhóm 5 giống eBay, phần hỗ trợ giản lược

Trạng thái: Accepted, 06/10/2026. Nguồn: câu trả lời trực tiếp của người dùng, dựa trên file đề bài `D:\WINDOWS\PRN3\Project Requiments\Ebay Clone Requiments.docx` (bản trích xuất: [requirements-extracted](../requirements/sources/requirements-extracted.md)). Người dùng thuộc **nhóm 5**.

## Quyết định

**Nhóm 5 (Interaction Layer) phải làm giống eBay** về UI và nghiệp vụ, dựa trên eBay thật hoặc nguồn chính thức. Phạm vi gồm:

1. Chat buyer–seller qua WebSocket (G5-01).
2. Lưu lịch sử chat (G5-02).
3. Đánh giá sau mỗi đơn đã nhận, gồm rating và comment (G5-03).
4. Hiển thị trung bình đánh giá trên mỗi sản phẩm (G5-04).
5. Điểm uy tín seller (G5-05).
6. Giao diện gửi khiếu nại, có nội dung và chọn lý do (G5-06).
7. Quản lý khiếu nại: buyer xem khiếu nại đã gửi, seller xem khiếu nại đã nhận (G5-07).
8. Phản hồi tự động kết nối với admin (G5-08).
9. Các yêu cầu phi chức năng G5-N.

**Tính năng của nhóm khác** (1 Buyer, 2 Seller, 3 Admin, 4 Payment/Shipping) chỉ làm khi cần để demo nhóm 5, và được **giản lược**. Ví dụ: đăng ký/đăng nhập, catalog, checkout COD, giao/nhận mô phỏng, giao diện admin. Điều kiện bắt buộc: giản lược **không được làm hỏng hoặc làm sai** tính năng của nhóm 5.

**Ranh giới:**

- Điểm vào và phần hiển thị của tính năng nhóm 5 nằm trên trang của nhóm khác thì vẫn phải giống eBay. Ví dụ: nút Message trên trang sản phẩm, Leave feedback / More actions trên Purchases, rating và % positive trên card sản phẩm.
- Phần còn lại của trang đó vẫn giản lược.

## Hệ quả cho các quyết định trước

- B01 (đăng nhập một trang, đăng ký chỉ Buyer), B04 (catalog dạng lưới), B05 (COD), B06 (Buyer bấm "Confirm received") giữ nguyên dạng giản lược. Lý do: đây không phải yêu cầu nhóm 5, và nhóm 5 vẫn demo được.
- D01–D05 và C01–C05 đã làm theo eBay; các điểm vào còn lệch được ghi trong [UI_REVIEW](../architecture/UI_REVIEW.md).
- Nhóm E (khiếu nại) dùng mẫu eBay mới thay mẫu cũ (Q-UI-003). Thông số nghiệp vụ lấy theo eBay Help: báo sau ngày giao dự kiến; seller có 3 ngày làm việc; Ask eBay to step in; eBay phản hồi khoảng 48 giờ. Phần thời gian có thể rút ngắn khi demo, nhưng phải ghi rõ.

Khi đổi quyết định này phải có ADR mới.
