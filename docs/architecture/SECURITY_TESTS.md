# F04 — Kiểm thử bảo mật

Contract do agent soạn ngày 07/10/2026 trên main `a67927b`, trước khi sửa source.

Dựa trên [SECURITY](SECURITY.md) (quyết định B02–F03), [DEPLOYMENT](DEPLOYMENT.md), [SCALE_OUT](SCALE_OUT.md), [JOURNEYS](JOURNEYS.md) và PLAN §9 (Bảo mật: stored XSS, CSRF, IDOR, brute force, request quá lớn, lỗi lộ dữ liệu, secret trong repo).

Acceptance: **kết quả thực** cho IDOR, XSS, CSRF, auth, secret scan; lệnh chạy lại được.

Requirement:

- **G5-N01** (HTTPS/WSS, membership, không log nội dung/secret, không tuyên bố E2EE): F04 bổ sung kiểm thử TLS/WSS-only, IDOR chat, CSRF chat. Done khi mọi kiểm tra dưới đây pass trên stack và CI.
- **OPS-05**: F04 làm phần an ninh; phần JMeter thuộc F05, nên OPS-05 vẫn Pending.

Ngoài phạm vi: đo tải (F05), K8s (F06), thay đổi luật nghiệp vụ, pentest công cụ ngoài (ZAP/Burp), tải thêm công cụ quét (gitleaks…) về máy.

## Khảo sát trước khi code

| Hạng mục | Hiện trạng trên `a67927b` | Xử lý F04 |
|---|---|---|
| Header bảo mật | Không có CSP, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` → **trang có thể bị nhúng iframe (clickjacking)** | Thêm middleware header (bên dưới) |
| Inline script | 2 khối: `Checkout/Index` (nhúng giá bằng Razor), `Reviews/Create` (đếm ký tự). Không có `style=`/`on*=`/`<style>`/`javascript:` | Chuyển ra `wwwroot/js/checkout.js`, `review-form.js`; giá qua `data-unit-price` → CSP không cần `'unsafe-inline'` |
| HSTS | Chưa bật (localhost) | `UseHsts()` ngoài Development; mặc định ASP.NET **bỏ qua localhost/127.0.0.1/[::1]**, nên chỉ phát khi có domain thật trong `AllowedHosts` |
| Raw HTML | Không có `Html.Raw`, `innerHTML`, `insertAdjacentHTML`, `eval` | Kiểm thử bảo đảm |
| JSON API | **Phát hiện khi chạy test:** encoder mặc định của MVC là "relaxed", trả `<`, `>` thô (ví dụ `peerName`). Không khai thác được (JSON + client dùng `textContent`) nhưng thiếu phòng thủ nhiều lớp | `JsonOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)`: escape ký tự HTML, giữ Unicode |
| SQL thô | Không có `FromSqlRaw`/`ExecuteSqlRaw` | Kiểm payload injection ở ô tìm kiếm |
| Gói NuGet | `dotnet list package --vulnerable --include-transitive`: 0 gói có lỗ hổng | Thêm vào Test-CI |
| Secret | Quét thô toàn bộ lịch sử: không có khóa/token; file theo dõi chỉ `.env.example` (placeholder) | Script quét chạy lại được |

## Header bảo mật (mọi response của app, kể cả static, lỗi, API)

Đặt bằng `Response.OnStarting` ngay sau forwarded headers (`Web/Security/SecurityHeaders.cs`):

- `Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'`
  - `connect-src 'self'` gồm cả `wss:` cùng origin (CSP3).
- `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Cross-Origin-Opener-Policy: same-origin`, `Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()`.
- `Strict-Transport-Security` (mặc định 30 ngày): chỉ HTTPS, không phải localhost, ngoài Development.

Response do Nginx tự sinh (308, 413, 502) không qua app; chấp nhận, ghi giới hạn.

## Kiểm thử

### A. Trong tiến trình, SQL thật (`tests/EbayGroup5.Database.Tests/SecurityTests.cs`)

