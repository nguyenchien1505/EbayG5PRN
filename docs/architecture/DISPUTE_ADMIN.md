# E05 — Admin xử lý request và kết quả mô phỏng

Contract do agent soạn ngày 07/10/2026 trên main `029674b`, trước khi sửa source. Task này dựa trên:

- [DISPUTES](DISPUTES.md) (E01): lệnh `Decide`/Message của Admin, outcome `DecidedForBuyer` (hoàn tiền mô phỏng toàn dòng) / `DecidedForSeller`, cột `resolution`.
- [DISPUTE_AUTOMATION](DISPUTE_AUTOMATION.md) (E04): request vào hàng đợi khi `Escalated`, hạn xem xét 48 giờ.
- [MODERATION](MODERATION.md) (D05) và [SECURITY](SECURITY.md) (B02): policy `AdminDemo` + AdminNetwork; service kiểm lại role và credential trong DB ở mỗi thao tác.

Requirement:

- **G5-08:** phần còn lại "admin phân xử có audit".
- **SUP-05:** phần dispute: chặn user thường, mạng demo được phép, audit, không xóa vật lý.
- PLAN §3.3: "Phân xử có lịch sử actor, thời gian và kết quả; hoàn tiền chỉ mô phỏng."

Ngoài phạm vi:

- Appeal sau phân xử (E01 đã loại trừ).
- Partial refund do admin quyết.
- Email/push.
- Phân công admin xử lý request.
- Không thêm migration; luật E01 giữ nguyên.

## Nguồn nghiệp vụ

