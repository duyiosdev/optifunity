using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module1
{
    public static class ResourceManagementAnalyzer
    {
        private static readonly Regex RxEventSub = new(@"\b([a-zA-Z0-9_\.]+)\s*\+=\s*([a-zA-Z0-9_\.]+)", RegexOptions.Compiled);
        private static readonly Regex RxEventUnsub = new(@"\b([a-zA-Z0-9_\.]+)\s*-=\s*([a-zA-Z0-9_\.]+)", RegexOptions.Compiled);
        
        private static readonly Regex RxResourcesLoad = new(@"\bResources\s*\.\s*(Load|LoadAll)\b", RegexOptions.Compiled);
        private static readonly Regex RxInstantiateDestroy = new(@"\b(Instantiate|Destroy)\b\s*\(", RegexOptions.Compiled);
        private static readonly Regex RxExplicitLoop = new(@"\b(for|while|foreach)\s*\(", RegexOptions.Compiled);

        public static List<PerformanceIssue> Analyze(string filePath, string[] lines)
        {
            var issues = new List<PerformanceIssue>();
            string fileName = Path.GetFileName(filePath);

            HashSet<string> subs = new HashSet<string>();
            HashSet<string> unsubs = new HashSet<string>();
            List<(int line, string key, string raw)> subInstances = new List<(int, string, string)>();

            bool insideLoop = false;
            int totalBraceDepth = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//")) continue;

                int lineNo = i + 1;
                string loc = $"{fileName}:{lineNo}";

                // 1. Event Sub/Unsub tracking
                var matchSub = RxEventSub.Match(line);
                if (matchSub.Success)
                {
                    string key = matchSub.Groups[1].Value + " += " + matchSub.Groups[2].Value;
                    subs.Add(key);
                    subInstances.Add((lineNo, key, trimmed));
                }

                var matchUnsub = RxEventUnsub.Match(line);
                if (matchUnsub.Success)
                {
                    string key = matchUnsub.Groups[1].Value + " += " + matchUnsub.Groups[2].Value; // Match keys
                    unsubs.Add(key);
                }

                // 2. Resources.Load check
                if (RxResourcesLoad.IsMatch(trimmed))
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Title = "Sử dụng Resources.Load đồng bộ",
                        Description = "Thư mục Resources cản trở giảm dung lượng App. Tải tài nguyên trên main thread gây Spike.",
                        CodeLocation = loc,
                        FixSuggestion = "Chuyển sang LoadAsync hoặc dùng Addressables."
                    });
                }

                // 3. Instantiate/Destroy in Loops
                if (RxExplicitLoop.IsMatch(trimmed)) insideLoop = true;
                
                totalBraceDepth += CountChars(line, '{') - CountChars(line, '}');
                if (totalBraceDepth <= 0) insideLoop = false;

                if (insideLoop && RxInstantiateDestroy.IsMatch(trimmed))
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Title = "Instantiate/Destroy trong vòng lặp",
                        Description = "Khởi tạo/Xóa object liên tục phá vỡ batching và gây tốn kém CPU.",
                        CodeLocation = loc,
                        FixSuggestion = "Sử dụng Object Pooling."
                    });
                }
            }

            // Final check for leaks
            foreach (var sub in subInstances)
            {
                if (!unsubs.Contains(sub.key))
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Title = "Rò rỉ sự kiện (Event Leak)",
                        Description = $"Đăng ký sự kiện `{sub.raw}` nhưng không tìm thấy lệnh `-=` gỡ bỏ tương ứng.",
                        CodeLocation = $"{fileName}:{sub.line}",
                        FixSuggestion = "Đảm bảo luôn hủy đăng ký event trong OnDisable hoặc OnDestroy."
                    });
                }
            }

            return issues;
        }

        private static int CountChars(string s, char c)
        {
            int count = 0;
            if (string.IsNullOrEmpty(s)) return 0;
            for (int i = 0; i < s.Length; i++) if (s[i] == c) count++;
            return count;
        }
    }
}
