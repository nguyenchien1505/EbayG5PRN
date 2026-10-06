# Handoff — D02 review UI và điểm trung bình

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `ae10f9d`; nhánh `task/D02-review-ui`.
- Checkpoint: `git log --oneline --grep=D02`.
- Handoff D01 lưu ở [D01](D01.md).

## Hoàn tất

- Q-BIZ-001/002 **Answered** (đối chiếu eBay, người dùng đồng ý; ghi trong [questions](../questions/INDEX.md)):
  - Rating bắt buộc, comment tùy chọn.
  - Buyer không tự sửa/xóa review; thay bằng Report + Admin ẩn ở D05.
  - Chỉ tính review gắn đơn.
  - Không có tiêu đề review.
- Contract D02 nằm trong [REVIEWS](../architecture/REVIEWS.md). Không có migration; schema giữ nguyên.
- Service, `IReviewService`:
  - `ProductReviewsAsync`: summary (`Ratings`, `Reviews`, `Average` 1 chữ số thập phân, phân bố) + trang 10 review + sort recent/highest/lowest.
  - `FindItemAsync`: lấy một dòng đơn kèm eligibility.
  - `ListForOrderAsync` trả thêm `Rating`.
  - Bộ lọc dùng chung `Visible()`: D05 thêm điều kiện ẩn tại đây.
- Web:
  - `GET/POST /Reviews/Create` (form R1). Khi lỗi thì render lại form: 400 cho input sai, 409 cho chưa nhận/trùng. Thành công → thông báo qua TempData.
  - Orders/Details có nút "Leave a review" / "You rated…" / lý do không review được.
  - Catalog/Details: dòng sao dưới tiêu đề + mục "Ratings and reviews".
  - `/Catalog/Reviews/{id}`.
  - Partials `_Stars`, `_RatingSummary`, `_ReviewList`; CSS trong `site.css` (gồm `.validation-errors` dùng chung).
- Tests: `ReviewDisplayTests.cs` (4 test lớn). Các helper của `ReviewTests` đổi sang `internal`.

## Verification

- [Evidence](../evidence/D02.md):
  - Test-CI local: **161/161 .NET + 14/14 Node**, build 0 warnings.
  - Mutation bỏ lọc legacy bị test bắt.
  - Browser desktop + 320px: tổng hợp đúng, gửi review, encode, không tràn ngang.
- Trạng thái requirement:
  - **G5-03 Done**.
  - G5-04 Pending phần kiểm duyệt (D05).
  - G5-05 Pending D03–D04.
  - SUP-05 Pending D05/E05; UI-01 Pending.
- GitHub CI: kết quả ghi ở bên dưới sau khi push.
- `launchSettings.json` (untracked) không bị đụng. Fixture/DB/ảnh preview nằm trong `artifacts/d02`, đã ignore.

## Lưu ý cho task sau

- **D03/D04:** seller feedback (Positive/Neutral/Negative) dùng phần trên của mẫu R1. Không suy feedback từ số sao review. Profile chưa có mẫu UI (Q-UI-002 Partial).
- **D05:**
  - Thêm điều kiện ẩn/kiểm duyệt vào `ReviewService.Visible`.
  - Thêm Report + Admin ẩn mềm có audit; không xóa vật lý.
  - Summary phải đổi khi review bị ẩn.

## Tiếp theo / Jira

- D02 xong. Chờ người dùng chọn task tiếp, không tự làm D03/E/F.
- Ledger chờ gom đợt sync:
  - B05+B06 → KAN-11/TV1.
  - C01 → KAN-13; C02 → KAN-14; C03+C04 → KAN-15; C05 → KAN-16 (TV2).
  - **D01+D02 → KAN-17/TV3 Pham Van Cuong: hoàn tất local** (chưa sync live).
  - KAN-20 vẫn còn D03–D05.
- Không tạo worklog/review/issue.
