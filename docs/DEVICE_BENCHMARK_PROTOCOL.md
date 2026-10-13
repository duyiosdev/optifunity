# Quy trình benchmark thiết bị — Mốc B

Trạng thái: chưa có game/thiết bị/scenario do người dùng xác nhận. Không dùng Windows fixture để nghiệm thu hiệu năng máy yếu.

## Chuẩn bị một cặp đo

- Ghi game/build trước và sau, thiết bị cụ thể, OS, Unity, graphics API, Quality, target FPS và ngân sách **resident memory**. Dùng cùng thiết bị, input replay, scene, độ phân giải, trạng thái pin/nguồn, điều kiện nhiệt, mạng và cache.
- Định nghĩa warmup và thời lượng đo trước khi chạy; tối thiểu nhiều lần lặp độc lập cho mỗi build. Chọn target/budget theo yêu cầu sản phẩm; plugin không tự áp một ngưỡng để chứng nhận mọi game.
- Chỉ gắn `Optifunity.Runtime.DeviceEvidenceRecorder` vào scene/harness đo khi chủ động muốn capture. Đặt `scenario`, `variant`, `targetFps`, `memoryBudgetBytes`, `warmupSeconds`, `maxSamples`; gọi `Begin()` sau khi cấu hình và `StopAndSave()` để ghi JSON trong `Application.persistentDataPath`. Recorder không tự chạy và không gửi dữ liệu.
- Gọi `AddLoadingMeasurement(name, elapsedMilliseconds)` từ đồng hồ monotonic quanh đúng thao tác loading cần đo. Dùng tên duy nhất cho mỗi interval trong một capture. Không suy loading từ frame hitch.
- RSS/resident và nhiệt độ cần nguồn đo thực được đồng bộ với sample. Dùng `AddExternalMetrics(residentBytes, thermalCelsius, source)`; `-1` nghĩa chưa có metric đó. Ví dụ nguồn phải ghi rõ công cụ/API, đơn vị và sensor; nhiệt độ pin không mặc định là SoC/thermal throttling. Plugin chưa có collector Android/iOS tự động.

## Phân tích

Trong Evidence and Benchmarks, chọn hai capture JSON. Context khác bị từ chối; nguồn metric/thời lượng khác được cảnh báo. Công thức:

- Percentile nearest-rank: sắp xếp frame interval rồi lấy phần tử `ceil(p*N)-1` cho p50/p95/p99.
- Hitch: frame interval lớn hơn `2 * 1000 / targetFps`; báo cả count và tỷ lệ. Đây là định nghĩa của protocol, không là chuẩn chất lượng phổ quát.
- Peak memory/thermal là max của các sample đã có; không chứng minh peak nằm giữa hai sample đã được quan sát. Unity allocated không phải resident/RSS; chưa có resident thì không kết luận đạt ngân sách resident.
- Delta p95/loading là sau trừ trước, âm nghĩa số đo giảm. Một cặp capture không chứng minh quan hệ nhân quả; đối chiếu nhiều lần lặp, biến thiên và chức năng/chất lượng hình ảnh.

Gắn findings vào capture bằng project/build/scenario, thời điểm và đường dẫn object/code; profiler marker inclusive ở nhiều thread có thể chạy đồng thời, không cộng để suy frame wall time. Report inventory/build có thời điểm riêng và có thể cũ hơn code hiện tại.

## Memory

Editor aggregate diff chỉ nhóm object đang được Editor nạp. Để phân tích heap, dùng nút native `.snap`, sau đó mở Unity Memory Profiler, import hai snapshot và Compare. Theo dõi retained objects, roots và vòng đời; tăng bytes đơn thuần không phải chứng minh leak. Không thu lặp snapshot trong đoạn timing benchmark vì capture làm thay đổi thời gian/bộ nhớ. Snapshot có thể lớn và chứa dữ liệu game nhạy cảm; giữ trong môi trường dự án.

## Điều kiện nghiệm thu còn mở

Cần game/device/scenario thật, capture trước–sau có thể tái lập, RSS/thermal/loading đủ nguồn, nhiều lần lặp, đối chiếu chất lượng/chức năng và chủ dự án chấp nhận kết quả. Chưa thỏa các điều kiện này thì trạng thái B vẫn là **công cụ đã triển khai, benchmark chưa nghiệm thu**.

Nguồn API/workflow đã đối chiếu ngày 13/10/2026:
- [Unity 6 BuildReport](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Build.Reporting.BuildReport.html): báo cáo quá trình build, không phải runtime memory.
- [Unity 6 RawFrameDataView](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Profiling.RawFrameDataView.html): đọc thread và raw samples.
- [Memory Profiler comparison](https://docs.unity3d.com/Packages/com.unity.memoryprofiler@1.1/manual/snapshots-comparison.html): workflow so sánh snapshot chính thức.
- [Addressables 2.3.16](https://docs.unity3d.com/Packages/com.unity.addressables@2.3/manual/index.html): roots cấu hình không thay cho layout nội dung build/local/remote.
