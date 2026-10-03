# Auth contract — B01

03/10/2026, nền 553f266; A06 seed và A07 SQL CI đã xác minh. Không sửa schema gốc, không EF migrations hoặc bảng Identity thứ hai.

- MVC cookie cùng origin; register chỉ Buyer, role/ID từ SQL khi login, không nhận role từ form. Email trim/uppercase invariant, tối đa 100 ký tự; tên 1–100, password 12–128 ký tự không trim. Login email/password, không username/social/reset/email verification trong B01. Đây là bản clone giản lược, không phải chính sách eBay.
- ASP.NET Core PasswordHasher<User> IdentityV3 PBKDF2, 210000 iterations; không plaintext/fake hash. NULL/malformed hash/role lạ/không tìm thấy đều trả cùng lỗi; dummy hash verification cho user không có credential. Không log credential hoặc SQL parameter. Rehash thành công dùng compare-and-update để không ghi đè password mới.
- Register dùng SQL unique email để chặn race; kiểm tra normalized legacy email trước insert, không đổi legacy rows. Nếu legacy normalized email mơ hồ thì login từ chối. Validation server; lỗi duplicate an toàn. Login chỉ returnUrl nội bộ; logout POST, antiforgery bắt buộc cả login/register/logout. Không trả password vào HTML khi validation lỗi.
- Cookie HttpOnly, SameSite=Lax, 30 phút, nonpersistent, không sliding. Secure Always ngoài Development; Development cho HTTP loopback để demo local. B02 bổ sung authorization policies/object checks và chống abuse; F01/F02 HTTPS/reverse proxy/shared Data Protection keys. Chưa tuyên bố đa replica hoặc session revocation sau logout trên cookie đã bị sao chép.
- Demo credential là lệnh opt-in riêng, chỉ gán password cho 3 user A06 còn NULL với email/username/role đúng và journal A06-v1 tồn tại; không overwrite/rotate, transaction toàn bộ. Script sinh password riêng vào .env.auth ignored nếu chưa có; không in secret. B01 test login thật các role; seed v1 bất biến.
- UI Q-UI-004 Answered: S1 đã chốt; L1 là ảnh eBay Welcome back do user gửi 03/10/2026 (thay ảnh Agent Router gửi nhầm). Giữ bố cục trắng/form hẹp, dùng email/password theo scope; không Google/reset password/Stay signed in. Không lưu email cá nhân hoặc ảnh nguồn vào Git.
- Files: Infrastructure Authentication service, Web account DTO/controller/cookie config/views khi UI được chốt; Database demo provisioning + script; HTTP/SQL tests trong project test hiện có để cùng CI chạy. Không làm catalog/checkout/admin UI.

Nguồn: [Microsoft cookie auth](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-8.0), [PasswordHasher](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.passwordhasher-1), [eBay sign in](https://www.ebay.com/help/account/account-getting-started/signing-ebay?id=4189), [eBay register](https://www.ebay.com/help/account/account-getting-started/setup-verify?id=4191).

Luồng hoàn chỉnh: Register thành công redirect Login với TempData notice; Login lỗi 401 + view chung, password xóa; Login thành công redirect local hoặc Me view; logout POST qua nút Sign out. Navigation tới Sign in/Register hoặc Your account. UI English; không hiển thị chức năng social/reset chưa triển khai.

B02 đã triển khai policy/resource checks, cookie revalidation, IP gate và rate limit; xem [SECURITY](SECURITY.md). Giới hạn triển khai vẫn theo F01/F02.
