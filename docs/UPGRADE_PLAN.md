# Kế hoạch nâng cấp Optifunity và triển khai mốc A

- Ngày lập: 13/10/2026.
- Trạng thái: **Đã triển khai Mốc A; xem báo cáo nghiệm thu và giới hạn còn mở**.
- Baseline repository: `26a4677`.
- Phạm vi hiện tại: người dùng đã duyệt triển khai Mốc A. Kết quả kỹ thuật tại [MILESTONE_A_ACCEPTANCE.md](MILESTONE_A_ACCEPTANCE.md); chưa commit/push/phát hành.

Mục tiêu sản phẩm là tích hợp vào Unity, rà soát mã nguồn, tài nguyên và cấu hình game; sử dụng knowledge đã kiểm chứng và AI khi cần để đưa ra giải pháp tối ưu cho thiết bị di động đời thấp. Kết luận phải có bằng chứng, điều kiện áp dụng và cách đo lại trên thiết bị mục tiêu.

Ưu tiên triển khai: **A — sửa tính đúng đắn và chuẩn hóa knowledge; B — mở rộng độ phủ và đo trên thiết bị; C — hoàn thiện sửa lỗi an toàn và AI**. Mốc A tạo nền tảng đáng tin cậy, chưa chứng nhận hiệu năng của một game trên thiết bị thực.

## 1. Phạm vi đề xuất để chốt mốc A

| Hạng mục | Quyết định đề xuất |
|---|---|
| Kết quả đầu ra | Kết luận chính xác trong phạm vi được hỗ trợ; dữ liệu thiếu phải hiển thị rõ |
| Kiến trúc | Giữ các module hiện tại; sửa nền tảng chung và các bộ kiểm tra đang sai |
| Knowledge | Kiểm kê hai tài liệu knowledge và toàn bộ danh mục 60 references; kiểm chứng nội dung quyết định các rule của A |
| Phiên bản | Đề xuất Unity 6000.0.58f2 làm baseline đầu tiên; chốt chính xác phiên bản URP bằng fixture ở A01 |
| Phiên bản khác | Chỉ ghi Supported sau khi import, compile và EditMode tests đạt; nếu giữ Unity 2021.3 phải có fixture và minimum patch tương ứng |
| C# analysis | Sửa nhóm allocation, boxing và nhận diện cú pháp/phạm vi; phân tích toàn bộ luồng chương trình nằm ở B |
| Auto-fix | Tạm vô hiệu hóa fix chưa chứng minh an toàn; các fix giữ lại phải kiểm tra lại kết quả |
| MCP/AI | Sửa định danh project, quyền ghi và vòng đời request; chưa mở rộng thành AI agent |
| Điều kiện hoàn thành | A01–A09 và checklist nghiệm thu đạt; không còn lỗi P1 đã xác nhận trong phạm vi A |

Chốt mốc A nghĩa là triển khai A01–A09 theo tài liệu này. Các mục B/C chưa nằm trong gói A. Commit, push và phát hành cần quyết định riêng; việc duyệt kế hoạch không tự động cho phép các thao tác đó.

## 2. Baseline và khoảng trống đã xác định

Review ban đầu (trước triển khai A) dựa trên đọc mã nguồn, kiểm tra tài liệu chính thức, các tình huống tái hiện tách biệt và metadata API của Unity đã cài. Chưa chạy toàn bộ plugin trong Unity hoặc benchmark trên điện thoại. Những kiểm tra này không thay thế nghiệm thu tích hợp ở A09.

Các nhóm vấn đề cần giải quyết trước:

