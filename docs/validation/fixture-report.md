# Optifunity Performance Report

**Scanned:** 2026-10-13 14:17:47  
**Project:** Optifunity-A-20261013  
**Unity:** 6000.0.58f2  
**Scope:** Assets and loaded scene/settings only; Packages, build dependencies and device runtime are not fully covered.  
**Module captures (unlisted modules not scanned):** AssetAudit: 2026-10-13T14:17:49 / Quality=Ultra / Pipeline=Built-in / Target=Android / Texture2D: inspected=0, skipped=0; top-level imported assets under Assets only; package/generated/sub-asset coverage excluded; Model: inspected=0, skipped=0; top-level imported assets under Assets only; package/generated/sub-asset coverage excluded; AudioClip: inspected=0, skipped=0; top-level imported assets under Assets only; package/generated/sub-asset coverage excluded; URPDiagnostics: 2026-10-13T14:17:49 / Quality=Ultra / Pipeline=Built-in / Target=Android / current effective pipeline and available settings; unknown fields explicitly excluded; MobileWorkflow: 2026-10-13T14:17:49 / Quality=Ultra / Pipeline=Built-in / Target=Android / current effective pipeline and available settings; unknown fields explicitly excluded; SceneRendererAudit: 2026-10-13T14:17:49 / Quality=Ultra / Pipeline=Built-in / Target=Android / Loaded scenes=1; candidate renderers=0; options={"IncludeInactive":true,"IncludeSkinnedMeshRenderers":false,"RepeatedRendererThreshold":3,"HeavyMeshVertexThreshold":20000,"HeavyBoundsMagnitudeThreshold":25.0,"EmitPerRendererIssues":true,"EnableGpuInstancingFilter":false,"GpuInstancingExpectedOn":true,"EnableMaterialPropertyBlockFilter":false,"MaterialPropertyBlockExpectedOn":true,"EnableStaticBatchingFilter":false,"StaticBatchingExpectedOn":true,"EnableLodGroupFilter":false,"LodGroupExpectedOn":true,"EnableXrMotionFilter":false,"XrMotionExpectedOn":true,"EnableDotsInstancingFilter":false,"DotsInstancingExpectedOn":true}; unopened scenes/prefabs excluded; CodeAnalysis: 2026-10-13T14:17:49 / Quality=Ultra / Pipeline=Built-in / Target=Android / Complete within selected scope: Assets C#; MyScripts=False; scanned=2; skipped=0; Packages/generated sources excluded  
**Completion:** Module results available; consult coverage and cancellation status  
**Quality / pipeline:** Ultra / Built-in  
**Platform:** Android (4096MB RAM)  
**Rule compliance score (scanned scope only; not measured performance):** 88/100 (selected code rules)  

---

## Summary
| Module | Errors | Warnings | Infos |
|--------|--------|----------|-------|
| Code Analysis | 0 | 4 | 0 |
| Asset Audit | 0 | 0 | 0 |
| Shader/Materials | 0 | 0 | 2 |
| Render Pipeline | 0 | 0 | 9 |
| Scene Renderer | 0 | 0 | 1 |

## Code Analysis Issues

### 🟡 Cấp phát đối tượng mới trong gameplay method
- **Severity:** Warning
- **Rule / evidence:** code.allocation / Confirmed static operation
- **Knowledge status:** Verified concept; Unity 6000.0.58f2 / URP 17.0.4 validation baseline; C# 9 and resolved compilation symbols
- **Sources:** https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals
- **Asset:** `C:\Users\Duy NB\AppData\Local\Temp\Optifunity-A-20261013\Assets\OptifunityValidation\Gameplay\ValidationGameplay.cs`
- **Location:** `C:\Users\Duy NB\AppData\Local\Temp\Optifunity-A-20261013\Assets\OptifunityValidation\Gameplay\ValidationGameplay.cs:L10`
- **Description:** Reference-type construction when this expression executes; no measured frame impact implied.
- **Fix:** Measure allocation and consider reuse or pooling.

### 🟡 Cấp phát mảng mới trong gameplay method
- **Severity:** Warning
- **Rule / evidence:** code.allocation / Confirmed static operation
- **Knowledge status:** Verified concept; Unity 6000.0.58f2 / URP 17.0.4 validation baseline; C# 9 and resolved compilation symbols
- **Sources:** https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals
- **Asset:** `C:\Users\Duy NB\AppData\Local\Temp\Optifunity-A-20261013\Assets\OptifunityValidation\Gameplay\ValidationGameplay.cs`
- **Location:** `C:\Users\Duy NB\AppData\Local\Temp\Optifunity-A-20261013\Assets\OptifunityValidation\Gameplay\ValidationGameplay.cs:L11`
- **Description:** Array creation is a managed allocation when executed; frequency and impact require profiling.
- **Fix:** Reuse a buffer if lifetime and ownership permit.

