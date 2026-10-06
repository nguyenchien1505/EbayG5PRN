# Handoff — E04: phản hồi tự động và chuyển quá hạn

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `fb9521f`. Commit docs Q-UI-009 `2728473`, sau đó là runtime trên `task/E04-dispute-automation`. Checkpoint `git log --oneline --grep=E04`. GitHub CI: xem mục Verification.
- Handoff trước: [E03](E03.md).
- Q-UI-009 Answered: giữ nhãn clone, tạm giữ luật offer của E01, đồng ý tên "Returns and requests".

## Hoàn tất

- Contract: [DISPUTE_AUTOMATION](../architecture/DISPUTE_AUTOMATION.md).
- **DbUp `0013_DisputeNotice.sql`:**
  - Thêm cột `DisputeEvent.NoticeKey` (varchar(20) BIN2).
  - CHECK: có khóa ⇔ `AutoNotice` + System + có message + khóa thuộc 6 giá trị.
  - Thêm `UX_DisputeEvent_Notice (DisputeId, NoticeKey)` có lọc.
  - Manifest có 14 assets.
- **Application** (`DisputeAutomation.cs`):
  - `DisputeAutomationOptions` (`Disputes:Automation`: Enabled / IntervalSeconds 60 / BatchSize 100).
  - `IDisputeAutomation`: `RunOnceAsync` và `NotifyAsync`.
  - `DisputeNotices.Expected`: hàm thuần, chỉ trả thông báo của trạng thái hiện tại; dùng chung `Date` với Web.
- **Infrastructure** (`DisputeAutomationService`):
  - Chuyển quá hạn bằng `ActAsSystemAsync` (seller im lặng → Escalate; return không gửi → ExpireReturn).
  - Tìm request còn thiếu thông báo; query chỉ so với hằng số, vì collation của `status` và `NoticeKey` khác nhau.
  - `NotifyAsync`: khóa dòng UPDLOCK/HOLDLOCK, ghi các khóa còn thiếu, unique violation coi như replica khác đã ghi.
- **Web:**
  - `Disputes/DisputeAutomationWorker`: BackgroundService chạy một lần lúc khởi động, sau đó theo chu kỳ; chỉ log số lượng.
  - `DisputesController` gọi `NotifyAsync` sau khi Open/Act thành công; lỗi chỉ log cảnh báo.
  - Trang chi tiết bỏ câu "eBay is reviewing" bị lặp.
- **Tests:**
  - `DisputeAutomationTests`: 5 test (pure, service, song song/batch, constraint, HTTP + worker thật).
  - `AccountHttpTests.Factory(…, automation: false)` mặc định tắt worker; `ChatSocketHost` cũng tắt.
  - Số migration trong `DatabaseTests`/`CheckoutTests` tăng lên 13/10.

## Verification

- [Evidence](../evidence/E04.md):
  - Test tập trung 63/63.
  - 4 replica song song không nhân đôi.
  - Mutation 2/2 bị bắt.
  - Browser có worker thật.
- **Test-CI local pass: 239/239 .NET + 20/20 Node**, build 0 warnings, container CI tự xóa.
- GitHub CI cho runtime E04: ghi bằng commit docs-only `[skip ci]` sau khi pass.
- Fixture E04 nằm trong `artifacts/d02/preview/Program.cs` (bị ignore). DB preview đã lên 0013.
- **DB `EbayGroup5` của người dùng phải được áp 0013 trước khi chạy app**, vì EF đã map cột `NoticeKey`. Lệnh: `pwsh -NoProfile -File scripts/Initialize-Database.ps1`.
- Không đụng `launchSettings.json`.

## Cho task sau (E05)

- Hàng đợi admin: request `Escalated` (`EscalatedAtUtc`, `DueAtUtc` = hạn 48 giờ).
- Admin đọc qua `FindAsync` (Viewer = Admin); route riêng dưới AdminNetwork, vì `/Disputes/*` trả 403 cho Admin.
- `Decide` (ForBuyer, note) và Message của admin đi qua `ActAsync`. Audit có sẵn trong timeline: actor/thời điểm/kết quả, `resolution`.
- Sau `Decide`: request Closed, nên không có thông báo tự động mới. Nếu muốn thông báo kết quả thì thêm khóa bằng migration mới (0014), không sửa 0013 sau khi phát hành.
- SUP-05 và G5-08 Done sau E05.

## Tiếp theo / Jira

- E04 → **KAN-23 / TV4 Nguyên Nguyễn: hoàn tất local**. Chưa sync live; không tạo worklog/review/issue.