- C# analyzer có thể bỏ sót allocation do cách đặt dấu ngoặc, coi giá trị struct là heap allocation, nhầm cast reference thành boxing và nhầm phép cộng gán thành event subscription.
- Profiler có thể phân loại frame chậm nhưng thiếu bằng chứng thành Balanced; nhận diện marker chưa đầy đủ, gộp allocation với collection và suy diễn thời gian GC.
- Đọc thống kê bằng reflection có tên thành viên không khớp; thất bại có thể trở thành số 0 và vẫn báo đã capture.
- URP và platform overrides chưa được giải quyết nhất quán; có khuyến nghị batching mâu thuẫn hoặc thiếu điều kiện phiên bản.
- Fix texture, Read/Write và shader có nguy cơ thay đổi hành vi; danh sách lỗi có thể được xóa trước khi xác nhận đã sửa thành công.
- MCP dùng endpoint cố định, chưa gắn chặt request với project/Editor; cần tránh tác động nhầm instance và thực thi ghi muộn sau timeout.
- Phạm vi Assets, scene đang mở và bộ lọc chưa tương đương toàn project/build; điểm sức khỏe chưa phản ánh đúng giới hạn đó.
- Knowledge có các khẳng định quá rộng về bộ nhớ, texture, batching, GC và ngân sách thiết bị; danh mục nguồn chưa phải một hệ thống rule có phiên bản và trạng thái kiểm chứng.

## 3. Nguyên tắc vận hành sau nâng cấp

1. Phân biệt số đo bằng 0 với không đọc được dữ liệu; phân biệt Unsupported, Not applicable và Incomplete.
2. Mỗi phát hiện có rule ID, vị trí/bằng chứng và mức kết luận: đã xác nhận, nghi vấn hoặc hướng dẫn cần đo thêm.
3. Mỗi báo cáo ghi Unity/URP, platform, Quality, phạm vi quét, thời điểm, phần đã quét và phần bị bỏ qua.
4. Static analysis không được tự nhận đã chứng minh bottleneck runtime, memory leak hoặc FPS đạt mục tiêu.
5. Ngân sách thiết bị phải có điều kiện áp dụng; RAM không đại diện đầy đủ cho GPU, CPU, băng thông hay nhiệt độ.
6. Frame chậm chưa xác định nguyên nhân phải giữ trạng thái chưa xác định.
7. Chỉ đánh dấu đã sửa sau khi đọc lại hoặc chạy lại phép kiểm tra phù hợp.
8. Ưu tiên thay đổi nhỏ trong module hiện có; không xây framework rule tổng quát hoặc thêm dependency khi chưa cần.

## 4. Nâng cấp knowledge và references

### 4.1. Quy trình kiểm chứng

1. Lập inventory cho từng claim có thể tác động đến chẩn đoán hoặc thao tác sửa: vị trí tài liệu, nguồn, phiên bản, rule sử dụng.
2. Phân loại: đúng; đúng có điều kiện; lỗi thời; sai; chưa đủ bằng chứng.
3. Kiểm kê toàn bộ 60 references. Link còn truy cập được không đồng nghĩa nội dung đã được xác minh. Mỗi nguồn ghi rõ mức kiểm tra và nội dung hỗ trợ.
4. Ưu tiên tài liệu Unity, Android, Microsoft hoặc nhà cung cấp liên quan. Nguồn cộng đồng chỉ bổ sung kinh nghiệm, không tự trở thành yêu cầu bắt buộc.
5. Sửa hai tài liệu gốc và README; tách chức năng đã có với roadmap. Claim chưa kiểm chứng không được điều khiển cảnh báo bắt buộc hoặc auto-fix.

Mốc A yêu cầu kiểm chứng đầy đủ các claim được dùng bởi rule thuộc A. Các nguồn còn lại phải được kiểm kê và gắn trạng thái rõ; không ghi toàn bộ references đã đúng nếu chưa đọc và đối chiếu nội dung.

### 4.2. Những nội dung phải chỉnh

| Nhóm | Yêu cầu hiệu chỉnh |
|---|---|
| Bộ nhớ | Phân biệt resident/allocated/reserved; hướng dẫn 70% không phải bảo đảm an toàn cho mọi app; preset ngân sách phải ghi là chính sách của plugin |
| Texture | Phân biệt kích thước file với runtime memory; xét format GPU hỗ trợ, platform overrides và điều kiện NPOT; tránh blanket rule “luôn giải nén/gấp đôi” |
| C#/GC | Phân biệt struct/reference, allocation/collection, generic/non-generic; xét GC roots, lifetime và coroutine; vòng tham chiếu đơn thuần không chứng minh leak |
| Rendering | Mô tả điều kiện và ưu tiên giữa batching/SRP Batcher/instancing/GRD theo phiên bản; không bảo đảm số draw call cố định |
| Profiler | Marker đúng phiên bản; wait không tự chứng minh GPU bottleneck; tránh cộng trùng thời gian cha/con hoặc suy diễn số đo |
| Audio | Xét memory, CPU, I/O, loading và số voice; overhead không phải tổng ngân sách clip |
| Mobile budgets | Không đặt ngưỡng draw call, polygon, texture hay FPS phổ quát; ghi thiết bị, scenario và phép đo để xác nhận |
| API Unity | Ví dụ Camera.main, FindObjectsByType và GraphicsSettings phải khớp phiên bản hỗ trợ |

