# Kiến trúc rendering và batching trong Unity URP

Cập nhật 13/10/2026. Baseline kiểm chứng của plugin: Unity 6000.0.58f2, URP 17.0.4. Tài liệu này thay thế sơ đồ diễn giải cũ; không phải mô tả chi tiết toàn bộ implementation của Unity.

## Chọn đường render theo điều kiện

Một frame có thể chứa nhiều đường render, nhưng các kỹ thuật không độc lập hoàn toàn và không mặc định cộng dồn trên cùng renderer. Unity có điều kiện tương thích và ưu tiên giữa các kỹ thuật. Cần kiểm tra phiên bản pipeline, renderer, shader, material, pass và cấu hình đang có hiệu lực.

| Kỹ thuật | Vai trò | Điều không được suy ra chỉ từ settings |
|---|---|---|
| SRP Batcher | Giảm chi phí CPU khi chuẩn bị render giữa các material phù hợp | Không bảo đảm giảm số draw call; text CBUFFER hoặc comment generator không chứng minh shader compile tương thích |
| GPU instancing | Gộp instance đáp ứng điều kiện | Bật checkbox không chứng minh renderer dùng instancing; giới hạn phụ thuộc API và dữ liệu |
| Static batching | Chuẩn bị geometry phù hợp để giảm chi phí gửi render | Không bảo đảm một draw/material; có chi phí memory và cả đường tạo batch runtime |
| Dynamic batching | Xử lý geometry trên CPU khi đủ điều kiện | Vertex count đơn lẻ không đủ; còn vertex attributes và chi phí CPU |
| GPU Resident Drawer | Đường GPU-driven dựa trên BatchRendererGroup trong URP được hỗ trợ | Không bảo đảm 1–2 draw call; không đồng nhất với một lời gọi DrawMeshInstancedIndirect trong source |
| Đường thông thường | Render những đối tượng không đi theo tối ưu tương ứng | Không bảo đảm một draw/renderer: submesh, pass và shadows có thể tạo thêm công việc |

Xem [Unity 6 — lựa chọn phương pháp tối ưu draw call](https://docs.unity3d.com/6000.0/Documentation/Manual/optimizing-draw-calls-choose-method.html). Thông tin này là điều kiện thiết kế, không phải kết quả benchmark của game.

## GPU Resident Drawer

Đối chiếu cấu hình URP, renderer Forward+, nền tảng/API và compute support theo phiên bản trước khi đề xuất GRD. GPU occlusion là lựa chọn cần đo riêng, không phải yêu cầu bật vô điều kiện. Device capability phải lấy từ thiết bị mục tiêu; GPU của Editor không thay thế nó. Settings shader stripping/BRG phải đọc từ đúng asset sở hữu, không suy ra từ mức logging shader.

Dùng [hướng dẫn GRD cho Unity 6](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/gpu-resident-drawer.html) và [điều kiện tương thích object](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/make-object-compatible-gpu-rendering.html). Xác nhận đường thực thi bằng Frame Debugger, bao gồm Hybrid Batch Group khi áp dụng; không suy ra GRD chỉ vì có indirect draw.

## Cách plugin sử dụng tài liệu

- Đọc effective pipeline của Quality đang chọn; null Quality override có nghĩa kế thừa default.
- Chỉ báo On/Off khi đọc được giá trị; không đọc được phải Unknown.
- Các lựa chọn chiến lược thay thế là advisory; không bắt vừa bật vừa tắt static batching.
- Shader compatibility chưa có dữ liệu compile là Unknown. Auto-rewrite CBUFFER bị tắt.
- Đo CPU/GPU frame time và chất lượng hình ảnh trước/sau trên thiết bị trước khi chấp nhận tối ưu.

Catalog và trạng thái kiểm chứng nằm trong `knowledge/rules.json`; lịch sử danh mục nguồn nằm trong `docs/REFERENCE_AUDIT.md`.
