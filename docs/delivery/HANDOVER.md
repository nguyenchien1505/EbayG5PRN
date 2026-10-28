# Bàn giao và chuẩn bị trình bày

Đầu mối đọc: [REPORT](REPORT.md) →[SETUP](SETUP.md) →[DEMO](DEMO.md),
[MATRIX](../requirements/MATRIX.md) và [evidence/INDEX](../evidence/INDEX.md).
Repo là sản phẩm bàn giao; `.env`/secrets/log/build/artifacts không thuộc gói source.

## Source cần đọc và câu hỏi mỗi người phải trả lời

| Người | File/module | Câu hỏi luyện tập |
|---|---|---|
| Dat | Database/MigrationRunner, DemoSeedRunner; Persistence/EbayDbContext; CheckoutService/OrderService | Vì sao DbUp thay EF migrations? Transaction tránh oversell ra sao? Owner/Delivered được lấy từ đâu? |
| Huy | Infrastructure/Chat/ChatService; Web/Chat/ChatHub; JS chat-state/chat-recovery | SQL commit khi nào? Mất ACK retry UUID xử lý ra sao? Redis mất thì dữ liệu nào vẫn còn? |
| Cuong | Reviews/ReviewService; Reputation/FeedbackService; ModerationService | Rating khác sentiment thế nào? P−N/%12tháng, Hide/RemoveComment đổi aggregate gì? Vì sao audit append-only? |
| Nguyên | Application/Disputes/DisputeRules, DisputeTiming; DisputeService/AutomationService/Worker | Owner/state/action kiểm ở đâu? Clock demo đổi gì? Nhiều worker tránh nhân notice thế nào? Admin ghi audit gì? |
| Hải | Dockerfile,compose,k8s,Deploy-K8s,ci/cd,Test-RollingUpdate | Key ring/rate limit chia sẻ ra sao? Artifact identity classic/containerd? Rollback ứng dụng khác rollback DB? |

Paths nằm dưới `src/` trừ deploy/scripts/tests. [Source map](../architecture/SOURCE_MAP.md)
có một số ghi chú checkpoint lịch sử; module hiện tại tra contracts hoặc các file trong bảng trên.
Tài liệu này là yêu cầu luyện tập, **không phải chứng nhận các thành viên đã hiểu/trình bày**.

## Checklist cá nhân trước buổi demo

- Tự đọc contract và tối thiểu một test liên quan phần mình.
- Tự chạy setup hoặc cùng TV5 từ clone sạch, login đúng role và tập luồng của mình.
- Giải thích ít nhất một negative case về quyền/duplicate/time/state.
- Đọc khác biệt với eBay và giới hạn demo; không gọi refund mô phỏng là chuyển tiền thật.
- Ghi các điểm chưa hiểu để nhóm giải đáp; chưa có review/worklog thì không bịa trên Jira.

## Trạng thái bàn giao Jira (hoãn theo user)

OPS-04 **Pending**. Không có thao tác connector/API ghi Jira trong G02.
Bảng [JIRA_MAPPING](../team/JIRA_MAPPING.md) giữ trạng thái live đã ghi lần trước,
không đổi thành Done chỉ vì code local hoàn tất. Khi user yêu cầu sync:

1. Đọc account/key/dependency và các issue hiện có KAN-4–28, xác minh trạng thái/field thật.
2. Ghép evidence task nhỏ vào issue module (không tạo ticket trùng).
3. KAN-11–24 và KAN-26–28 cần rà evidence/acceptance theo mapping; Epic KAN-4–8
   chỉ đóng khi task con đã đạt và người phụ trách xác nhận theo workflow thật.
4. KAN-12/KAN-20 có phần nghiệm thu con người: xác nhận luyện tập/giải thích trước khi
   ghi hoạt động này hoàn thành; test Codex không thay chứng nhận cá nhân.
5. Cập nhật trạng thái đúng transitions; không thêm worklog, ngày review/tên reviewer giả.
6. Ghi URL/time/kết quả đồng bộ rồi mới nghiệm thu OPS-04.

## Secrets, dữ liệu và vận hành

- Kind credentials/TLS ở `secrets/`, Compose ở `.env*`; chỉ trao đổi bằng kênh riêng.
- Không in full `docker inspect`, rendered Compose config/Secret YAML lên màn hình demo.
- Không copy credentials sang repo clone; mỗi clone/stack test dùng credential riêng.
- Seed chạy lại bảo toàn lịch sử; ngày demo dùng **đơn mới**, không sửa seed/schema nguồn.
- Dừng kind dùng `-Stop`, giữ volume; không `-Delete`/prune/down-v trong xử lý lỗi thường.
- Cập nhật schema chỉ thêm version DbUp mới, không sửa script đã phát hành.
- Trước nâng hạ tầng: backup database/volume theo runbook; TLS local không dùng production.

## Mốc và giới hạn còn lại

Ngày nộp/demo **chưa xác định** (Q-TEAM-002), không tự ấn định lịch.
Q-UI-010 unread badge Deferred. SQL/Redis single-node local, refund/shipping mô phỏng,
không outbox/E2EE; các khác biệt đầy đủ ở [UI_ACCEPTANCE](../architecture/UI_ACCEPTANCE.md) (bộ ảnh H07 28/10/2026).
UI đợt H theo ADR-003; ảnh mẫu eBay ở [references](../references/ui/INDEX.md). Chụp lại bằng `Test-AppStack.ps1 -Screens <dir>` (tự bật demo clock 30 s/ngày, tạo đơn mới).
Các test/CI được tổng hợp trong evidence; rehearsal live của nhóm và Jira sync là việc sau.
