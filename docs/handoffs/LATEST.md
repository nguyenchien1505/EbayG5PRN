# Handoff — F03: test tích hợp xuyên suốt và hành trình trình duyệt

07/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `15e72bc`. Runtime trên `task/F03-system-integration-journeys`; checkpoint `git log --oneline --grep=F03`. GitHub CI: xem mục Verification.
- Handoff trước: [F02](F02.md).
- Người dùng chốt Playwright .NET + Chrome có sẵn ngày 07/10/2026.

## Hoàn tất

- Contract: [JOURNEYS](../architecture/JOURNEYS.md). **Không có migration.**
- **`tests/EbayGroup5.Journeys`** (Playwright 1.63, `channel: chrome`, chỉ tin SPKI của chứng chỉ tạm): J1–J6.
  - Mua hàng; chat realtime hai chiều qua WSS; ship/receive + feedback; return đến refund; report + admin ẩn; quyền truy cập.
  - Khi fail thì chụp ảnh vào `artifacts/stack/<hex>/journeys/`.
- **`scripts/Test-AppStack.ps1`:**
  - Seed demo + credential ngẫu nhiên (qua biến môi trường).
  - Chạy Journeys **trước** smoke, vì smoke cố ý dùng hết lượt `auth`.
  - Kiểm correlation SQL ↔ log Nginx ↔ log web; dòng INFO về số replica giữ WebSocket chat.
  - `-KeepRunning` để lặp nhanh.
- **`AccessMatrixTests`:** 16 route × 6 vai trò/đối tượng, gom mọi ô lệch; kiểm thêm trang 403 và trang review không có form.
- **Nginx:**
  - `traceparent` lấy từ `$request_id`, bỏ `tracestate`.
  - **`Host $http_host`**: sửa lỗi redirect mất port.
- **Compose:** web log dạng JSON (`Logging__Console__FormatterName=json`).
- **App:** `Views/Home/Forbidden.cshtml`; `HomeController.Status` trả 403 kèm trang lỗi.
- **Smoke:** thêm "Sign-in redirect keeps the public origin and port".

## Verification

- [Evidence](../evidence/F03.md):
  - Stack thật: Journeys 7/7, smoke 14/14, 2 upstream, correlation pass.
  - Ma trận quyền pass; mutation trên ma trận bị bắt.
  - Lỗi thật đã sửa: redirect mất port, 403 body rỗng.
- **Test-CI local pass: 251/251 .NET + 20/20 Node**.
  - Lần trước fail 2 test do `Copy-Item` giữ thời gian sửa cũ của file khôi phục, nên MSBuild dùng lại assembly đã bị mutate.
  - Đã build lại và dọn container của lần fail.
- GitHub CI: ghi sau khi chạy (commit docs-only `[skip ci]`).
- Không đụng `launchSettings.json`.

## Requirement

- **G5-N05: Done.**
- G5-N01 Pending (F04).
- OPS-01: quyết định sau khi có dòng INFO về replica giữ WebSocket chat trên CI (xem commit docs CI).

## Cho task sau

- **F04:** IDOR (ma trận quyền là nền), XSS (journey đã dùng nội dung thật), CSRF, auth/rate limit, secret scan; HSTS khi có domain; key ring chưa được mã hóa khi lưu.
- **F05:** JMeter qua Nginx; dùng seed demo + credential ngẫu nhiên giống `Test-AppStack.ps1`.
- **Lưu ý:** khôi phục file sau mutation bằng `cp` (cập nhật thời gian sửa), không dùng `Copy-Item`.

## Tiếp theo / Jira

- F03 → **KAN-27 / TV5 Hải Lê: hoàn tất local một phần** (còn F04, F05, F08). Chưa sync live; không tạo worklog/review/issue.