- [Help 4042](https://www.ebay.com/help/buying/returns-refunds/get-help-item-hasnt-arrived?id=4042) và [Help 4702](https://www.ebay.com/help/selling/managing-returns-refunds/ask-ebay-step-help-sellers?id=4702): sau "Ask eBay to step in", eBay xem xét và "usually within 48 hours" đưa ra quyết định. Kết quả nghiêng về buyer (hoàn tiền) hoặc về seller.
- Giao diện admin nội bộ của eBay không public, nên trang admin là **thiết kế của clone**, cùng phong cách với hàng đợi kiểm duyệt D05 (ADR-002: phần admin được giản lược). Phần buyer/seller nhìn thấy (timeline, nhãn trạng thái) vẫn theo E03.

## Quyền

- Route dưới `AdminController` (`[Authorize(Policy="AdminDemo")]`) và middleware AdminNetwork (B02). Buyer/Seller → 403. Chưa đăng nhập → redirect.
- **Siết E01:** service chỉ coi user là Admin khi role là `Admin` **và có credential** (`password` NOT NULL), giống D05. Thay đổi này áp dụng cho `FindAsync`, `ActAsync` và hàng đợi mới. Admin không có credential → `null`/NotFound.
- Admin chỉ có quyền ở các trang `/Admin/*`. `/Disputes/*` vẫn trả 403 cho Admin (E03).

## Service (mở rộng `IDisputeService`)

| Phương thức | Hành vi |
|---|---|
| `QueueAsync(adminId, DisputeQueueQuery(State, Page))` | Chỉ Admin có credential; người khác → null. **Waiting**: request đang `Escalated`, sắp theo `DueAtUtc` (hạn 48 giờ) tăng dần, rồi `id`. **Closed**: request đã từng escalate (`EscalatedAtUtc` NOT NULL) và đã Closed, sắp theo `ClosedAtUtc` giảm dần. 20 dòng/trang |
| `FindAsync(adminId, id, page)` | Như E03, Viewer = Admin. Riêng với Admin, mỗi event có thêm `ActorName` (username của buyer/seller/admin đã thao tác); buyer/seller không bao giờ thấy tên admin |
| `ActAsync(adminId, id, Decide/Message)` | Luật E01: chỉ khi `Escalated`; `Decide` bắt buộc `ForBuyer` và note 1–2000 ký tự. Note được ghi vào event `Decided` (cả hai bên thấy) và cột `resolution`. Hai admin quyết cùng lúc: dòng bị khóa, người sau nhận Conflict |

Một dòng hàng đợi gồm: Id, OrderId, ItemTitle, Kind, Reason, Status, Outcome, EscalatedAtUtc, DueAtUtc, ClosedAtUtc, LineTotal, RefundAmount, BuyerName, SellerName.

## Trang và route

Tất cả `no-store`; POST cần CSRF.

| Route | Nội dung |
|---|---|
| `GET /Admin/Disputes?state=Waiting\|Closed&page=` | H1 "Requests for eBay review". Tab "Waiting for a decision" / "Closed". Mỗi dòng: loại · lý do, item, Order #, giá dòng, buyer, seller, thời điểm escalate, "Decide by {due}" kèm nhãn **Overdue** khi đã quá hạn 48 giờ (Waiting), hoặc outcome + thời điểm đóng (Closed). Link "Review" |
| `GET /Admin/Dispute/{id}?activity=` | Tóm tắt (trạng thái theo góc nhìn admin, hạn); buyer, seller, item, order, lý do, preference, details, offer/tracking/refund; **Decision** và **Message both parties** (khi `Actions` cho phép); **Audit trail**: timeline đủ actor (tên + vai trò), thời điểm, nội dung, phân trang 20 |
| `POST /Admin/DisputeAct` | Field: `DisputeId`, `Action` (chỉ `Decide` hoặc `Message`), `ForBuyer`, `Message`. Thành công → 302 về trang chi tiết, kèm notice "Decision recorded. The request is closed." / "Message sent to the buyer and seller." Invalid → 400 hiển thị lại với dữ liệu đã nhập; Conflict → 409; NotFound → 404; Forbidden → 403 |

**Form Decision:**

- Radio "Decide for the buyer — refund US $X (simulated) and close" / "Decide for the seller — close without a refund".
- Textarea "Decision note" bắt buộc, có ghi "shown to the buyer and seller".
- Nút "Confirm decision".
- Ghi rõ đây là hoàn tiền mô phỏng: không có tiền di chuyển, Payment không đổi (E01).

**Nhãn trạng thái phía admin:**

| Trạng thái | Nhãn |
|---|---|
| Escalated | Waiting for an eBay decision |
| DecidedForBuyer | Closed — decided for the buyer |
| DecidedForSeller | Closed — decided for the seller |
| BuyerClosed | Buyer closed this request |
| SellerDeclined | Closed — seller declined the return |
| Open / ReturnAccepted / ReturnShipped / các kết quả khác | Dùng câu trung lập ("Waiting for the seller…", "Closed — refunded"…) |

**Menu tài khoản:** Admin có thêm "Request reviews" bên cạnh "Moderation".

## Thông báo và audit

- **Thông báo cho buyer/seller:**
  - Event `Decided` hiện trong timeline của cả hai bên: "eBay decided this request in your favor and refunded US $X (simulated)." hoặc "…in the seller's favor", kèm note của admin.
  - Nhãn trạng thái ở danh sách, Purchases và trang chi tiết đơn đổi theo outcome (E02/E03).
  - Tin nhắn của admin hiện là "eBay sent a message."
  - Không tạo AutoNotice mới cho request đã Closed (E04). Vì vậy không cần migration.
- **Audit:** timeline append-only (trigger 0011) ghi actor (`ActorId` + vai trò), thời điểm, loại và nội dung. `resolution` lưu note phân xử. Request không xóa được (trigger). Admin xem audit đầy đủ kèm tên người thao tác.

## Kiểm tra

- **Service:**
  - Hàng đợi chỉ cho Admin có credential; thứ tự Waiting theo hạn; Closed gồm request đã escalate rồi đóng (do admin, seller refund hoặc buyer close); không lẫn request chưa escalate; phân trang.
  - `ActorName` chỉ có khi Viewer là Admin.
  - Admin không có credential → null/NotFound.
  - Decide: Payment không đổi; `RefundAmount` toàn dòng khi quyết cho buyer.
  - Hai admin quyết song song → đúng 1 Success, 1 Conflict, 1 event.
  - Seller refund trong lúc chờ → admin Decide → Conflict.
- **HTTP:**
  - Buyer/Seller → 403; chưa đăng nhập → redirect; Admin ngoài mạng demo → 403.
  - CSRF.
  - `Action` khác Decide/Message → 400. Thiếu ForBuyer hoặc note → 400 giữ dữ liệu đã nhập. Quyết lần hai → 409.
  - Sau khi quyết: buyer/seller thấy kết quả và note trên trang request và trong danh sách; nội dung được encode.
- **Browser:** hàng đợi → chi tiết → Decide → buyer thấy kết quả; desktop/390/320px.