### 🟡 Cấp phát mảng mới trong gameplay method
- **Severity:** Warning
- **Rule / evidence:** code.allocation / Confirmed static operation
- **Knowledge status:** Verified concept; Unity 6000.0.58f2 / URP 17.0.4 validation baseline; C# 9 and resolved compilation symbols
- **Sources:** https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals
- **Asset:** `C:\Users\Duy NB\AppData\Local\Temp\Optifunity-A-20261013\Assets\OptifunityValidation\Gameplay\ValidationGameplay.cs`
- **Location:** `C:\Users\Duy NB\AppData\Local\Temp\Optifunity-A-20261013\Assets\OptifunityValidation\Gameplay\ValidationGameplay.cs:L13`
- **Description:** Array creation is a managed allocation when executed; frequency and impact require profiling.
- **Fix:** Reuse a buffer if lifetime and ownership permit.

### 🟡 Boxing conversion
- **Severity:** Warning
- **Rule / evidence:** code.boxing / Confirmed static operation
- **Knowledge status:** Verified concept; Unity 6000.0.58f2 / URP 17.0.4 validation baseline; C# 9 and resolved compilation symbols
- **Sources:** https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/types/boxing-and-unboxing
- **Asset:** `C:\Users\Duy NB\AppData\Local\Temp\Optifunity-A-20261013\Assets\OptifunityValidation\Gameplay\ValidationGameplay.cs`
- **Location:** `C:\Users\Duy NB\AppData\Local\Temp\Optifunity-A-20261013\Assets\OptifunityValidation\Gameplay\ValidationGameplay.cs:L9`
- **Description:** The compiler resolves a value-type to reference/interface conversion. Runtime cost depends on execution frequency.
- **Fix:** Consider a generic API or typed storage, then profile.

## Shader/Materials Issues

### 🔵 URP không được kích hoạt trong project
- **Severity:** Info
- **Rule / evidence:** legacy.unverified / Advisory
- **Knowledge status:** Unverified; Legacy asset/settings/scene detectors within the explicitly reported scan scope; per-rule platform and usage conditions remain unverified
- **Sources:** 
- **Description:** UNVERIFIED HEURISTIC — applicability and performance impact require independent confirmation.
Không tìm thấy Universal Render Pipeline Asset trong Graphics Settings.
- **Fix:** Vào Project Settings → Graphics → Scriptable Render Pipeline Settings và gán URP Asset.

### 🔵 Draw Call Analysis cần chạy trong Play Mode
- **Severity:** Info
- **Rule / evidence:** legacy.unverified / Advisory
- **Knowledge status:** Unverified; Legacy asset/settings/scene detectors within the explicitly reported scan scope; per-rule platform and usage conditions remain unverified
- **Sources:** 
- **Description:** UNVERIFIED HEURISTIC — applicability and performance impact require independent confirmation.
Draw Call stats chỉ khả dụng trong Play Mode.
- **Fix:** Enter Play Mode và chạy scene, sau đó chạy lại analysis này.

## Render Pipeline Issues

### 🔵 [Pipeline Architecture] Conditional rendering strategies
- **Severity:** Info
- **Rule / evidence:** legacy.unverified / Advisory
- **Knowledge status:** Unverified; Legacy asset/settings/scene detectors within the explicitly reported scan scope; per-rule platform and usage conditions remain unverified
- **Sources:** 
- **Description:** UNVERIFIED HEURISTIC — applicability and performance impact require independent confirmation.
Các đường render có điều kiện tương thích và ưu tiên theo Unity/URP. Các lựa chọn trong checklist không độc lập hoàn toàn và không mặc định cộng dồn trên cùng renderer.
- **Fix:** Mở tab Render Pipeline để xem match/mismatch theo từng pipeline và locate setting tương ứng.

### 🔵 [GPU Resident Drawer (GRD)] NotApplicable
- **Severity:** Info
- **Rule / evidence:** legacy.unverified / Advisory
- **Knowledge status:** Unverified; Legacy asset/settings/scene detectors within the explicitly reported scan scope; per-rule platform and usage conditions remain unverified
- **Sources:** 
- **Description:** UNVERIFIED HEURISTIC — applicability and performance impact require independent confirmation.
The selected pipeline or adapter does not support this check.
- **Fix:** Select a supported URP configuration before applying this checklist.

