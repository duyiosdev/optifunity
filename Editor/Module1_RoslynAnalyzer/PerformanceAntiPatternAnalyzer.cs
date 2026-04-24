using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using Optifunity.Editor.Core;

namespace Optifunity.Editor.Module1
{
    public static class PerformanceAntiPatternAnalyzer
    {
        private static readonly Regex RxLoopMethod = new(
            @"void\s+(Update|LateUpdate|FixedUpdate|OnGUI)\s*\(\s*\)", RegexOptions.Compiled);
        
        private static readonly Regex RxExplicitLoop = new(
            @"\b(for|while|foreach)\s*\(", RegexOptions.Compiled);

        private static readonly Regex RxSearchMethods = new(
            @"\b(Find|FindWithTag|FindGameObjectsWithTag|FindObjectOfType|FindObjectsOfType|GetComponent|GetComponentInChildren|GetComponentInParent)\b\s*[\(<]", RegexOptions.Compiled);

        private static readonly Regex RxTagCompare = new(
            @"\.\s*tag\s*==\s*""|\.\s*tag\s*!=\s*""|""\s*==\s*[a-zA-Z0-9_\.]+\.\s*tag|""\s*!=\s*[a-zA-Z0-9_\.]+\.\s*tag", RegexOptions.Compiled);

        private static readonly Regex RxCameraMain = new(
            @"\bCamera\s*\.\s*main\b", RegexOptions.Compiled);

        private static readonly Regex RxPhysicsAll = new(
            @"\bPhysics\s*\.\s*(OverlapSphere|OverlapBox|OverlapCapsule|RaycastAll|SphereCastAll|BoxCastAll|CapsuleCastAll)\b", RegexOptions.Compiled);

        private static readonly Regex RxPropertyAlloc = new(
            @"\.\s*(materials|vertices|normals|triangles|uv)\b", RegexOptions.Compiled);

        public static List<PerformanceIssue> Analyze(string filePath, string[] lines)
        {
            var issues = new List<PerformanceIssue>();
            string fileName = Path.GetFileName(filePath);

            bool insideLoopMethod = false;
            int methodBraceDepth = 0;
            int totalBraceDepth = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//")) continue;

                // Track Method entry
                if (RxLoopMethod.IsMatch(trimmed))
                {
                    insideLoopMethod = true;
                    methodBraceDepth = totalBraceDepth;
                }

                bool hasOpen = line.Contains("{");
                bool hasClose = line.Contains("}");
                totalBraceDepth += CountChars(line, '{') - CountChars(line, '}');

                // Track Method exit
                if (insideLoopMethod && totalBraceDepth <= methodBraceDepth && hasClose)
                {
                    insideLoopMethod = false;
                }

                bool isDirectLoop = RxExplicitLoop.IsMatch(trimmed);
                bool isHotPath = insideLoopMethod || isDirectLoop;

                int lineNo = i + 1;
                string loc = $"{fileName}:{lineNo}";

                // 1. Search Methods
                if (RxSearchMethods.IsMatch(trimmed))
                {
                    var sev = isHotPath ? IssueSeverity.Error : IssueSeverity.Info;
                    string context = isHotPath ? "trong vòng lặp/Update (Cựu chậm)" : "ngoài vòng lặp (Đắt đỏ)";
                    issues.Add(new PerformanceIssue
                    {
                        Severity = sev,
                        Title = "Lạm dụng hàm Tìm Kiếm (Find/GetComponent)",
                        Description = $"Phát hiện lệnh tìm kiếm {context}. Các hàm này tốn O(n) tài nguyên traverse Hierarchy.",
                        CodeLocation = loc,
                        FixSuggestion = "Cache reference ở Awake/Start hoặc truyền trực tiếp qua Inspector."
                    });
                }

                // 2. Camera.main
                if (RxCameraMain.IsMatch(trimmed))
                {
                    // Chỉ cảnh báo nếu ở trong hot path (Update/Loop). 
                    // Nếu ở ngoài (Awake/Start) thì coi như là cache hợp lệ, bỏ qua.
                    if (isHotPath)
                    {
                        issues.Add(new PerformanceIssue
                        {
                            Severity = IssueSeverity.Error,
                            Title = "Sử dụng Camera.main trong vòng lặp",
                            Description = "Camera.main thực chất là FindWithTag(\"MainCamera\") ngầm, chạy mỗi khi gọi.",
                            CodeLocation = loc,
                            FixSuggestion = "Cache Camera.main vào một biến private ở Awake hoặc Start."
                        });
                    }
                }

                // 3. Tag Comparison
                if (RxTagCompare.IsMatch(trimmed))
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Title = "So sánh Tag bằng chuỗi",
                        Description = "go.tag == \"String\" tạo ra một bản sao chuỗi mới (GC Alloc) trước khi so sánh.",
                        CodeLocation = loc,
                        FixSuggestion = "Sử dụng go.CompareTag(\"String\") để tối ưu bộ nhớ."
                    });
                }

                // 4. Physics All
                if (RxPhysicsAll.IsMatch(trimmed))
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Title = "Hàm Vật lý gây GC Alloc (Array return)",
                        Description = "Các hàm vật lý dạng trả về Array luôn tạo mảng mới mỗi lần gọi.",
                        CodeLocation = loc,
                        FixSuggestion = "Sử dụng biến thể NonAlloc (ví dụ OverlapSphereNonAlloc) với buffer truyền sẵn."
                    });
                }

                // 5. Property Alloc
                if (isHotPath && RxPropertyAlloc.IsMatch(trimmed))
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Title = "Truy xuất Property sinh bản sao mảng",
                        Description = "Truy cập .materials hoặc .vertices trả về một bản clone mới của mảng.",
                        CodeLocation = loc,
                        FixSuggestion = "Cache mảng ra biến cục bộ hoặc dùng sharedMaterials nếu phù hợp."
                    });
                }

                // 6. OnGUI check
                if (trimmed.Contains("void OnGUI()"))
                {
                    issues.Add(new PerformanceIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Title = "Sử dụng OnGUI()",
                        Description = "OnGUI cực kỳ tốn hiệu năng và sinh nhiều rác bộ nhớ.",
                        CodeLocation = loc,
                        FixSuggestion = "Sử dụng UI Toolkit hoặc Unity UI cho giao diện Production."
                    });
                }
                
                // 7. Empty Update
                if (insideLoopMethod && trimmed == "{")
                {
                    // Check next line or look for empty body - simpler check: if it just has } close by
                    if (i + 1 < lines.Length && lines[i+1].Trim() == "}")
                    {
                        issues.Add(new PerformanceIssue
                        {
                            Severity = IssueSeverity.Warning,
                            Title = "Hàm Lifecycle rỗng",
                            Description = "Hàm Update/FixedUpdate rỗng vẫn tốn chi phí gọi từ C++ sang C#.",
                            CodeLocation = loc,
                            FixSuggestion = "Xóa bỏ các hàm lifecycle rỗng."
                        });
                    }
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
