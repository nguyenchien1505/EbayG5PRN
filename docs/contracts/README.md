# Contract theo module

Chưa có contract runtime hoàn chỉnh ở A00–A01. Đọc quy tắc đã duyệt trong PLAN mục 3 chỉ khi làm module liên quan; không tự coi DTO trong code kỳ trước là contract.

- B01–B02: Auth, quyền, lỗi, cookie/CSRF.
- B04–B06: Catalog, order/status và adapter mô phỏng.
- C01: REST/Hub chat, cursor, membership, clientMessageId và event ACK/reconnect.
- D01/D03: Review eligibility, uniqueness, feedback và công thức tổng hợp.
- E01: Dispute reason/state/actor, draft, active uniqueness, event và quá hạn.

Mỗi contract ghi input/output, authorization, validation, lỗi và hành vi retry/concurrency có liên quan. Không cần đọc contract module khác trừ dependency thực tế.
