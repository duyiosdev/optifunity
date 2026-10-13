# Nghiệm thu kỹ thuật Mốc A — Optifunity

Ngày: 13/10/2026. Baseline Git: `26a4677`; thay đổi đang nằm trong working tree, **chưa commit/push/phát hành**. Phạm vi triển khai: A01–A09 theo `UPGRADE_PLAN.md`; không triển khai B/C.

## Kết quả và điều kiện bàn giao

Đã triển khai các sửa đổi correctness, knowledge, báo cáo và an toàn của A. **Hai fixture A/B đều đạt 52/52 EditMode tests, 0 failed, 0 skipped**; log không có lỗi compiler/import trong các mẫu đã kiểm tra. **4 nhóm kiểm tra live bridge đều đạt**, gồm hai Editor, từ chối sai identity/mutation, native reload và shutdown cleanup. Kết quả máy chạy cuối được lưu trong [validation](validation/); chỉ coi kiểm tra đạt khi artifact tương ứng ghi `Passed`. Chủ dự án chưa nghiệm thu sản phẩm. Kiểm tra UI hiện là smoke tự động trong Unity có thiết bị đồ họa; chưa đánh giá bố cục bằng mắt hoặc thao tác dài trên game thực tế.

| Hạng mục | Đầu ra và bằng chứng |
|---|---|
| A01 | Unity **6000.0.58f2**, URP **17.0.4**, Test Framework **1.4.5**, Windows. Hai fixture độc lập import/compile và chạy EditMode; kiểm tra MonoScript của Core/URP resolve đúng. Roslyn 3.8 có dependency pin, provenance/hash và license. Không quảng bá Unity 2021.3 hoặc phiên bản khác là supported. |
| A02 | Hai tài liệu knowledge được sửa; 8 rule có applicability, evidence, nguồn, tradeoff, test và policy. Catalog thiếu metadata/nguồn cho rule không phải advisory bị từ chối. 60 references cũ được giữ trong inventory; chưa tuyên bố tất cả nội dung đã được kiểm chứng. |
| A03 | Scope, completion/cancel, Quality/pipeline/target và thời điểm từng module đi cùng report/export. Quét một module giữ kết quả module khác. Rule chưa xác minh là Info/advisory; score chỉ có ý nghĩa với nhóm code đã quét, không chứng nhận FPS. |
| A04 | Effective Quality pipeline, texture/audio platform overrides, GRD prerequisites, counter 0 khác lỗi đọc; thiếu adapter ghi Unknown. Static batching/GRD là các chiến lược có điều kiện, không là hai mệnh lệnh mâu thuẫn. |
| A05 | Roslyn semantic model đọc source/defines/references từ assembly Unity; fixture có asmdef tham chiếu asmdef khác. Struct, boxing, event, loop, comment, lambda, unresolved được kiểm tra. Không có whole-program lifetime/call graph. |
| A06 | Không coi allocation là collection, kể cả biểu đồ category; không bịa GC time hoặc kết luận GPU từ wait/draw count. Capture reset/target change xóa trạng thái; cùng frame index không trả cache cũ. Main Thread và độ sâu 0–10 được ghi rõ. |
| A07 | Vô hiệu hóa mọi asset/shader auto-fix qua issue/UI/import hook/MCP. Không có fix tự động nào được giữ lại; rollback cho fix mới thuộc C. |
| A08 | Descriptor theo PID, endpoint động, token + project + instance; hai client đọc lại descriptor mỗi call. Queue có hạn và timeout; dọn descriptor/queue khi reload/quit. Kiểm tra client Node/Python thật qua stdio và HTTP trong Editor. Kiểm tra hai Editor/reload bằng `ValidateBridge.py`. |
| A09 | Regression, Dashboard full/module scan, filter, Markdown export, số đo fixture và tài liệu được đồng bộ. UI smoke tự động không thay cho visual QA và canary trên game thật. |

## Ma trận T01–T22

Tên test đầy đủ và kết quả ở `validation/editmode-A.json` và `editmode-B.json`. Các test logic chạy bên trong Unity; không đánh đồng dữ liệu Profiler tổng hợp với capture thiết bị.

