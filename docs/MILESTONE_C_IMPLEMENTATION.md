# Triển khai Mốc C

Ngày 14/10/2026. Baseline repository `4193d2f`. Mốc B đã có công cụ/fixture, benchmark game và thiết bị thật vẫn chưa nghiệm thu theo xác nhận của chủ dự án.

| Phần | Phạm vi C | Điểm nghiệm thu |
|---|---|---|
| Ưu tiên tối ưu | Evidence tier, impact/cost do người review ước lượng; tradeoff/source từ catalog; baseline và suppression có lý do | Không đổi score gốc, không xóa findings, không gọi absence là resolved; giữ thời điểm/scope baseline |
| Thay đổi có kiểm soát | Default Texture2D, giảm Max Size cho Android/iPhone bằng thao tác chủ động trong Unity | Preview không sửa asset; approval gắn đúng preview; source/meta/GUID guards; backup integrity, post-import verification, failure restore, rollback không đè thay đổi mới |
| AI assistant | MCP context có findings, catalog/source, review decisions, change history và captures chia sẻ chủ động | Cả Node/Python dùng project/instance authentication; không đọc file tùy ý, không tự gửi cloud, không có endpoint ghi; AI phải viện nguồn và giữ uncertainty |

C không bật lại Fix All/import hook/shader patch/MCP optimize. Thay đổi texture là giao dịch kỹ thuật có thể khôi phục; quyết định chất lượng và lợi ích hiệu năng cần review/đo riêng. Không tự áp dụng lên game, commit/push hoặc phát hành.

Đầu ra nghiệm thu: `MILESTONE_C_ACCEPTANCE.md`, artifact riêng `validation-c/`. Native mobile benchmark, provider-specific AI eval và crash/power-loss recovery chưa phải bằng chứng đạt chỉ nhờ unit tests.

## Kết quả

Gói công cụ C đã triển khai; 81/81 test Optifunity đạt trên cả hai fixture. Xem [nghiệm thu C](MILESTONE_C_ACCEPTANCE.md) và các giới hạn visual/device/model còn mở. Chưa commit/push/phát hành.
