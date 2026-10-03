# Vì sao khác cấu trúc ProjectCode?

Đối chiếu source ngày 03/10/2026, không sửa project nguồn. Quyết định nền: [ADR-001](../decisions/ADR-001-foundation.md), PLAN §2.2–2.3 và [A03](../tasks/A03.md).

| Khía cạnh | ProjectCode PRN232_Project | EbayGroup5 hiện tại |
|---|---|---|
| Host chạy | Backend ASP.NET API và Frontend ASP.NET MVC riêng | Web host chứa MVC và API; SignalR cùng host khi tới module chat |
| Kết nối | Frontend Program đăng ký HttpClient API; ApiClientHelper gọi HTTP | Controller gọi service trong process; API là adapter công khai riêng |
| Auth | Backend JWT bearer; Frontend helper lấy JwtToken từ Session | Cookie cùng origin + CSRF, role/object authorization |
| Code dữ liệu | Backend Models, Repositories, Services | Core POCO; Infrastructure EF/query; Application DTO/interface/use case |
| Database | DbContext trong Backend | EF mapping/query, console Database chạy DbUp riêng |
| Giao diện | Frontend Controllers/Views/wwwroot | Web Controllers/Views/wwwroot |

Số project C# không phải số dịch vụ chạy. Core, Application, Infrastructure là class libraries; Database là công cụ migration chủ động, không phải web service. Vẫn cùng .NET 8/MVC/SQL Server; khác cách đóng gói và ranh giới HTTP.

Mục tiêu của lựa chọn mới: viết mới theo scope nhóm 5, dùng code cũ làm tham khảo; giảm cấu hình URL/token/CORS và gọi HTTP nội bộ; tách kiểm thử và migration khỏi Web startup; giữ nguyên schema nguồn. Không phải .NET bắt buộc, không có kết luận cấu trúc cũ sai.

Đánh đổi: ít độc lập triển khai frontend/backend; phải giữ controller gọn và dependency rõ để monolith không bị trộn logic; thêm project/thư mục khiến người mới thấy phức tạp. Tách host phù hợp nếu cần release/scale frontend và backend độc lập hoặc phục vụ nhiều client theo contract riêng. Một host vẫn có API và có thể chạy nhiều replica; deployment đó chưa hoàn tất.

Trạng thái thực: Application trước B04 chủ yếu là khung; B04 bắt đầu đặt ICatalogService/DTO tại đây. AccountService hiện vẫn ở Infrastructure và AccountController có một truy vấn resource trực tiếp; đây chưa phải Clean Architecture nghiêm ngặt hay module hóa hoàn chỉnh. Không nên khẳng định cấu trúc mới đã tối ưu mọi mặt.

Source đã đọc: project cũ Frontend/Program.cs, Helpers/ApiClientHelper.cs, Frontend.csproj (còn ProjectReference tới Backend); Backend/Backend.csproj, Program.cs, ProgramConfig/HoangAnh.cs và Khoa.cs. Không đọc/in cấu hình secrets và không suy ra database migration cũ chỉ từ EF Tools package.