| ID | Bằng chứng / giới hạn |
|---|---|
| T01–T02 | `T01_T02_ArrayAllocation`, `T02_Vector4DoesNotAllocate`; actual asmdef fixture có class/struct/array. |
| T03–T04 | `T03_BoxingUsesResolvedConversion`, `T04_GenericDeclarationIsNotBoxing`, `T04_UnresolvedTypeIsAdvisory`, `ActualAsmdefReferencesAndDefinesResolve`. |
| T05 | `T05_ArithmeticIsNotEvent`, `T05_EventIsAdvisoryNotLeakProof`; subscription không phải chứng minh leak. |
| T06–T07 | `T06_LoopDoesNotSpill`, comments/strings/disabled code/nested lambda và compilation defines thật. |
| T08 | Adapter UnityStats thật và missing property; unavailable capture khác zero. Không phải benchmark Play Mode/device. |
| T09 | Quality override và null inheritance với URP asset thật, khôi phục settings sau test. |
| T10 | Import texture và WAV, default/platform overrides; kiểm tra kết quả auditor sử dụng giá trị effective. |
| T11–T12 | Graph không đưa yêu cầu static batching mâu thuẫn; unknown capability/setting không ghi Pass. |
| T13–T15 | Frame 100 ms thiếu nguyên nhân vẫn Unknown; wait không chứng minh GPU; GPU timing hỗ trợ hypothesis; GC collection dùng thời gian đo trong fixture, không suy từ bytes. |
| T16 | Reused frame index và capture reset; không kiểm thử mọi dạng file `.data` hoặc kết nối điện thoại thật. |
| T17 | Cancel/incomplete, module retention, full→module Dashboard và export có cùng coverage. |
| T18 | Policy không cung cấp action và không đánh dấu issue đã sửa. **Rollback N/A vì không giữ auto-fix nào**, không tuyên bố rollback engine đã tồn tại. |
| T19 | Hai Editor chạy đồng thời ở hai thư mục fixture. Request token/project/instance sai bị chặn. Trường hợp cùng path dùng request giả mạo instance trên cùng endpoint; Unity khóa việc mở cùng project hai lần, không tuyên bố chạy hai process cùng path. |
| T20 | Timeout thật khi tạm dừng queue; manual lifecycle regression và native script reload; JSON qua serializer với locale `vi-VN`. |
| T21 | Shader chưa xác minh không được coi tương thích; mọi patch/importer mutation bị khóa, không dựa vào text để sửa Read/Write/shader. |
| T22 | Filter match là thông tin; renderer đơn giản không mặc định cần LOD; Info không trừ điểm. |

## Cách tái lập

1. Cài Node dependency bằng `npm ci --prefix McpServer`.
2. Chạy `Tools~/Validate.ps1` với `-UnityPath`, `-PythonPath` và `-ProjectPath` ngoài package. Lặp lại với thư mục fixture thứ hai. Công cụ trả PID ngay; chờ `results.xml` và Editor thoát trước bước tiếp theo.
3. Chạy `python Tools~/ValidateBridge.py <Unity.exe> <fixture-A> <fixture-B> <output.json>` sau khi hai bộ test đã thoát.
4. Kiểm tra log không có lỗi compile/import, kết quả không có failed/skipped, và `live-bridge.json` ghi Passed.

Fixture chứa hai source script và hai asmdef; asset/scene regression tạo tài nguyên tạm rồi dọn trong test. Project phải có `.optifunity-fixture`; công cụ từ chối project không thuộc quyền quản lý. Fixture phải nằm ngoài package local: đặt project bên trong package đã gây lỗi ánh xạ MonoScript URP, dù compiler và nhiều unit test vẫn chạy được. Đây là lý do có kiểm tra import riêng và chặn layout đó.

## Số đo và giới hạn

`scan-metrics-A.json` / `scan-metrics-B.json` lưu thời gian một lần quét code, managed memory trước/sau và coverage. GC có thể chạy giữa hai lần đọc: delta **không phải peak memory, không phải allocation chính xác và không phải mức tiết kiệm**. Fixture rất nhỏ; không suy rộng thời gian này cho game sản xuất. Chưa có phép đo trước/sau phiên bản cũ đủ tương đương để kết luận hiệu năng plugin cải thiện.

- Chưa benchmark điện thoại, FPS/thermal/loading/memory thực; không cam kết game chạy tốt trên máy yếu chỉ từ report A.
- Chưa visual QA thủ công, kiểm thử game lớn, native macOS/Linux, Unity khác hoặc mọi tổ hợp SDK có thể xung đột Roslyn.
- Chưa bao phủ toàn bộ Packages, unopened scenes/prefabs, sub-assets, Addressables/build graph, nhiều thread hoặc ownership/lifetime.
- Nguồn chưa đối chiếu vẫn ghi chưa xác minh. Legacy findings chỉ là gợi ý; giảm số Warning sau A không đồng nghĩa hiệu năng game tăng.
- Mốc B: inventory/build/runtime/device evidence. Mốc C: verified fix preview/rollback và AI assistant. Không tự kích hoạt các hạng mục này.

Các giới hạn visual QA và canary game thật cần được xem khi chủ dự án chấp nhận kết quả; checklist chủ dự án vẫn để mở.
