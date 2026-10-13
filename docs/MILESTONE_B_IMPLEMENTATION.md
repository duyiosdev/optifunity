# Mốc B — triển khai công cụ và fixture

Baseline: `57b4c25`, ngày 13/10/2026. Người dùng đã yêu cầu triển khai B và xác nhận chưa có game/thiết bị benchmark.

| Gói | Phạm vi triển khai | Nghiệm thu |
|---|---|---|
| B1 | Inventory Assets/Packages, scene/prefab/sub-assets, dependency và Addressables khi có adapter; đối chiếu BuildReport | Fixture có sub-assets và dependency, scope/cancel/error rõ; build evidence tách khỏi dự đoán |
| B2 | Call graph cho lời gọi resolve được; allocation qua helper và kiểm tra ownership/event cleanup có giới hạn | Corpus positive/negative; không tuyên bố chứng minh leak hoặc phân giải mọi dynamic dispatch |
| B3 | Usage rendering từ scene/prefab: Canvas/UI, particle, camera, material/shader/keyword và URP renderer features | Fixture usage thật; variant đang dùng khác variant cuối sau stripping |
| B4 | Evidence nhiều thread; memory snapshot/counter diff; recorder frame/device/scenario | Fixture cùng thread/frame, kiểm tra metric/diff/context, kết quả thiếu dữ liệu giữ Unknown |
| B5 | Công cụ so sánh trước/sau: percentile, hitch, memory, loading và thermal có nguồn | Dữ liệu fixture tái lập; benchmark game/thiết bị thật CHƯA NGHIỆM THU do chưa có đầu vào |

Tích hợp qua cửa sổ evidence, export JSON/Markdown và luồng quét code hiện có. Không sửa asset game, không auto-fix, không tự build game, không tự gửi dữ liệu. Không triển khai C/AI, commit/push/phát hành.

Kết quả kiểm thử và giới hạn cập nhật tại `MILESTONE_B_ACCEPTANCE.md`. Chỉ đánh dấu hoàn tất toàn bộ B sau benchmark thiết bị và nghiệm thu các giới hạn.

## Kết quả bàn giao

Công cụ đã triển khai và có fixture nghiệm thu; xem [MILESTONE_B_ACCEPTANCE.md](MILESTONE_B_ACCEPTANCE.md). B chưa nghiệm thu toàn bộ: chưa có game/thiết bị và vẫn có giới hạn call graph, Addressables layout, native heap workflow được ghi rõ. Mốc C và commit/push chưa thực hiện.
