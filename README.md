# Optifunity — Unity URP Performance Analysis Plugin

[![Unity 2021.3+](https://img.shields.io/badge/Unity-2021.3%2B-black.svg)](https://unity3d.com)
[![URP](https://img.shields.io/badge/Pipeline-URP-blue.svg)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Modules](https://img.shields.io/badge/Modules-5-brightgreen.svg)](#tổng-quan)

> **Hệ thống plugin phân tích hiệu năng tự động** cho Unity URP.  
> Phát hiện bottleneck, audit assets, phân tích memory, và tự động phân loại frame spike ngay trong Editor.

---

## Tổng Quan

Optifunity tích hợp **5 phân hệ** hoạt động song song:

| # | Phân Hệ | Phạm Vi | Trigger |
|---|---------|---------|---------|
| 1 | **Roslyn Code Analyzer** | GC Alloc, Boxing, Memory Leak trong C# | Scan thủ công |
| 2 | **Asset Auditor** | Texture / Mesh / Audio theo tiêu chuẩn platform | Scan thủ công + Auto khi import |
| 3 | **Memory Snapshot Profiler** | Baseline/Peak/Teardown snapshot, Differential Analysis | Play Mode |
| 4 | **URP Diagnostics** | Render pipeline PC vs Mobile, SRP Batcher, Draw Calls | Scan thủ công |
| 5 | **🔬 Spike Analyzer** | Tự động phân tích 11 loại bottleneck khi chọn frame trong Profiler | **Real-time, tự động** |

---

## Cài Đặt

### Unity Package Manager (Local Path) — Khuyến nghị

1. **Window → Package Manager**
2. Nhấn **`+`** → **Add package from disk...**
3. Chọn file `package.json` trong thư mục này (`d:\Optifunity\package.json`)

### Yêu Cầu

| Yêu cầu | Phiên bản |
|---------|-----------|
| Unity | 2021.3 LTS trở lên |
| Render Pipeline | Universal Render Pipeline (URP) |
| Memory Profiler *(optional)* | `com.unity.memoryprofiler >= 1.1.0` |

---

## Hướng Dẫn Sử Dụng Nhanh

### Dashboard

```
Tools > Optifunity > Dashboard    (Ctrl+Shift+O)
```

Nhấn **▶ Full Scan** để chạy toàn bộ 4 module tĩnh (Code + Assets + URP + Memory Budget).

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
    │   └── CodeAnalysisRunner.cs
    ├── Module2_AssetAuditor/
    │   ├── TextureAuditor.cs              Compression, POT, MaxSize, MipMap
    │   ├── MeshAuditor.cs                 Read/Write, Anim Compression, LOD
    │   ├── AudioAuditor.cs                Streaming threshold, Vorbis, Force Mono
    │   ├── AssetAuditRunner.cs
    │   └── AssetPostprocessorHook.cs      Auto-apply khi import
    ├── Module3_MemoryProfiler/
    │   ├── SnapshotCapturer.cs            Chụp Baseline/Peak/Teardown
    │   ├── SnapshotAnalyzer.cs            Differential Analysis
    │   └── MemoryBudgetValidator.cs       So sánh vs budget
    ├── Module4_URPDiagnostics/
    │   ├── URPAssetScanner.cs             Đọc URP Asset properties
    │   ├── URPRecommendationEngine.cs     Ma trận PC vs Mobile
    │   ├── SRPBatcherChecker.cs           CBUFFER compatibility
    │   └── DrawCallAnalyzer.cs            SetPass/Draw Call bottleneck
    ├── Module5_SpikeAnalyzer/             ← PHÂN HỆ MỚI
    │   ├── ProfilerSample.cs              Data models (tree + lookup)
    │   ├── FrameDataReader.cs             HierarchyFrameDataView reader
    │   ├── BottleneckClassifier.cs        11 case checkers
    │   ├── SpikeDetector.cs               Poll 10×/s, rolling buffer
    │   └── SpikeAnalysisRunner.cs         Orchestrator + cache
    └── UI/
        ├── OptifunityStyles.cs            Dark theme styles
        ├── DashboardWindow.cs             6-tab main window
        ├── ReportWindow.cs                Filter/sort/export report viewer
        └── SpikeAnalyzerWindow.cs         Timeline + analysis UI
```

---

## Phân Hệ Chi Tiết

### Module 1: Code Analyzer

Phát hiện các pattern nguy hiểm bằng Regex trên toàn bộ `.cs` trong `Assets/`:

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

### Module 3: Memory Snapshot

```
1. Vào Play Mode
2. Tại scene start → nhấn [📷 Baseline]
3. Tại điểm tải nặng nhất → nhấn [📷 Peak Load]
4. Trước khi thoát scene → nhấn [📷 Teardown]
5. Differential Analysis = Teardown − Baseline
6. Nếu delta > 10% → cảnh báo memory leak
```

> ⚠ Yêu cầu package `com.unity.memoryprofiler >= 1.1.0`

### Module 4: URP Diagnostics

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

### Module 5: Spike Analyzer 🔬 *(MỚI)*

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
- **4 analysis tabs**: Bottleneck (confidence score + recommendations) | Samples | GC Alloc | Compare
- **Frame Comparison** — so sánh side-by-side 2 frames bất kỳ
- **Export** — kết quả phân tích có thể export qua Report Viewer

---

## Dashboard — 6 Tabs

| Tab | Nội dung |
|-----|---------|
| **Overview** | Health Score (0–100), summary cards 5 module, top errors |
| **Code** | GC Alloc + Boxing + Memory Leak issues |
| **Assets** | Texture + Mesh + Audio audit issues |
| **Memory** | Snapshot controls + budget comparison |
| **URP** | URP settings + SRP Batcher + Draw Call issues |
| **🔬 Spike** | Profiler status, last frame analysis, link tới SpikeAnalyzerWindow |

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

## Cấu Hình

| Tham số | Mô tả | Mặc định |
|---------|-------|---------|
| Target Platform | Android / iOS / PC | Android |
| Android Physical RAM | RAM vật lý thiết bị đích (MB) | 4096 (4GB) |
| Auto-Fix Enabled | Tự động sửa khi import asset | `false` |
| Audio Stream Threshold | Ngưỡng giây cho Streaming | 5s |
| Spike Threshold | Multiplier để mark spike (× avg) | 1.5× |

---

## Chạy Unit Tests

```
Window > General > Test Runner > EditMode > Run All
```

**18 test cases** covering: PlatformConfig budget, GCAllocAnalyzer patterns, BoxingAnalyzer, MemLeakAnalyzer patterns, ReportEngine HealthScore.

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
