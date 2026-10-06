# Thông tin đang chờ

| ID | Cần biết | Ảnh hưởng | Trạng thái |
|---|---|---|---|
| Q-ENV-001 | Private repo [Twiash/EbayGroup5](https://github.com/Twiash/EbayGroup5), tự tạo theo yêu cầu người dùng 03/10/2026 | Origin đã nối, main đã push | Answered: gh xác minh owner Twiash và isPrivate=true |
| Q-TEAM-001 | Tài khoản Jira ánh xạ TV1–TV5 | Đã gán đúng account assignable | Answered 06/10/2026: Dat/Huy/Cuong/Nguyên/Hải theo phân công đã duyệt |
| Q-TEAM-002 | Ngày nộp/mốc giảng viên | Chưa lập lịch ngày cụ thể; không chặn task kỹ thuật | Open |
| Q-UI-001 | Inbox và conversation eBay | Chốt UI C04 | Answered: U1 + M1 desktop; mobile/trạng thái bổ sung khi làm C04 |
| Q-UI-002 | Purchase history và form review/feedback | Chốt UI B06, D02–D04 | Partial: R1 và P1 đã chốt; profile chưa có mẫu |
| Q-UI-003 | Form khiếu nại và case buyer/seller | Chốt UI E02–E03 | Partial: D1/D2 đã chốt; timeline/escalation chưa đủ ảnh |

A02 đã quan sát home/menu và đọc Help. Ngày 02/10/2026 người dùng gửi U1 và xác nhận “Chốt cả M1, R1, D1, D2”. Xem [gallery/quyết định](../architecture/UI_CANDIDATES.md); các ảnh là mẫu thiết kế, không chứng minh toàn bộ UI hiện hành. Chi tiết còn thiếu: [UI-REFERENCES](UI-REFERENCES.md). P1 cũng đã được chốt qua lời nhắn trực tiếp ngày 02/10/2026. Không chặn A05.

Mẫu câu hỏi: ID, task/màn hình, nguồn đã kiểm tra, chưa rõ điều gì, cần link/ảnh/video/mô tả nào, phần bị chặn, phần làm tiếp được, giả định đề xuất và câu trả lời/ngày/nguồn.

Khi có trả lời, cập nhật contract/ADR và task liên quan, đánh dấu Answered hoặc Deferred. Không để câu trả lời chỉ tồn tại trong chat. Hết thời gian chờ không phải xác nhận.

Q-UI-004 (B01, Answered 03/10/2026): S1 register đã chốt; L1 dùng ảnh eBay Welcome back user gửi sau, thay ảnh Agent Router gửi nhầm. Scope email/password giữ nguyên; không social/reset/Stay signed in. Không lưu ảnh/email cá nhân vào repo.

Q-UI-005 (B04, Open 03/10/2026): đề xuất PD1 từ https://www.ebay.com.hk/import/newtoebay (không ghi năm), ảnh https://ir.ebaystatic.com/pictures/hk/new-to-ebay/P3-6.png đã trình trong chat. Chờ chốt bản giản lược ảnh trái/thông tin phải, Contact seller theo U1. Tiếp tục query/API/test độc lập; chưa áp dụng UI đề xuất.

Q-UI-005 cập nhật 03/10/2026: người dùng trả lời 'Tôi sẽ gửi ảnh khác'. PD1 không được duyệt; chờ ảnh thay thế. Backend/API đã làm độc lập, UI detail chưa triển khai.

Q-UI-005 Answered: người dùng đã gửi ảnh chi tiết sản phẩm thay thế và yêu cầu tiếp tục; bố cục ảnh trái/title-seller-price-stock phải. Đã áp dụng/kiểm chứng B04 ngày 06/10/2026. PD1 Internet là đề xuất lịch sử, không dùng. Không commit ảnh/tài khoản trong ảnh nguồn.

Q-TEAM-001: user xác nhận 'Tạo và gán theo đề xuất'; đã tạo 5 Epic +20 ticket KAN-4–28. [Jira mapping](../team/JIRA_MAPPING.md).

Q-UI-006 (B05 checkout, Answered 06/10/2026): đã khảo sát eBay Checkout Help, eBay Connect Payments 2023 slide 3 và Pay with card chính thức; trình GIF C1 kèm nguồn không ghi năm của ảnh. User trả lời “Chốt C1 và bố cục đề xuất”. Áp dụng Ship to/item-quantity/COD demo/total-Place order, desktop hai cột/mobile một cột; [contract](../architecture/CHECKOUT.md), [mẫu](../architecture/UI_CANDIDATES.md). Không hỏi lại C1.

Q-BIZ-001 (D01/D02, **Open**, giả định agent 06/10/2026, chưa có câu trả lời người dùng): server D01 đang cho comment tùy chọn và không có sửa/xóa review. Đã duyệt: rating 1–5 bắt buộc, một review mỗi OrderItem (MATRIX G5-03, PLAN §3.2). Chưa rõ: (a) comment bắt buộc hay tùy chọn; (b) Buyer có được sửa/xóa review đã gửi không. Ảnh hưởng: nếu (a)=bắt buộc phải thêm kiểm tra ở ReviewService + test (UI không thay server; chặn SQL cần migration mới, không sửa 0007); nếu (b)=có thì cần contract/endpoint/audit mới, liên quan D05. Không đổi hành vi server khi chưa có trả lời.

Q-BIZ-002 (D02, **Open**, 06/10/2026): Review legacy `OrderItemId IS NULL` (schema nguồn, có thể rating ngoài 1–5/NULL) có được hiển thị và tính vào trung bình/số lượt sản phẩm không. D01 đã quyết định giữ nguyên, không backfill, không coi là review gắn đơn. Đề xuất cho D02 (chưa chốt): chỉ tính và hiển thị review có `OrderItemId`. Chốt trong contract D02 trước khi code G5-04.
