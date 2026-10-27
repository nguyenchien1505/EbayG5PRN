# Handoff — H06a (checkout, đăng nhập/đăng ký theo eBay)

28/10/2026. Nhánh `task/H06a-checkout-signin` từ main `957deb4` (OPS-001); commit theo `git log --oneline --grep=H06a`. Đã push nhánh; **chưa merge main** — chờ người dùng.
Handoff trước: [OPS-001](OPS-001.md).

## Đã xong

- H06 tách: [H06a](../tasks/H06a.md) (làm), [H06b](../tasks/H06b.md) (contract đã chốt, chưa code).
- Contract: [CHECKOUT §H06a](../architecture/CHECKOUT.md#quyết-định-h06a-checkout-theo-c2), [AUTH §H06a](../architecture/AUTH.md#quyết-định-h06a-đăng-nhậpđăng-ký-theo-l0), [ORDERS §H06b](../architecture/ORDERS.md#quyết-định-h06b-purchases-order-details-account--chốt-28102026-chưa-code); UI_CANDIDATES §Q-UI-011 ghi phần áp dụng.
- `_Layout`: `ViewData["Shell"]` = `checkout`/`auth` → header tối giản (logo [+ "Checkout"], section `HeaderAside`) và footer chỉ dòng legal.
- Checkout C2: Pay with (COD), Ship to, Review order (seller + % positive, ảnh H05, Quantity, Returns accepted, Delivery Free + ngày ước tính), Order Summary xám + **Confirm and pay**. `CheckoutController` thêm reputation + `DisputeTiming` vào `CheckoutPage`. Add address cùng shell (Add/Cancel), Confirmation đổi kiểu giữ nội dung.
- Sign in / Register theo L0; Business → `/Help/Unavailable/business-account` (key mới).
- Test đổi chữ: AccountHttpTests ("Sign in to your account"), CheckoutTests + Journeys J1/G02 ("Confirm and pay"); JOURNEYS/DEMO cập nhật.

## Test

- Trình duyệt in-app trên SQL/Redis tạm (container riêng, đã xoá): desktop + 375 px, 0 tràn ngang, 0 lỗi console; luồng thêm địa chỉ → Confirm and pay → confirmation chạy.
- **Test-CI local pass: 295/295 .NET + 20/20 Node**, NuGet audit sạch, 0 warning, Verify-Planning/Migrations/ClientAssets/Secrets pass.
- **Test-AppStack local pass** (sau Test-CI, không chạy cùng lúc): J1–J7 (J1 bấm "Confirm and pay"), smoke HTTPS/WSS, CSP, 2 replica.
- GitHub CI [40551007118](https://github.com/Twiash/EbayGroup5/actions/runs/40551007118) trên `ffc22b5` (gồm merge main `b108356`): success attempt 1 — 295/295 .NET, J1–J7 + smoke HTTPS/WSS, load Short 0 lỗi, K8s, rolling 5.328 mẫu 0 HTTP 5xx, ACK 1.212 đủ.
- **Chưa chạy:** `-Screens` (H07).

## Chặn / câu hỏi

- 28/10/2026 người dùng chốt Deferred (chưa làm): Q-BIZ-007 (eBay automated feedback), Q-UI-012 (ảnh kèm feedback/chat) — không ảnh hưởng H06b.
- Q-TEAM-002 (ngày nộp) Open; Q-UI-011 trả lời một phần.

## Bước tiếp theo

1. Người dùng cho merge `task/H06a-checkout-signin` vào main (CI đã xanh).
2. H06b theo contract ORDERS §H06b; sau đó H07.
3. Cluster demo vẫn chạy G02: deploy lại trước demo.

## Lưu ý vận hành

- SDK 8 ở `%LOCALAPPDATA%\EbayGroup5\dotnet`: dot-source `scripts/Use-LocalDotnet.ps1`. Stage file cụ thể; `launchSettings.json` của người dùng. Không chạy Test-CI cùng Test-AppStack.
- CSS cũ `checkout-card`/`checkout-grid`/`account-card` vẫn dùng ở Orders/Details và Account/Me (H06b dọn).
