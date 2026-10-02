# ADR-001 — Nền tảng đã chốt

Trạng thái: Accepted, 02/10/2026. Nguồn: lựa chọn trực tiếp của người dùng và kế hoạch đã duyệt.

- Viết mới tại EbayGroup5; code cũ chỉ để tham khảo. Không sửa hai thư mục nguồn.
- .NET 8 MVC/Razor, EF Core, SignalR, SQL Server, modular monolith, UI tiếng Anh; tài liệu tiếng Việt.
- Full nhóm 5; tài khoản, catalog, Buy Now/COD, giao nhận và admin đủ demo độc lập. Không xây full năm nhóm.
- Schema SQL nguồn giữ nguyên; DbUp quản lý migration mở rộng, không dùng EF migrations song song.
- Cookie cùng origin + CSRF; khóa Data Protection chia sẻ giữa replica. Không mặc định bê JWT từ code cũ.
- Chat dùng WebSocket/SkipNegotiation và Redis; SQL là nguồn lưu bền vững, client có idempotency và tải bù.
- Product review tách seller feedback; đơn/dòng hàng là bằng chứng quyền đánh giá. Công thức bản clone được ghi rõ là giản lược.
- Local Docker/K8s, Nginx, CI/CD GitHub Actions; chưa tạo remote/runner/cluster.
- Hai backlog: coding nhỏ cho agent, Jira theo module cho TV1–TV5. Không ghi nhận hoạt động chưa thực hiện.

Lý do: Giảm phụ thuộc nhóm khác và số thành phần vận hành nhưng vẫn đáp ứng yêu cầu 25% hạ tầng. Code cũ có điểm không tương thích quy tắc schema/bảo mật.

Chi phí: Cần bổ sung .NET 8 SDK; .NET 8 kết thúc hỗ trợ 10/11/2026 theo chính sách Microsoft đã tra cứu. Không tự nâng framework.

Các contract cụ thể được chốt tại task thiết kế từng module. Khi thay quyết định này phải có ADR mới và chỉ rõ ảnh hưởng tới task/test.
