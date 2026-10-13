# Knowledge tối ưu Unity URP cho thiết bị di động hạn chế

Cập nhật 13/10/2026. Đây là hướng dẫn có điều kiện; các ngưỡng và preset của plugin không phải giới hạn an toàn do Unity bảo đảm. Baseline triển khai A: Unity 6000.0.58f2 và URP 17.0.4.

## Bằng chứng trước khuyến nghị

Mỗi finding cần vị trí, rule, điều kiện áp dụng và cách kiểm chứng. Phân biệt thao tác xác nhận từ mã nguồn với nguyên nhân giật đo được trên thiết bị. Static analysis không tự chứng minh FPS, memory leak hay bottleneck. Khi thiếu dữ liệu, báo Unknown/Incomplete thay vì số 0 hoặc pass.

## C# và GC

Roslyn được dùng để phân biệt reference construction, array, value type và boxing conversion. Generic collection declaration không tự gây boxing; phải xét thao tác và kiểu được resolve. Comment, string và nhánh preprocessing không hoạt động không phải code thực thi. Lambda/local function có thể được gọi về sau nên không mặc định chạy cùng callback chứa nó.

[Microsoft — Boxing and unboxing](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/types/boxing-and-unboxing) là căn cứ cho rule `code.boxing`. Allocation không có nghĩa collection diễn ra ngay. Việc giữ object cần xét GC roots và lifetime; một vòng tham chiếu đơn thuần không chứng minh leak. Xem [GC fundamentals](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals); không áp dụng máy móc mô hình collector của .NET desktop cho Unity Mono/IL2CPP.

Event subscription chỉ mở một câu hỏi về publisher/subscriber lifetime; có hoặc thiếu `-=` trong cùng file không chứng minh cleanup đúng/sai trên mọi đường chạy. Coroutine cần xét lifecycle của GameObject/MonoBehaviour. `Camera.main` phải đối chiếu phiên bản API, không coi mọi lần gọi là FindWithTag. Các vấn đề lifetime/call graph sâu thuộc mốc B.

## Texture, mesh và audio

- Texture: xét platform override, GPU format support, alpha, mip chain và mục đích asset. Không chọn ASTC block size hoặc max size chỉ từ RAM. Kích thước file không phải runtime memory. NPOT không luôn giải nén hay luôn gấp đôi bộ nhớ.
- Phần trăm thiết bị hỗ trợ format trên thị trường không chứng minh một thiết bị yếu cụ thể có hỗ trợ. Xem [Android texture format targeting](https://developer.android.com/guide/playcore/asset-delivery/texture-compression) và [Unity platform formats](https://docs.unity3d.com/2021.3/Documentation/Manual/class-TextureImporterOverride.html).
- Mesh/texture Read/Write có thể cần cho truy cập CPU, xử lý runtime hoặc gameplay. Chỉ tắt sau khi kiểm tra usage; không tự tắt lúc import.
- Audio: cân bằng decoded memory, compression/decode CPU, I/O, loading latency và số voice. Streaming overhead không phải trần memory của clip. Ước tính PCM phải ghi sample rate/channel/sample representation; không thay số đo residency thực tế.
- Nén ảnh, mesh và mono audio có thể đổi chất lượng hoặc hành vi. Auto-fix các mục này bị tắt trong A; developer xem và đổi thủ công sau khi đánh giá.

## Memory và ngân sách

Phân biệt allocated, reserved và resident. Preset dựa trên RAM trong plugin là chính sách gợi ý, không phải lượng RAM hệ điều hành bảo đảm cấp cho app. Hướng dẫn tỷ lệ sử dụng bộ nhớ cần xét OS, app khác, peak, scene transitions và thiết bị. Xem [Unity Memory Profiler — memory on device](https://docs.unity3d.com/Packages/com.unity.memoryprofiler@1.1/manual/memory-on-device.html).

Không coi Android và iOS có cùng cơ chế giới hạn bộ nhớ. Để xác nhận cần capture trên thiết bị và thử tải dài; snapshot/diff/device profiles nâng cao thuộc B.

## Rendering và Profiler

Xem tài liệu kiến trúc rendering đi kèm. Không có ngân sách draw call/triangle phổ quát cho mọi máy yếu. HDR 32/64-bit mô tả format render target, không được đồng nhất với FP32 cho từng channel. Chất lượng hình ảnh và render features phải được đo theo scenario.

`Gfx.WaitForPresentOnGfxThread` có thể bao gồm chờ trình bày hoặc đồng bộ; không chứng minh GPU bottleneck nếu thiếu GPU timing. `GC.Alloc` không phải `GC.Collect`. Không tự dựng thời gian collection, không cộng trùng thời gian cha/con và không kết luận Render Thread từ capture chỉ Main Thread. Xem [Unity profiler markers](https://docs.unity3d.com/6000.0/Documentation/Manual/profiler-markers.html).

## Quản trị references

Danh mục 60 nguồn cũ được giữ tại `docs/REFERENCE_AUDIT.md` với trạng thái kiểm chứng. Các nguồn từ Unity 5.x, Unity 6.x, XR, diễn đàn và bài kinh nghiệm không được trộn thành một yêu cầu chung. Catalog `knowledge/rules.json` ánh xạ những rule đã có evidence contract; `legacy.unverified` giữ các detector cũ ở dạng advisory trong khi chưa có ánh xạ riêng.

Các tài liệu này không tuyên bố đã kiểm chứng toàn bộ 60 nguồn. Không biến nguồn chưa xác minh thành thao tác ghi hoặc kết luận chắc chắn.
