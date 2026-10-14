# Nghiệm thu công cụ Mốc C

Ngày 14/10/2026. Baseline `4193d2f`; thay đổi C chưa commit/push/phát hành. Mốc C bổ sung review ưu tiên, giao dịch texture có kiểm soát và giao diện evidence cho AI. Benchmark mobile của B vẫn chờ game/thiết bị/scenario từ chủ dự án.

## Phạm vi đã triển khai

| Phần | Hành vi | Giới hạn |
|---|---|---|
| Ưu tiên | Evidence tier → impact do người review ước lượng → cost; tradeoff/source từ catalog đi cùng finding | Không dự báo FPS; runtime counter không tự chứng minh bottleneck |
| Baseline | Fingerprint rule/module/path/location/title, thời điểm và coverage | Dời code có thể thành finding mới; absent không phải resolved |
| Suppression | Bắt buộc lý do; lưu cost/impact; vẫn hiển thị finding và giữ score gốc | State ràng buộc project path; không tự migrate quyết định sang project khác |
| Fix có kiểm soát | Giảm platform Max Size trên Default Texture2D trong Assets; preview → approval Unity → apply → import verification → history/rollback | Chỉ Android/iPhone; không sprite/normal/cubemap/Packages/linked asset; không xác nhận chất lượng/FPS |
| Recovery | Backup integrity, source/meta/GUID guards, atomic journal; import verification lỗi thì khôi phục metadata; rollback từ chối khi có edit mới | Backup nằm trong Library; crash/power loss giữa thao tác cần review thủ công, không tự ghi đè |
| AI | `unity_get_assistant_context` cho Node/Python, project/instance authentication, findings/catalog/source/review/history và session captures được chia sẻ chủ động | Không nhúng model, không tự gửi cloud, không API ghi; chưa đánh giá chất lượng suy luận của model thực |

Menu mới: **Tools → Optifunity → Optimization Review**. Workflow: [OPTIMIZATION_REVIEW_WORKFLOW.md](OPTIMIZATION_REVIEW_WORKFLOW.md).

## Nghiệm thu kỹ thuật

Kết quả cuối ở `validation-c/editmode-A.json` và `editmode-B.json`; chỉ coi đạt nếu artifact ghi Passed, zero failed/skipped. Fixture baseline Unity **6000.0.58f2 / URP 17.0.4 / Windows**; cấu hình B thêm Addressables **2.3.16** và Memory Profiler **1.1.11**.

Các regression C kiểm tra nội dung và tác động:

- Preview không đổi `.meta`; thiếu approval bị từ chối; apply đúng platform, giữ source và platform khác; rollback trả metadata về byte gốc; không apply lại giao dịch đã hoàn tất.
- Android và iPhone kiểm tra độc lập. Asset source/meta đổi, project khác, ID path traversal, platform/size không hợp lệ, texture chuyên biệt và backup hỏng đều không được ghi.
- Test postprocessor cố ý đổi Max Size trong lúc import: verification thất bại, metadata gốc được khôi phục và journal ghi thất bại đã restore.
- Baseline/suppression lưu và đọc lại, giữ finding, reason bắt buộc; absent baseline keys không gọi resolved. Evidence tier đứng trước impact ước lượng; không có lợi ích đo giả.
- Shared captures chỉ có sau Publish, có giới hạn kích thước/count và xóa được; context có nguồn boxing, scope/policy và chỉ dẫn coi tên/nội dung capture là dữ liệu không đáng tin.
- Cả MCP client Node/Python khởi chạy thật qua stdio, gọi context tới Unity HTTP thật, kiểm tra instance và nguồn rule; tool ghi cũ tiếp tục trả error. Regression auth/timeout/lifecycle A vẫn chạy cùng suite.
- Hai Editor thật: isolation, native reload, sourced assistant context sau readiness và shutdown cleanup đều đạt; xem `validation-c/live-bridge.json`. Lần kiểm tra đầu request quá sớm khi descriptor mới xuất hiện đã timeout; harness được sửa để đợi Editor sẵn sàng, rồi xác minh lại identity/context. Descriptor tồn tại không đảm bảo Editor đã phục vụ request ngay trong reload.
- Menu Optimization Review mở được trong Unity. Đây là smoke tự động, chưa là visual QA thủ công.

Không thay đổi asset game của người dùng khi triển khai: các thử nghiệm ghi chỉ chạy trong fixture có `.optifunity-fixture`, tạo asset riêng rồi dọn. Giao dịch sample/sanitized context và hash source được lưu ở `validation-c/` để đối chiếu. Không lưu secret descriptor/token vào artifacts.

## Policy và đánh đổi

Không bật lại `CanAutoFix`, Fix All, import hook, shader patch hoặc MCP `/optimize`. Giao dịch texture là đường thao tác mới, chủ động và có preview. Verification xác nhận importer setting và khả năng khôi phục trong fixture, không chứng minh một lựa chọn Max Size phù hợp game cụ thể. Issue không tự bị xóa sau apply; rescan và benchmark/visual review vẫn cần thiết.

Review state lưu trong `ProjectSettings/OptifunityReview.json`. Change journal/backup tại `Library/OptifunityChanges`; không xóa Library nếu còn cần rollback. Process bị ngắt lúc Applying/Rolling back để lại trạng thái chưa hoàn tất; không có khẳng định recovery tự động. Import postprocessor của game có thể có side effects ngoài metadata mà giao dịch này không bao phủ.

AI context phục vụ host/model do người dùng chọn. Catalog/source và policy là dữ liệu đầu vào, không bảo đảm model luôn tuân thủ. Không có provider-specific eval, prompt-injection red-team hoặc câu trả lời model thực được dùng làm bằng chứng nghiệm thu. Phần thực sự được kiểm chứng là đọc đúng context, provenance, identity và không có đường ghi MCP.

## Phần còn mở

- Benchmark trước–sau trên game/điện thoại, xác nhận visual quality, thermal/RSS/loading và chủ dự án nghiệm thu.
- Visual QA thủ công, game lớn, platform/Unity/package khác baseline; độ ổn định fingerprint khi refactor và chi phí report lớn.
- Phục hồi crash/power loss và mọi side effect từ custom import hooks của game.
- Fix family khác và AI provider/model evaluation; không mở rộng tự động từ giao dịch texture đầu tiên.

Nguồn API cho giao dịch đã đối chiếu ngày 14/10/2026: [TextureImporter.SetPlatformTextureSettings](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextureImporter.SetPlatformTextureSettings.html), [maxTextureSize](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextureImporterPlatformSettings-maxTextureSize.html), [SaveAndReimport](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetImporter.SaveAndReimport.html). Kết quả integration nằm trong fixture, không suy từ tài liệu rằng rollback/chất lượng game mặc định an toàn.
