# Nghiệm thu Mốc B — công cụ và fixture

Ngày: 13/10/2026. Baseline: `57b4c25` (Mốc A đã được lưu trước lượt này). Thay đổi B chưa commit/push/phát hành. Người dùng đã xác nhận **chưa có project game, thiết bị và scenario benchmark**.

## Trạng thái bàn giao

Công cụ B được triển khai trong phạm vi dưới đây. **Chưa nghiệm thu toàn bộ Mốc B trên game/thiết bị thật**. Không tuyên bố FPS/memory/thermal của game đã cải thiện. Native heap graph comparison sử dụng workflow Memory Profiler chính thức; Optifunity không tự suy leak từ aggregate diff.

Menu: **Tools → Optifunity → Evidence and Benchmarks**; Dashboard Build có nút dẫn tới đây. Kết quả có thể xuất JSON. Code scan cũ vẫn giữ phạm vi A; nút code trong cửa sổ evidence bật helper/ownership scan.

| Gói | Đã triển khai | Phạm vi chưa được chứng minh |
|---|---|---|
| B1 Inventory/build | Assets/Packages tùy chọn, GUID/local ID cho imported sub-assets, direct dependencies, scene/prefab paths; packed entries từ BuildReport có target/thời điểm; Addressables roots từ settings đã lưu | Không chứng minh remote catalog/bundle layout, dynamic loading hoặc mọi variant cuối; BuildReport có thể cũ, bytes đóng gói không phải resident RAM |
| B2 Code sâu | Theo lời gọi helper resolve được trong cùng compilation, chống vòng lặp, giới hạn 512 method; phát hiện allocation ở helper và bằng chứng event/handler removal | Không whole-program dynamic dispatch, reflection, cross-assembly body traversal hoặc chứng minh cleanup chạy/leak tồn tại; findings là advisory |
| B3 Rendering usage | Preview scene/prefab riêng, Camera/Canvas/CanvasRenderer/ParticleSystem/Renderer; material/shader/keywords, URP renderer features cấu hình | Không đo overdraw/rebuild/variant stripping hoặc khẳng định feature thực sự chạy trong camera; ghi rõ serialized configuration |
| B4 Runtime/memory | Raw frame nhiều thread, tối đa 256 thread và 200.000 samples/thread, top 20 marker inclusive; Editor object-type aggregate/diff; tạo native `.snap`; mở Memory Profiler để compare graph | Thread thời gian chạy đồng thời không được cộng thành wall time. Aggregate diff không phải managed heap graph. Import/Compare `.snap` trong GUI chưa được kiểm tra bằng mắt |
| B5 Benchmark tooling | Recorder opt-in, warmup/scenario/build/device metadata; p50/p95/p99, hitch count/rate, sampled peaks, signed loading delta, kiểm tra context/provenance và dữ liệu không hợp lệ | Collector RSS/thermal Android/iOS chưa tự động tích hợp; cần nguồn đo ngoài. Chưa có cặp mobile trước–sau hoặc nhiều lần lặp |

## Bằng chứng nghiệm thu

Artifacts trong `validation-b/` là bằng chứng có thể đọc lại; không copy snapshot heap lớn vào Git.

