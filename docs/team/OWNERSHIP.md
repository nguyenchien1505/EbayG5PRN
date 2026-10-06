# Phân công và Jira

Project đã xác minh lúc lập kế hoạch: PRN_GROUP_5, key KAN, https://dattv.atlassian.net. Chưa tạo/sửa issue trong checkpoint A00–A01.

| Vai trò | Epic dự kiến | Task liên quan | Trách nhiệm nghiệm thu |
|---|---|---|---|
| TV1 | Foundation and Commerce | A01, A05–A06, B01–B06 | Database, tài khoản, mua/nhận hàng |
| TV2 | Chat and Messaging | C01–C05 | Realtime, phục hồi, nhiều instance |
| TV3 | Reviews and Reputation | D01–D05 | Điều kiện review, công thức, kiểm duyệt |
| TV4 | Disputes and Resolution | E01–E05 | Draft, quyền, escalation, admin |
| TV5 | DevOps and Quality | A00, A02–A04, A07, F01–G02 | Hạ tầng, kiểm thử, tổng hợp báo cáo |

Mỗi Epic có task phân tích/contract, triển khai, kiểm thử và bàn giao. Các task Jira ánh xạ nhiều task coding nhỏ; không tự gán account theo tên gần giống.

Khi đồng bộ Jira: đọc skill backlog phù hợp, kiểm tra issue có sẵn, loại issue/trạng thái/field thực tế, sau đó tạo hoặc cập nhật theo phạm vi người dùng đã duyệt. Lưu key thật và liên kết ở file mapping. Assignee Unassigned tới khi có Q-TEAM-001.

Done cần bằng chứng; không bịa giờ làm, ngày, review hay tên tác giả. Codex hỗ trợ coding; thành viên phụ trách kiểm tra, hiểu module và trình bày. Mỗi người chuẩn bị kịch bản và giải thích phần mình.

Cập nhật 06/10/2026: user chốt tài khoản thật TV1 Tran Van Dat, TV2 Đoàn Công Huy, TV3 Pham Van Cuong, TV4 Nguyên Nguyễn, TV5 Hải Lê. Đã tạo/gán 5 Epic +20 ticket KAN-4–28 và26 Blocks links; [mapping](JIRA_MAPPING.md), [phạm vi](JIRA_BACKLOG_PLAN.md). Các đoạn Unassigned/chưa tạo ở trên là ghi nhận checkpoint trước.

Quy ước user 06/10/2026 sau B05: gom công việc và evidence theo một đợt task để phân chia cho thành viên rồi mới đồng bộ Jira; không tự cập nhật sau mỗi task nhỏ. Số task/mốc mỗi đợt chưa ấn định, lưu các task hoàn tất chưa sync trong handoff/mapping. B06 chỉ triển khai và ghi evidence local; chưa ghi Jira hay worklog. Giữ account/issue mapping đã duyệt, không tạo ticket trùng.
