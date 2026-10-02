# Nội dung trích từ tài liệu yêu cầu

Nguồn: Project Requiments/Ebay Clone Requiments.docx. Giữ nguyên nội dung đoạn văn, kể cả khoảng trắng thiếu trong nguồn; không phải bản diễn giải. Không chứa bố cục/ảnh của Word.

Nhóm 1: Ngườimua (Buyer)
Yêucầutổnghợp:
Đăngkýtàikhoảnngườidùng (email, password, xácnhận email)
Đăngnhập/đăngxuất, cậpnhậtthông tin cánhân
Xemdanhsáchsảnphẩm (phântrang, lọctheodanhmục, giá, tên...)
Xem chi tiếtsảnphẩm (ảnh, môtả, ngườibán, đánhgiá...)
Thêmsảnphẩmvàogiỏhàng (Cart local & server)
Thựchiệnđặthàng (tạođơnhàngmới)
Chọnđịachỉgiaohàng (Address - nhiềuđịachỉ, chọnmặcđịnh)
Thanhtoán (giảlập qua PayPal, COD)
Xemlịchsửđơnhàng: trạngthái, chi tiếttừngđơnhàng
Gửiyêucầuhoàntrảđơnhàng
Gửiđánhgiásảnphẩm (Review: sốsao, bìnhluận)
Sửdụngmãgiảmgiá (Coupon)
Xemthôngbáohệthống: đơnhàng, khuyếnmãi, phảnhồi
✅Bảomậtthông tin cánhânvàgiaodịch (hash password, JWT, CSRF, SSL)
🚀Tốcđộtảitrangtìmkiếmvà chi tiếtsảnphẩmphảidưới 1s
📱Giaodiệnphải responsive, dễthaotáctrên mobile
⚙️Hệthốngphảicóthểmởrộngkhicónhiều user truycậpcùnglúc
🐞Gỡlỗidễ qua log chi tiếtvàphânbiệtlỗi client/server
Nhóm 2: Ngườibán (Seller)
Yêucầutổnghợp:
Đăngnhậpvàchuyểnđổi sang chếđộbánhàng
Cậpnhậthồsơcửahàng (Store name, banner, môtả)
Đăngbánsảnphẩm (form nhậpdữliệu, upload ảnh)
Quảnlýdanhsáchsảnphẩm (chỉnhsửa, tạmẩn, xoá)
Quảnlýtồnkho (Inventory), sốlượngsảnphẩmcònlại
Tạomãgiảmgiá, voucher theosảnphẩmcụthể
Xácnhậnđơnhàng, in phiếuvậnchuyển (giảlập)
Cậpnhậttrạngtháiđơnhàng (giaohàngthànhcông/thấtbại)
Xemđánhgiásảnphẩm do ngườimuađánhgiá
Trảlờiphảnhồihoặcgửiphảnhồihệthống (Feedback)
Báocáodoanhsố, sốđơnhàngtheotuần/tháng
Quảnlýcáckhiếunạiliênquanđếnđơnhàngđãbán
✅Chỉ seller đãxác minh mớiđượcđăngsảnphẩm
🛡Hạnchế spam sảnphẩm (rate limit API + xácthựcreCaptchanếucần)
📊 Dashboard phảitảinhanhkểcảkhi seller cóhàngtrămsảnphẩm
📈 Cho phépmởrộngtínhnăngtạo store riêngtheothờigian
🧪 Log chi tiếtlỗikhiđăng/bánhàngđểhỗtrợnhanh support
Nhóm 3: Quảntrịviên (Admin)
Yêucầutổnghợp:
Giaodiện dashboard tổngquan: sốlượng user, sảnphẩm, đơnhàng
Danhsáchngườidùng: duyệttàikhoảnmới, khoá/mởkhoá
Danhsáchsảnphẩm: xoáhoặcẩncácsảnphẩm vi phạm
Quảnlýđơnhàngtoànhệthống
Thốngkêhệthốngtheongày, tháng, quý (doanhthu, đơnhàng, ngườidùngmới)
Giámsáthệthốngđánhgiá – phảnhồi (Review + Feedback)
Quảnlýdanhsáchkhiếunại (Dispute): duyệt, điềuphối, phânxử
Xétduyệtyêucầuhoàntrảđơnhàng
Gửi email cảnhbáohệthốnghoặcthôngbáotoànbộngườidùng
🔐 Admin panel chỉtruycậptừ IP nộibộhoặcxác minh 2FA
📉Phânquyềnrõràngchotừngloại admin (monitor, support...)
📈Cókhảnăngmởrộng dashboard đểtheodõinhiềuloạithốngkêhơn
💾Dữliệunhạycảmphảiđược log riêng (kèmmãhóakhilưu)
🧩 Giaodiệndễsửdụngchoquảntrịviênkhôngchuyênkỹthuật
Nhóm 4: Hệthốngthanhtoán & giaohàng (System Integration)
Yêucầutổnghợp:
Module thanhtoánmôphỏng (PayPal, COD)
Tínhphígiaohàngdựatheokhuvực (giảlập logic đơngiản)
Kếtnối API vậnchuyển (giảlập): tạomãvậnđơn, cậpnhậttrạngtháigiaohàng
Gửi email xácnhậnthanhtoánthànhcông
Gửi email khitrạngtháiđơnhàngthayđổi (giaohàngthànhcông, thấtbại)
Tựđộnghuỷđơnhàngquáthờigianchờthanhtoán
Tínhtổngtiềnđơnhàng: giásảnphẩm, sốlượng, phívậnchuyển, mãgiảmgiá
🔐Kếtnốicác API thanhtoángiảlậpphảiđượckiểmtraauth token & secured key
⚡Tốcđộxácnhậnthanhtoánkhôngquá 2 giây
🔁Hệthống retry nếukếtnốivới API vậnchuyểnthấtbại
🧱 Module thanhtoánvàvậnchuyểnphảicóthể plug-in dễdàng (microservice/hook)
🐞 Log chi tiết transaction ID vàlỗigiaotiếpgiữacác module
Nhóm 5: Chat, Đánhgiá & Khiếunại (Interaction Layer)
Yêucầutổnghợp:
Giaodiện chat giữangườimua – ngườibán (socket.io hoặcWebSocket)
Lưulịchsử chat (message store theothờigian, ngườigửi)
Gửiđánhgiásaumỗiđơnhàngđãnhận (rating + comment)
Hiểnthịtrungbìnhđánhgiátrênmỗisảnphẩm
Hệthốngđiểmuytínngườibán (tínhtheo feedback tíchcực)
Giaodiệngửikhiếunạiđơnhàng (nội dung, chọnlý do)
Giaodiệnquảnlýkhiếunạiđãgửi (buyer) hoặcđãnhận (seller)
Kếtnốihệthốngphảnhồitựđộngvớihệthống admin
🔒 Tin nhắngiữangườidùngphảiđượcbảomật (WebSocket qua SSL, mãhóakhicần)
💬Giaodiện chat phảimượt, khônggiật lag, hỗtrợrealtime
⚙️Hệthốngxửlýkhiếunạicầnđảmbảokhôngmấtdữliệukhi reload
📈Mởrộngkhảnănglưutrữlịchsử chat, phảnhồinhanhkhisốlượnglớn
🧪 Gỡlỗiphảihỗtrợ debug từng tin nhắn qua thờigian/log ID
Cácyêucầukhác (25% sốđiểm)
Đápưngđượckhilượngngườidùngtănglên: load balance, Nginx
Hạnchếsốlượt request củangườitrongkhoảngthờigian: rate limiting
CI&CD: jenkins,github action
Quảnlý team. Jira
Kiểmthửvềtải, an ninhmạng: Jmetter
ĐảmbảoZero Downtime khihệthốngcậpnhật. K8s

LƯU Ý
Đâychỉlànhữngyêucầugợi ýtốithiếucầnđạtđược (làmđủđược 80% điểm).Từngnhómcầntrảinghiệmhệthốngthựcsựvàlàmđúngtheohệthốngđãcó.
