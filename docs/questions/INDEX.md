# Thông tin đang chờ

| ID | Cần biết | Ảnh hưởng | Trạng thái |
|---|---|---|---|
| Q-ENV-001 | Private repo [Twiash/EbayGroup5](https://github.com/Twiash/EbayGroup5), tự tạo theo yêu cầu người dùng 03/10/2026 | Origin đã nối, main đã push | Answered: gh xác minh owner Twiash và isPrivate=true |
| Q-TEAM-001 | Tên/email Jira ánh xạ TV1–TV5 | Chỉ chặn gán assignee; KAN và vai trò đã chốt | Open |
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