- Hai fixture Unity **6000.0.58f2 / URP 17.0.4 / Windows**: bộ Optifunity **71/71**, không failed/skipped. Fixture mở rộng có thêm test stub Addressables nên tổng suite **72/72**. Xem `editmode-A.json`, `editmode-B.json` cho tên và thời gian từng test.
- Fixture A không cài Addressables/Memory Profiler; fixture B cài **Addressables 2.3.16**, **Memory Profiler 1.1.11**. Adapter được kiểm tra cả absence và persisted settings có entry thật; file settings không bị thay đổi khi scan. Đọc default-settings getter đã bị tránh vì code package có nhánh migration ghi asset.
- Corpus helper gồm 6 tình huống: 2 positive (helper trực tiếp/qua helper), 4 negative (struct, không gọi, lambda chưa thực thi, recursive helper không allocation). Trên corpus nhỏ này: 2 TP, 4 TN, 0 FP, 0 FN. Không suy độ chính xác trên code game từ 6 mẫu. Ownership có case có/không có handler removal; vẫn không kết luận leak.
- Rendering fixture lưu prefab với Camera/Canvas/particles, quét rồi đối chiếu bytes file không đổi. Inventory fixture có material + Mesh sub-asset, unique local ID; có cancellation và no-BuildReport coverage.
- Build Windows Player fixture thật thành công với `DetailedBuildReport`; reader lấy được **2.831 packed entries**. Đây là build thử trong project tạm, không build game của người dùng.
- Player fixture ghi **60 frame samples**, tự dừng và lưu JSON ngoài Editor. Chạy headless/Null graphics chỉ xác minh recorder/data flow, không là benchmark rendering/mobile.
- Native Editor Profiler capture đọc **97 thread**; snapshot native tạo thành công **787.879.819 bytes**. Diff object aggregate dùng hai lần đọc thật. Receipt không chứng nhận managed graph đã được phân tích.
- UI smoke mở cửa sổ Evidence và menu Memory Profiler khi package có mặt. Chưa visual QA thủ công.

Các con số fixture native cụ thể gắn với lần thu trong receipt; phiên bản mã cuối được ghi hash riêng. Nếu build/recorder được chạy lại, xem artifact cuối để lấy session/bytes/frame count tương ứng thay vì suy từ lời mô tả.

## Tái lập

1. Cài Node dependency như Mốc A. Chạy `Tools~/Validate.ps1` với Unity/Python path và project tạm ngoài package. Chạy fixture thứ hai với `-ExtendedPackages` để cài đúng các phiên bản optional ở trên. Đợi Editor thoát trước lần tiếp theo.
2. Native build: mở fixture bằng Unity batch mode, gọi `-executeMethod Optifunity.Tests.BFixtureHarness.Build -quit`. Harness từ chối project không có `.optifunity-fixture`, tạo scene/build bên trong fixture.
3. Chạy `BPlayer/OptifunityFixture.exe -batchmode -nographics -optifunity-b-probe -logFile <path>`; log chỉ ra capture dưới persistentDataPath. Probe không chạy nếu thiếu cờ riêng hoặc đang trong Editor.
4. Native thread/memory: gọi `-executeMethod Optifunity.Tests.BRuntimeHarness.Run` trên fixture, không `-quit`; harness tự thoát sau callback capture. Có thể tạo snapshot lớn; thực hiện ngoài khoảng timing benchmark.
5. Với game/thiết bị thật, theo [DEVICE_BENCHMARK_PROTOCOL.md](DEVICE_BENCHMARK_PROTOCOL.md). Không coi fixture là thay thế.

## Điểm còn mở và rủi ro

- B chưa được nghiệm thu trên mobile: cần game/device, target FPS/RAM, scenario, nhiều lần đo trước–sau, nguồn RSS/thermal/loading và kiểm tra chất lượng/chức năng.
- Scan inventory có thể nạp nhiều imported objects; chưa đo latency/peak RAM trên game lớn. Rendering preview có thể kích hoạt hành vi Editor của script dự án; scanner không gọi save/patch game assets.
- Phân tích C# có giới hạn nêu trên; absence of finding không chứng minh code không allocation/leak. Các trường hợp ngoài scope không được coi Pass.
- Memory Profiler GUI comparison cần import hai snapshot và kiểm tra roots/retention; chưa tích hợp parser heap riêng hoặc xuất mọi graph findings tự động.
- Package versions khác, macOS/iOS/Android native execution và thermal collector chưa kiểm tra. Chưa visual QA thủ công.
- Native runtime log có thông báo môi trường licensing/cleanup khi Editor thoát; capture callback/file receipt đã xác nhận thành công. Không dùng thông báo đó để khẳng định game có lỗi memory.

Mốc C (AI, ưu tiên tối ưu, verified auto-fix/rollback) chưa triển khai. Không thay đổi policy read-only của Mốc A; mọi auto-fix vẫn bị khóa.