### 4.3. Catalog rule tối thiểu

| Trường | Nội dung |
|---|---|
| `ruleId`, `revision`, `category` | Định danh ổn định, revision và nhóm |
| `sources`, `verifiedAt`, `verificationStatus` | Nguồn, ngày kiểm chứng và trạng thái |
| `applicability`, `exclusions` | Unity/URP, platform, capability và trường hợp loại trừ |
| `requiredEvidence` | Dữ liệu tối thiểu để đưa ra kết luận |
| `severity`, `evidenceLevel` | Mức nghiêm trọng và độ chắc chắn có căn cứ; không dùng phần trăm confidence chưa hiệu chuẩn |
| `recommendation`, `tradeoffs` | Hành động và đánh đổi về chất lượng/hành vi/chi phí |
| `verificationSteps`, `fixPolicy` | Cách đo lại; advisory, manual hoặc fix có kiểm soát |

Dùng dữ liệu đơn giản trong repo, định dạng tương thích parser sẵn có của Unity. Logic detector tiếp tục ở C# và có test; không xây ngôn ngữ rule mới. Mỗi finding ánh xạ được về catalog hoặc được ghi rõ là trạng thái vận hành.

## 5. Backlog triển khai mốc A

### A01 — Chốt compatibility và fixture nghiệm thu

- Kiểm tra README, package manifest, assembly definitions và API đang dùng; xác định phiên bản Unity/URP thực sự hỗ trợ.
- Tạo hoặc chỉ định Unity fixture riêng, có version lock và dữ liệu mẫu; không trộn dữ liệu thử nghiệm vào source package.
- Đề xuất baseline Unity 6000.0.58f2; phiên bản URP phải được xác nhận từ fixture. Nếu cần giữ 2021.3, xác định minimum patch qua API thực tế và chạy fixture riêng.
- **Đầu ra:** ma trận Supported/Untested/Unsupported, hướng dẫn chạy, baseline import/compile/EditMode.
- **Nghiệm thu:** mọi cấu hình ghi Supported có kết quả chạy thực; README và package manifest không hứa rộng hơn ma trận.

### A02 — Chuẩn hóa knowledge và catalog rule

- Thực hiện mục 4; lập ánh xạ claim → source → rule → detector → test.
- Kiểm tra ID trùng, metadata bắt buộc và rule không có nguồn/điều kiện áp dụng.
- Sửa hai tài liệu knowledge hiện có cùng README; giữ lịch sử thay đổi qua Git.
- **Đầu ra:** catalog, inventory references, tài liệu đã sửa và danh sách nội dung chưa xác minh.
- **Nghiệm thu:** rule của A có nguồn phù hợp phiên bản và evidence contract; claim chưa xác minh chỉ ở dạng advisory.

### A03 — Thống nhất bằng chứng, phạm vi và báo cáo

- Sửa `Editor/Core`, runner và UI/export để giữ trạng thái đọc dữ liệu, phạm vi và mức hoàn tất.
- Phân biệt full scan, module scan, lọc My Scripts, canceled và partial; không xóa kết quả module khác ngoài ý định thao tác.
- Không trừ điểm cho thông tin tích cực hoặc đối tượng chỉ khớp bộ lọc. Nếu giữ HealthScore, định nghĩa là mức tuân thủ rule trong phạm vi đã quét, không phải bảo đảm hiệu năng.
- **Đầu ra:** contract kết quả chung và thông tin coverage hiển thị được.
- **Nghiệm thu:** UI và export nhất quán; thiếu dữ liệu không hiển thị như pass hoặc số đo 0.

### A04 — Đọc đúng settings, counters và applicability