1. **Kiểm kê endpoint:** reflection mọi action MVC/API và method của Hub, so với bảng kiểm kê trong test (verb + quyền). Action mới chưa phân loại → fail. Không có `IgnoreAntiforgeryToken`; filter `AutoValidateAntiforgeryToken` toàn cục còn đăng ký.
2. **IDOR trên thao tác ghi:** người cùng vai trò nhưng không sở hữu, có CSRF token hợp lệ, gọi mọi POST có đối tượng: Orders Ship/Receive, Checkout Place (địa chỉ người khác) + Confirmation, Disputes Open/Draft/Act, Reviews Create, Sellers Reply, Chat Open/Send/Read/History/Conversation, Account Details. Khẳng định mã theo contract module (đối tượng người khác 404; sai vai trò 403; địa chỉ không phải của mình ở Checkout là lựa chọn không hợp lệ 400 theo B05 và trang không lộ địa chỉ đó; Admin trên object chat 404 theo C02) **và dữ liệu không đổi** (trạng thái đơn, số event dispute, số review, reply, số tin, nháp, read cursor).
3. **CSRF:** mỗi POST, với chủ sở hữu: thiếu token → 400; token lấy từ phiên **người khác** → 400; API chat thiếu `X-CSRF-TOKEN` → 400; Logout/Login thiếu token → 400. Dữ liệu không đổi.
4. **Stored XSS:** payload `<img src=x onerror=…>` + `<script>…</script>` gắn marker được đưa qua **endpoint thật** vào: tên đăng ký, địa chỉ, tin chat, comment review, comment feedback, seller reply, chi tiết dispute, tin trả lời dispute, chi tiết report. Đọc mọi trang hiển thị (sản phẩm, reviews, hồ sơ seller, đơn buyer/seller, lịch sử, request hai phía, admin moderation/dispute, API chat). Khẳng định **dạng encode có mặt** (payload thật sự được render ở đó) và **dạng thô không có**. API JSON escape `<`.
5. **Header:** trang HTML, API 401, static, 404 có đủ header; HSTS vắng với localhost và có với host thật qua HTTPS.
6. **Auth và đầu vào:** login sai không phân biệt email tồn tại; cookie bị sửa → khách; open redirect (`https://evil.example`, `//evil.example`, `/\evil.example`) → `/Account/Me`; tìm kiếm `' OR 1=1 --` → 200 bình thường; JSON chat hỏng → 400 chung, không lộ exception.

### B. Trên stack thật (`tests/EbayGroup5.StackSmoke`, chạy bởi `Test-AppStack.ps1`)

- Header bảo mật đi qua Nginx; không có `X-Powered-By`/`X-AspNet-Version`; HSTS vắng trên localhost.
- TLS 1.2 và 1.3 bắt tay được; `nginx -T` chỉ khai báo `ssl_protocols TLSv1.2 TLSv1.3`.
- `ws://` qua cổng HTTP không được nâng cấp (308), chỉ `wss://`.
- Body > 1 MiB → 413 từ Nginx.
- 404 không lộ stack trace/đường dẫn.
- Giữ kiểm tra cũ: Origin lạ bị từ chối trên WSS; brute force bị giới hạn theo IP thật dù giả `X-Forwarded-For` (chạy cuối).

### C. Trình duyệt (`tests/EbayGroup5.Journeys`)

- Mọi trang J1–J6 chạy dưới CSP thật: **không có vi phạm CSP** (`securitypolicyviolation` ghi qua init script) → CSP không làm hỏng app.
- **J7 XSS:** buyer gửi tin chat chứa payload; seller thấy **nguyên văn** trong inbox và hội thoại; review J3 và chi tiết return J4 mang payload, hiển thị nguyên văn. Không có `dialog`, `window.__xss` không được đặt.

### D. Secret và phụ thuộc (`scripts/Test-Secrets.ps1`, gọi từ Test-CI)

- File theo dõi bị cấm: `.env*` (trừ `.env.example`), `*.pfx|*.p12|*.key|*.pem`, `artifacts/`, `secrets/`.
- Mẫu nội dung trong file hiện tại **và toàn bộ lịch sử** (dòng thêm vào): private key, AWS, GitHub, Slack, Google, Atlassian token, JWT, chuỗi kết nối có mật khẩu literal. Allowlist có lý do nằm trong script.
- CI checkout `fetch-depth: 0` để quét lịch sử.
- `dotnet list package --vulnerable --include-transitive` → fail khi có gói lỗ hổng.

