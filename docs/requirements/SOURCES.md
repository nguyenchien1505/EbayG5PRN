# Nguồn và cách đối chiếu

## Nguồn chuẩn

| ID | Nguồn | Vai trò |
|---|---|---|
| SRC-REQ | Project Requiments/Ebay Clone Requiments.docx | Yêu cầu giảng viên; phần Nhóm 5, Các yêu cầu khác và Lưu ý |
| SRC-SQL | Project Requiments/clone_ebay_sqlserver_schema.sql | Schema bất biến, 17 bảng |
| SRC-USER | Kế hoạch được duyệt và yêu cầu bổ sung ngày 02/10/2026 | Phạm vi, stack, cách tổ chức task, hỗ trợ thông tin chưa rõ |
| SRC-REF | ProjectCode, đặc biệt PRN232_Project_Group_5 | Code tham khảo; không phải chuẩn đúng |

[Bản trích Word](sources/requirements-extracted.md) giữ nội dung đoạn văn, gồm cả năm nhóm để tra cứu; không nạp toàn bộ vào mỗi phiên. Khoảng trắng thiếu có sẵn trong nguồn. Đây là trích xuất nội dung, không phải kiểm tra bố cục Word.

[Bản SQL nguyên byte](sources/clone_ebay_sqlserver_schema.sql) có SHA256:

```text
D56EC9C0FFCEA721E2F7BA3453249794F03AFEB1F17CC4A2F9EBD0ADA38E0754
```

Bản trong repo và bản nguồn ngoài repo đã khớp checksum lúc A00–A01. Đây là bản nguồn tham chiếu; migration bootstrap/runtime sẽ làm ở A05. Không chỉnh trực tiếp file này để tạo migration.

17 bảng: User, Address, Category, Product, OrderTable, OrderItem, Payment, ShippingInfo, ReturnRequest, Bid, Review, Message, Coupon, Inventory, Feedback, Dispute, Store.

## Lưu ý cách diễn giải

- Đề ghi “Các yêu cầu khác (25% số điểm)” và “làm đủ được 80% điểm”. Giữ cả hai ghi chú của đề, không tự cộng thành công thức chấm điểm. Hỏi giảng viên nếu cần giải thích tỷ trọng chính xác.
- Nginx/load balance, rate limit, CI/CD, Jira, kiểm thử tải/an ninh và K8s được xem là bắt buộc trong phạm vi đã duyệt.
- Yêu cầu dưới các nhóm 1–4 không tự động trở thành full scope. Chỉ triển khai phần phụ thuộc đã chọn; giữ nguyên các bảng còn lại.
- Các ngưỡng tải/p95 của dự án là mục tiêu nội bộ, không phải trích nguyên văn đề.
- Tài liệu eBay chính thức và UI quan sát được phục vụ A02; phân biệt hành vi thật, quyết định giản lược và điều chưa xác minh.

## Tài liệu bên ngoài đã tham khảo khi lập kế hoạch

- [.NET support](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [SignalR scale](https://learn.microsoft.com/en-us/aspnet/core/signalr/scale)
- [eBay seller ratings](https://www.ebay.com/help/buying/resolving-issues-sellers/seller-ratings?id=4023)
- [eBay step in](https://www.ebay.com/help/selling/managing-returns-refunds/ask-ebay-step-help-sellers?id=4702)
- [Kubernetes Deployment](https://kubernetes.io/docs/concepts/workloads/controllers/deployment/)

Ngày tham khảo: 02/10/2026. Khi sử dụng thông tin dễ thay đổi cho implementation, xác minh lại tài liệu chính thức của đúng phiên bản.