- Sửa `Module2_AssetAuditor`, `Module4_URPDiagnostics`, `Module7_MobileWorkflow` tại các đường đọc liên quan.
- Giải quyết effective Render Pipeline theo Quality; đọc texture/audio overrides theo platform mục tiêu.
- Sửa adapter EditorStats và batching API; reflection thất bại phải trả trạng thái rõ, không đoán tên trường hoặc giá trị mặc định.
- GRD/batching phải có điều kiện Unity/URP/capability và chiến lược áp dụng; không biến lựa chọn thay thế thành hai yêu cầu bắt buộc mâu thuẫn.
- **Nghiệm thu:** thay đổi Quality/platform cho kết quả tương ứng; null Quality override kế thừa đúng; cấu hình không hỗ trợ trả Unsupported/Not applicable.

### A05 — Sửa nhóm C# analysis gây sai kết luận

- Đánh giá sớm khả năng đóng gói và tương thích Roslyn với các assembly Unity/IDE hiện có; chốt dependency trước khi mở rộng.
- Dùng syntax/semantic information phù hợp cho allocation, boxing, generic type và phân biệt event với toán tử số học; xét compilation references và defines.
- Sửa lỗi phạm vi method/loop, ngoặc khác dòng, comment và string; khi không resolve được symbol phải giảm mức kết luận.
- Dùng fixture C# hợp lệ, có cả trường hợp phải bắt và không được bắt. Không dùng đoạn mã không compile làm bằng chứng cho semantic correctness.
- **Nghiệm thu:** T01–T07 đạt, không xung đột assembly trong fixture Unity.
- **Giới hạn:** phân tích interprocedural, toàn bộ ownership/lifetime và chứng minh leak thuộc B. Nếu semantic integration chưa đạt, ghi A05 chưa hoàn tất hoặc xin điều chỉnh phạm vi rõ ràng; không đổi tên regex thành Roslyn để coi là xong.

### A06 — Sửa chẩn đoán Profiler và cache

- Sửa `Module5_SpikeAnalyzer`: mapping marker theo phiên bản, phân biệt GC allocation/collection và loại bỏ thời gian suy diễn.
- Không dùng wait marker đơn lẻ để khẳng định GPU bound; frame chậm thiếu bằng chứng phải Unknown/Inconclusive.
- Không kết luận Render Thread từ dữ liệu chỉ đọc Main Thread; ghi rõ giới hạn capture.
- Cache gắn với capture/target và frame, vô hiệu hóa khi capture đổi/reset; thống nhất ngưỡng spike giữa các đường chạy.
- **Nghiệm thu:** T13–T16 đạt; mọi số hiển thị truy được tới dữ liệu hoặc được ghi là ước lượng có công thức.
- **Giới hạn:** phân tích nhiều thread đầy đủ, Memory Profiler diff và capture trên thiết bị mở rộng ở B.

### A07 — Chặn fix không an toàn và xác minh fix giữ lại

- Kiểm kê toàn bộ đường ghi: import hook, từng nút Fix, Fix All, shader patch và MCP.
- Chuyển fix chưa chứng minh an toàn thành advisory. Không áp dụng chung mipmaps, ASTC 8x8, max size 1024 hoặc Read/Write Off cho mọi asset/platform.
- Fix giữ lại phải có điều kiện áp dụng, xem trước giá trị cũ/mới, đúng platform, phục hồi khi lỗi và rescan/readback.
- Shader patch chưa chứng minh compile và tương thích thì vô hiệu hóa. Không coi instancing buffer hay comment của generator là bằng chứng SRP compatibility.
- **Nghiệm thu:** UI và MCP cùng tuân thủ policy ở lớp thực thi; fix thất bại không làm issue biến mất; rollback khôi phục dữ liệu đã thay đổi trong phạm vi hỗ trợ.

### A08 — Ràng buộc MCP đúng project và vòng đời request

- Sửa `Editor/Core/UnityMcpBridge` cùng cả client Node/Python trong `McpServer`.
- Discovery/handshake có project identity, Editor instance identity và token phiên; request phải chỉ rõ đích và được xác thực trước khi đọc/ghi.
- Request hết hạn khi đang chờ không được bắt đầu mutation sau đó; phân biệt timeout với kết quả chưa xác định nếu mutation đã bắt đầu, không tự retry thao tác ghi.
- Dọn listener/queue khi reload/quit; xử lý cancel và timeout; dùng JSON serializer thay nối chuỗi, số không phụ thuộc locale.
- Phân biệt thông tin máy chạy Editor với target build settings.
- **Nghiệm thu:** hai Editor kể cả cùng đường dẫn project không bị nhầm; token/identity sai bị từ chối; cả hai client có kiểm tra tích hợp; các đường ghi tuân thủ A07.

