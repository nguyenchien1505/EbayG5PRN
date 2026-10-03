# B02 security contract

- Account resource: `/Account/Details/{id}` loads the database row and permits only the authenticated owner. Missing and foreign accounts both return 404. No Admin bypass.
- All authenticated access by Admin requires a direct remote IP in `Security:AdminAllowedAddresses`, default IPv4/IPv6 loopback. Missing IP fails closed. Forwarded headers are not trusted; deployed proxy/network configuration belongs to F01.
- Named Buyer, Seller and AdminDemo policies support later modules; their object checks must be implemented with each module. No claim of order/chat/dispute authorization yet.
- Cookies are revalidated against database ID, role and nonempty password on each request. Deleted accounts, removed credentials and changed roles require login again. Password changes and copied cookies after logout are not globally revoked yet.
- Existing global MVC antiforgery remains mandatory on unsafe methods. Forbidden responses return 403. Exceptions return generic 500 in every environment.
- Login/register POST share a fixed window of 10 requests/minute per direct IP, no queue, 429 on excess (including invalid CSRF). GET and logout are unaffected. This is single-process demo protection; distributed limits and proxy trust remain F01/F02.
- Files: Web Security/AccessSecurity.cs, Program.cs, AccountController.cs; SQL-backed HTTP security tests. No schema changes, new business module or UI design.
