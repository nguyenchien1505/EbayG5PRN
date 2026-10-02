# ID — Tên task

Status: Planned | Ready | InProgress | Blocked | Done

## Mục tiêu và phạm vi

Một kết quả nghiệm thu được; ghi rõ việc không thuộc task nếu dễ mở rộng nhầm.

## Dependency và đầu vào tối thiểu

- Task trước; bằng chứng commit/test cần xác minh.
- Requirement IDs, contract, ADR và source cần đọc. Không dẫn agent đọc cả repo.

## Thay đổi và contract

- File/module dự kiến; DTO/schema/event/state bị ảnh hưởng.
- Những quyết định đã chốt; câu hỏi cần giải quyết trước khi code phần phụ thuộc.

## Acceptance và kiểm tra

- [ ] Kết quả nghiệp vụ và edge case liên quan.
- [ ] Lệnh kiểm tra cụ thể và kết quả mong đợi; không ghi pass trước khi chạy.
- [ ] Tài liệu, traceability và handoff cập nhật.

## Kết quả

Commit/bằng chứng; test pass/fail/chưa chạy; câu hỏi; việc còn lại. Không ghi SHA tự tham chiếu của commit chứa chính file.