### A09 — Nghiệm thu tích hợp và đồng bộ tài liệu

- Chạy bộ test hiện có và regression của A trong Unity fixture; kiểm tra menu, full/module scan, filters, UI và export.
- Ghi thời gian quét và mức sử dụng bộ nhớ trên fixture cố định để phát hiện suy giảm; không lấy đó làm bằng chứng game chạy nhanh hơn.
- Cập nhật README về khả năng thực tế, phiên bản hỗ trợ, giới hạn scope và cách xác nhận trên thiết bị.
- **Đầu ra:** báo cáo nghiệm thu ghi commit, Unity/URP, fixture, kết quả và phần chưa kiểm tra; danh sách chuyển B/C.
- **Nghiệm thu:** checklist mục 7 đạt; kết quả standalone không được ghi thành Unity integration đã pass.

## 6. Ma trận regression bắt buộc

| ID | Tình huống | Kết quả cần chứng minh |
|---|---|---|
| T01 | Ngoặc method cùng dòng/khác dòng | Kết quả tương đương |
| T02 | `new int[]` và `new Vector4` | Nhận diện allocation đúng ngữ cảnh; không mặc định struct là heap allocation |
| T03 | Cast reference/value type sang object | Không nhầm reference cast với boxing |
| T04 | Generic/non-generic, symbol trùng tên hoặc unresolved | Phân biệt theo symbol; unresolved không kết luận chắc chắn |
| T05 | Phép cộng gán số, event subscribe/unsubscribe | Không coi mọi `+=` là event hoặc chứng minh leak chỉ bằng chuỗi |
| T06 | Loop kết thúc và method tiếp theo | Trạng thái phạm vi không lan sang method khác |
| T07 | Comment, string, multiline, compilation defines | Không cảnh báo từ nội dung không thực thi; áp dụng đúng cấu hình |
| T08 | Counter thật bằng 0 và lỗi đọc counter | Hai trạng thái khác nhau |
| T09 | Default RP, Quality override và null override | Dùng effective pipeline đúng |
| T10 | Texture/audio default và platform override | Phân tích/sửa đúng platform mục tiêu |
| T11 | Static batching và chiến lược GRD | Không có hai yêu cầu bắt buộc mâu thuẫn |
| T12 | Thiếu field, API, version hoặc capability | Unknown/Unsupported/Not applicable đúng nghĩa |
| T13 | Frame 100 ms không đủ dữ liệu nguyên nhân | Không báo Balanced như một kết luận tốt |
| T14 | Wait marker hiện hành; GPU timing thiếu/thấp; VSync | Không tự suy ra GPU bound |
| T15 | Có GC.Alloc nhưng không có GC.Collect | Không tạo sự kiện collection hoặc thời gian GC giả |
| T16 | Hai capture có cùng frame index | Không tái sử dụng cache của capture trước |
| T17 | Cancel, partial, module scan và filters | Báo coverage đúng; không nhận là toàn project |
| T18 | Fix lỗi, chạy lại và rollback | Issue chỉ được xử lý khi xác minh; dữ liệu khôi phục đúng |
| T19 | Hai Editor, kể cả cùng project path | Request chỉ chạy trên instance đã chọn |
| T20 | Timeout/reload, tên có ký tự JSON, locale số | Không ghi muộn từ queue hết hạn; JSON hợp lệ |
| T21 | Shader include, asset cần Read/Write, texture đặc thù | Fix chưa đủ điều kiện bị chặn |
| T22 | Settings hợp lệ và object chỉ khớp filter | Không bị tính là lỗi hoặc trừ điểm |

Test phải kiểm tra nội dung kết luận và bằng chứng, không chỉ kiểm tra “có issue”. Fixture phải khôi phục settings/preferences sau chạy và không gây tác động project khác.

## 7. Checklist nghiệm thu mốc A

