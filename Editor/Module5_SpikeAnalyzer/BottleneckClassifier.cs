using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Optifunity.Editor.Module5
{
    /// <summary>
    /// Phân loại bottleneck của một FrameSnapshot.
    /// Kiểm tra 11 loại spike pattern theo dữ liệu sample profiler.
    /// Trả về SpikeAnalysisReport với primary + contributing findings + recommendations.
    /// </summary>
    public static class BottleneckClassifier
    {
        // ══════════════════════════════════════════════════════════════════════
        // Ngưỡng thời gian (ms) — tham chiếu từ tài liệu kỹ thuật
        // ══════════════════════════════════════════════════════════════════════
        private const float TARGET_60FPS_MS        = 16.67f;
        private const float TARGET_30FPS_MS        = 33.33f;

        private const float GFX_WAIT_THRESHOLD_PCT = 0.30f;  // > 30% tổng frame → GPU-bound
        private const float GFX_WAIT_CRITICAL_PCT  = 0.50f;  // > 50% → Critical GPU-bound

        private const float PHYSICS_HIGH_MS        = 5.0f;
        private const float PHYSICS_CRITICAL_MS    = 10.0f;

        private const float SCRIPTING_HIGH_MS      = 10.0f;
        private const float SCRIPTING_CRITICAL_MS  = 20.0f;

        private const float CANVAS_HIGH_MS         = 3.0f;
        private const float CANVAS_CRITICAL_MS     = 8.0f;

        private const float ANIMATION_HIGH_MS      = 3.0f;
        private const float ANIMATION_CRITICAL_MS  = 7.0f;

        private const float AUDIO_HIGH_MS          = 2.0f;
        private const float VSYNC_HIGH_MS          = 5.0f;

        private const long  GC_ALLOC_HIGH_BYTES    = 10_000;      // 10KB/frame
        private const long  GC_ALLOC_CRITICAL_BYTES = 100_000;    // 100KB/frame

        // ══════════════════════════════════════════════════════════════════════
        // Profiler marker names (Unity internal — verified against Unity source)
        // ══════════════════════════════════════════════════════════════════════
        // GC
        private const string MARKER_GC_COLLECT        = "GC.Collect";
        private const string MARKER_GC_ALLOC          = "GC.Alloc";

        // GPU / Rendering
        private const string MARKER_GFX_WAIT          = "Gfx.WaitForPresent";
        private const string MARKER_CAMERA_RENDER      = "Camera.Render";
        private const string MARKER_RENDER_LOOP        = "RenderLoop.Draw";
        private const string MARKER_SHADOWS            = "UpdateDepthTexture";

        // Physics
        private const string MARKER_PHYSICS_PROCESS   = "Physics.Processing";
        private const string MARKER_PHYSICS_FETCH      = "Physics.FetchResults";
        private const string MARKER_PHYSICS_UPDATE     = "FixedUpdate.PhysicsFixedUpdate";

        // Scripting
        private const string MARKER_BEHAVIOUR_UPDATE  = "BehaviourUpdate";
        private const string MARKER_FIXED_UPDATE       = "FixedUpdate.ScriptRunBehaviourFixedUpdate";
        private const string MARKER_LATE_UPDATE        = "LateBehaviourUpdate";
        private const string MARKER_COROUTINE          = "DelayedCallManager.Update";

        // UI / Canvas
        private const string MARKER_CANVAS_BUILD       = "Canvas.BuildBatch";
        private const string MARKER_CANVAS_SEND        = "Canvas.SendWillRenderCanvases";
        private const string MARKER_CANVAS_REBUILD_ALL = "CanvasRenderer.UpdateGeometryForCanvas";
        private const string MARKER_EVENT_SYSTEM       = "EventSystem.Update";

        // Animation
        private const string MARKER_ANIMATOR_UPDATE   = "Animator.Update";
        private const string MARKER_ANIM_SAMPLE        = "AnimationClip.SampleAnimation";
        private const string MARKER_ANIM_APPLY         = "AnimationApplyBuiltinRootMotion";

        // Asset Loading
        private const string MARKER_LOADING_READ       = "Loading.ReadObject";
        private const string MARKER_LOADING_COMPLETE   = "Loading.ReadObjectComplete";
        private const string MARKER_ASSET_BUNDLE       = "AssetBundle.LoadAsset";
        private const string MARKER_INSTANTIATE        = "Object.InstantiateFromScene";

        // Audio
        private const string MARKER_AUDIO_UPDATE       = "AudioManager.Update";
        private const string MARKER_AUDIO_THREAD        = "AudioCustomFilter";

        // VSync
        private const string MARKER_WAIT_FPS           = "WaitForTargetFPS";
        private const string MARKER_GFXDEVICE_PRESENT  = "Gfx.PresentFrame";

        // Job System
        private const string MARKER_JOB_WAIT           = "JobSystem.WaitForJob";
        private const string MARKER_JOB_WORK_STEAL     = "WaitingForWorkerThread";
        private const string MARKER_WORKER_THREAD       = "JobHandle.Complete";

        // ══════════════════════════════════════════════════════════════════════
        // Public API
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Phân loại toàn diện một FrameSnapshot.
        /// </summary>
        public static SpikeAnalysisReport Classify(FrameSnapshot snapshot, float averageFrameMs)
        {
            var report = new SpikeAnalysisReport
            {
                FrameIndex     = snapshot.FrameIndex,
                FrameTotalMs   = snapshot.TotalCpuTimeMs,
                AverageFrameMs = averageFrameMs,
                IsSpike        = snapshot.TotalCpuTimeMs > averageFrameMs * 1.5f,
                Severity       = CalcSeverity(snapshot.TotalCpuTimeMs, averageFrameMs)
            };

            if (!snapshot.IsValid)
            {
                report.PrimaryBottleneck  = BottleneckType.Unknown;
                report.PrimaryTitle       = "Không thể đọc dữ liệu frame";
                report.PrimaryDescription = snapshot.ErrorMessage;
                return report;
            }

            // ─── Chạy tất cả 11 checkers ────────────────────────────────────
            var findings = new List<BottleneckFinding>();

            findings.Add(CheckGarbageCollection(snapshot));
            findings.Add(CheckGPUBound(snapshot));
            findings.Add(CheckCPURenderThread(snapshot));
            findings.Add(CheckPhysics(snapshot));
            findings.Add(CheckScripting(snapshot));
            findings.Add(CheckUICanvas(snapshot));
            findings.Add(CheckAnimation(snapshot));
            findings.Add(CheckAssetLoading(snapshot));
            findings.Add(CheckAudio(snapshot));
            findings.Add(CheckVSync(snapshot));
            findings.Add(CheckJobSystemStall(snapshot));

            // Lọc null và sắp xếp theo confidence
            findings = findings.Where(f => f != null && f.Confidence > 0.05f)
                               .OrderByDescending(f => f.Confidence)
                               .ToList();

            if (findings.Count == 0)
            {
                report.PrimaryBottleneck  = BottleneckType.Balanced;
                report.PrimaryTitle       = "Frame bình thường — Không phát hiện bottleneck rõ ràng";
                report.PrimaryDescription = $"Frame {snapshot.TotalCpuTimeMs:F2}ms, gần với target 60fps ({TARGET_60FPS_MS:F1}ms).";
                report.PrimaryConfidence  = 1.0f;
            }
            else
            {
                var primary = findings[0];
                report.PrimaryBottleneck  = primary.Type;
                report.PrimaryConfidence  = primary.Confidence;
                report.PrimaryTitle       = GetBottleneckTitle(primary.Type);
                report.PrimaryDescription = primary.Evidence;

                // Contributing factors (trừ primary)
                for (int i = 1; i < findings.Count; i++)
                    report.ContributingFactors.Add(findings[i]);

                // Nếu nhiều factor có confidence gần nhau → Mixed
                if (findings.Count >= 2 && findings[1].Confidence > findings[0].Confidence * 0.7f)
                    report.PrimaryBottleneck = BottleneckType.Mixed;
            }

            // ─── Top samples ─────────────────────────────────────────────────
            report.TopSlowSamples    = snapshot.GetTopByTime(12);
            report.TopGCAllocSamples = snapshot.GetTopByGCAlloc(8);

            // ─── Key Metrics ─────────────────────────────────────────────────
            BuildKeyMetrics(report, snapshot);

            // ─── Recommendations ─────────────────────────────────────────────
            BuildRecommendations(report, findings, snapshot);

            return report;
        }

        // ══════════════════════════════════════════════════════════════════════
        // 11 Checkers
        // ══════════════════════════════════════════════════════════════════════

        // ─── Case 1: Garbage Collection ──────────────────────────────────────
        private static BottleneckFinding CheckGarbageCollection(FrameSnapshot s)
        {
            bool hasCollect  = s.HasMarker(MARKER_GC_COLLECT);
            bool hasAllocMarker = s.HasMarker(MARKER_GC_ALLOC);
            long gcBytes     = s.TotalGCAllocBytes;
            float gcTime     = s.SumTime(MARKER_GC_COLLECT);

            if (!hasCollect && gcBytes < GC_ALLOC_HIGH_BYTES && !hasAllocMarker)
                return null;

            float confidence = 0f;
            string evidence;

            if (hasCollect)
            {
                confidence += 0.7f;
                // GC.Collect time thường được tính vào nhiều sample — override với wall time
                if (gcTime < 0.1f) gcTime = s.TotalCpuTimeMs * 0.3f; // estimate
            }

            if (gcBytes >= GC_ALLOC_CRITICAL_BYTES)
            {
                confidence += 0.3f;
                evidence = $"GC.Collect kích hoạt! GC Alloc frame này: {FormatBytes(gcBytes)}. " +
                           $"GC.Collect time ≈ {gcTime:F2}ms. " +
                           $"Mỗi {gcBytes / 1024f:F0}KB/frame = {gcBytes * 60 / 1048576f:F0}MB/s rác bộ nhớ.";
            }
            else if (gcBytes >= GC_ALLOC_HIGH_BYTES)
            {
                confidence += 0.2f;
                evidence = $"GC Alloc cao: {FormatBytes(gcBytes)}/frame. " +
                           (hasCollect ? "GC.Collect đã kích hoạt trong frame này." : "Tích lũy sẽ trigger GC.Collect sớm.");
            }
            else
            {
                evidence = $"GC.Collect marker phát hiện (alloc nhỏ: {FormatBytes(gcBytes)}).";
                confidence = 0.3f;
            }

            // Top GC allocators
            var topAlloc = s.GetTopByGCAlloc(5);
            if (topAlloc.Count > 0)
            {
                evidence += "\nTop allocators: " + string.Join(", ",
                    topAlloc.Take(3).Select(a => $"{a.Name}({FormatBytes(a.GCAllocBytes)})"));
            }

            return new BottleneckFinding
            {
                Type       = BottleneckType.GarbageCollection,
                Confidence = Mathf.Clamp01(confidence),
                Evidence   = evidence,
                TimeMs     = gcTime,
                Fix        = "Object Pooling, tránh new() trong Update(), dùng StringBuilder"
            };
        }

        // ─── Case 2: GPU-Bound ────────────────────────────────────────────────
        private static BottleneckFinding CheckGPUBound(FrameSnapshot s)
        {
            float gfxWait    = s.SumTime(MARKER_GFX_WAIT);
            float frameTotal = s.TotalCpuTimeMs;

            if (frameTotal <= 0 || gfxWait < 1.0f) return null;

            float pct        = gfxWait / frameTotal;
            float confidence = 0f;
            string evidence;

            if (pct >= GFX_WAIT_CRITICAL_PCT)
            {
                confidence = 0.90f;
                evidence = $"Gfx.WaitForPresent = {gfxWait:F2}ms ({pct * 100:F0}% frame). " +
                           $"CPU đang nằm chờ GPU hoàn thành draw call. GPU-bound nghiêm trọng!";
            }
            else if (pct >= GFX_WAIT_THRESHOLD_PCT)
            {
                confidence = 0.65f;
                evidence = $"Gfx.WaitForPresent = {gfxWait:F2}ms ({pct * 100:F0}% frame). " +
                           $"GPU đang là bottleneck. Camera.Render = {s.SumTime(MARKER_CAMERA_RENDER):F2}ms.";
            }
            else
            {
                confidence = 0.20f;
                evidence = $"Gfx.WaitForPresent thấp ({gfxWait:F2}ms, {pct * 100:F0}%) — GPU không phải bottleneck chính.";
            }

            return new BottleneckFinding
            {
                Type       = BottleneckType.GPUBound,
                Confidence = confidence,
                Evidence   = evidence,
                TimeMs     = gfxWait,
                Fix        = "Giảm overdraw, nén texture ASTC, tắt SSAO/HDR trên mobile, reduce shadow cascade"
            };
        }

        // ─── Case 3: CPU Render Thread ───────────────────────────────────────
        private static BottleneckFinding CheckCPURenderThread(FrameSnapshot s)
        {
            float cameraRender  = s.SumTime(MARKER_CAMERA_RENDER);
            float renderLoop    = s.SumTime(MARKER_RENDER_LOOP);
            float gfxWait       = s.SumTime(MARKER_GFX_WAIT);
            float frameTotal    = s.TotalCpuTimeMs;

            // CPU Render Thread: Camera.Render cao nhưng Gfx.Wait thấp
            // (nếu Gfx.Wait cao thì là GPU-bound, không phải CPU render)
            bool cpuRenderDominant = cameraRender > 8f && gfxWait < cameraRender * 0.5f;
            if (!cpuRenderDominant && cameraRender < 5f) return null;

            float confidence;
            string evidence;
            float totalRenderMs = cameraRender + renderLoop;

            if (totalRenderMs > 15f)
            {
                confidence = 0.80f;
                evidence   = $"Camera.Render = {cameraRender:F2}ms (CPU render thread bận). " +
                             $"SetPass Calls và Draw Calls đang gây CPU overhead. " +
                             $"Gfx.Wait = {gfxWait:F2}ms (thấp → CPU là bottleneck, không phải GPU).";
            }
            else if (totalRenderMs > 8f)
            {
                confidence = 0.50f;
                evidence   = $"Camera.Render = {cameraRender:F2}ms. CPU đang mất nhiều thời gian thiết lập render state.";
            }
            else
            {
                confidence = 0.20f;
                evidence   = $"Camera.Render = {cameraRender:F2}ms — render overhead vừa phải.";
            }

            return new BottleneckFinding
            {
                Type       = BottleneckType.CPURenderThread,
                Confidence = confidence,
                Evidence   = evidence,
                TimeMs     = totalRenderMs,
                Fix        = "Bật SRP Batcher, kiểm tra CBUFFER compatibility, GPU Instancing, giảm Material diversity"
            };
        }

        // ─── Case 4: Physics ─────────────────────────────────────────────────
        private static BottleneckFinding CheckPhysics(FrameSnapshot s)
        {
            float physicsProcess = s.SumTime(MARKER_PHYSICS_PROCESS);
            float physicsFetch   = s.SumTime(MARKER_PHYSICS_FETCH);
            float physicsFixed   = s.SumTime(MARKER_PHYSICS_UPDATE);
            float totalPhysics   = physicsProcess + physicsFetch + physicsFixed;

            if (totalPhysics < 1.0f) return null;

            float confidence;
            string evidence;

            if (totalPhysics >= PHYSICS_CRITICAL_MS)
            {
                confidence = 0.85f;
                evidence   = $"Physics tổng = {totalPhysics:F2}ms (Critical!). " +
                             $"Processing={physicsProcess:F2}ms, FetchResults={physicsFetch:F2}ms. " +
                             $"Quá nhiều Rigidbody/Collider hoặc FixedUpdate quá tải.";
            }
            else if (totalPhysics >= PHYSICS_HIGH_MS)
            {
                confidence = 0.60f;
                evidence   = $"Physics tổng = {totalPhysics:F2}ms. " +
                             $"Processing={physicsProcess:F2}ms — đang ở mức cao.";
            }
            else
            {
                confidence = 0.25f;
                evidence   = $"Physics = {totalPhysics:F2}ms (thấp, không phải bottleneck chính).";
            }

            return new BottleneckFinding
            {
                Type       = BottleneckType.Physics,
                Confidence = confidence,
                Evidence   = evidence,
                TimeMs     = totalPhysics,
                Fix        = "Giảm số Rigidbody, tăng Fixed Timestep, simplify Collider mesh, Layer Collision Matrix"
            };
        }

        // ─── Case 5: Scripting ────────────────────────────────────────────────
        private static BottleneckFinding CheckScripting(FrameSnapshot s)
        {
            float behaviourUpdate = s.SumTime(MARKER_BEHAVIOUR_UPDATE);
            float fixedUpdate     = s.SumTime(MARKER_FIXED_UPDATE);
            float lateUpdate      = s.SumTime(MARKER_LATE_UPDATE);
            float coroutines      = s.SumTime(MARKER_COROUTINE);
            float totalScript     = behaviourUpdate + lateUpdate + coroutines;

            if (totalScript < 2.0f) return null;

            float confidence;
            string evidence;

            // Tìm MonoBehaviour scripts tốn kém nhất trong BehaviourUpdate
            var updateChildren = s.FindAll(MARKER_BEHAVIOUR_UPDATE)
                                  .SelectMany(b => b.Children)
                                  .OrderByDescending(c => c.TotalTimeMs)
                                  .Take(5)
                                  .ToList();

            string topScripts = updateChildren.Count > 0
                ? "\nTop scripts: " + string.Join(", ",
                    updateChildren.Select(c => $"{c.Name}({c.TotalTimeMs:F2}ms)"))
                : "";

            if (totalScript >= SCRIPTING_CRITICAL_MS)
            {
                confidence = 0.85f;
                evidence   = $"Script execution = {totalScript:F2}ms (Critical!). " +
                             $"BehaviourUpdate={behaviourUpdate:F2}ms, Late={lateUpdate:F2}ms, " +
                             $"Coroutines={coroutines:F2}ms.{topScripts}";
            }
            else if (totalScript >= SCRIPTING_HIGH_MS)
            {
                confidence = 0.60f;
                evidence   = $"Script execution = {totalScript:F2}ms. " +
                             $"BehaviourUpdate={behaviourUpdate:F2}ms.{topScripts}";
            }
            else
            {
                confidence = 0.25f;
                evidence   = $"Script execution = {totalScript:F2}ms — không phải bottleneck chính.";
            }

            return new BottleneckFinding
            {
                Type       = BottleneckType.Scripting,
                Confidence = confidence,
                Evidence   = evidence,
                TimeMs     = totalScript,
                Fix        = "Tối ưu script nặng nhất, dùng Burst Compiler / Jobs, tránh GetComponent trong Update()"
            };
        }

        // ─── Case 6: UI / Canvas ─────────────────────────────────────────────
        private static BottleneckFinding CheckUICanvas(FrameSnapshot s)
        {
            float canvasBuild  = s.SumTime(MARKER_CANVAS_BUILD);
            float canvasSend   = s.SumTime(MARKER_CANVAS_SEND);
            float canvasRebuild = s.SumTime(MARKER_CANVAS_REBUILD_ALL);
            float eventSystem  = s.SumTime(MARKER_EVENT_SYSTEM);
            float totalCanvas  = canvasBuild + canvasSend + canvasRebuild;

            if (totalCanvas < 0.5f) return null;

            float confidence;
            string evidence;

            if (totalCanvas >= CANVAS_CRITICAL_MS)
            {
                confidence = 0.85f;
                evidence   = $"Canvas rebuild = {totalCanvas:F2}ms (Critical!). " +
                             $"BuildBatch={canvasBuild:F2}ms, SendWillRender={canvasSend:F2}ms, " +
                             $"EventSystem={eventSystem:F2}ms. Canvas đang rebuild toàn bộ geometry.";
            }
            else if (totalCanvas >= CANVAS_HIGH_MS)
            {
                confidence = 0.60f;
                evidence   = $"Canvas rebuild = {totalCanvas:F2}ms. " +
                             $"BuildBatch={canvasBuild:F2}ms — Canvas bị dirty và rebuild.";
            }
            else
            {
                confidence = 0.20f;
                evidence   = $"Canvas rebuild = {totalCanvas:F2}ms — nhỏ.";
            }

            return new BottleneckFinding
            {
                Type       = BottleneckType.UICanvas,
                Confidence = confidence,
                Evidence   = evidence,
                TimeMs     = totalCanvas,
                Fix        = "Tách UI tĩnh/động sang Canvas riêng, tránh SetActive() trên Canvas child trong Update(), dùng Object Pooling cho UI elements"
            };
        }

        // ─── Case 7: Animation ───────────────────────────────────────────────
        private static BottleneckFinding CheckAnimation(FrameSnapshot s)
        {
            float animUpdate = s.SumTime(MARKER_ANIMATOR_UPDATE);
            float animSample = s.SumTime(MARKER_ANIM_SAMPLE);
            float animApply  = s.SumTime(MARKER_ANIM_APPLY);
            float totalAnim  = animUpdate + animSample + animApply;

            if (totalAnim < 0.5f) return null;

            float confidence;
            string evidence;

            if (totalAnim >= ANIMATION_CRITICAL_MS)
            {
                confidence = 0.75f;
                evidence   = $"Animation = {totalAnim:F2}ms. " +
                             $"Animator.Update={animUpdate:F2}ms, SampleAnimation={animSample:F2}ms. " +
                             $"Quá nhiều Animator đang active hoặc BlendTree phức tạp.";
            }
            else if (totalAnim >= ANIMATION_HIGH_MS)
            {
                confidence = 0.45f;
                evidence   = $"Animation = {totalAnim:F2}ms. Animator.Update={animUpdate:F2}ms.";
            }
            else
            {
                return null; // Không đáng kể
            }

            return new BottleneckFinding
            {
                Type       = BottleneckType.Animation,
                Confidence = confidence,
                Evidence   = evidence,
                TimeMs     = totalAnim,
                Fix        = "Tắt Animator khi không nhìn thấy (Culling Mode=Cull Completely), giảm Animation Complexity, dùng Animator.SetBool/Trigger thay vì Update liên tục"
            };
        }

        // ─── Case 8: Asset Loading ────────────────────────────────────────────
        private static BottleneckFinding CheckAssetLoading(FrameSnapshot s)
        {
            float loadingRead     = s.SumTime(MARKER_LOADING_READ);
            float loadingComplete = s.SumTime(MARKER_LOADING_COMPLETE);
            float assetBundle     = s.SumTime(MARKER_ASSET_BUNDLE);
            float instantiate     = s.SumTime(MARKER_INSTANTIATE);
            float totalLoad       = loadingRead + loadingComplete + assetBundle;

            bool hasLoadingMarker = s.HasMarker(MARKER_LOADING_READ) ||
                                    s.HasMarker(MARKER_LOADING_COMPLETE) ||
                                    s.HasMarker(MARKER_ASSET_BUNDLE);

            if (!hasLoadingMarker && totalLoad < 0.5f) return null;

            float confidence;
            string evidence;

            if (totalLoad > 5f || s.HasMarker(MARKER_LOADING_READ))
            {
                confidence = 0.90f;
                evidence   = $"Asset Loading trên MAIN THREAD! Loading.ReadObject={loadingRead:F2}ms, " +
                             $"LoadingComplete={loadingComplete:F2}ms, AssetBundle={assetBundle:F2}ms. " +
                             $"Tải file trên main thread gây hitching nghiêm trọng không thể tránh khỏi.";
            }
            else
            {
                confidence = 0.50f;
                evidence   = $"Asset Loading = {totalLoad:F2}ms. " +
                             $"Có tải asset xảy ra trong frame này.";
            }

            return new BottleneckFinding
            {
                Type       = BottleneckType.AssetLoading,
                Confidence = confidence,
                Evidence   = evidence,
                TimeMs     = totalLoad,
                Fix        = "Dùng LoadAsync/LoadAssetAsync, pre-load trong background, Addressables async loading, tránh Resources.Load trong gameplay"
            };
        }

        // ─── Case 9: Audio ────────────────────────────────────────────────────
        private static BottleneckFinding CheckAudio(FrameSnapshot s)
        {
            float audioUpdate = s.SumTime(MARKER_AUDIO_UPDATE);
            float audioThread = s.SumTime(MARKER_AUDIO_THREAD);
            float totalAudio  = audioUpdate + audioThread;

            if (totalAudio < AUDIO_HIGH_MS) return null;

            float confidence;
            string evidence;

            if (totalAudio > 5f)
            {
                confidence = 0.70f;
                evidence   = $"Audio = {totalAudio:F2}ms (High!). " +
                             $"AudioManager.Update={audioUpdate:F2}ms. " +
                             $"Có thể do quá nhiều AudioSource active hoặc Decompress on Load tốn CPU.";
            }
            else
            {
                confidence = 0.35f;
                evidence   = $"Audio = {totalAudio:F2}ms — cao hơn bình thường.";
            }

            return new BottleneckFinding
            {
                Type       = BottleneckType.Audio,
                Confidence = confidence,
                Evidence   = evidence,
                TimeMs     = totalAudio,
                Fix        = "Giảm AudioSource active, dùng Vorbis compression, Streaming cho nhạc dài, giảm DSP Buffer Size"
            };
        }

        // ─── Case 10: VSync Wait ──────────────────────────────────────────────
        private static BottleneckFinding CheckVSync(FrameSnapshot s)
        {
            float vsyncWait = s.SumTime(MARKER_WAIT_FPS);
            float present   = s.SumTime(MARKER_GFXDEVICE_PRESENT);
            float frameTotal = s.TotalCpuTimeMs;

            if (vsyncWait < VSYNC_HIGH_MS) return null;

            // VSync wait lớn = CPU idle chờ GPU hoặc chờ frame boundary
            float pct = frameTotal > 0 ? vsyncWait / frameTotal : 0f;

            float confidence;
            string evidence;

            if (vsyncWait > 10f && pct > 0.3f)
            {
                confidence = 0.70f;
                evidence   = $"WaitForTargetFPS = {vsyncWait:F2}ms ({pct * 100:F0}% frame). " +
                             $"CPU idle đợi frame boundary. " +
                             (pct > 0.5f ? "Frame này CPU có nhiều thời gian rảnh — không phải spike thực sự."
                                         : "Có thể GPU-bound hoặc App.targetFrameRate quá thấp.");
            }
            else
            {
                confidence = 0.30f;
                evidence   = $"WaitForTargetFPS = {vsyncWait:F2}ms.";
            }

            return new BottleneckFinding
            {
                Type       = BottleneckType.VSync,
                Confidence = confidence,
                Evidence   = evidence,
                TimeMs     = vsyncWait,
                Fix        = "Nếu VSync chiếm nhiều = frame bình thường (CPU rảnh). Tắt VSync khi profile để thấy real CPU time."
            };
        }

        // ─── Case 11: Job System Stall ────────────────────────────────────────
        private static BottleneckFinding CheckJobSystemStall(FrameSnapshot s)
        {
            float jobWait    = s.SumTime(MARKER_JOB_WAIT);
            float workSteal  = s.SumTime(MARKER_JOB_WORK_STEAL);
            float jobHandle  = s.SumTime(MARKER_WORKER_THREAD);
            float totalJob   = jobWait + workSteal + jobHandle;

            if (totalJob < 1.0f) return null;

            float confidence;
            string evidence;

            if (totalJob > 8f)
            {
                confidence = 0.75f;
                evidence   = $"Job System stall = {totalJob:F2}ms. " +
                             $"WaitForJob={jobWait:F2}ms, WaitingForWorker={workSteal:F2}ms. " +
                             $"Main thread đang block chờ Job hoàn thành.";
            }
            else if (totalJob > 3f)
            {
                confidence = 0.45f;
                evidence   = $"Job stall = {totalJob:F2}ms. Main thread chờ Job System.";
            }
            else
            {
                confidence = 0.20f;
                evidence   = $"Job stall nhỏ = {totalJob:F2}ms.";
            }

            return new BottleneckFinding
            {
                Type       = BottleneckType.JobSystemStall,
                Confidence = confidence,
                Evidence   = evidence,
                TimeMs     = totalJob,
                Fix        = "Schedule Jobs sớm hơn, Complete() muộn hơn, tránh immediate Complete() ngay sau Schedule()"
            };
        }

        // ══════════════════════════════════════════════════════════════════════
        // Helpers
        // ══════════════════════════════════════════════════════════════════════

        private static void BuildKeyMetrics(SpikeAnalysisReport report, FrameSnapshot s)
        {
            report.KeyMetrics.Add($"Frame Time: {s.TotalCpuTimeMs:F2}ms (target 60fps = {TARGET_60FPS_MS:F1}ms)");
            report.KeyMetrics.Add($"GC Alloc: {FormatBytes(s.TotalGCAllocBytes)}/frame");

            if (s.TotalGpuTimeMs > 0)
                report.KeyMetrics.Add($"GPU Time: {s.TotalGpuTimeMs:F2}ms");

            float gfxWait = s.SumTime(MARKER_GFX_WAIT);
            if (gfxWait > 1f)
                report.KeyMetrics.Add($"Gfx.WaitForPresent: {gfxWait:F2}ms ({gfxWait/s.TotalCpuTimeMs*100:F0}%)");

            float cameraRender = s.SumTime(MARKER_CAMERA_RENDER);
            if (cameraRender > 1f)
                report.KeyMetrics.Add($"Camera.Render: {cameraRender:F2}ms");

            float physics = s.SumTime(MARKER_PHYSICS_PROCESS) + s.SumTime(MARKER_PHYSICS_FETCH);
            if (physics > 1f)
                report.KeyMetrics.Add($"Physics: {physics:F2}ms");

            float scripts = s.SumTime(MARKER_BEHAVIOUR_UPDATE) + s.SumTime(MARKER_LATE_UPDATE);
            if (scripts > 1f)
                report.KeyMetrics.Add($"Scripts (Update/Late): {scripts:F2}ms");

            float canvas = s.SumTime(MARKER_CANVAS_BUILD) + s.SumTime(MARKER_CANVAS_SEND);
            if (canvas > 0.5f)
                report.KeyMetrics.Add($"Canvas Rebuild: {canvas:F2}ms");
        }

        private static void BuildRecommendations(
            SpikeAnalysisReport report,
            List<BottleneckFinding> findings,
            FrameSnapshot s)
        {
            // Primary recommendation
            if (findings.Count > 0)
            {
                report.Recommendations.Add($"[CHÍNH] {findings[0].Fix}");
            }

            // Type-specific detailed recommendations
            switch (report.PrimaryBottleneck)
            {
                case BottleneckType.GarbageCollection:
                    report.Recommendations.Add("Object Pooling: dùng Unity ObjectPool<T> (2021+) cho Bullets, Particles, UI");
                    report.Recommendations.Add("Tránh LINQ trong Update() — mỗi .Where()/.Select() tạo closure allocation");
                    report.Recommendations.Add("Dùng TryGetValue thay vì ContainsKey + indexer (tránh double-lookup)");
                    report.Recommendations.Add("Strings: dùng string.Format hoặc StringBuilder, tránh $\"{x}\" trong hot paths");
                    report.Recommendations.Add("Kiểm tra Optifunity Code Analysis tab — GCAllocAnalyzer đã đánh dấu các điểm vi phạm");
                    break;

                case BottleneckType.GPUBound:
                    report.Recommendations.Add("Tắt HDR trên mobile: URP Asset → Quality → HDR = Disabled");
                    report.Recommendations.Add("Giảm Shadow Resolution: URP Asset → Shadows → Main Light Shadow Resolution → 512");
                    report.Recommendations.Add("Tắt SSAO Renderer Feature (dùng Baked GTAO thay thế)");
                    report.Recommendations.Add("Giảm overdraw: dùng Occlusion Culling, tránh transparent quá nhiều lớp");
                    report.Recommendations.Add($"ASTC {Core.PlatformConfig.GetRecommendedASTCBlockSize()}: nén texture để giảm GPU bandwidth");
                    break;

                case BottleneckType.CPURenderThread:
                    report.Recommendations.Add("Kiểm tra SRP Batcher: Optifunity URP tab → SRPBatcherChecker");
                    report.Recommendations.Add("GPU Instancing cho cỏ, cây, các vật thể đồng nhất (>100 instances)");
                    report.Recommendations.Add("Static Batching cho geometry tĩnh (không di chuyển trong scene)");
                    report.Recommendations.Add("Hợp nhất Materials: giảm số Material khác nhau trong scene giảm SetPass Calls");
                    report.Recommendations.Add("Tắt Dynamic Batching nếu SRP Batcher đang bật (hai cơ chế xung đột)");
                    break;

                case BottleneckType.UICanvas:
                    report.Recommendations.Add("Tách Canvas: 1 Canvas cho UI tĩnh (headers, HUD tĩnh), 1 Canvas cho UI động (health bar, updates)");
                    report.Recommendations.Add("Tránh SetActive() trên Canvas children — dùng CanvasGroup.alpha = 0 thay thế");
                    report.Recommendations.Add("Disable Canvas, không Destroy khi ẩn popup → tránh rebuild batch");
                    report.Recommendations.Add("Object Pooling cho list items (ScrollView với nhiều item)");
                    break;

                case BottleneckType.Scripting:
                    var topScripts = s.FindAll(MARKER_BEHAVIOUR_UPDATE)
                                      .SelectMany(b => b.Children)
                                      .OrderByDescending(c => c.TotalTimeMs)
                                      .Take(3).ToList();

                    foreach (var sc in topScripts)
                        report.Recommendations.Add($"Tối ưu '{sc.Name}': {sc.TotalTimeMs:F2}ms/frame → profile bên trong");

                    report.Recommendations.Add("Chuyển heavy compute sang Burst Compiled Job (IJob + [BurstCompile])");
                    report.Recommendations.Add("Cache Component references trong Awake(), tránh GetComponent<>() trong Update()");
                    break;

                case BottleneckType.AssetLoading:
                    report.Recommendations.Add("KHẨN CẤP: Không bao giờ load asset synchronously trong gameplay!");
                    report.Recommendations.Add("Dùng: Resources.LoadAsync<T>() hoặc Addressables.LoadAssetAsync<T>()");
                    report.Recommendations.Add("Pre-load assets trong Loading Screen trước khi vào gameplay scene");
                    report.Recommendations.Add("AssetBundle: LoadFromFileAsync, không LoadFromFile()");
                    break;

                case BottleneckType.Physics:
                    report.Recommendations.Add("Tăng Fixed Timestep: Edit → Project Settings → Time → Fixed Timestep = 0.02 → 0.033");
                    report.Recommendations.Add("Simplify Collider: Box/Sphere/Capsule thay vì Mesh Collider cho dynamic objects");
                    report.Recommendations.Add("Layer Collision Matrix: Project Settings → Physics → tắt layer pairs không cần thiết");
                    report.Recommendations.Add("Dùng Physics.queriesHitTriggers = false nếu không cần trigger queries");
                    break;
            }

            // Contributing factors recommendations
            foreach (var cf in report.ContributingFactors.Take(2))
                if (!string.IsNullOrEmpty(cf.Fix))
                    report.Recommendations.Add($"[PHỤ - {cf.Type}] {cf.Fix}");

            // General recommendation nếu frame > 2x target
            if (report.FrameTotalMs > TARGET_60FPS_MS * 2)
                report.Recommendations.Add($"Frame này ({report.FrameTotalMs:F1}ms) gấp {report.FrameTotalMs/TARGET_60FPS_MS:F1}x target 60fps. " +
                                           $"Cần giải quyết đồng thời nhiều vấn đề, bắt đầu với bottleneck chính.");
        }

        private static SpikeSeverity CalcSeverity(float frameMs, float avgMs)
        {
            if (avgMs <= 0) avgMs = TARGET_60FPS_MS;
            float ratio = frameMs / avgMs;
            if (ratio >= 4.0f) return SpikeSeverity.Critical;
            if (ratio >= 2.0f) return SpikeSeverity.High;
            if (ratio >= 1.5f) return SpikeSeverity.Medium;
            return SpikeSeverity.Low;
        }

        private static string GetBottleneckTitle(BottleneckType type) => type switch
        {
            BottleneckType.GarbageCollection => "Garbage Collection — GC.Collect kích hoạt",
            BottleneckType.GPUBound          => "GPU-Bound — CPU chờ GPU (Gfx.WaitForPresent cao)",
            BottleneckType.CPURenderThread   => "CPU Render Thread — Camera.Render / SetPass Calls quá tải",
            BottleneckType.Physics           => "Physics — Physics.Processing chiếm quá nhiều thời gian",
            BottleneckType.Scripting         => "Scripting — Scripts (BehaviourUpdate) chiếm quá nhiều CPU",
            BottleneckType.UICanvas          => "UI / Canvas — Canvas.BuildBatch rebuild geometry",
            BottleneckType.Animation         => "Animation — Animator.Update tốn kém",
            BottleneckType.AssetLoading      => "Asset Loading trên Main Thread — Gây hitching nghiêm trọng",
            BottleneckType.Audio             => "Audio — AudioManager.Update cao bất thường",
            BottleneckType.VSync             => "VSync Wait — CPU nhàn rỗi chờ frame boundary / GPU",
            BottleneckType.JobSystemStall    => "Job System Stall — Main thread chờ Job hoàn thành",
            BottleneckType.Mixed             => "Mixed Bottleneck — Nhiều nguyên nhân đồng thời",
            BottleneckType.Balanced          => "Frame bình thường — Không phát hiện bottleneck",
            _                               => "Unknown — Không đủ dữ liệu"
        };

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1048576) return $"{bytes / 1048576f:F1}MB";
            if (bytes >= 1024)    return $"{bytes / 1024f:F1}KB";
            return $"{bytes}B";
        }
    }
}
