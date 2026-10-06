# Handoff — E03: trang request, timeline và danh sách buyer/seller

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `0230693`. Runtime trên `task/E03-dispute-details-dashboard`; checkpoint `git log --oneline --grep=E03`. GitHub CI: xem mục Verification.
- Handoff trước: [E02](E02.md).

## Hoàn tất

- Contract: [DISPUTE_DETAILS](../architecture/DISPUTE_DETAILS.md). **Không có migration**; DbUp vẫn 0001–0012, 13 assets.
- **Application/Infrastructure** (chỉ đọc, luật E01 giữ nguyên):
  - `ListAsync(actor, DisputeListQuery)`: buyer xem request đã gửi, seller xem request đã nhận; lọc kind/state, 10 dòng/trang, `id DESC`; vai trò khác → null.
  - `FindAsync(actor, id, timelinePage)`: timeline mới nhất trước, 20 event/trang, kèm `EventTotal`. Thêm `Counterpart`/`BuyerName`/`SellerName`. Overload cũ trả trang 1.
  - `RequestsForOrderAsync(actor, orderId)`: request theo dòng hàng, cho trang chi tiết đơn phía seller.
- **Web:**
  - `DisputesController`: class `[Authorize(Roles="Buyer,Seller")]`; 4 action của E02 giữ policy Buyer.
    - `Index`: "Returns and requests". Seller có tab Returns / Requests and disputes; cả hai có chip Open/Closed.
    - `Details`: theo D2/H1, với `?reply=` là bước 2 và `?activity=` là trang timeline.
    - `Act` (POST): lý do Escalate/Close ghép vào message; amount parse theo invariant culture; chặn lệnh system/admin.
  - `Models/DisputeRequests.cs`: form, câu chữ, nhãn reply, câu timeline, các bước.
  - `DisputeText.Status` có thêm nhãn theo người xem (seller).
  - Views `Disputes/Index`, `Disputes/Details`; `wwwroot/js/dispute-request.js` (bộ đếm ký tự, khóa nút khi submit); CSS trong `site.css`.
  - **Điểm vào:**
    - Purchases → See request details mở `/Disputes/Details/{id}`.
    - Trang chi tiết đơn: buyer có link ở dòng trạng thái; seller có trạng thái và See request details.
    - Menu tài khoản có "Returns and requests".
  - Sau khi gửi form E02 vẫn redirect về trang chi tiết đơn như cũ.
- **Tests:**
  - `DisputeDetailsTests`: 4 test mới gồm service list/paging, timeline, HTTP luồng Return, HTTP luồng INR escalate/close.
  - Test E01 (helper đảo thứ tự timeline) và E02 (link mới) được điều chỉnh có chủ ý.

## Verification

- [Evidence](../evidence/E03.md):
  - `Dispute*` 31/31.
  - **Test-CI local `scripts/Test-CI.ps1` pass: 234/234 .NET + 20/20 Node**, build 0 warnings, container CI tự xóa.
  - Mutation 2/2 bị bắt.
  - Browser desktop/390/320px trên preview cô lập.
- Verify-Planning pass; `git diff --check` sạch.
- Đã chạy `Clear-TestArtifacts.ps1 -Apply -DatabasesOnly`, xóa 53 DB test từ các lần chạy tập trung. Giữ DB preview `EbayGroup5_Test_D02Visual…`; xóa bằng `-IncludePreview` khi không cần nữa.
- Fixture E03 nằm trong `artifacts/d02/preview/Program.cs` (bị ignore, không commit).
- GitHub CI cho runtime E03: ghi bằng commit docs-only `[skip ci]` sau khi pass.
- Không đụng `launchSettings.json` (untracked).

## Câu hỏi / giả định

- **Q-UI-009 (Open, không chặn):**
  - Nhãn lý do Ask eBay to step in / Close your request do clone tự soạn.
  - D2 ghi "one chance to offer", nhưng luật E01 cho seller offer lại sau khi buyer từ chối.
- Tên trang "Returns and requests" là giả định của clone, vì clone không có hủy đơn.

## Cho task sau

- **E04:**
  - Job gọi `ActAsSystemAsync`: Escalate khi `Open` quá hạn mà seller chưa phản hồi; ExpireReturn khi `ReturnAccepted` quá hạn.
  - Thêm event `AutoNotice`. Trang Details đã hiển thị actor System là "eBay" và có câu cho `AutoNotice`.
  - Dùng index `IX_Dispute_Due`.
- **E05:**
  - Admin đọc qua `FindAsync` (Viewer = Admin).
  - Cần trang/route riêng dưới AdminNetwork, vì `/Disputes/*` đang trả 403 cho Admin.
  - `Decide` và Message của admin đi qua `ActAsync`.

## Tiếp theo / Jira

- E03 → **KAN-22 / TV4 Nguyên Nguyễn: hoàn tất local**. Chưa sync live; không tạo worklog/review/issue.
