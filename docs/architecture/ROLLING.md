# F08 — Rolling update và rollback dưới tải

08/10/2026, baseline main `60fc6a5`. Dependency F05/LOAD và F07/CD đã có CI/CD thật.
Phạm vi: kiểm chứng thay image ứng dụng và rollback tương thích 14 migration hiện có;
không rollback SQL, không đổi nghiệp vụ, không reset dữ liệu/cluster demo.

## Bài đo

- `Test-RollingUpdate.ps1 -Profile Short|Full` tạo cluster kind riêng
  `ebaygroup5-rolling-<12 hex>`, TLS/mật khẩu/port riêng; dùng Deploy-K8s và LoadSeed.
- Full: 100.000 tin/10.000 review/1.000 dispute, 50 HTTP user +20 cặp WSS ×600 giây.
  Short CI: dataset 1/10, 10 HTTP user +10 cặp WSS ×120 giây.
  F05 đã đo JMeter Full/Stress; F08 dùng probe HTTP .NET không retry để ghi liên tục
  từng lỗi mạng/status, đồng thời SignalR client theo contract SQL/cursor/idempotency C05.
- Hai image immutable A/B build từ cùng source nghiệp vụ, B khác OCI label/image ID;
  thay container thật, ghi tag/image ID/pod UID/revision để chứng minh rollout.
  Đây là kiểm mechanics deployment/rollback, không mô phỏng migration không tương thích.
- Sau khi đủ tải/WS joined: warmup → `kubectl set image` B + rollout status →
  `kubectl rollout undo --to-revision=<A>` + rollout status → chạy tiếp tới đủ thời lượng.
- Giữ 2 replica, maxUnavailable=0/maxSurge=1, readiness SQL/Redis, preStop 10s,
  terminationGracePeriod 30s và nginx DNS/connect timeout của F06.
- Chỉ tăng auth permit trong deployment test (nhiều tài khoản đăng nhập từ cùng IP);
  chat limits vẫn giữ nguyên. Không đổi giới hạn của cluster người dùng.

## Tin nhắn và gate

- WSS xác minh CA/hostname; cookie thật, principal thật. Reconnect tự động,
  rejoin + SQL/REST catch-up theo cursor, retry cùng UUID khi ACK bị mất.
- Tách transient retries, reconnect/recovery duration và latency; không giấu lỗi cuối.
  Dedupe live/history theo UUID; đối chiếu SQL từng UUID đã ACK: đúng một dòng.
- HTTP: 0 5xx, 0 lỗi mạng/status ngoài dự kiến; mỗi phase có request thực.
- Chat: thực sự có reconnect trong cả update/rollback; tất cả lần recovery ≤10 giây;
  0 tin ACK bị thiếu, 0 UUID có nhiều dòng SQL, 0 lỗi gửi cuối.
- Full: p95 route chính và p95 realtime <1 giây; Short báo latency, vẫn gate tính đúng
  và recovery ≤10 giây. Không hạ ngưỡng nếu máy CI dao động.
- Report ghi máy/source SHA/dataset/profile/thời lượng, phase timeline,
  HTTP status/latency, ACK/delivery/SQL/recovery, revision/pod UID và giới hạn.

## Vòng đời và nghiệm thu

- Pass: xoá đúng cluster test và image tag test; giữ report tổng hợp (không secrets/tin chat).
  Fail: giữ cluster/log để chẩn đoán; mọi target cleanup phải đúng prefix và path đã kiểm.
- CI chạy Short sau Test-K8s, upload chỉ report; local chạy Full.
- OPS-06/PERF-01 Done khi có kết quả thật đủ gate; task tài liệu không tự tạo acceptance.
  F07 đã chứng minh CD vào cluster local; F08 kiểm transition trên cluster cô lập.
- Bảo đảm một node kind, không chứng minh multi-node/SQL failover hoặc E2EE.

## Q-OPS-001 — lỗi mạng chập chờn khi thay pod

27/10/2026, task [OPS-001](../tasks/OPS-001.md). Gate và timeout probe không đổi; không thêm retry phía client.

- Triệu chứng CI: 1–4 `status 0` chỉ ở `api/v1/chat/conversations` trong update/rollback, 0 5xx, không mất tin.
  Probe đếm `status 0` cho cả lỗi kết nối lẫn timeout 5 s của HttpClient; report nay có `networkErrorSamples` để phân biệt.
- Đã loại trừ bằng nginx log có upstream connect/response time (kind local): nginx bỏ pod cũ ≤1 s sau khi pod bị xoá,
  trước khi preStop 10 s hết; không request nào retry hay connect tới pod đã dừng; CoreDNS tắt cache `cluster.local`;
  WSS chỉ đứt lúc SIGTERM. Drain/preStop/Kestrel shutdown/`proxy_next_upstream` không phải nguyên nhân.
- Nguyên nhân (suy luận từ đo local; artifact CI cũ không có loại lỗi/log nginx): inbox chạy 1 + 1 + 3×20 ≈ 62 round trip SQL tuần tự trong một transaction (unread, tên peer, preview
  cho từng hội thoại). Runner CI 2 vCPU đã bão hoà (kind, SQL, 2–3 pod, client tải): p95 inbox lúc warmup ~0,7 s
  (local không giới hạn CPU: 0,04 s). Khi thay pod, pod mới nguội (JIT/EF) và WSS reconnect tạo burst CPU; mỗi round trip
  chờ CPU nên đuôi inbox vượt 5 s. Route khác chỉ vài round trip nên không vượt.
- Sửa: một câu SQL cho cả trang inbox (subquery unread/peer/preview theo dòng), giữ transaction, HOLDLOCK actor và
  quy tắc DTO; test so DTO inbox với summary. Đã thử làm ấm pool SQL trước khi listen: không cải thiện rõ, bỏ.
- Local, node kind giới hạn 2 CPU (CFS): p95 inbox update/rollback/ổn định 495–500/691–808/203–277 ms → 95/315/37 ms
  (run A), 187/99/9 ms (run B); 0 lỗi mạng/5xx. Chưa tái hiện được lỗi gốc ở local (24 CPU), nên bằng chứng cuối là GitHub CI.
- Tần suất tăng từ 8.0.33 chưa quy được nguyên nhân (SqlClient 5.1.9 không đổi, chỉ 2 lần chạy).

Nguồn: [Kubernetes Deployment/rollback](https://kubernetes.io/docs/concepts/workloads/controllers/deployment/),
[pod termination](https://kubernetes.io/docs/concepts/workloads/pods/pod-lifecycle/#pod-termination-flow),
[ngưỡng dự án](../PLAN.md#91-ngưỡng-nghiệm-thu-của-dự-án).
