# Handoff — H06b (My eBay: Purchases, Order details, Account)

28/10/2026. Nhánh `task/H06b-my-ebay` từ main `80ed79c` (H06a); commit theo `git log --oneline --grep=H06b`. **Chưa push/merge** — chờ người dùng.
Handoff trước: [H06a](H06a.md).

## Đã xong

- Contract [ORDERS §H06b](../architecture/ORDERS.md#quyết-định-h06b-purchases-order-details-account--chốt-28102026) chốt chi tiết (đã có từ H06a, nay bổ sung phần bỏ/giữ).
- `_MyEbayNav` (My eBay + tab Activity/Messages/Account); Purchases/Sales theo P3/P1/P2 (cột trái, pill, "Orders", "No orders were found", thẻ có ảnh, View order details trên dải xám, Leave feedback xanh, More actions giữ nguyên).
- Order details theo OD: thẻ trạng thái + tiến trình, Item, Shipping, cột phải Order/Payment ("What you paid"). `.order-status`, `#line-{id}`, Mark as shipped/Confirm received, "Estimated delivery" giữ cho test/journey.
- Account theo AC: Personal Info (tên, vai trò, Sign out), Activity, Selling (Seller), Admin.
- `OrderSummary.Image`/`OrderLine.Image` (ảnh đầu sản phẩm, quy tắc CatalogImages); `_Layout` bỏ `isAccountPage`.
- Test: EntryPointTests chip "All Purchases"; OrderTests "Cash on delivery · Paid". Xoá CSS `account-card`/`account-submit` không còn dùng.

## Test

- Trình duyệt in-app trên SQL/Redis tạm (đã xoá): desktop 1050 + 375 px, 0 tràn ngang, 0 lỗi console.
- Test-CI local pass 295/295 .NET + 20/20 Node, NuGet audit sạch, 0 warning (lần 1 fail 1 test: OrderTests còn kiểm "Payment: COD" → đổi sang "Cash on delivery · Paid"); Test-AppStack local pass J1–J7 + smoke HTTPS/WSS, CSP.
- **Chưa chạy:** GitHub CI, `-Screens` (H07).

## Chặn / câu hỏi

- Q-BIZ-007, Q-UI-012 Deferred (người dùng 28/10/2026); Q-TEAM-002 Open; Q-UI-011 trả lời một phần.

## Bước tiếp theo

1. Người dùng cho push → GitHub CI → fast-forward main; H06b Done, H06 Done.
2. H07 (nghiệm thu UI lại, `-Screens`) là bước riêng.
3. Cluster demo vẫn chạy G02: deploy lại trước demo.

## Lưu ý vận hành

- SDK 8 ở `%LOCALAPPDATA%\EbayGroup5\dotnet`: dot-source `scripts/Use-LocalDotnet.ps1`. Stage file cụ thể; `launchSettings.json` của người dùng. Không chạy Test-CI cùng Test-AppStack.
- CSS `checkout-card`/`checkout-grid` vẫn dùng ở Disputes/Admin/Reviews — không xoá.
