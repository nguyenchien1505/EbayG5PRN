# Demo seed contract — A06

03/10/2026. Dependency A05: checkpoint 10c830a, source runner/mapping/tests đã đối chiếu; SQL/Redis healthy.

- Lệnh chủ động `Seed-Demo.ps1`, không chạy trong Web startup hoặc migration. Chỉ target allowlist như migration, phải initialize trước.
- Migration 0003 thêm duy nhất dbo.DemoSeedHistory (version + timestamp); app không có quyền journal này. Không sửa 17 bảng nguồn.
- Dataset `A06-v1` chạy trong một transaction với application lock transaction trên database đích. Journal ghi cùng transaction. Chạy lại trả skipped, giữ nguyên mọi chỉnh sửa/xóa dữ liệu demo; không tự chữa hoặc reset.
- Email cố định `buyer.a06@example.invalid`, `seller.a06@example.invalid`, `admin.a06@example.invalid`. Nếu đã tồn tại email trước lần seed đầu: từ chối toàn bộ, không nhận nhầm/ghi đè user. ID do SQL cấp, không giả định ID 1/2/3; không tái sử dụng category/product có cùng tên.
- Role `Buyer`, `Seller`, `Admin`; password NULL, không mật khẩu dùng chung hoặc hash giả. B01 phải từ chối NULL và thiết kế cấp credential riêng; hiện chưa đăng nhập được.
- Dữ liệu English, hư cấu: một store/address, hai categories, ba fixed-price products/inventory; bốn orders Pending/Shipped/Delivered/Cancelled với item totals và COD payment nhất quán. Timestamp cố định UTC, không phụ thuộc ngày chạy. Các nhãn trạng thái chỉ là fixture; B05/B06 chốt transition/validation sau. Không seed review/dispute/chat giả trước contract module.
- Images NULL cho tới task UI chọn ảnh. Không cần gallery trong A06. Không gọi dịch vụ ngoài.
- File dự kiến: migration 0003, DemoSeedRunner + embedded Seed SQL riêng, CLI dispatch, Seed-Demo script, SQL integration tests và tài liệu/evidence. Test lần đầu/rerun/concurrent, dữ liệu có sẵn/đã sửa, collision và lỗi giữa transaction rollback; chạy lại bảo toàn metadata A05.
