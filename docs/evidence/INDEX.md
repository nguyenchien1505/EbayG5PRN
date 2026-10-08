# Chỉ mục minh chứng

| ID | Task/requirement | Bằng chứng | Giới hạn |
|---|---|---|---|
| EV-A00-01 | A00, CON-03 | Git local: `git log --oneline --grep=A00` | Chưa có remote/CI |
| EV-A01-01 | A01, CON-04, CON-05 | `pwsh -NoProfile -File scripts/Verify-Planning.ps1`; [kết quả checkpoint](A00-A01.md) | Chỉ kiểm tra tài liệu và SQL nguồn, không test ứng dụng |

Minh chứng khảo sát A02: [EV-A02-01](A02.md). Chỉ hoàn tất bản đồ tham khảo; UI-01 vẫn Pending.

Minh chứng scaffold A03: [EV-A03-01](A03.md). Restore/build và HTTP smoke pass; Docker/DB/nghiệp vụ còn Pending.

Minh chứng infrastructure A04: [EV-A04-01](A04.md). SQL/Redis healthy, auth và volume persistence pass; schema ứng dụng/nghiệp vụ còn Pending.

Minh chứng database A05: [EV-A05-01](A05.md). Baseline/EF/DbUp và 8 test SQL pass; CON-01 xác minh tại A05, CON-02 Done. Chức năng nghiệp vụ/OPS còn Pending.

Trạng thái hiện tại xem [ma trận yêu cầu](../requirements/MATRIX.md); các đoạn dưới là minh chứng theo checkpoint.
Khi thêm minh chứng: ghi requirement ID, commit, môi trường, lệnh, pass/fail, artifact và giới hạn. Không đưa log tải lớn hay secret vào repo.

Minh chứng seed A06: [EV-A06-01](A06.md). 12 SQL cases pass, seed local created/rerun skipped; auth và SUP vẫn Pending.

Minh chứng CI A07: [EV-A07-01](A07.md). Local entry point/12 SQL tests/actionlint và GitHub Actions Ubuntu run pass, TRX đã upload; CD vẫn Pending.

Auth B01: [EV-B01-01](B01.md), 18 SQL/HTTP tests và browser journey pass, S1/L1 UI hoàn tất; SUP/UI toàn dự án Pending.

Review D01: [EV-D01-01](D01.md), 157 .NET +14 Node pass, review gắn OrderItem service/endpoint/SQL constraints/migration; G5-03 Pending tới UI D02.

Review UI D02: [EV-D02-01](D02.md), 161 .NET +14 Node, browser desktop/320px; G5-03 Done, G5-04 Pending D05.

Seller feedback D03: [EV-D03-01](D03.md), 177 .NET +14 Node, browser desktop/320px; G5-05 Pending D04.

Reputation page D04: [EV-D04-01](D04.md), 183 .NET +14 Node, browser desktop/320px; G5-05 Done.

Moderation D05: [EV-D05-01](D05.md), 190 .NET +14 Node, browser; G5-04 Done, SUP-05 phần review/feedback (dispute E05).

CD F07: [evidence F07](F07.md), runtime `f0b65b0`, CI37668281294/CD37669952216 success:
263 .NET +20 Node và artifact/archive checks; image SHA build hosted, deploy kind local,
2 web Ready, migration 0 mới, read-only TLS smoke; ephemeral runner deregister/không service.
**OPS-03 Done**. OPS-06/PERF-01 Pending F08.

Rolling F08: [evidence F08](F08.md), CI37678243173 attempt 2 success trên `ad0ce27`;
Full local 50 HTTP user +20 cặp/600s, 100k tin/10k review/1k dispute:
152.130 request không lỗi, 12.891 ACK đủ/một dòng SQL, recovery max 2,51s,
max HTTP p95 241,48ms/chat p95 27,98ms. Update/undo thay pod thật, journal giữ 14.
**OPS-06/PERF-01 Done**; single-node kind, image tương thích/cùng nghiệp vụ.

## Bổ sung G01 (08/10/2026): các minh chứng trước đây chỉ được dẫn từ MATRIX

| Task | Minh chứng | Requirement |
|---|---|---|
| B02 | [B02](B02.md) | SUP-01, SUP-05 (quyền admin) |
| B03 | [B03](B03.md) | SUP-02, UI-01 (shell) |
| B04 | [B04](B04.md) | SUP-02 |
| B05 | [B05](B05.md) | SUP-03 |
| B06 | [B06](B06.md) | SUP-04 |
| C01 | [C01](C01.md) | G5-01, G5-02 (schema) |
| C02 | [C02](C02.md) | G5-01, G5-02 (service/REST) |
| C03 | [C03](C03.md) | G5-01, G5-N01 (WebSocket/Redis) |
| C04 | [C04](C04.md) | G5-01, G5-N02 (UI) |
| C05 | [C05](C05.md) | G5-02, G5-N02, G5-N05 (khôi phục, trace) |
| D06 | [D06](D06.md) | G5-01, G5-03–05 (điểm vào) |
| E01 | [E01](E01.md) | G5-06 (schema/state machine) |
| E02 | [E02](E02.md) | G5-06, G5-N03 |
| E03 | [E03](E03.md) | G5-07 |
| E04 | [E04](E04.md) | G5-08 (tự động) |
| E05 | [E05](E05.md) | G5-08, SUP-05 |
| F01 | [F01](F01.md) | OPS-01, G5-N01 (HTTPS/WSS) |
| F02 | [F02](F02.md) | OPS-01, OPS-02, G5-N04 |
| F03 | [F03](F03.md) | G5-N05, journeys |
| F04 | [F04](F04.md) | G5-N01, OPS-05 (an ninh) |
| F05 | [F05](F05.md) | OPS-01, OPS-05, G5-N02, G5-N04, PERF-01 |
| F06 | [F06](F06.md) | OPS-06 (nền K8s) |
| G01 | [G01](G01.md) | **UI-01**, truy vết toàn ma trận; ảnh [ui/](ui/) |
| G02 | [G02](G02.md) | **DOC-01** (báo cáo/setup/demo/handover); OPS-04 Pending theo user |
| H01 | [H01](H01.md) | G5-03, G5-05 (quy tắc eBay, form R1) |
| H02 | [H02](H02.md) | G5-04, G5-05 (hồ sơ F2, About this seller), UI-01 |
