# Ảnh UI và quyết định đã chốt

Ngày tìm 02/10/2026. Người dùng yêu cầu tìm ảnh thiếu trên Internet và cho xem trước khi chốt. **Đã chốt M1, R1, D1, D2** qua câu trả lời trực tiếp: “Chốt cả M1, R1, D1, D2”. U1 dùng cho panel Message seller theo câu hỏi đã được xác nhận. **P1 đã chốt** ngày 02/10/2026 qua lời nhắn “ok tôi chốt P1, hãy tiếp tục”. Chọn bố cục không tự mở rộng scope nghiệp vụ. Chỉ nạp ảnh của màn hình đang triển khai; không nạp toàn bộ gallery mỗi phiên.

## U1 — ảnh người dùng: Message seller

Đã xem ảnh đính kèm trong chat ngày 02/10/2026: panel trắng sát phải, nền product detail phủ tối, tiêu đề/đóng, seller context, mini product card, notice, vùng hội thoại, composer dưới cùng, icon ảnh, checkbox email và nút gửi disabled khi trống. Đây là nguồn người dùng cung cấp cho C04. Không suy ra luồng gửi thành công/draft/reconnect từ ảnh tĩnh. Không commit ảnh gốc vì có thông tin tài khoản; không tự thêm email copy hoặc upload ảnh vào scope.

## M1 — inbox/conversation desktop

[Nguồn chính thức eBay Seller Center, Summer 2024](https://www.ebay.com/sellercenter/resources/seller-updates/2024-summer/tools). Mẫu nhiều cột cho C04; không khẳng định giống tuyệt đối UI 2026.

![M1 — desktop messaging](https://ucpstatic.ebay.com/static-assets/images/2024/06/us-desktop-messaging.png?crafterSite=sellercenter-us)

## R1 — feedback form

[Nguồn eBay Community, thảo luận tháng 6/2024](https://community.ebay.com/forum/selling-57920/topic/more-changes-to-feedback-127861/). Ảnh thành viên đăng, không phải đặc tả nghiệp vụ chính thức; dùng tham khảo sentiment/stars/comment cho D02–D03. Review sản phẩm và feedback seller vẫn theo contract của clone.

![R1 — feedback](https://res.cloudinary.com/startup-grind/image/upload/c_fill,dpr_2,f_auto,g_center,q_auto:good/v1/gcs/platform-data-ebay/discussions/large_KpIC0iC)

## P1 — purchase history item

[Nguồn eBay Community, thảo luận năm 2023](https://community.ebay.com/t5/Buying/Tracking-links-stopped-showing-in-Purchase-History/td-p/33825818). Mẫu item/order context và nhóm action cho B06; ảnh không chứng minh mọi action trong trạng thái Delivered.

![P1 — purchase history](https://res.cloudinary.com/startup-grind/image/upload/c_fill,dpr_2,f_auto,g_center,q_auto:good/v1/gcs/platform-data-ebay/discussions/medium_zKDl6WM)

## D1 — buyer return request

[Nguồn eBay Community, thảo luận năm 2023](https://community.ebay.com/forum/returns-57918/topic/i-ordered-two-items-but-only-got-one-104714/). Mẫu reason/detail/action cho E02. Không dùng lời khuyên trong bình luận làm quy tắc xử lý dispute.

![D1 — buyer return request](https://res.cloudinary.com/startup-grind/image/upload/c_fill,dpr_2,f_auto,g_center,q_auto:good/v1/gcs/platform-data-ebay/discussions/large_WkKELnz)

## D2 — seller xử lý return (đã chốt)

[Nguồn eBay Community, tháng 9/2024](https://community.ebay.com/t5/Returns/how-to-return-to-send-partial-refund-from-the-create-return/td-p/34699999). Mẫu xử lý return/partial refund cho E03. Đây là mẫu D2 trong câu hỏi người dùng đã chốt; URL ảnh cũ đã chuyển sang CDN. Không mặc định áp dụng hoàn tiền thật hoặc mọi lựa chọn hiển thị.

![D2 — seller return response](https://res.cloudinary.com/startup-grind/image/upload/c_fill,dpr_2,f_auto,g_center,q_auto:good/v1/gcs/platform-data-ebay/discussions/large_w0ZKvm5)

## Cách chốt và giới hạn

- M1/R1/P1/D1/D2 đã được chốt ngày 02/10/2026; không hỏi lại quyền dùng bố cục này. Task B06 dùng P1, C04/D02/D03/E02/E03 dùng các mẫu tương ứng cùng contract và U1; không yêu cầu mở tất cả ảnh.
- Link ảnh M1/R1/P1/D1/D2 đã kiểm tra HTTP HEAD 200, content-type ảnh; dùng URL hiện tại vì URL Community cũ trả 410. Ảnh chỉ nhúng từ nguồn, không copy tài khoản/order/comment làm dữ liệu demo.
- Chưa đủ bằng chứng UI mobile, đầy đủ timeline/escalation, trạng thái lỗi/draft và seller feedback profile. Khi đến task liên quan, tiếp tục tìm nguồn, trình mẫu hoặc đề xuất trạng thái clone để người dùng chốt. Admin thiết kế riêng, không mạo nhận UI admin eBay.

## C1 — checkout (B05, đã chốt 06/10/2026)

[eBay Pay with card chính thức](https://pages.ebay.com/pay-with-card/index.html), trang không ghi năm của ảnh; URL kiểm tra ngày 06/10/2026. User chốt “Chốt C1 và bố cục đề xuất” qua Q-UI-006. Nguồn bổ sung [eBay Connect Payments 2023, slide 3](https://developer.ebay.com/cms/files/connect-2023/payments.pdf) và [Checkout Help](https://www.ebay.com/help/buying/paying/checkout?id=4036).

![C1 checkout](https://ir.ebaystatic.com/pictures/aw/pics/EUO-240/W43_PayWithCard_Gif_ROW.gif)

Clone giản lược: Ship to → item/quantity → COD mô phỏng → total/Place order, desktop hai cột/mobile một cột. Không triển khai card/PayPal/coupon/vault/tax/shipping options từ mẫu. COD là quyết định demo của dự án, không suy ra từ ảnh hoặc chính sách eBay. Không copy thông tin cá nhân trong nguồn.

C04 06/10/2026 triển khai U1/M1 đã chốt: right white dialog/backdrop/item context/plaintext composer và Messages navigation/inbox/thread ba cột. Mobile full-width drawer hoặc list/thread Back là adaptation clone, không nguồn eBay mobile mới. Không attachments/offer/email/archive. [C04 evidence](../evidence/C04.md) ghi thực chứng desktop/mobile/error/paging/read/ownership; không hỏi lại phê duyệt U1/M1.
