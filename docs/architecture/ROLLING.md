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

Nguồn: [Kubernetes Deployment/rollback](https://kubernetes.io/docs/concepts/workloads/controllers/deployment/),
[pod termination](https://kubernetes.io/docs/concepts/workloads/pods/pod-lifecycle/#pod-termination-flow),
[ngưỡng dự án](../PLAN.md#91-ngưỡng-nghiệm-thu-của-dự-án).