- [x] Ma trận phiên bản ghi Supported đã pass import, compile và EditMode tests thực tế.
- [ ] A01–A09 có đầu ra; T01–T22 được ánh xạ trong báo cáo. Còn visual QA thủ công; same-path native Editor và rollback được ghi N/A có giải thích, chưa chủ dự án chấp nhận giới hạn.
- [x] Không còn lỗi P1 đã xác nhận trong phạm vi A: tác động nhầm project, làm hỏng dữ liệu hoặc kết luận sai nghiêm trọng ở luồng đã hỗ trợ.
- [x] Rule của A truy được nguồn, điều kiện áp dụng và evidence; nội dung chưa xác minh được gắn advisory.
- [x] Hai tài liệu knowledge, catalog và README nhất quán; references có trạng thái kiểm chứng.
- [x] Unknown, Unsupported, Not applicable và Incomplete không bị coi là pass.
- [x] Không còn đường UI/import/MCP gọi được fix chưa được phép theo policy.
- [x] Hai MCP client đã kiểm tra tích hợp đúng project/instance và vòng đời request.
- [x] Báo cáo nghiệm thu có baseline, phiên bản, kết quả, giới hạn và backlog B/C.
- [ ] Chủ dự án chấp nhận kết quả và các giới hạn được ghi rõ.

Mốc A không có tiêu chí “game đạt FPS trên mọi máy yếu”. Tiêu chí đó cần cấu hình thiết bị, scenario và đo thực tế trong B.

## 8. Thứ tự triển khai và điểm dừng

| Đợt | Công việc | Điểm chốt |
|---|---|---|
| 1 | A01, A02 nền tảng; kiểm kê và chặn đường ghi nguy hiểm của A07; feasibility A05 | Chốt fixture, compatibility, mẫu catalog và khả năng semantic integration |
| 2 | A03, A04 | Contract kết quả và dữ liệu settings/counters đáng tin cậy |
| 3 | A05, A06 | Regression C# và Profiler đạt |
| 4 | Hoàn thiện A07, A08 | Fix và MCP vượt các tình huống lỗi/nhầm instance |
| 5 | A09 | Checklist nghiệm thu hoàn tất, trình chủ dự án chốt |

Mỗi đợt giao thay đổi nhỏ, kết quả kiểm tra và giới hạn còn lại. Nếu phát hiện yêu cầu mở rộng kiến trúc ngoài A, ghi vào B/C; không âm thầm mở rộng phạm vi. Chưa ấn định ngày hoàn thành trước khi A01 và đánh giá semantic integration ở A05 xác định khối lượng thực.

**Điểm dừng của gói A:** bàn giao báo cáo nghiệm thu; không tự triển khai B/C, commit/push hoặc phát hành package.

## 9. Lộ trình tiếp theo: B và C

**Cập nhật 13/10/2026:** người dùng đã yêu cầu triển khai B và xác nhận chưa có game/thiết bị. Công cụ và fixture B được triển khai; xem [nghiệm thu B](MILESTONE_B_ACCEPTANCE.md). Benchmark mobile và các giới hạn nghiệm thu còn mở; không đánh dấu toàn bộ B hoàn tất. C chưa triển khai.

| Mốc | Hạng mục | Đầu ra/điều kiện nghiệm thu dự kiến |
|---|---|---|
| B | Inventory project và build | Scene/prefab/sub-assets/Packages/Addressables theo scope được khai báo; đối chiếu BuildReport thay vì chỉ suy đoán dependency |
| B | C# chuyên sâu | Call graph và lifetime/ownership cho nhóm rule cần thiết; đánh giá false positive/false negative trên corpus |
| B | Rendering chuyên sâu | UI, particles, shaders/variants, cameras và renderer features gắn với usage thực tế |
| B | Runtime evidence | Nhiều thread, memory snapshot/diff và device profile; kết nối findings với số đo |
| B | Đo trước/sau trên thiết bị | Frame-time percentiles, hitch, peak/resident memory, loading và thermal trong scenario cố định |
| C | Ưu tiên tối ưu | Xếp hạng theo bằng chứng, chi phí, tác động và đánh đổi; baseline/suppression có lý do |
| C | Fix nâng cao | Preview, kiểm chứng, rollback và lịch sử cho nhóm thay đổi đã chứng minh an toàn |
| C | AI assistant | Công cụ đọc evidence/knowledge/capture đúng project; đề xuất có nguồn và chia sẻ policy ghi với UI/MCP |

