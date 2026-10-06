# E02 — Form khiếu nại và nháp trên server

Contract do agent soạn ngày 06/10/2026 trên main `e61e3ec`, trước khi sửa source. Task này dựa trên [DISPUTES](DISPUTES.md) (E01) và [ADR-002](../decisions/ADR-002-group5-fidelity.md).

Không thuộc phạm vi E02:

- Trang request details/timeline và các thao tác sau khi mở (E03).
- Job tự động (E04).
- Admin (E05).

## Nguồn UI

Lấy từ [UI_CANDIDATES](UI_CANDIDATES.md#rà-soát-06102026--khiếu-nại-q-ui-003):

- **E1:** "Return item" 2 bước.
  - Bước 1: "Why are you returning this item?" với radio lý do → Next.
  - Bước 2: "Add details" ("Give us a little more info.") → Confirm return.
  - Có banner thông tin ở đầu.
- **E1m:** nhãn 10 lý do theo bản 2026: Ordered the wrong item, Changed my mind, No longer needed, Found a better price, Doesn't fit, Item damaged, Item defective, Missing parts or pieces, Item didn't match description, Received the wrong item.
- **E0/H1** (Help 4042):
  - Form INR: "I'd like a refund" / "I'd still like the item" → "Add any additional information" → Send request.
  - Khối "What happens from here" gồm 4 bước.

**Lệch có chủ ý so với eBay** (quyết định của agent, người dùng có thể đổi):

- **Không có "Add photos (0/10)".** Upload ảnh cần kho file và kiểm duyệt nội dung, ngoài phạm vi đã duyệt. Trang ghi rõ đây là bản demo không nhận ảnh.
- **Banner Return ghi "30-day returns"** (Q-BIZ-006), kèm dòng: lỗi hàng thì được bảo vệ theo Money Back Guarantee (demo).
- **Lý do đổi ý** có thêm gợi ý "The seller may decline returns for this reason."

## Điểm vào (Buyer)

- **Purchases → More actions ▾** (menu D06), áp cho dòng đầu tiên còn sản phẩm của đơn:
  - "I didn't receive it" khi INR `CanOpen`.
  - "Return this item" khi Return `CanOpen`.
  - "See request details" khi dòng đó đang có request. E02 trỏ tới trang chi tiết đơn (`#line-{id}`); E03 sẽ trỏ tới trang request.
  - Contact seller giữ như cũ.
- **Trang chi tiết đơn**, theo từng dòng:
  - Hai link trên khi tương ứng `CanOpen`.
  - Khi đã có request: một khối trạng thái với nhãn kiểu eBay và hạn của bước hiện tại.
- Eligibility của trang Purchases được tính theo lô: `EligibilityAsync(buyerId, orderItemIds)` với tối đa 50 dòng, cùng luật với bản cho một dòng.

Nhãn trạng thái:

| Trạng thái | Nhãn |
|---|---|
| Open | Waiting for the seller to respond by {due} |
| ReturnAccepted | Return accepted — ship it by {due} |
| ReturnShipped | Return shipped — waiting for the seller's refund by {due} |
| Escalated | eBay is reviewing this request |
| Closed | Theo outcome: Refunded / Partial refund / You closed this request / Item received / Seller declined the return / Return not shipped in time / Decided in your favor / Decided in the seller's favor |

## Trang và route

Tất cả dành cho Buyer policy, `no-store`, CSRF dùng form token (`AutoValidateAntiforgeryToken` toàn cục).

| Route | Nội dung |
|---|---|
| `GET /Disputes/NotReceived?orderItemId=` | Tiêu đề "Report an item you didn't receive". Item, order #, giá dòng, ngày giao dự kiến; 2 radio; textarea tùy chọn kèm đếm 0/2000; "Send request"; khối "What happens from here" |
| `GET /Disputes/Return?orderItemId=` | Bố cục E1: bước 1 radio lý do + Next, bước 2 details bắt buộc kèm đếm + "Confirm return". Không có JS thì hiện cả hai bước |
| `POST /Disputes/Open` | Gọi `OpenAsync` (xem kết quả bên dưới) |
| `POST /Disputes/Draft` | Lưu nháp, trả JSON `{ savedAtUtc }`. Lỗi: 400 Invalid, 404 NotFound, 409 khi không còn mở được |

Kết quả của `POST /Disputes/Open`:

- **Success:** redirect (302) về trang chi tiết đơn `#line-{id}`, kèm notice "Your request was sent. The seller has until {due} to respond."
- **Invalid:** 400, hiển thị lại form với dữ liệu đã nhập.
- **Conflict:**
  - Nếu request đang mở có cùng loại, do chính buyer này mở → coi là **gửi lại**: redirect về cùng chỗ, notice "This request was already sent." và không tạo bản thứ hai.
  - Các trường hợp khác → 409, hiển thị lại form kèm lý do.
- **Forbidden/NotFound:** 403/404.

Còn lại:

- Khi `GET` mà dòng không mở được → 409, trang báo lý do và có link quay lại đơn.
- Khi `DisputeTiming.IsDemo` → mọi trang hiện "Demo timing: 1 business day = N seconds."

## Nháp (G5-N03)

- **DbUp `0012_DisputeDraft.sql`:**
  - Bảng `DisputeDraft`: `BuyerId`, `OrderItemId`, `Kind`, `Reason` (NULL hoặc khớp Kind), `Preference` (chỉ cho INR), `Details` nvarchar(2000), `UpdatedAtUtc`.
  - UNIQUE (BuyerId, OrderItemId, Kind); FK tới User và OrderItem.
  - Grant SELECT/INSERT/UPDATE/DELETE cho `ebay_app`.
- **Lưu nháp:**
  - Chỉ lưu khi buyer sở hữu dòng và loại đó đang `CanOpen`.
  - Upsert một dòng cho mỗi bộ (buyer, dòng hàng, loại); trường rỗng được lưu là NULL.
  - Details được kiểm như form: tối đa 2000 ký tự, không có ký tự điều khiển. Đây là nháp nên được phép để trống.
- **Khôi phục:** `GET` form đọc nháp và điền sẵn; hiện "Draft restored". Nếu nháp đã có lý do thì Return mở thẳng bước 2.
- **Xóa:** `OpenAsync` xóa nháp **trong cùng transaction** khi mở thành công. Mở thất bại thì giữ nháp. Nháp cũ không tự hết hạn (ghi nhận, không xử lý).
- **Client** (`wwwroot/js/dispute-form.js` + module thuần `dispute-draft.mjs` có Node test):
  - Lưu sau 800 ms không gõ.
  - Lưu ngay khi `visibilitychange` sang hidden hoặc `pagehide` (`fetch` keepalive, gửi form-data kèm token).
  - Hiện trạng thái "Saving draft…" / "Draft saved" / "Couldn't save draft — your text is still here".
  - Không gửi lại khi dữ liệu không đổi.
  - Lần lưu sau chỉ bắt đầu khi lần trước xong, để thứ tự lưu không bị đảo.

## Kiểm tra

- **SQL/service:**
  - Lưu nháp sai owner/loại/dữ liệu; upsert giữ đúng một dòng.
  - Open xóa nháp; Open lỗi giữ nháp; rollback.
  - Eligibility theo lô khớp bản cho một dòng.
  - Constraint của bảng nháp; migration.
- **HTTP:**
  - Quyền và CSRF.
  - Form điền lại nháp sau reload.
  - Gửi hai lần → một request; Invalid giữ dữ liệu.
  - Điểm vào trên Purchases và trang chi tiết đơn; trang báo không mở được.
- **Node:** autosave debounce, flush, chống đảo thứ tự, không gửi trùng.
- **Browser** desktop/390/320px: luồng INR và Return; reload giữa chừng vẫn còn dữ liệu.