### 🔵 [SRP Batcher] NotApplicable
- **Severity:** Info
- **Rule / evidence:** legacy.unverified / Advisory
- **Knowledge status:** Unverified; Legacy asset/settings/scene detectors within the explicitly reported scan scope; per-rule platform and usage conditions remain unverified
- **Sources:** 
- **Description:** UNVERIFIED HEURISTIC — applicability and performance impact require independent confirmation.
The selected pipeline or adapter does not support this check.
- **Fix:** Select a supported URP configuration before applying this checklist.

### 🔵 [GPU Instancing (Traditional)] NotApplicable
- **Severity:** Info
- **Rule / evidence:** legacy.unverified / Advisory
- **Knowledge status:** Unverified; Legacy asset/settings/scene detectors within the explicitly reported scan scope; per-rule platform and usage conditions remain unverified
- **Sources:** 
- **Description:** UNVERIFIED HEURISTIC — applicability and performance impact require independent confirmation.
The selected pipeline or adapter does not support this check.
- **Fix:** Select a supported URP configuration before applying this checklist.

### 🔵 [Static Batching] NotApplicable
- **Severity:** Info
- **Rule / evidence:** legacy.unverified / Advisory
- **Knowledge status:** Unverified; Legacy asset/settings/scene detectors within the explicitly reported scan scope; per-rule platform and usage conditions remain unverified
- **Sources:** 
- **Description:** UNVERIFIED HEURISTIC — applicability and performance impact require independent confirmation.
The selected pipeline or adapter does not support this check.
- **Fix:** Select a supported URP configuration before applying this checklist.

### 🔵 [Dynamic Batching (Legacy)] NotApplicable
- **Severity:** Info
- **Rule / evidence:** legacy.unverified / Advisory
- **Knowledge status:** Unverified; Legacy asset/settings/scene detectors within the explicitly reported scan scope; per-rule platform and usage conditions remain unverified
- **Sources:** 
- **Description:** UNVERIFIED HEURISTIC — applicability and performance impact require independent confirmation.
The selected pipeline or adapter does not support this check.
- **Fix:** Select a supported URP configuration before applying this checklist.

### 🔵 [Fallback (Normal Draw)] NotApplicable
- **Severity:** Info
- **Rule / evidence:** legacy.unverified / Advisory
- **Knowledge status:** Unverified; Legacy asset/settings/scene detectors within the explicitly reported scan scope; per-rule platform and usage conditions remain unverified
- **Sources:** 
- **Description:** UNVERIFIED HEURISTIC — applicability and performance impact require independent confirmation.
The selected pipeline or adapter does not support this check.
- **Fix:** Select a supported URP configuration before applying this checklist.

### 🔵 [Guidance] Unity 6 GRD benchmark strategy
- **Severity:** Info
- **Rule / evidence:** legacy.unverified / Advisory
- **Knowledge status:** Unverified; Legacy asset/settings/scene detectors within the explicitly reported scan scope; per-rule platform and usage conditions remain unverified
- **Sources:** 
- **Description:** UNVERIFIED HEURISTIC — applicability and performance impact require independent confirmation.
Vulkan đã bật; có thể benchmark A/B giữa Vulkan và OpenGLES3 theo từng device tier.
- **Fix:** Đo GPU frame time và stability trên tier thấp/trung/cao trước khi chốt API mặc định.

### 🔵 [Guidance] Android API fallback strategy
- **Severity:** Info
- **Rule / evidence:** legacy.unverified / Advisory
- **Knowledge status:** Unverified; Legacy asset/settings/scene detectors within the explicitly reported scan scope; per-rule platform and usage conditions remain unverified
- **Sources:** 
- **Description:** UNVERIFIED HEURISTIC — applicability and performance impact require independent confirmation.
OpenGLES3 fallback đang có; phù hợp cho chiến lược phủ thiết bị rộng.
- **Fix:** Quyết định fallback theo dữ liệu crash/perf từ test farm và thiết bị thật.

## Scene Renderer Issues

### 🔵 Open Scene Renderer Scan
- **Severity:** Info
- **Rule / evidence:** legacy.unverified / Advisory
- **Knowledge status:** Unverified; Legacy asset/settings/scene detectors within the explicitly reported scan scope; per-rule platform and usage conditions remain unverified
- **Sources:** 
- **Description:** UNVERIFIED HEURISTIC — applicability and performance impact require independent confirmation.
Không tìm thấy renderer nào trong các scene đang mở theo cấu hình hiện tại.
- **Fix:** Mở scene gameplay hoặc bật thêm tùy chọn scan (ví dụ Skinned Mesh Renderer) rồi scan lại.

