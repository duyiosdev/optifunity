# Optifunity — Unity URP Performance Analysis Plugin

[![Unity 2021.3+](https://img.shields.io/badge/Unity-2021.3%2B-black.svg)](https://unity3d.com)
[![URP](https://img.shields.io/badge/Pipeline-URP-blue.svg)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Modules](https://img.shields.io/badge/Modules-8-brightgreen.svg)](#tổng-quan)

> **Hệ thống plugin phân tích hiệu năng tự động** cho Unity URP.  
> Phát hiện bottleneck, audit assets, và tự động phân loại frame spike ngay trong Editor.

---

## Tổng Quan

Optifunity tích hợp **8 phân hệ**:

| # | Phân Hệ | Phạm Vi | Trigger |
|---|---------|---------|---------|
| 1 | **Roslyn Code Analyzer** | GC Alloc, Boxing, Memory Leak + anti-pattern + resource management trong C# | Scan thủ công |
| 2 | **Asset Auditor** | Texture / Mesh / Audio theo tiêu chuẩn platform | Scan thủ công + Auto khi import |
| 3 | **Shader/Materials** | Render pipeline PC vs Mobile, SRP Batcher, Draw Calls | Scan thủ công |
| 4 | **🔬 Spike Analyzer** | Tự động phân tích bottleneck khi chọn frame trong Profiler | **Real-time, tự động** |
| 5 | **📦 Build Analyzer** | Quét dependency build để liệt kê Material/Shader thực sự đi vào build | Scan thủ công |
| 6 | **URP Diagnostics** | Đọc URP Asset và đề xuất setting theo target platform | Scan thủ công |
| 7 | **📱 Mobile Render Pipeline** | 6 pipeline renderer song song: GRD, SRP Batcher, GPU Instancing, Static/Dynamic Batching, Fallback | Full Scan |
| 8 | **🧩 Scene Renderer Audit** | Lọc renderer trong scene theo MPB, GPU Instancing, Static Batching, LOD, XR Motion, DOTS Instancing | Scene Scan / Full Scan |

---

## Cài Đặt

### Unity Package Manager (Git URL) — Khuyến nghị

1. **Window → Package Manager**
2. Nhấn **`+`** → **Add package from git URL...**
3. Dán URL:

```text
https://github.com/duyiosdev/optifunity.git#main
```

> Khuyến nghị phát hành theo tag để ổn định version cho team, ví dụ: `#v1.0.0`.

### Yêu Cầu

| Yêu cầu | Phiên bản |
|---------|-----------|
| Unity | 2021.3 LTS trở lên |
| Render Pipeline | Universal Render Pipeline (URP) |

---

## Hướng Dẫn Sử Dụng Nhanh

### Dashboard

```text
Tools > Optifunity > Dashboard    (Ctrl+Shift+O)
```

Nhấn **▶ Full Scan** để chạy các scan tĩnh chính (Code + Assets + URP).

### My Scripts Filter (quan trọng)

Trong toolbar của Dashboard, bật nút **📌 My Scripts** để chỉ scan các thư mục code của bạn, tránh quét toàn bộ package/plugin bên thứ ba.

**Thiết lập folder cho My Scripts:**
1. Mở Dashboard
2. Bật **⚙ Config**
3. Vào mục **My Scripts — Inclusion Folders**
4. Nhấn **+ Add Folder** để thêm các thư mục muốn scan (ví dụ `Assets/Scripts`, `Assets/Game`)
5. (Tuỳ chọn) Nhấn **✕** để bỏ folder không dùng

### Spike Analyzer *(tính năng mới)*

```
Tools > Optifunity > Spike Analyzer
```

1. Mở **Window → Analysis → Profiler** và bắt đầu record
2. Chạy game vào Play Mode
3. Click vào **bất kỳ frame nào** trong Profiler Timeline (đặc biệt các spike frame màu đỏ)
4. **Spike Analyzer tự động phân tích và hiển thị kết quả ngay lập tức**

---

## Kiến Trúc

```
d:\Optifunity\
├── package.json
├── Runtime/
│   └── MemoryBudget.cs                    ScriptableObject ngân sách RAM
└── Editor/
    ├── Core/
    │   ├── PlatformConfig.cs              Cấu hình platform + 70% RAM rule
    │   └── ReportEngine.cs                Tổng hợp issues, HealthScore, Export
    ├── Module1_RoslynAnalyzer/
    │   ├── GCAllocAnalyzer.cs             Phát hiện GC trong Update()
    │   ├── BoxingAnalyzer.cs              Phát hiện boxing value types
    │   ├── MemLeakAnalyzer.cs             Phát hiện memory leak patterns
    │   ├── PerformanceAntiPatternAnalyzer.cs  Find/GetComponent, Camera.main, tag compare...
    │   ├── ResourceManagementAnalyzer.cs      Resources.Load, event leak, Instantiate/Destroy trong loop
    │   └── CodeAnalysisRunner.cs
    ├── Module2_AssetAuditor/
    │   ├── TextureAuditor.cs              Compression, POT, MaxSize, MipMap
    │   ├── MeshAuditor.cs                 Read/Write, Anim Compression, LOD
    │   ├── AudioAuditor.cs                Streaming threshold, Vorbis, Force Mono
    │   ├── AssetAuditRunner.cs
    │   └── AssetPostprocessorHook.cs      Auto-apply khi import
    ├── Module4_URPDiagnostics/
    │   ├── URPAssetScanner.cs             Đọc URP Asset properties
    │   ├── URPRecommendationEngine.cs     Ma trận PC vs Mobile
    │   ├── SRPBatcherChecker.cs           CBUFFER compatibility
    │   └── DrawCallAnalyzer.cs            SetPass/Draw Call bottleneck
    ├── Module5_SpikeAnalyzer/
    │   ├── ProfilerSample.cs              Data models (tree + lookup)
    │   ├── FrameDataReader.cs             HierarchyFrameDataView reader
    │   ├── BottleneckClassifier.cs        Bottleneck checkers + confidence
    │   ├── SpikeDetector.cs               Poll profiler + rolling buffer
    │   ├── SpikeAnalysisRunner.cs         Orchestrator + cache
    │   ├── EditorCodeLocator.cs           Map profiler sample -> script/shader path
    │   └── ProfilerHelper.cs              Cross-version selected-frame helper
    ├── Module6_BuildAnalyzer/
    │   └── BuildAssetScanner.cs           Quét dependency build (Materials/Shaders)
    ├── Module7_MobileWorkflow/
    │   ├── MobileWorkflowGraphData.cs       Data model cho 6 pipeline renderer song song
    │   ├── MobileWorkflowGraphBuilder.cs    Build report: GRD/SRP/Instancing/Static/Dynamic/Fallback
    │   ├── MobileWorkflowRunner.cs          Orchestrator cho Mobile Render Pipeline report
    │   ├── MobileWorkflowAnalyzer.cs        Chuyển setting checks thành issue list
    │   ├── ProjectSettingsMismatchAnalyzer.cs  Phát hiện lệch Project Settings vs URP
    │   └── Unity6VulkanGuidanceAnalyzer.cs     Guidance Unity 6 GRD + Vulkan path
    └── UI/
        ├── OptifunityStyles.cs            Dark theme styles
        ├── DashboardWindow.cs             8-tab main window (gồm Module 7 + Module 8)
        ├── ReportWindow.cs                Filter/sort/export report viewer
        └── SpikeAnalyzerWindow.cs         Timeline + diagnosis + root-cause UI
```

---

## Phân Hệ Chi Tiết

### Module 1: Code Analyzer

Phát hiện các pattern nguy hiểm bằng Regex trên toàn bộ `.cs` trong `Assets/` (GC/Boxing/MemLeak + anti-pattern + resource management):

| Pattern | Ví dụ | Severity |
|---------|-------|---------|
| Allocation trong Update | `var list = new List<int>()` | **ERROR** |
| String concat trong Update | `name + " HP"` | **WARNING** |
| Return new array trong loop | `return new int[n]` | **ERROR** |
| Lambda capture | `() => DoSomething(localVar)` | **WARNING** |
| Boxing: object cast | `object o = (object)myVector` | **ERROR** |
| Non-generic collection | `ArrayList`, `Hashtable` | **ERROR** |
| Event += không có -= | `+=` trong OnEnable, không có OnDestroy | **ERROR** |
| Coroutine `while(true)` | Coroutine không có exit condition | **ERROR** |
| Singleton giữ scene ref | `Instance.player = GameObject.Find(...)` | **WARNING** |

### Module 2: Asset Auditor

**Tiêu chuẩn nén Texture theo nền tảng:**

| Platform | Tier RAM | Format | Block Size |
|----------|----------|--------|-----------|
| Android | ≤ 2GB (Low) | ASTC | 8×8 / 10×10 |
| Android | ≤ 4GB (Mid) | ASTC | 6×6 |
| Android | > 4GB (High) | ASTC | 4×4 |
| iOS | Tất cả | ASTC | 4×4 ~ 6×6 |
| PC | — | BC7 / DXT5 | — |

**Ngân sách RAM (70% Rule):**

| Android RAM | Available Budget | Texture | Mesh | Managed Heap |
|------------|-----------------|---------|------|-------------|
| 2 GB | 1,434 MB | 80 MB | 215 MB | 287 MB |
| 4 GB | 2,867 MB | 150 MB | 430 MB | 573 MB |
| 6 GB+ | 4,300 MB+ | 250 MB | 645 MB | 860 MB |

### Module 3: Shader/Materials

**Ma trận khuyến nghị PC vs Mobile:**

| Setting | PC | Mobile |
|---------|-----|--------|
| SRP Batcher | ✅ Bật | ✅ **Bắt buộc bật** |
| HDR Rendering | ✅ FP32 | ❌ Tắt |
| Opaque Texture | ✅ Nếu cần | ❌ Tắt nếu không dùng |
| Depth Texture | ✅ Nếu cần | ❌ Tắt nếu không dùng SSAO |
| Shadow Cascade | 4 levels | ≤ 2 levels |
| Additional Shadows | ✅ OK | ❌ Tắt |
| MSAA | 4× | ≤ 2× hoặc FXAA |

### Module 4: Spike Analyzer 🔬 *(MỚI)*

Tự động phân loại **11 loại bottleneck** khi click bất kỳ frame nào trong Profiler:

| # | Spike Type | Marker Chính | Ngưỡng |
|---|-----------|-------------|--------|
| 1 | **GC Collection** | `GC.Collect`, `GC.Alloc` | Alloc > 10KB/frame |
| 2 | **GPU-Bound** | `Gfx.WaitForPresent` | > 30% frame time |
| 3 | **CPU Render Thread** | `Camera.Render` | > 8ms (khi Gfx.Wait thấp) |
| 4 | **Physics** | `Physics.Processing` | > 5ms |
| 5 | **Scripting** | `BehaviourUpdate` | > 10ms |
| 6 | **UI / Canvas** | `Canvas.BuildBatch` | > 3ms |
| 7 | **Animation** | `Animator.Update` | > 3ms |
| 8 | **Asset Loading** | `Loading.ReadObject` | Có mặt = CRITICAL ⚠ |
| 9 | **Audio** | `AudioManager.Update` | > 2ms |
| 10 | **VSync Wait** | `WaitForTargetFPS` | > 5ms |
| 11 | **Job System Stall** | `JobSystem.WaitForJob` | Stall detected |

**Tính năng SpikeAnalyzerWindow:**
- **Timeline chart** 300 frames — click để phân tích, Shift+Click để set Compare frame
- **Auto-Follow** — tự động cập nhật khi chọn frame trong Profiler
- **Frame Diagnosis** — tóm tắt workload CPU/GPU, spike ratio, dominant contributors và điểm cần kiểm tra đầu tiên
- **6 analysis tabs**: Bottleneck | Root Cause | Samples | GC Alloc | Rendering | Compare
- **Frame Comparison** — so sánh side-by-side 2 frames bất kỳ

---

### Module 7: Mobile Render Pipeline 📱

Module 7 không còn xem Unity renderer như một workflow tuyến tính. Thay vào đó,
nó hiển thị **6 pipeline tối ưu độc lập** mà Unity có thể chọn theo từng
renderer/material/scene context:

| Pipeline | Ý nghĩa | Cách xác minh |
|----------|---------|---------------|
| **GRD** | GPU Resident Drawer / BatchRendererGroup / indirect draw path | URP Asset + Frame Debugger |
| **SRP Batcher** | Giảm CPU cost cho SetPass/state changes, không trực tiếp giảm draw call | URP Asset + shader compatibility |
| **GPU Instancing** | Batch object cùng Mesh + Material bằng `DrawMeshInstanced` | Material `Enable GPU Instancing` + Scene Audit |
| **Static Batching** | Combine object static để giảm draw setup | Player Settings + renderer static state |
| **Dynamic Batching** | Legacy CPU batching cho mesh nhỏ | URP Asset + mesh nhỏ trong Scene Audit |
| **Fallback** | Draw path thường khi renderer không match các path tối ưu | Frame Debugger + Scene Audit |

**Cách sử dụng:**

1. Mở `Tools > Optifunity > Dashboard`.
2. Nhấn **▶ Full Scan**.
3. Vào tab **📱 Render Pipeline**.
4. Chọn filter `All`, `GRD`, `SRP`, `GPU Instancing`, `Static`, `Dynamic`, hoặc `Fallback`.
5. Click **Inspect** trên pipeline card để xem các setting checks.
6. Click **Locate** để nhảy nhanh tới nơi cấu hình liên quan:
   - URP Asset
   - Project Settings > Player
   - Project Settings > Graphics
   - Project Settings > Quality
   - Scene Renderer Audit
   - Frame Debugger / Material guidance

**Ý nghĩa màu trạng thái:**

| Trạng thái | Màu | Ý nghĩa |
|------------|-----|---------|
| `Matched` | Xanh | Setting đang đúng với checklist |
| `Partial` | Vàng | Có override hoặc điều kiện cần kiểm tra thêm; không mặc định là lỗi đỏ |
| `Mismatched` | Đỏ | Setting sai rõ ràng so với khuyến nghị mobile |
| `Unknown` | Xám | Không đọc được field từ Unity/URP version hiện tại |
| `Advisory` | Xanh dương | Chỉ là hướng dẫn; cần xác minh bằng scene/material/profiler |

> [!IMPORTANT]
> **Quality Override Detected** là cảnh báo màu vàng. Nếu Graphics Settings dùng
> `URP-Medium.asset` nhưng Quality level hiện tại dùng `URP-Low.asset`, Module 7
> không coi đây là lỗi đỏ. Nó cảnh báo để bạn biết runtime có thể đang dùng URP
> Asset khác với asset mặc định trong Graphics Settings.

**Khi nào cần Module 8/Frame Debugger?**

Một số pipeline không thể kết luận chỉ bằng Project Settings:

- GPU Instancing phụ thuộc Material và renderer cụ thể.
- GRD có thể bị ảnh hưởng bởi shader compatibility, MPB, renderer type.
- Fallback chỉ chắc chắn khi xem draw path thực tế trong Frame Debugger.

Vì vậy Module 7 sẽ dùng trạng thái `Advisory` hoặc `Partial` và hướng bạn sang
Module 8 hoặc Frame Debugger khi cần bằng chứng renderer-level.

### Module 8: Scene Renderer Audit 🧩

Module 8 quét các renderer trong scene đang mở để tìm điều kiện batching/render
path ở cấp object. Đây là module bổ trợ trực tiếp cho Module 7.

**Cách sử dụng:**

1. Mở scene gameplay cần kiểm tra.
2. Mở `Tools > Optifunity > Dashboard`.
3. Vào tab **🧩 Scene Renderer Audit**.
4. Chọn phạm vi scan:
   - `Include Inactive`: quét cả object inactive.
   - `Scan Skinned Mesh Renderer`: thêm SkinnedMeshRenderer vào kết quả.
5. Bật ít nhất một filter điều kiện:
   - **MPB**: tìm renderer có/không có `MaterialPropertyBlock`.
   - **GPU Instancing**: tìm renderer có/không có material bật instancing.
   - **Static Batching**: tìm renderer có/không có static batching.
   - **LODGroup**: tìm object có/không thuộc LODGroup.
   - **XR Motion**: kiểm tra motion-vector/XR motion state theo material.
   - **DOTS Instancing**: kiểm tra keyword/property hỗ trợ DOTS instancing.
6. Nhấn **Scene Scan** hoặc chạy **Full Scan**.
7. Dùng issue list để locate object/material liên quan.

**Ví dụ workflow thực tế:**

| Mục tiêu | Filter nên bật |
|----------|----------------|
| Tìm object phá batching vì dùng MPB | MPB = On |
| Tìm object lặp lại nhưng chưa bật instancing | GPU Instancing = Off |
| Tìm môi trường tĩnh chưa static batch | Static Batching = Off |
| Tìm mesh nặng thiếu LOD | LODGroup = Off |
| Kiểm tra material có hỗ trợ DOTS/GRD path | DOTS Instancing = On/Off |

> [!TIP]
> Module 8 chỉ đọc trạng thái renderer/material tại thời điểm scan. Với vấn đề
> runtime như script bật/tắt MPB trong Play Mode, hãy scan trong Play Mode hoặc
> xác minh thêm bằng Frame Debugger.

---

## Dashboard — 8 Tabs

| Tab | Nội dung |
|-----|---------|
| **Overview** | Health Score (0–100), summary cards, top errors |
| **Code** | GC Alloc + Boxing + Memory Leak + anti-pattern/resource-management issues |
| **Assets** | Texture + Mesh + Audio audit issues |
| **URP** | URP settings + SRP Batcher + Draw Call issues |
| **🧩 Scene Renderer Audit** | Renderer-level scan theo MPB, Instancing, Static Batching, LOD, XR Motion, DOTS |
| **📱 Render Pipeline** | 6 pipeline mobile song song + setting checks + Locate buttons |
| **🔬 Spike** | Profiler status, frame diagnosis, bottleneck/root-cause analysis |
| **📦 Build** | Danh sách Material/Shader thực tế đi vào build |

---

## Health Score

```
Health Score = max(0, 100 − errors×10 − warnings×3 − infos×0.5)
```

| Score | Trạng thái |
|-------|-----------|
| 80–100 | 🟢 Tốt |
| 50–79 | 🟡 Cần cải thiện |
| 0–49 | 🔴 Nghiêm trọng |

---

## Cấu Hình (Settings trong Dashboard)

Mở **Dashboard** → bật **⚙ Config** để chỉnh các setting chính:

| Tham số | Mô tả | Mặc định |
|---------|-------|---------|
| Target Platform | Android / iOS / PC | Android |
| Android Physical RAM | RAM vật lý thiết bị đích (MB) | 4096 (4GB) |
| Auto-Fix Enabled | Tự động sửa khi import asset | `false` |
| Audio Stream Threshold | Ngưỡng giây cho Streaming | 5s |
| Spike Threshold | Multiplier để mark spike (× avg) | 1.5× |
| My Scripts Folders | Danh sách folder được scan khi bật **📌 My Scripts** | Trống (user tự thêm) |

**Gợi ý cấu hình nhanh:**
- Team game thường dùng: thêm `Assets/Scripts`, `Assets/Game`, `Assets/UI` vào **My Scripts Folders**.
- Khi audit release: tắt **📌 My Scripts** để quét full project.
- Khi debug nhanh theo team feature: bật **📌 My Scripts** để kết quả gọn và nhanh hơn.

---

## Chạy Unit Tests

```
Window > General > Test Runner > EditMode > Run All
```

**20 test cases** covering: PlatformConfig budget, GCAllocAnalyzer patterns, BoxingAnalyzer, MemLeakAnalyzer patterns, ReportEngine HealthScore.

---

## Menu Items

| Menu | Shortcut | Chức năng |
|------|---------|-----------|
| `Tools > Optifunity > Dashboard` | `Ctrl+Shift+O` | Mở Dashboard chính |
| `Tools > Optifunity > Spike Analyzer` | — | Mở Spike Analyzer |
| `Tools > Optifunity > Report Viewer` | — | Mở Report Viewer chi tiết |
| `Tools > Optifunity > Run Full Scan` | — | Chạy scan nhanh |

---

## Giấy Phép

MIT License