## Rủi ro còn lại (ghi trong evidence, không sửa ở F04)

- ~~Logout không thu hồi bản sao cookie~~ → **đã xử lý** ở phần bổ sung bên dưới (người dùng chốt Q-SEC-001). Ghi chú gốc: cookie copy vẫn hợp lệ tới hạn tuyệt đối 30 phút. Đề xuất: denylist phiên trên Redis. Hỏi người dùng ở Q-SEC-001.
- Key ring DataProtection trên Redis không mã hóa khi lưu (F02): giảm thiểu bằng mật khẩu Redis + mạng nội bộ; đề xuất `ProtectKeysWithCertificate` với K8s Secret ở F06.
- HSTS chỉ hiệu lực khi có domain; không bật trên localhost để không ảnh hưởng các dev server khác trên máy.
- Không có E2EE (đúng MATRIX); TLS kết thúc ở Nginx, mạng app nội bộ là HTTP.

## File dự kiến

`Web/Security/SecurityHeaders.cs`, `WebApplicationHost.cs`, `Views/Checkout/Index.cshtml`, `Views/Reviews/Create.cshtml`, `wwwroot/js/checkout.js`, `wwwroot/js/review-form.js`, `tests/EbayGroup5.Database.Tests/SecurityTests.cs`, `tests/EbayGroup5.StackSmoke/Program.cs`, `tests/EbayGroup5.Journeys/Program.cs`, `scripts/Test-Secrets.ps1`, `scripts/Test-CI.ps1`, `scripts/Test-AppStack.ps1`, `.github/workflows/ci.yml`. Không migration.

## Bổ sung: thu hồi phiên khi logout (Q-SEC-001)

Người dùng chốt ngày 07/10/2026: **thêm danh sách phiên bị thu hồi trên Redis**. Làm như phần bổ sung của F04 (PLAN cố định 40 task), branch `task/F04-session-revocation`. Contract soạn trước khi code.

- **Đăng nhập:** cookie mang claim `eg5:sid` = 128 bit ngẫu nhiên (32 hex). Mỗi lần đăng nhập là một phiên riêng.
- **Logout:** ghi `{Redis:KeyPrefix}:revoked-session:{sid}` với TTL = thời gian còn lại của cookie (≤ 30 phút), rồi xóa cookie. Chỉ phiên đó bị thu hồi; phiên khác của cùng người dùng không bị ảnh hưởng.
- **Mỗi request có cookie** (`AccessCookieEvents.ValidatePrincipal`, sau kiểm tra DB hiện có): thiếu `sid`, sai định dạng, hoặc `sid` đã bị thu hồi → từ chối như cookie hết hạn (trang → Login, API/Hub → 401). Cookie phát hành trước thay đổi này (không có `sid`) phải đăng nhập lại một lần.
- **Hub chat:** `ChatHubFilter` kiểm cùng điều kiện ở mỗi lần gọi, nên kết nối WebSocket đang mở của phiên đã logout bị đóng ở lần gọi kế tiếp.
- **Redis gián đoạn** (theo nguyên tắc F02: suy giảm, không khóa người dùng): instance nhận logout luôn ghi thêm vào bộ nhớ cục bộ (TTL như trên) nên phiên vẫn bị từ chối trên instance đó; ghi Redis thất bại hoặc đọc Redis lỗi → log cảnh báo (không ghi `sid`), instance khác chưa biết phiên bị thu hồi cho tới khi cookie hết hạn. Đây là rủi ro còn lại có giới hạn (≤ 30 phút, chỉ khi Redis lỗi).
- Không thêm migration; không lưu dữ liệu cá nhân trong Redis (chỉ `sid` ngẫu nhiên).
- **Kiểm thử** (`SessionRevocationTests`): bản sao cookie sau logout bị từ chối trên cùng host và trên host thứ hai dùng chung Redis/key ring; phiên khác vẫn dùng được; cookie hợp lệ chữ ký nhưng thiếu `sid` bị từ chối; kết nối Hub đang mở bị chặn sau logout; Redis không tới được → logout vẫn thu hồi trên instance đó và request không lỗi 500. Inventory/CSRF của F04 giữ nguyên.