Thiết bị, target FPS, ngân sách memory và scenario phải được chốt trước khi đo B. Không cam kết phần trăm cải thiện từ số lượng issue giảm. AI không thay thế detector, profiler hoặc kiểm chứng trên thiết bị.

## 10. Rủi ro và cách kiểm soát

| Rủi ro | Cách xử lý |
|---|---|
| API khác giữa Unity/URP | Ma trận phiên bản có fixture; adapter có trạng thái unsupported |
| Roslyn xung đột assembly hoặc chi phí tích hợp lớn | Đánh giá sớm ở đợt 1; pin phiên bản tương thích; không tuyên bố semantic analysis khi chưa đạt |
| Sửa knowledge làm thay đổi nhiều cảnh báo | Lưu mẫu báo cáo trước/sau, giải thích rule thay đổi; ít issue hơn không đồng nghĩa FPS tốt hơn |
| Vô hiệu hóa auto-fix làm tăng thao tác thủ công | Hiển thị hướng dẫn và điều kiện rõ; chỉ bật lại sau bằng chứng an toàn |
| Fixture nhỏ không đại diện game thật | A xác minh correctness; B kiểm tra project và thiết bị thực |
| Marker/counter không tồn tại | Giữ trạng thái thiếu dữ liệu; không thay bằng giá trị 0 hoặc thời gian tự suy diễn |

## 11. Nguồn chính thức cho việc hiệu chỉnh knowledge

Các nguồn dưới đây đã được dùng trong review nền tảng. A02 vẫn phải đối chiếu từng claim với phiên bản plugin hỗ trợ; danh sách này không thay cho inventory 60 references.

- [Unity Memory Profiler — Memory on device](https://docs.unity3d.com/Packages/com.unity.memoryprofiler@1.1/manual/memory-on-device.html)
- [Android — Texture compression format targeting](https://developer.android.com/guide/playcore/asset-delivery/texture-compression)
- [Unity 2021.3 — Importing textures](https://docs.unity3d.com/2021.3/Documentation/Manual/ImportingTextures.html)
- [Unity 2021.3 — GraphicsSettings.currentRenderPipeline](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Rendering.GraphicsSettings-currentRenderPipeline.html)
- [Unity 2021.3 — Optimizing draw calls](https://docs.unity3d.com/2021.3/Documentation/Manual/optimizing-draw-calls.html)
- [Unity 6 — Choose a draw call optimization method](https://docs.unity3d.com/6000.0/Documentation/Manual/optimizing-draw-calls-choose-method.html)
- [Unity 6 URP — GPU Resident Drawer](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/gpu-resident-drawer.html)
- [Unity 6 URP — GPU rendering compatibility](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/make-object-compatible-gpu-rendering.html)
- [Unity 6 URP — URP Asset settings](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/universalrp-asset.html)
- [Unity 6 — Profiler markers](https://docs.unity3d.com/cn/6000.0/Manual/profiler-markers.html)
- [Unity 2021.3 — Coroutines](https://docs.unity3d.com/2021.3/Documentation/Manual/Coroutines.html)
- [Unity 2021.3 — Camera.main](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Camera-main.html)
- [Unity 2021.3.18f1 — Release notes](https://unity.com/ja/releases/editor/whats-new/2021.3.18f1)
- [Microsoft — Fundamentals of garbage collection](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals)

## 12. Các quyết định cần ghi nhận khi duyệt

| Quyết định | Trạng thái |
|---|---|
| Duyệt gói A01–A09 và điểm dừng mục 8 | Đã duyệt qua yêu cầu “Triển khai Mốc A” |
| Compatibility chính thức | Đã kiểm tra Unity 6000.0.58f2 / URP 17.0.4 / Windows |
| Cách tích hợp semantic analysis | Roslyn 3.8 tích hợp với compilation context Unity; regression đạt |
| Thiết bị và scenario benchmark | Thuộc B |
| Chấp nhận kết quả A | Sau báo cáo A09 |
| Commit/push/phát hành | Theo chỉ định riêng của chủ dự án |