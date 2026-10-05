# Kế hoạch đồng bộ Jira KAN

06/10/2026. Đích đã xác minh: PRN_GROUP_5 (KAN), https://dattv.atlassian.net, board 2. Đã tạo 5 Epic +20 task/story theo user approval; xem JIRA_MAPPING.md. Ba issue có sẵn KAN-1–3 có tên chung, giữ nguyên.

Nguồn backlog: PLAN §8, tasks/INDEX, evidence A00–A07/B01–B04; checkpoint code 13e5ec5. Tạo 5 Epic trước, rồi 20 task/story con, cùng tiêu chí nghiệm thu và liên kết source. Không tạo worklog, comment dưới danh nghĩa thành viên hoặc ngày hoàn thành hồi tố.

## Phân công đã được người dùng chốt

| Vai trò | Tài khoản Jira đã xác minh assignable | Epic |
|---|---|---|
| TV1 | Tran Van Dat | Foundation and Commerce |
| TV2 | Đoàn Công Huy | Chat and Messaging |
| TV3 | Pham Van Cuong | Reviews and Reputation |
| TV4 | Nguyên Nguyễn | Disputes and Resolution |
| TV5 | Hải Lê | DevOps and Quality |

Người dùng xác nhận “Tạo và gán theo đề xuất”. Đây là phân công trách nhiệm. Trạng thái Done của phần code có commit/test thực tế không gán tác giả thực hiện cho người nhận task.

## Issue con đã tạo

| Vai trò | Loại | Tên issue | Task coding | Trạng thái |
|---|---|---|---|---|
| TV1 | Task | Chốt requirement và bảo toàn database nguồn | A01, A05, A06 | Done, kèm evidence |
| TV1 | Story | Triển khai tài khoản và catalog có phân quyền | B01–B04 | Done, kèm CI 32 tests |
| TV1 | Story | Triển khai Buy Now, COD và lịch sử giao nhận | B05–B06 | To Do |
| TV1 | Task | Nghiệm thu và bàn giao luồng commerce | kiểm tra B01–B06 | To Do |
| TV2 | Task | Chốt schema và contract chat | C01 | To Do |
| TV2 | Story | Lưu và đọc hội thoại theo quyền participant | C02 | To Do |
| TV2 | Story | Triển khai SignalR, Redis và giao diện chat | C03–C04 | To Do |
| TV2 | Task | Kiểm thử reconnect, tải bù và bàn giao chat | C05 | To Do |
| TV3 | Story | Triển khai product review theo đơn đã nhận | D01–D02 | To Do |
| TV3 | Story | Triển khai seller feedback, uy tín và reply | D03–D04 | To Do |
| TV3 | Story | Kiểm duyệt và audit review/feedback | D05 | To Do |
| TV3 | Task | Nghiệm thu công thức và bàn giao reputation | kiểm tra D01–D05 | To Do |
| TV4 | Story | Chốt state machine và lưu nháp khiếu nại | E01–E02 | To Do |
| TV4 | Story | Triển khai trao đổi case buyer/seller | E03 | To Do |
| TV4 | Task | Xử lý phản hồi tự động và chuyển quá hạn | E04 | To Do |
| TV4 | Story | Admin phân xử và nghiệm thu dispute | E05 | To Do |
| TV5 | Task | Thiết lập scaffold, hạ tầng local và CI | A00, A02–A04, A07 | Done, kèm evidence |
| TV5 | Task | Triển khai HTTPS, nhiều instance và CD Kubernetes | F01–F02, F06–F07 | To Do |
| TV5 | Task | Kiểm thử hệ thống, tải và rolling update | F03–F05, F08 | To Do |
| TV5 | Task | Rà requirement, báo cáo và bàn giao nhóm | G01–G02 | To Do |

Epic TV1/TV5 In Progress do module đã hoàn thành một phần; TV2/TV3/TV4 To Do. Work chưa triển khai giữ To Do. Mỗi issue có người chịu trách nhiệm, acceptance và test dự kiến; deadline/giờ làm chưa được cung cấp nên để trống.

Dependency chính: commerce B05–B06 → review và dispute; schema chat → service → realtime/UI → recovery; module chat/reputation/dispute → deployment → hệ thống/tải → bàn giao. Jira link dùng workflow/link type đã xác minh; không suy ra mọi task Planned đang thực thi.

Đã tạo và đọc lại25 issue keys/parent/assignee/status; mapping và bộ lọc JQL theo năm người tại [JIRA_MAPPING](JIRA_MAPPING.md). Không mời/thêm tài khoản mới và không đổi ba issue có sẵn.
