# Handoff — A03

Ngày 02/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5. Commit nền 33be053; nhánh task/A03-scaffold, fast-forward main sau kiểm tra. Tìm checkpoint bằng `git log --oneline --grep=A03`.

## Đã xong

- Scaffold bốn project .NET 8, MVC shell, static CSS, `/health/live`; project references đúng chiều.
- SDK 8.0.425 cài riêng user, runtime 8.0.31; SDK 10 hệ thống giữ nguyên. global.json và NuGet lock files được commit.
- [LOCAL_SETUP](../runbooks/LOCAL_SETUP.md) có lệnh setup/restore/build/run/smoke; [SOURCE_MAP](../architecture/SOURCE_MAP.md) là bản đồ source thực tế.
- Người dùng đã chốt U1 Message seller + M1 inbox, R1 feedback, D1/D2 returns. [Gallery](../architecture/UI_CANDIDATES.md) có nguồn và ảnh; P1 Purchase history mới tìm còn chờ duyệt. Không tự mở rộng scope theo icon/action trong ảnh.
- Quy định mới: ảnh còn thiếu tự tìm Internet, trình cho người dùng chốt. Không hỏi lại mẫu đã duyệt, không commit raw screenshot có dữ liệu tài khoản.

## Kiểm tra

PASS SDK 8.0.425, restore locked, Release build (0 warning/error), HTTP home/liveness/CSS. Đã dừng web sau smoke, không còn listener port 5085. Chi tiết [EV-A03-01](../evidence/A03.md).
PASS validator: checksum SQL/17 bảng, 39 task đồng nhất, requirement IDs và link trong 68 Markdown; Git whitespace pass. Không có test nghiệp vụ/DB vì chưa triển khai; yêu cầu runtime vẫn Pending.

## Tiếp theo

**A04** — Compose SQL Server/Redis và local config. Card còn Planned: đọc card/ADR/CON liên quan, cụ thể hóa contract trong task rồi triển khai. Không cần nạp tài liệu UI hoặc code cũ.
Đọc LOCAL_SETUP khi chạy dotnet; dot-source `scripts/Use-LocalDotnet.ps1` trong mỗi terminal mới vì PATH hệ thống vẫn là SDK 10.
Chưa có auth/EF/DbUp/connection string/Redis hoặc HTTPS. AllowedHosts chỉ localhost/127.0.0.1; khi thêm container phải cấu hình rõ theo môi trường. Không lấy liveness làm readiness.

## Còn chờ

GitHub private URL (A07), mapping TV1–TV5, deadline. P1 chờ lựa chọn; mobile/profile/timeline chưa đủ bằng chứng và cần tìm tiếp khi làm task UI tương ứng. Các mẫu đã chốt không phải ảnh eBay 2026 được xác minh tuyệt đối.

Handoff A02 lưu riêng; không nạp lịch sử mặc định.
