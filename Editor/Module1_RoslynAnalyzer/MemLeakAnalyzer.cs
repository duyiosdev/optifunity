using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Optifunity.Editor.Module1
{
    /// <summary>
    /// Phát hiện các pattern gây rò rỉ bộ nhớ (Memory Leaks) trong môi trường GC của Unity:
    /// 1. Static event đăng ký += mà không có -= trong OnDestroy/OnDisable
    /// 2. Coroutine while(true) không có điều kiện dừng liên kết với lifecycle
    /// 3. Singleton giữ tham chiếu đến scene objects
    /// </summary>
    public static class MemLeakAnalyzer
    {
        // Phát hiện đăng ký sự kiện tĩnh
        private static readonly Regex RxEventSubscribe = new(
            @"\b\w+\.\w+\s*\+=\s*",
            RegexOptions.Compiled);

        // Phát hiện hủy đăng ký
        private static readonly Regex RxEventUnsubscribe = new(
            @"\b\w+\.\w+\s*-=\s*",
            RegexOptions.Compiled);

        // Phát hiện OnDestroy / OnDisable method
        private static readonly Regex RxOnDestroy = new(
            @"void\s+(OnDestroy|OnDisable)\s*\(\s*\)",
            RegexOptions.Compiled);

        // Phát hiện static event declaration
        private static readonly Regex RxStaticEvent = new(
            @"\bstatic\s+event\s+",
            RegexOptions.Compiled);

        // Phát hiện Coroutine với while(true)
        private static readonly Regex RxCoroutineInfinite = new(
            @"while\s*\(\s*true\s*\)",
            RegexOptions.Compiled);

        // Phát hiện StartCoroutine
        private static readonly Regex RxStartCoroutine = new(
            @"\bStartCoroutine\s*\(",
            RegexOptions.Compiled);

        // Phát hiện IEnumerator (Coroutine function)
        private static readonly Regex RxIEnumerator = new(
            @"\bIEnumerator\b",
            RegexOptions.Compiled);

        // Phát hiện DontDestroyOnLoad — Singleton pattern
        private static readonly Regex RxDontDestroy = new(
            @"\bDontDestroyOnLoad\s*\(",
            RegexOptions.Compiled);

        // Phát hiện pattern Singleton giữ reference list/array sang MonoBehaviour
        private static readonly Regex RxSingletonRef = new(
            @"\bprivate\s+(static\s+)?(List|Dictionary|Array|HashSet)<.*>(Component|Behaviour|GameObject).*>",
            RegexOptions.Compiled);

        public static List<Core.PerformanceIssue> Analyze(string filePath, string[] lines)
        {
            var issues   = new List<Core.PerformanceIssue>();
            string fileName = Path.GetFileName(filePath);
            string fullText = string.Join("\n", lines);

            // ─── Check 1: Static event += mà không có -= ───────────────────────────
            bool hasStaticEvent      = RxStaticEvent.IsMatch(fullText);
            bool hasSubscribe        = RxEventSubscribe.IsMatch(fullText);
            bool hasUnsubscribe      = RxEventUnsubscribe.IsMatch(fullText);
            bool hasOnDestroyMethod  = RxOnDestroy.IsMatch(fullText);

            if (hasSubscribe && !hasUnsubscribe)
            {
                // Tìm dòng đăng ký sự kiện
                for (int i = 0; i < lines.Length; i++)
                {
                    if (RxEventSubscribe.IsMatch(lines[i]) && !lines[i].Trim().StartsWith("//"))
                    {
                        issues.Add(new Core.PerformanceIssue
                        {
                            Severity      = Core.IssueSeverity.Error,
                            Title         = "Memory Leak: Đăng ký sự kiện (+= ) mà không có hủy đăng ký (-=)",
                            Description   = "Đối tượng đăng ký sự kiện qua += nhưng không có -= trong OnDestroy/OnDisable. " +
                                            "Tham chiếu tĩnh từ event handler sẽ giữ đối tượng sống mãi trên heap.",
                            CodeLocation  = $"{fileName}:L{i + 1}",
                            FixSuggestion = "Thêm void OnDestroy() {{ SomeEvent -= OnEvent; }} tương ứng với mọi += trong Awake/OnEnable."
                        });
                        break;
                    }
                }
            }

            if (hasSubscribe && hasUnsubscribe && !hasOnDestroyMethod)
            {
                issues.Add(new Core.PerformanceIssue
                {
                    Severity      = Core.IssueSeverity.Warning,
                    Title         = "Thiếu OnDestroy() để hủy đăng ký sự kiện",
                    Description   = "File có đăng ký/hủy sự kiện nhưng không có method OnDestroy() — " +
                                    "hủy đăng ký có thể không được gọi khi GameObject bị Destroy.",
                    CodeLocation  = $"{fileName}",
                    FixSuggestion = "Đặt -= bên trong void OnDestroy() hoặc void OnDisable() để đảm bảo cleanup luôn xảy ra."
                });
            }

            // ─── Check 2: Coroutine while(true) vô hạn ──────────────────────────
            bool insideCoroutine = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (RxIEnumerator.IsMatch(trimmed)) insideCoroutine = true;

                if (insideCoroutine && RxCoroutineInfinite.IsMatch(trimmed))
                {
                    // Kiểm tra có điều kiện dừng dựa trên gameObject/enabled không
                    bool hasStopCondition = fullText.Contains("gameObject.activeInHierarchy") ||
                                           fullText.Contains("enabled") ||
                                           fullText.Contains("destroyCancellationToken") ||
                                           fullText.Contains("StopCoroutine");

                    if (!hasStopCondition)
                    {
                        issues.Add(new Core.PerformanceIssue
                        {
                            Severity      = Core.IssueSeverity.Error,
                            Title         = "Memory Leak: Coroutine while(true) không có điều kiện dừng lifecycle",
                            Description   = "Vòng lặp while(true) trong Coroutine sẽ giữ toàn bộ stack state và " +
                                            "biến cục bộ trên heap sau khi GameObject bị phá hủy.",
                            CodeLocation  = $"{fileName}:L{i + 1}",
                            FixSuggestion = "Đổi thành `while (gameObject.activeInHierarchy)` hoặc " +
                                            "dùng `destroyCancellationToken` (Unity 2022+). " +
                                            "Gọi StopCoroutine() trong OnDestroy()."
                        });
                    }
                    break;
                }
            }

            // ─── Check 3: DontDestroyOnLoad Singleton giữ references ────────────
            bool isDontDestroySingleton = RxDontDestroy.IsMatch(fullText);
            if (isDontDestroySingleton && RxSingletonRef.IsMatch(fullText))
            {
                issues.Add(new Core.PerformanceIssue
                {
                    Severity      = Core.IssueSeverity.Warning,
                    Title         = "Singleton DontDestroyOnLoad giữ tham chiếu đến scene-specific objects",
                    Description   = "Singleton tồn tại qua nhiều Scene nhưng lưu trữ danh sách tham chiếu " +
                                    "đến Component/GameObject trong Scene — các objects này sẽ không được GC dọn sạch.",
                    CodeLocation  = fileName,
                    FixSuggestion = "Xóa/clear danh sách tham chiếu trong OnDestroy() của scene objects. " +
                                    "Singleton chỉ nên lưu data, không lưu references đến scene objects. " +
                                    "Dùng WeakReference<T> nếu cần theo dõi tùy chọn."
                });
            }

            // ─── Check 4: static List/Dictionary không được clear ────────────────
            Regex rxStaticCollection = new(
                @"\bstatic\s+(List|Dictionary|HashSet|Queue|Stack)<",
                RegexOptions.Compiled);
            if (rxStaticCollection.IsMatch(fullText))
            {
                bool hasClear = fullText.Contains(".Clear()") || fullText.Contains(".Remove(");
                if (!hasClear)
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (rxStaticCollection.IsMatch(lines[i]))
                        {
                            issues.Add(new Core.PerformanceIssue
                            {
                                Severity      = Core.IssueSeverity.Warning,
                                Title         = "Static collection không được dọn dẹp",
                                Description   = "Static List/Dictionary tích lũy dữ liệu vô thời hạn — đây là nguồn rò rỉ bộ nhớ phổ biến.",
                                CodeLocation  = $"{fileName}:L{i + 1}",
                                FixSuggestion = "Gọi .Clear() khi chuyển Scene hoặc dùng SceneManager.sceneUnloaded callback để dọn dẹp."
                            });
                            break;
                        }
                    }
                }
            }

            return issues;
        }
    }
}
