# Handoff — E01: schema và state machine khiếu nại

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `aaec085`. Runtime `a8406ee` trên `task/E01-dispute-state-machine`, fast-forward vào main sau khi [CI37460995489](https://github.com/Twiash/EbayGroup5/actions/runs/37460995489) pass.
- Checkpoint: `git log --oneline --grep=E01`. Handoff trước: [D06](D06.md).

## Hoàn tất

- **Q-BIZ-006 (người dùng chốt):**
  - Ngày giao dự kiến tính lúc checkout và lưu lại.
  - Return có bước gửi trả.
  - Mọi listing coi như "30-day returns"; seller chỉ được Decline lý do đổi ý.
- **Contract** [DISPUTES](../architecture/DISPUTES.md) gồm nguồn eBay Help 4042/4041, thời gian, điều kiện mở, trạng thái/outcome và bảng chuyển trạng thái.
- **DbUp `0011_Dispute.sql`:**
  - `OrderTable.EstimatedDeliveryUtc` + backfill.
  - `UQ_OrderItem_Id_Order`.
  - Mở rộng bảng gốc `Dispute`; CHECK chỉ áp cho dòng của module và NULL-safe.
  - `UX_Dispute_ActiveLine`, `UX_Dispute_LineKind`; index buyer/seller/due.
  - `TR_Dispute_Guard`: không xóa, không đổi danh tính, request đã đóng bị đóng băng.
  - `DisputeEvent` append-only. Manifest có 12 assets.
- **Application/Disputes:**
  - `DisputeTiming`: ngày làm việc UTC; `Disputes:DemoBusinessDaySeconds` để demo; `EstimatedDeliveryBusinessDays` = 5.
  - `DisputeRules`: hàm thuần `Decide` / `StateAllows` / `AvailableActions`.
  - `IDisputeService` cùng các enum và record.
- **Infrastructure/Disputes/`DisputeService`:**
  - Eligibility, Open (khóa đơn, unique index là chốt chặn cuối), Act / ActAsSystem (khóa dòng request), Find (buyer/seller/admin).
  - Nội bộ `ItemReceivedAsync` được `OrderService` gọi khi buyer Confirm received.
- **B05/B06:**
  - Checkout lưu ngày giao dự kiến.
  - Ship ghi `estimatedArrival`.
  - Receive đóng INR.
  - Trang chi tiết đơn hiện "Estimated delivery".
  - DI đăng ký `DisputeTiming` và `IDisputeService`.

## Verification

- [Evidence](../evidence/E01.md): Test-CI local **225/225 .NET + 14/14 Node**, build 0 warnings.
- Mutation: 3/3 bị test bắt.
- **GitHub [CI37460995489](https://github.com/Twiash/EbayGroup5/actions/runs/37460995489) exact runtime `a8406ee` success**: job build-test 5m05s, 225/225 .NET + 14/14 Node, build 0 warnings. Docs-only `[skip ci]` sau CI.
- Chưa kiểm trên browser vì E01 không có UI khiếu nại.
- Không đụng `launchSettings.json` (untracked).

## Cho task sau

- **E02:**
  - Form ở `Purchases → More actions` (menu D06 trong `Views/Orders/History.cshtml`) và trang chi tiết đơn.
  - Dùng `EligibilityAsync` để hiện/ẩn "I didn't receive it" / "Return this item" kèm lý do.
  - Gọi `OpenAsync`; mẫu UI E1/E1m/E0.
  - Nháp server (G5-N03) cần migration riêng (0012).
  - Khi đang chạy thời gian demo (`DisputeDetail.DemoTiming`) phải ghi rõ trên UI.
- **E03:** trang request details/timeline lấy từ `FindAsync`; nút theo `Actions`; nhãn trạng thái kiểu eBay; mẫu D2/H1.
- **E04:** job gọi `ActAsSystemAsync(Escalate | ExpireReturn)`; dùng index `IX_Dispute_Due`; lệnh đã idempotent (trả Conflict khi không còn áp dụng); event `AutoNotice` đã có trong CHECK.
- **E05:** `ActAsync(Decide, ForBuyer, note)` ghi `resolution`; cần thêm AdminNetwork ở tầng web.
- Ngoài phạm vi: appeal 30 ngày; chọn số lượng khi return; hoàn tiền thật.

## Tiếp theo / Jira

- Chờ người dùng chọn task.
- E01 thuộc KAN-21 (cùng E02) → **hoàn tất local một phần**, còn chờ E02.
- Chưa sync live; không tạo worklog/review/issue.
