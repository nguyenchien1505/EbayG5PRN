# Handoff — Đợt H (giống eBay sau bàn giao)

09/10/2026. Main sau phiên này: H00–H04 (xem `git log --oneline --grep=H0`). Handoff trước [G02](G02.md).

## Quyết định người dùng (08/10/2026)

- Uy tín seller (G5-05) làm lại theo eBay; màn nhóm khác: UI giống eBay, backend giản lược ([ADR-003](../decisions/ADR-003-ui-fidelity-references.md)).
- Ảnh UI đã chốt lưu trong repo: [references/ui](../references/ui/INDEX.md). Người dùng sẽ đặt mua thật để chụp màn cần đơn hàng (Q-UI-011).
- 09/10/2026: người dùng yêu cầu "hoàn thành task hiện tại rồi dừng lại" → dừng sau H04.

## Đã xong (đều có GitHub CI success trên hash runtime)

- **H00** thư viện ảnh + ADR-003. **H01** quy tắc uy tín eBay (tuần Pacific lưu bằng trigger, DSR, sao, form R1; DbUp 0015) — [evidence](../evidence/H01.md).
- **H02** hồ sơ feedback F2/F2m, About this seller/Seller feedback, `/Catalog?seller=`, `/Help/Unavailable/{key}` (DbUp 0016) — [evidence](../evidence/H02.md).
- **H03** header/thanh danh mục/footer/trang chủ thật — [evidence](../evidence/H03.md). **H04** trang kết quả tìm kiếm SR — [evidence](../evidence/H04.md).

## Còn lại

- **H05** trang sản phẩm + ảnh sản phẩm demo (Planned, chưa bắt đầu). **H06** Blocked chờ ảnh Q-UI-011. **H07** nghiệm thu lại UI + cập nhật REPORT/DEMO/UI_ACCEPTANCE (ảnh `docs/evidence/ui` vẫn là bản G01).
- Q-UI-012 (ảnh kèm feedback/chat) Open; Q-OPS-001 (lỗi mạng chập chờn ở rolling) Open.
- Cluster demo `ebaygroup5` vẫn chạy image G02; chưa deploy H01–H04 (0015/0016 chưa áp vào cluster đó).

## Lưu ý vận hành

- Stage file cụ thể; `src/EbayGroup5.Web/Properties/launchSettings.json` là file của người dùng (đã có trong `.git/info/exclude`). Không Co-Authored-By Claude.
- Test-CI giờ dùng SQL 3 GB (mỗi test giữ DB riêng). Không chạy Test-CI cùng lúc Test-AppStack; dọn bằng `scripts/Clear-TestArtifacts.ps1 [-Apply]`.
- eBay chặn Chrome headless: không vượt chặn; ảnh mẫu chụp bằng trình duyệt in-app hoặc người dùng gửi, che tên/mã bưu chính trước khi lưu.
