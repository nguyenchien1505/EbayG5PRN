# Thông tin đang chờ

| ID | Cần biết | Ảnh hưởng | Trạng thái |
|---|---|---|---|
| Q-ENV-001 | Private repo [Twiash/EbayGroup5](https://github.com/Twiash/EbayGroup5), tự tạo theo yêu cầu người dùng 03/10/2026 | Origin đã nối, main đã push | Answered: gh xác minh owner Twiash và isPrivate=true |
| Q-TEAM-001 | Tài khoản Jira ánh xạ TV1–TV5 | Đã gán đúng account assignable | Answered 06/10/2026: Dat/Huy/Cuong/Nguyên/Hải theo phân công đã duyệt |
| Q-TEAM-002 | Ngày nộp/mốc giảng viên | Chưa lập lịch ngày cụ thể; không chặn task kỹ thuật | Open |
| Q-UI-001 | Inbox và conversation eBay | Chốt UI C04 | Answered: U1 + M1 desktop; mobile/trạng thái bổ sung khi làm C04 |
| Q-UI-002 | Purchase history và form review/feedback | Chốt UI B06, D02–D04 | Answered 06/10/2026: R1, P1 và F1 (feedback profile, giản lược) đã chốt |
| Q-UI-003 | Form khiếu nại và case buyer/seller | Chốt UI E02–E03 | Answered 06/10/2026 theo [ADR-002](../decisions/ADR-002-group5-fidelity.md): E1 thay D1 (D1 là giao diện cũ); giữ D2; INR và request details theo eBay Help 4042 (request details là thiết kế clone vì không có ảnh thật) |
| Q-UI-009 | Nhãn lý do "Ask eBay to step in" / "Close your request" và quy tắc D2 "You have one chance to offer an amount" ([DISPUTE_DETAILS](../architecture/DISPUTE_DETAILS.md#nguồn-ui-và-nhãn)) | E03 dùng nhãn clone tự soạn; luật E01 cho seller offer lại sau khi buyer từ chối. Không chặn E04/E05 | Open 06/10/2026: dùng mặc định đã ghi; người dùng có thể gửi ảnh/link eBay để chỉnh |
| Q-UI-008 | Thời điểm đồng bộ điểm vào nhóm 5 A1–A5 ([UI_REVIEW](../architecture/UI_REVIEW.md#điểm-vào-nhóm-5-cần-đồng-bộ-theo-ebay-chưa-triển-khai)) | UI B04/B06/C04; không chặn E01 | Answered 06/10/2026: phương án 1, đã làm ở D06 |

A02 đã quan sát home/menu và đọc Help. Ngày 02/10/2026 người dùng gửi U1 và xác nhận “Chốt cả M1, R1, D1, D2”. Xem [gallery/quyết định](../architecture/UI_CANDIDATES.md); các ảnh là mẫu thiết kế, không chứng minh toàn bộ UI hiện hành. Chi tiết còn thiếu: [UI-REFERENCES](UI-REFERENCES.md). P1 cũng đã được chốt qua lời nhắn trực tiếp ngày 02/10/2026. Không chặn A05.

Mẫu câu hỏi: ID, task/màn hình, nguồn đã kiểm tra, chưa rõ điều gì, cần link/ảnh/video/mô tả nào, phần bị chặn, phần làm tiếp được, giả định đề xuất và câu trả lời/ngày/nguồn.

Khi có trả lời, cập nhật contract/ADR và task liên quan, đánh dấu Answered hoặc Deferred. Không để câu trả lời chỉ tồn tại trong chat. Hết thời gian chờ không phải xác nhận.

Q-UI-004 (B01, Answered 03/10/2026): S1 register đã chốt; L1 dùng ảnh eBay Welcome back user gửi sau, thay ảnh Agent Router gửi nhầm. Scope email/password giữ nguyên; không social/reset/Stay signed in. Không lưu ảnh/email cá nhân vào repo.

Q-UI-005 (B04, Open 03/10/2026): đề xuất PD1 từ https://www.ebay.com.hk/import/newtoebay (không ghi năm), ảnh https://ir.ebaystatic.com/pictures/hk/new-to-ebay/P3-6.png đã trình trong chat. Chờ chốt bản giản lược ảnh trái/thông tin phải, Contact seller theo U1. Tiếp tục query/API/test độc lập; chưa áp dụng UI đề xuất.

Q-UI-005 cập nhật 03/10/2026: người dùng trả lời 'Tôi sẽ gửi ảnh khác'. PD1 không được duyệt; chờ ảnh thay thế. Backend/API đã làm độc lập, UI detail chưa triển khai.

Q-UI-005 Answered: người dùng đã gửi ảnh chi tiết sản phẩm thay thế và yêu cầu tiếp tục; bố cục ảnh trái/title-seller-price-stock phải. Đã áp dụng/kiểm chứng B04 ngày 06/10/2026. PD1 Internet là đề xuất lịch sử, không dùng. Không commit ảnh/tài khoản trong ảnh nguồn.

Q-TEAM-001: user xác nhận 'Tạo và gán theo đề xuất'; đã tạo 5 Epic +20 ticket KAN-4–28. [Jira mapping](../team/JIRA_MAPPING.md).

Q-UI-006 (B05 checkout, Answered 06/10/2026): đã khảo sát eBay Checkout Help, eBay Connect Payments 2023 slide 3 và Pay with card chính thức; trình GIF C1 kèm nguồn không ghi năm của ảnh. User trả lời “Chốt C1 và bố cục đề xuất”. Áp dụng Ship to/item-quantity/COD demo/total-Place order, desktop hai cột/mobile một cột; [contract](../architecture/CHECKOUT.md), [mẫu](../architecture/UI_CANDIDATES.md). Không hỏi lại C1.

Q-BIZ-001 (D01/D02, **Answered 06/10/2026**): người dùng yêu cầu đề xuất giống eBay rồi trả lời "1. Đồng ý". Nguồn đã trình: [eBay Help Product reviews](https://www.ebay.com/help/selling/listings/product-reviews?id=5186), [Community content policy](https://www.ebay.com/help/policies/member-behavior-policies/community-content-policy?id=4265), [Changing feedback](https://www.ebay.com/help/buying/leaving-feedback-sellers/viewing-changing-feedback-left-sellers?id=4031), trang review eBay thật (72 product ratings / 43 reviews). Chốt: (a) rating 1–5 bắt buộc, comment tùy chọn; hiển thị "N product ratings · M reviews" (review = rating có comment). (b) Buyer không tự sửa/xóa review; nội dung vi phạm xử lý bằng Report + Admin ẩn mềm có audit ở D05. Server D01 đã đúng, không đổi hành vi. Câu hỏi bổ sung "ô tiêu đề review" (cần migration 0008): người dùng chọn "Giữ giản lược", không thêm tiêu đề.

Q-BIZ-002 (D02, **Answered 06/10/2026**, cùng câu trả lời "Đồng ý"): chỉ review gắn đơn (`OrderItemId IS NOT NULL`, tương đương eBay "verified purchase") được hiển thị và tính trung bình/số lượt. Review legacy giữ nguyên trong DB nhưng không hiển thị, không tính.

Q-BIZ-003 (D03, **Answered 06/10/2026**): đối chiếu eBay Help (feedback trong 60 ngày, Positive/Neutral/Negative, comment là thêm tùy ý) và R1 ("0 / 500", "wait 7 days before ... neutral or negative"). Người dùng chọn: Delivered + trong 60 ngày từ khi giao; comment tùy chọn tối đa 500; **áp dụng** chờ 7 ngày cho Neutral/Negative; form gộp như R1. Chi tiết [FEEDBACK](../architecture/FEEDBACK.md).

Q-UI-007 (D04 feedback profile, **Answered 06/10/2026**): đã trình F1 là trang eBay Feedback profile thật `https://www.ebay.com/fdbk/feedback_profile/bestbuy`, xem trực tiếp ngày 06/10/2026, kèm hai ảnh chụp lưu ignored trong `artifacts/d04/` (không commit). Nguồn phụ: [eBay Help reply feedback](https://www.ebay.com/help/selling/leaving-feedback-buyers/replying-feedback-received-item-sold?id=4102). Người dùng chọn **F1 giản lược**. Chi tiết [UI_CANDIDATES](../architecture/UI_CANDIDATES.md#f1--seller-feedback-profile).

Q-BIZ-004 (D04, **Answered 06/10/2026**):
- Tỷ lệ tích cực trên hồ sơ tính **12 tháng như eBay**; quyết định này thay quy tắc "dùng toàn bộ feedback" của PLAN §3.2 đối với tỷ lệ. Điểm uy tín (P−N) vẫn tính toàn thời gian; đây là cách agent hiểu, theo eBay (feedback score tích luỹ).
- Badge D03 phải đổi sang tỷ lệ 12 tháng khi triển khai D04.
- Seller reply: mỗi feedback chỉ reply một lần, không sửa và không rút lại (theo eBay Help).
- Chi tiết [FEEDBACK](../architecture/FEEDBACK.md#quyết-định-d04).

Q-BIZ-005 (D05, **Answered 06/10/2026**): người dùng yêu cầu "Tìm cách ebay xử lý rồi thực hiện D05". Đã đối chiếu [Feedback policy](https://www.ebay.com/help/policies/feedback-policies/feedback-policies?id=4208) ("may remove the comment, ratings and/or images", gỡ trong 24 giờ), [Product reviews](https://www.ebay.com/help/selling/listings/product-reviews?id=5186) (Report + chọn lý do) và Community (feedback bị gỡ "no longer there"). Áp dụng: nội dung bị gỡ biến mất, không placeholder; hai mức Hide (cả mục) / RemoveComment (giữ rating); Restore/Dismiss có audit là quyết định thiết kế của agent. Chi tiết [MODERATION](../architecture/MODERATION.md).

Nguyên tắc phạm vi (**Answered 06/10/2026**, [ADR-002](../decisions/ADR-002-group5-fidelity.md)): người dùng trả lời chung cho bốn câu hỏi rà soát (khiếu nại, thanh toán, catalog, tài khoản): đọc file đề bài; người dùng thuộc nhóm 5; tính năng thuộc yêu cầu nhóm 5 phải giống eBay; tính năng của nhóm khác chỉ cần cho demo thì làm giản lược, nhưng không được ảnh hưởng tính năng nhóm 5. Áp dụng: giữ giản lược B01/B04/B05/B06; điểm vào nhóm 5 trên trang nhóm khác phải giống eBay. Kết quả rà soát: [UI_REVIEW](../architecture/UI_REVIEW.md).

Q-UI-003 (**Answered 06/10/2026**): rà soát lại ảnh mẫu theo yêu cầu "tự tìm các mẫu UI, kể cả ảnh mẫu Q-UI-003". D1 (ảnh đăng 2023) là giao diện eBay đời cũ nên thay bằng E1 (return item desktop 2 bước, 11/2022) và danh sách lý do E1m (03/2026). D2 giữ nguyên. Item not received theo [Help 4042](https://www.ebay.com/help/buying/returns-refunds/get-help-item-hasnt-arrived?id=4042) và trang item picker live. Trang request details/timeline không có ảnh thật nên là thiết kế của clone theo câu chữ Help; nếu sau này có ảnh thật thì thay. Việc thay D1 là agent áp dụng ADR-002, người dùng có thể đổi khi chốt E02. Chi tiết [UI_CANDIDATES](../architecture/UI_CANDIDATES.md#rà-soát-06102026--khiếu-nại-q-ui-003).

Q-UI-008 (**Answered 06/10/2026**, người dùng: "chọn 1, triển khai task đồng bộ A1-A5" → task D06, contract [ENTRY_POINTS](../architecture/ENTRY_POINTS.md)). Câu hỏi ban đầu: năm điểm vào nhóm 5 còn lệch eBay (A1 nút Message trong seller card, A2 sao/% positive trên card catalog, A3 Leave feedback + Ready for feedback trên Purchases, A4 More actions ▾, A5 thu cột thư mục Messages). Phương án: (1) task nhỏ riêng trước E01; (2) gộp A4 vào E02, phần còn lại vào G01. Chưa triển khai khi người dùng chưa chọn.

Q-BIZ-006 (E01, **Answered 06/10/2026**): đối chiếu [Help 4042](https://www.ebay.com/help/buying/returns-refunds/get-help-item-hasnt-arrived?id=4042) và [Help 4041](https://www.ebay.com/help/buying/returns-refunds/return-item-refund?id=4041). Người dùng chọn: (1) ngày giao dự kiến tính lúc checkout, lưu lại, backfill đơn cũ; (2) Return có bước gửi trả (Accept → buyer gửi trả kèm tracking demo → seller refund mô phỏng); (3) mọi listing coi như 30-day returns, seller chỉ được Decline lý do đổi ý. Lý do/luồng eBay thay 4 lý do và trạng thái SellerResponded/Resolved/Rejected của PLAN §3.3 (ADR-002). Chi tiết [DISPUTES](../architecture/DISPUTES.md).
