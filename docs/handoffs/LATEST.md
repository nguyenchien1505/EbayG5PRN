# Handoff — F05: JMeter và đo realtime

07/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `1fe0a94`. Branch `task/F05-load-realtime`; checkpoint `git log --oneline --grep=F05`. Runtime `1be91ce`, fast-forward vào main sau khi [CI37620564985](https://github.com/Twiash/EbayGroup5/actions/runs/37620564985) pass (Load Short trên runner: 0 % lỗi, chat 583/583, 9/10 cặp khác replica).
- Handoff trước: [F04](F04.md) (gồm bổ sung Q-SEC-001: logout thu hồi phiên qua Redis).
- Người dùng chốt 07/10/2026: JMeter bản chính thức Apache (tải + SHA-512), đo đầy đủ ở local, CI chạy bản ngắn.

## Hoàn tất

- Contract: [LOAD](../architecture/LOAD.md). **Không có migration.**
- `scripts/Install-JMeter.ps1`: JMeter 5.6.3 từ dlcdn (dự phòng archive), kiểm SHA-512, giải nén vào `artifacts/tools/` (ignore), chmod trên Linux.
- `tests/load/ebaygroup5.jmx`: mỗi thread đăng nhập một lần (CSV trong Once Only Controller), lặp catalog/product/reviews/seller feedback/purchases/disputes/chat inbox/history (2 trang theo cursor)/send; think time 0,5–1,5 s.
- `tests/EbayGroup5.Load`: `seed` (đơn/review/dispute qua service thật, 100.000 tin bằng SqlBulkCopy kiểm constraint; ghi tuần tự mặc định, `EBAY_LOAD_WRITERS` để tái hiện deadlock), `chat` (N cặp WSS, đo gửi → nhận cùng đồng hồ).
- `scripts/Test-AppStack.ps1 -Load Short|Full|Stress` → `scripts/Invoke-LoadTest.ps1` (stack cô lập + `compose.load.yaml` nâng giới hạn auth chỉ cho stack tải; seed, JMeter + chat song song, p95 theo nhãn, replica từ log, deadlock từ `system_health`, gate, báo cáo `artifacts/load/<utc>-<profile>/`).
- App: `ChatHubFilter` log `Chat connection opened user=<id>` (chỉ id).
- CI: bước `Test-AppStack.ps1 -Load Short`, cache JMeter, artifact `load-report`; timeout job 50 phút.

## Verification

- [Evidence](../evidence/F05.md). Full local: 50 user × 600 s, 28.817 mẫu, **0 % lỗi, p95 ≤ 39 ms** (login 190 ms); lịch sử chat trên hội thoại 20.000 tin p95 7 ms; **20 cặp chat 11.753/11.753, p95 20,4 ms**; WS trên 2 replica, **13/20 cặp khác replica**.
- Stress: 50 → 1600 user; tới 800 user (~680 req/s) p95 < 100 ms; 1600 user (~1.075 req/s) p95 inbox 1,3 s, 429 do giới hạn theo user, không 5xx; realtime p95 < 25 ms mọi bậc.
- **Test-CI local: 262/262 .NET + 20/20 Node**, build 0 warning.

## Requirement

- **OPS-01, OPS-05, G5-N02, G5-N04: Done.**
- PERF-01 Pending (phần rolling update/rollback thuộc F08; phần tải/latency đạt).

## Câu hỏi / rủi ro

- **Q-PERF-001 (Open):** ship/receive đồng thời deadlock (5.141 deadlock khi seed 8 luồng: Dispute quét theo orderId dưới UPDLOCK/HOLDLOCK, Payment/ShippingInfo khóa phạm vi). Đề xuất task sửa: index `Dispute(orderId)` (DbUp 0014) + đổi khóa + test đồng thời. Luồng ghi đơn chưa được kiểm tải.
- JMeter chạy cùng máy với stack; giới hạn đo được gồm tải của máy sinh tải.

## Cho task sau

- **F06:** K8s local + migration Job; F08 dùng lại `Invoke-LoadTest.ps1`/`ChatLoad` để đo khi rolling update (chat phục hồi ≤ 10 s, không 5xx).
- Chạy `-Load Full` không song song với Test-CI (tranh RAM SQL).

## Tiếp theo / Jira

- F03+F04+F05 → **KAN-27 / TV5 Hải Lê: hoàn tất local một phần** (còn F08). Chưa sync live; không tạo worklog/review/issue.
