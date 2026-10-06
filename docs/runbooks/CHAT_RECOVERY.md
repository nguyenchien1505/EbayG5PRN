# Chat recovery và truy vết — C05

Contract [CHAT](../architecture/CHAT.md); bằng chứng [C05](../evidence/C05.md). SQL là nguồn bền vững, Redis/event best effort, không outbox.

## Người dùng

- Giữ trang mở đến khi bubble hiện Sent/Read. Draft và pending chỉ memory; reload không giữ tin chưa được xác nhận. Tin đã commit luôn đọc lại từ SQL khi session còn quyền.
- Mất socket: Reconnecting, tự start/rejoin và after-cursor catch-up. Foreground reconciliation15s cũng sửa lost-event/read summary dù Connected. Offline/hidden/panel đóng tạm dừng; online/focus/visible tiếp tục. Backoff jitter1–30s; Refresh đánh thức sớm. Bounded1000 messages/batch, continuation250ms; chưa tải hết không mark read.
- Transport/5xx/429: exact UUID/content/context tự retry sau reconciliation, SQL replay không tạo tin mới.400/403/404/409: Retry thủ công sau khi xử lý nguyên nhân.401: xóa memory/dừng recovery và Sign in; không tự gửi pending sau login mới.
- REST có thể gửi/tải bù khi live transport lỗi. Không coi trạng thái Connected hay publish enqueued là xác nhận lưu hoặc bảo đảm người kia đã nhận/đọc.

## Operator — metadata, không nội dung

Receipt log chứa `conversation/message/sequence/utc/trace/replayed/requestTrace`; publish log cùng record metadata và `result=enqueued` hoặc `catch-up-required`. Read publish có message/recordTrace null. History-after log có conversation/after/newest/watermark/count/more/UTC/requestTrace, không nội dung.

Lấy messageId/recordTrace từ receipt hoặc log để tra SQL qua kết nối quản trị đã cấu hình; chỉ chọn metadata:

```sql
DECLARE @MessageId int = 123; -- ID thực từ receipt/log, không đoán từ timestamp
SELECT id, ConversationId, Sequence, SenderId, ReceiverId, SentAtUtc, TraceId
FROM dbo.Message WHERE id = @MessageId AND ConversationId IS NOT NULL;
```

Record trace/UTC không thay khi replay; requestTrace mới liên kết attempt khác với cùng receipt. So sánh conversation+sequence/message ID, không thứ tự nhận event hoặc Message identity gaps. Không bật EF sensitive data logging, HTTP body logging, SignalR Debug/Trace, không xuất cookie/antiforgery/connection string/raw exception. Không tìm log bằng nội dung chat. `enqueued` chỉ là enqueue attempt, không receiver delivery ack.

Không reset/drop DB/volume khi event mất: kiểm tra session/owned summary, REST after cursor và SQL receipt; reconnect tạo group mới. Shared keys, HTTPS/WSS, readiness, log collection/retention và dataset/latency deployment thuộc F01–F05; runbook này không xác nhận các phần đó.

## Verification

`. ./scripts/Use-LocalDotnet.ps1; ./scripts/Test-CI.ps1` trên Node20+/Docker: locked build, source/migration/vendor integrity, Node recovery tests và isolated SQL/HTTP/real WebSocket/Redis tests. Browser dùng cua_repl và fake accounts/test DB riêng; fault helper chỉ ignored artifacts, không production route/hook. Không thử role/outage/fault trên DB hoặc tài khoản người dùng.
