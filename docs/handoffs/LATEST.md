# Handoff — F08 rolling update/rollback (đang kiểm thử)

08/10/2026. Baseline main 60fc6a5; branch task/F08-rolling-rollback.
Handoff trước: [F07](F07.md). Dependency F05/LOAD, F07/CD có evidence thật.

## Phạm vi / đã viết

- Contract [ROLLING](../architecture/ROLLING.md), [runbook](../runbooks/ROLLING.md).
- Test-RollingUpdate: cluster riêng ebaygroup5-rolling-<hex>, TLS/SQL/port riêng,
  seed LoadSeed; probe HTTP/WSS đồng thời qua nginx HTTPS.
- RollingLoad: HTTP không retry, WSS automatic reconnect/rejoin/SQL cursor catch-up,
  gửi retry UUID cũ; so sánh ACK/delivery và COUNT từng UUID SQL.
- Warmup → set image B → rollout → undo về revision A → rollout;
  xác minh hai replica/pod UID khác và migration journal không đổi.
- Short 10 user/10 cặp/120s (dataset 1/10); Full 50 user/20 cặp/600s (100k/10k/1k).
- CI thêm Short, upload report; Clear-TestArtifacts hỗ trợ prefix rolling.

## Verification hiện tại

- Load project build 0 warning; syntax/docs pass.
- Short đang chạy. Lần đầu script truyền --/dấu phẩy qua PS function sai;
  đã sửa quoting, dọn đúng cluster lỗi và chạy lại.
- Chưa nghiệm thu OPS-06/PERF-01; không ghi task Done từ code.

## Còn lại

- Hoàn thành Short và Full; sửa lỗi thực nếu gate không đạt, không hạ ngưỡng.
- Test-CI local, CI Short thật trên checkpoint runtime; report + evidence + matrix.
- Stage file cụ thể/commit [F08], [skip ci] + dispatch để tránh CI trùng, FF main sau pass.
- Không tự làm G01/Jira live. Cluster demo ebaygroup5 và user launchSettings.json giữ nguyên.
