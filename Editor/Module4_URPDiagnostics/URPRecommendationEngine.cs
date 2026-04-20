using System.Collections.Generic;
using Optifunity.Editor.Core;
using Optifunity.Runtime;

namespace Optifunity.Editor.Module4
{
    /// <summary>
    /// Engine tạo recommendations dựa trên ma trận PC vs Mobile từ tài liệu kỹ thuật.
    /// Phân tích URPSettingsSnapshot và sinh PerformanceIssues cụ thể theo nền tảng.
    /// </summary>
    public static class URPRecommendationEngine
    {
        /// <summary>
        /// Phân tích snapshot cấu hình URP và sinh danh sách issues
        /// </summary>
        public static List<PerformanceIssue> Analyze(URPAssetScanner.URPSettingsSnapshot snapshot)
        {
            var issues = new List<PerformanceIssue>();

            if (!snapshot.IsURPActive)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Error,
                    Title         = "URP không được kích hoạt trong project",
                    Description   = "Không tìm thấy Universal Render Pipeline Asset trong Graphics Settings.",
                    FixSuggestion = "Vào Project Settings → Graphics → Scriptable Render Pipeline Settings và gán URP Asset."
                });
                return issues;
            }

            bool isMobile = PlatformConfig.IsMobile;

            // ════════════════════════════════════════════════════════════════════
            // 1. SRP Batcher — QUAN TRỌNG NHẤT
            // ════════════════════════════════════════════════════════════════════
            if (!snapshot.SRPBatcherEnabled)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Error,
                    Title         = "SRP Batcher bị TẮT — CPU render thread quá tải",
                    Description   = "SRP Batcher là cơ chế tối ưu hóa Draw Calls quan trọng nhất trong URP. " +
                                    "Khi tắt, CPU phải thực hiện SetPass Calls tốn kém cho mỗi Material " +
                                    "thay vì dùng Constant Buffer (CBUFFER) để batch chúng lại. " +
                                    "Điều này gây nghẽn cổ chai nghiêm trọng trên Render Thread.",
                    AssetPath     = snapshot.AssetPath,
                    FixSuggestion = "Bật SRP Batcher trong URP Asset (Universal Render Pipeline Asset → Advanced → SRP Batcher). " +
                                    "Đảm bảo tất cả Custom Shaders đóng gói properties vào CBUFFER_START/CBUFFER_END."
                });
            }
            else
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Info,
                    Title         = "✓ SRP Batcher đang BẬT",
                    Description   = "SRP Batcher tối ưu hóa Draw Call bằng CBUFFER — giảm SetPass Calls đáng kể.",
                    FixSuggestion = "Kiểm tra Custom Shaders có CBUFFER compatibility. Xem SRPBatcherChecker."
                });
            }

            // ════════════════════════════════════════════════════════════════════
            // 2. HDR
            // ════════════════════════════════════════════════════════════════════
            if (isMobile && snapshot.HdrEnabled)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Warning,
                    Title         = "HDR bật trên Mobile — băng thông Color Buffer tăng gấp đôi",
                    Description   = "HDR yêu cầu Color Buffer FP16 thay vì RGB8, tăng gấp đôi băng thông bộ nhớ " +
                                    "trên GPU di động. GPU kiến trúc TBDR (Tile-Based) sẽ bị ảnh hưởng nặng nhất.",
                    AssetPath     = snapshot.AssetPath,
                    FixSuggestion = "Tắt HDR trong URP Asset → Quality → HDR nếu không có hiệu ứng Bloom/Exposure " +
                                    "đòi hỏi giá trị màu > 1.0. Đây là trade-off: chất lượng ánh sáng vs. hiệu năng."
                });
            }

            // ════════════════════════════════════════════════════════════════════
            // 3. Opaque Texture & Depth Texture
            // ════════════════════════════════════════════════════════════════════
            if (isMobile && snapshot.OpaqueTextureEnabled)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Warning,
                    Title         = "Opaque Texture bật trên Mobile — thêm render pass copy màn hình",
                    Description   = "Opaque Texture ra lệnh URP chụp lại màn hình vào texture mới sau khi vẽ opaque objects. " +
                                    "Nếu không có Shader nào dùng _CameraOpaqueTexture (fx nước, khúc xạ), " +
                                    "đây là lãng phí render pass hoàn toàn.",
                    AssetPath     = snapshot.AssetPath,
                    FixSuggestion = "Kiểm tra Shader Graph: nếu không có node 'Scene Color' nào, tắt Opaque Texture. " +
                                    "Vào URP Asset → Rendering → Opaque Texture = Disabled."
                });
            }

            if (isMobile && snapshot.DepthTextureEnabled)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Info,
                    Title         = "Depth Texture bật trên Mobile — kiểm tra có thực sự cần không",
                    Description   = "Depth Texture cần cho SSAO, soft particles, và depth-based effects. " +
                                    "Nếu không dùng các effect này, nên tắt để tiết kiệm render pass.",
                    AssetPath     = snapshot.AssetPath,
                    FixSuggestion = "Nếu không dùng SSAO, soft particles, hoặc ShaderGraph Scene Depth node, " +
                                    "tắt Depth Texture trong URP Asset."
                });
            }

            // ════════════════════════════════════════════════════════════════════
            // 4. Shadows
            // ════════════════════════════════════════════════════════════════════
            if (isMobile)
            {
                if (snapshot.AdditionalLightShadowsEnabled)
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Warning,
                        Title         = "Additional Light Shadows bật trên Mobile — tốn kém GPU",
                        Description   = "Mỗi Additional Light có bóng đổ đòi hỏi GPU vẽ lại toàn bộ geometry " +
                                        "từ góc nhìn của ánh sáng (shadow depth pass). Trên mobile, " +
                                        "đây là một trong những nguyên nhân phổ biến nhất gây giảm FPS.",
                        AssetPath     = snapshot.AssetPath,
                        FixSuggestion = "Tắt Additional Light Shadows. Dùng Light Probes + Baked Lighting " +
                                        "cho ánh sáng động phụ. Chỉ giữ bóng cho Main Light (sun)."
                    });
                }

                if (snapshot.ShadowCascadeCount > 2)
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Warning,
                        Title         = $"Shadow Cascade Count quá cao cho Mobile: {snapshot.ShadowCascadeCount}",
                        Description   = $"Cascade Count = {snapshot.ShadowCascadeCount} yêu cầu vẽ shadow map " +
                                        $"{snapshot.ShadowCascadeCount} lần. Trên mobile, khuyến nghị ≤ 2.",
                        AssetPath     = snapshot.AssetPath,
                        FixSuggestion = "Giảm Shadow Cascade Count về 1 hoặc 2 trong URP Asset → Shadows."
                    });
                }

                if (snapshot.SoftShadowsEnabled)
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Info,
                        Title         = "Soft Shadows bật trên Mobile — cân nhắc dùng Hard Shadows",
                        Description   = "Soft Shadows đẹp hơn nhưng tốn thêm shader samples. " +
                                        "Trên thiết bị mid-range, Hard Shadows thường là lựa chọn tốt hơn.",
                        AssetPath     = snapshot.AssetPath,
                        FixSuggestion = "Đổi sang Hard Shadows trong URP Asset nếu không đủ GPU budget."
                    });
                }

                if (snapshot.MainLightShadowResolution > 1024)
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity      = IssueSeverity.Warning,
                        Title         = $"Shadow Atlas Resolution quá lớn: {snapshot.MainLightShadowResolution}px",
                        Description   = $"Shadow map {snapshot.MainLightShadowResolution}x{snapshot.MainLightShadowResolution} " +
                                        $"chiếm nhiều VRAM. Trên mobile, 512-1024 là đủ.",
                        AssetPath     = snapshot.AssetPath,
                        FixSuggestion = $"Giảm Main Light Shadow Resolution về 512 hoặc 1024 trong URP Asset → Shadows."
                    });
                }
            }

            // ════════════════════════════════════════════════════════════════════
            // 5. MSAA
            // ════════════════════════════════════════════════════════════════════
            if (isMobile && snapshot.MsaaSampleCount > 2)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Warning,
                    Title         = $"MSAA x{snapshot.MsaaSampleCount} trên Mobile — xem xét giảm hoặc dùng FXAA",
                    Description   = $"MSAA {snapshot.MsaaSampleCount}x tăng băng thông memory đáng kể trên GPU TBDR mobile. " +
                                    $"Kiến trúc TBDR thực ra xử lý MSAA hiệu quả hơn PC, nhưng giá trị cao (4x, 8x) vẫn tốn kém.",
                    AssetPath     = snapshot.AssetPath,
                    FixSuggestion = "Giảm MSAA về 2x hoặc tắt MSAA và dùng Post-Processing FXAA thay thế " +
                                    "(chất lượng thấp hơn nhưng hiệu năng tốt hơn nhiều)."
                });
            }

            // ════════════════════════════════════════════════════════════════════
            // 6. Dynamic Batching
            // ════════════════════════════════════════════════════════════════════
            if (snapshot.DynamicBatchingEnabled && snapshot.SRPBatcherEnabled)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Info,
                    Title         = "Dynamic Batching bật cùng với SRP Batcher — có thể không cần thiết",
                    Description   = "Khi SRP Batcher đang hoạt động, Dynamic Batching hầu như không mang lại lợi ích. " +
                                    "Dynamic Batching chỉ gộp objects < 300 vertices — nhưng với SRP Batcher đang bật, " +
                                    "CPU vẫn phải kiểm tra compatibility và merge geometry.",
                    AssetPath     = snapshot.AssetPath,
                    FixSuggestion = "Tắt Dynamic Batching nếu SRP Batcher đang hoạt động tốt. " +
                                    "Kiểm tra qua Frame Debugger xem batches có giảm không khi tắt."
                });
            }

            // ════════════════════════════════════════════════════════════════════
            // 7. Additional Light Count
            // ════════════════════════════════════════════════════════════════════
            if (isMobile && snapshot.AdditionalLightCount > 4)
            {
                issues.Add(new PerformanceIssue
                {
                    Severity      = IssueSeverity.Warning,
                    Title         = $"Per-Object Additional Light Count cao: {snapshot.AdditionalLightCount}",
                    Description   = $"Mỗi object có thể bị ảnh hưởng bởi tới {snapshot.AdditionalLightCount} additional lights. " +
                                    $"Trên mobile, con số này nên ≤ 4 (khuyến nghị 1-2).",
                    AssetPath     = snapshot.AssetPath,
                    FixSuggestion = "Giảm Additional Lights Per Object Limit về 2-4 trong URP Asset → Lighting. " +
                                    "Dùng Light Probes và Baked Lighting thay vì real-time additional lights."
                });
            }

            return issues;
        }
    }
}
