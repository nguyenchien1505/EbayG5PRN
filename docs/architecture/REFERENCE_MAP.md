# Bản đồ code tham khảo — A02

Khảo sát tĩnh ngày 02/10/2026. Không chạy, sửa hoặc import code kỳ trước. Các nhận xét bên dưới áp dụng đúng phần code đã đọc, không phải kết luận kiểm thử toàn bộ dự án.

## Cách đọc có phạm vi

Root nhóm 5 cũ tính từ repo mới: `../ProjectCode/PRN232_Project_Group_5/PRN232_Project/PRN232_Project`. Bảng dưới dùng đường dẫn tương đối với root này; số dòng là mốc tại lần khảo sát, ưu tiên tìm symbol khi file thay đổi. Không đọc appsettings hoặc Test-Account để tránh nạp secret không cần thiết.

| Task mới | File/symbol đã kiểm tra | Học hỏi được | Không kế thừa / thay thế |
|---|---|---|---|
| C01–C03 | `Backend/Hubs/ChatHub.cs`: Register L56, SendMessage L67, GetConversation L116 | SignalR event và luồng lưu rồi gửi DTO | Register/userId và senderId do client cung cấp; dictionary static L12 chỉ cục bộ tiến trình; log content L69. Dùng principal, membership, Redis, log metadata |
| C02 | `Backend/Controllers/ChatController.cs`: PostMessage L27, GetConversation L112, GetChatUsers L128 | REST phục vụ lưu/đọc chat | Body nhận senderId, truy vấn cặp user tùy ý, adminId mặc định. Thay bằng DTO typed và người dùng hiện tại; không trả inner exception |
| C02, C05 | `Backend/Repositories/MessageRepository.cs`: GetConversationAsync, GetChatUsersAsync | Lịch sử theo cặp user và thời gian; hội thoại theo tin cuối | GetConversationAsync tải toàn bộ bằng ToListAsync, không cursor; chọn username từng user. Viết query phân trang/projection và không bỏ FK |
| C04 | `Frontend/Views/Shared/_ChatPartial.cshtml`: chat-root, chat-user-list, chat-body, chat-input | Danh sách hội thoại + vùng đọc + composer, aria-live | Đây là widget chat của bài cũ, chưa chứng minh là UI eBay. Không dùng làm chuẩn pixel/layout |
| A05, B02, F01 | `Backend/Program.cs` L95–131 | Danh sách cấu hình cần kiểm tra khi khởi động | Development tự DROP FK/index/cột Message. Tuyệt đối không mang startup này sang; không chạy code cũ để demo |
| D01 | `Backend/Services/ReviewService.cs`: CreateReviewAsync L18 | DTO → service → persistence | Service này chỉ tìm user và chặn review lại sản phẩm; không kiểm tra đơn Delivered. Dùng quyền theo OrderItem, cho phép mua lại đánh giá tiếp |
| B06, D02 | `Frontend/Views/Purchase/Index.cshtml`: isDelivered L144, action feedback L168; `Purchase/LeaveReview.cshtml`: reviewForm L42 | Đơn hàng dẫn tới form sao và comment; preview sản phẩm | View cũ dùng Completed, dự án mới dùng Delivered. Hidden ProductId/title/image không phải dữ liệu có thẩm quyền |
| D03–D04 | `Frontend/Views/Feedback/OverviewFeedback.cshtml` L192–198, L249–316 | Nhóm Positive/Neutral/Negative và tóm tắt uy tín | View phân loại sentiment bằng PositiveRate >=1/==0/<=-1; không dùng trường tỷ lệ tổng hợp để lưu sentiment của từng lượt |
| E01–E02 | `Backend/Services/Implementation/DisputeServiceV2.cs`: GetDisputableOrdersAsync, CreateDisputeAsync, GetDisputeForOrderAsync L65 | Có kiểm tra owner khi tạo, chặn Pending ở service | Chọn FirstOrDefault OrderItem để suy seller, trạng thái Pending khác nhánh cũ, method còn NotImplementedException. Dùng dòng hàng cụ thể, state machine và constraint đồng thời |
| E02 | `Frontend/Views/DisputeV2/Create.cshtml`: saveState L198, loadState L229, finalDescription L402 | Trình tự chọn đơn → lý do → chi tiết → kiểm tra/gửi; khôi phục form | sessionStorage là nơi lưu duy nhất; key chung, API localhost hardcode; ghép reason vào Description; innerHTML từ dữ liệu. Dùng draft server, field reason có cấu trúc, render an toàn |
| E03, E05 | `Frontend/Views/Dispute/BuyerDispute.cshtml`, `SellerDispute.cshtml`, `SupporterDispute.cshtml` | Tách danh sách theo vai trò, trạng thái, hành động cần xử lý | Không sao chép mã trạng thái chuỗi số hoặc coi chữ supporter là permission. Mọi action phải kiểm tra actor tại server |
| E04 | `Backend/Backgrounds/DisputeEscalationService.cs`: ExecuteAsync; `Backend/Repositories/DisputeRepository.cs`: AutoEscalateDisputesAsync L205 | Background service kiểm tra case quá hạn | Vòng lặp 24 giờ, status "1"→"3" khác V2 Pending. Viết lịch deadline rõ ràng, clock kiểm thử được, job idempotent nhiều replica |

Các vấn đề auth ở Hub/controller là điều quan sát trong file và mapping Hub ở Program; chưa chạy exploit hoặc đánh giá mọi lớp bảo vệ bên ngoài. Dự án mới phải có negative tests độc lập, không chỉ dựa vào nhận xét này.

## Tham khảo bổ sung khi cần đúng module

Các đường dẫn sau tính từ repo mới, dùng dưới dạng mã để không biến thành dependency tài liệu bắt buộc khi clone repo ở nơi khác.

| Nguồn | Đã kiểm tra | Task có thể đọc | Giới hạn |
|---|---|---|---|
| `../ProjectCode/Group3_v2/ebay_clone_adminRole/k8s/api-deployment.yaml` | replicas=2 L12; RollingUpdate L16–17; probes L87/L95 | F06–F08 | Chỉ học cơ chế. Không dùng image, secret/config cũ; readiness/liveness đều /health cần tách theo mục đích mới |
| `../ProjectCode/prn232/README.md` | Kiến trúc tách lớp L5, JMeter L6, Redis L26, PostgreSQL L25 | A03, F02, F05 | README mô tả, chưa test source/runtime. Không đổi SQL Server sang PostgreSQL; không thêm React/Cloudinary/Quartz chỉ vì ví dụ có |
| `../ProjectCode/EbayCloneAll/PROJECT_DOCUMENTATION.md` | SignalR/Redis L11, RabbitMQ L12, Vagrant L20 | F02, F06 | README mô tả triển khai lớn; không thêm RabbitMQ/Vagrant/multi-VM cho bài local đã chọn |

Không cần đọc thêm các repo khác trong A02: các module chính và ví dụ scale/rollout đã có điểm vào cụ thể. Khi dependency thực tế phát sinh mới mở rộng khảo sát.

## Quy tắc sử dụng

- Học luồng/sơ đồ, viết implementation mới theo [ADR-001](../decisions/ADR-001-foundation.md).
- UI cũ là gợi ý, bằng chứng eBay thật nằm trong [UI_REFERENCE](UI_REFERENCE.md) và [EBAY_JOURNEYS](../requirements/EBAY_JOURNEYS.md).
- Không gọi code tham khảo “đã chạy tốt” khi chỉ đọc file. Không copy schema đã bị thay đổi làm baseline.
