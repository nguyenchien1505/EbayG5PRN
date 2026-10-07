# Handoff — F04: kiểm thử bảo mật

07/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `a67927b`. Branch `task/F04-security-testing`; checkpoint `git log --oneline --grep=F04`. Runtime `4a20f28`, fast-forward vào main sau khi [CI37568707408](https://github.com/Twiash/EbayGroup5/actions/runs/37568707408) pass.
- Handoff trước: [F03](F03.md).

## Hoàn tất

- Contract: [SECURITY_TESTS](../architecture/SECURITY_TESTS.md); dòng F04 trong [SECURITY](../architecture/SECURITY.md). **Không có migration.**
- **App:**
  - `Web/Security/SecurityHeaders.cs`: CSP `'self'` không `unsafe-*`, `X-Frame-Options: DENY`, `nosniff`, `Referrer-Policy`, COOP, `Permissions-Policy` trên mọi response; `UseHsts()` ngoài Development (ASP.NET bỏ qua localhost).
  - JSON API: `JavaScriptEncoder.Create(UnicodeRanges.All)` (escape `<>&'"`).
  - Không còn inline script/style: `wwwroot/js/checkout.js`, `review-form.js`; sao/thanh rating dùng class `pct-N` trong `site.css`; validation summary chỉ render khi có lỗi.
- **`tests/EbayGroup5.Database.Tests/SecurityTests.cs`** (7 test): kiểm kê endpoint (endpoint mới phải được phân loại trong bảng `Inventory` và có mặt ở sweep CSRF), IDOR 29 lượt + dấu vân tay 17 bảng, CSRF 18 POST + đối chứng, stored XSS 10 trường × 27 trang + cấm `style=`/inline script, header/HSTS, auth/open redirect/brute force.
- **Stack:** smoke thêm header, TLS 1.2/1.3, TLS cũ thất bại, `ws://` bị từ chối, 413, 404 sạch; `Test-AppStack.ps1` kiểm `nginx -T`; Journeys theo dõi vi phạm CSP + dialog trên mọi trang và thêm **J7** (payload hiện nguyên văn).
- **`scripts/Test-Secrets.ps1`** (file + toàn lịch sử, không công cụ ngoài) và audit NuGet `--vulnerable` trong Test-CI; CI `fetch-depth: 0`.

## Verification

- [Evidence](../evidence/F04.md): SecurityTests 7/7; stack Journeys 8/8 (0 vi phạm CSP), smoke 23/23, `nginx -T` pass, correlation pass; secret scan sạch (393 file, 71 commit) và bắt đủ 5 secret giả; NuGet 0 lỗ hổng; mutation 5/5 bị bắt.
- **Test-CI local: 258/258 .NET + 20/20 Node**, build 0 warning.
- **GitHub [CI37568707408](https://github.com/Twiash/EbayGroup5/actions/runs/37568707408) trên runtime `4a20f28` success**: 258 .NET + 20 Node, Journeys J1–J7 pass trên runner, secret scan toàn lịch sử. Run đầu (`3af2c58`) fail vì `Get-Item` bỏ qua dotfile trên Linux; đã sửa.
- Lỗi thật đã sửa: thiếu header (clickjacking), inline script, `style=` ở sao (J7 bắt), `<li style>` của validation summary, JSON relaxed.
- Không đụng `launchSettings.json`.

## Requirement

- **G5-N01: Done.**
- OPS-05 Pending (F05 JMeter). OPS-01 Pending (F05): lần chạy stack này WebSocket chat nằm trên 2 replica nhưng chỉ là INFO, chưa khẳng định.

## Câu hỏi / rủi ro

- **Q-SEC-001 (Open):** logout chưa thu hồi bản sao cookie (hạn 30 phút). Đề xuất denylist phiên trên Redis là task riêng nếu người dùng đồng ý.
- Key ring DataProtection chưa mã hóa khi lưu → F06 (`ProtectKeysWithCertificate` + K8s Secret).
- HSTS chỉ hiệu lực khi có domain thật trong `AllowedHosts`.

## Cho task sau

- Thêm endpoint mới: cập nhật `Inventory` + sweep CSRF/IDOR trong `SecurityTests`; không dùng `style=`/inline script (CSP sẽ chặn, J7 và test sẽ fail).
- **F05:** JMeter qua Nginx, nhiều WebSocket và khẳng định phân bố ≥ 2 replica (OPS-01); seed demo + credential ngẫu nhiên như `Test-AppStack.ps1`.
- Khôi phục file sau mutation bằng `cp`, không dùng `Copy-Item`.

## Tiếp theo / Jira

- F03+F04 → **KAN-27 / TV5 Hải Lê: hoàn tất local một phần** (còn F05, F08). Chưa sync live; không tạo worklog/review/issue.
