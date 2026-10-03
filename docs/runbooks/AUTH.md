# Auth local — B01

[Contract](../architecture/AUTH.md), [evidence](../evidence/B01.md). S1 register và L1 eBay Welcome back do user gửi đã chốt. B01 dùng email/password; không social login/reset password hoặc persistent session.

Register tại `/Account/Register`: display name/email/password 12–128 ký tự, chỉ Buyer. Thành công chuyển `/Account/Login` với thông báo. Login lỗi chung, password không echo; thành công tới local returnUrl hoặc `/Account/Me`, có nút Sign out POST. Mọi POST yêu cầu antiforgery.

Cookie HttpOnly/Lax, 30 phút nonpersistent, Secure ngoài Development. Local HTTP loopback: dot-source Use-LocalDotnet và Use-LocalInfrastructure, set ASPNETCORE_ENVIRONMENT=Development rồi chạy Web. HTTPS/shared keys/proxy thuộc F01/F02; B02/F04 tiếp tục policies/object authorization/chống abuse.

Credential demo opt-in sau initialize/seed:

```powershell
pwsh -NoProfile -File scripts/Enable-DemoAuth.ps1
```

Password riêng Buyer/Seller/Admin lưu `.env.auth` ignored, không in giá trị. Email tương ứng buyer.a06@example.invalid, seller.a06@example.invalid, admin.a06@example.invalid. Chỉ gán password NULL, identity đúng và A06-v1 journal có; transaction, không overwrite. Local đã enable 3 rồi rerun 0 ngày 03/10/2026. Mất file hoặc password đã khác không tự rotate/reset. Không commit/gửi file vào chat.

Test-Database.ps1: 18 SQL/HTTP cases. Test-CI.ps1 cùng project trên SQL container riêng. Browser register/login/logout test account hư cấu đã pass, login mobile 390px đã kiểm tra. Local giữ một Buyer Browser Test phục vụ smoke, không xóa dữ liệu người dùng.
