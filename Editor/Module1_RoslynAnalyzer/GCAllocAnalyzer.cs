using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Optifunity.Editor.Module1
{
    /// <summary>
    /// Phân tích GC Allocation trong các Unity message methods.
    /// Phát hiện các cấu trúc mã sinh rác bộ nhớ trong Update/FixedUpdate/LateUpdate.
    /// </summary>
    public static class GCAllocAnalyzer
    {
        // Regex nhận diện Unity loop methods
        private static readonly Regex RxLoopMethod = new(
            @"void\s+(Update|LateUpdate|FixedUpdate|OnGUI)\s*\(\s*\)",
            RegexOptions.Compiled);

        // Phát hiện nối chuỗi "+" — thường tạo String allocation
        private static readonly Regex RxStringConcat = new(
            @"""[^""]*""\s*\+|[a-zA-Z_]\w*\s*\+\s*""",
            RegexOptions.Compiled);

        // Phát hiện `new T(` hoặc `new T[` trong body — GC allocation
        private static readonly Regex RxNewAlloc = new(
            @"\bnew\s+[A-Z][a-zA-Z0-9_<>]*\s*[\(\[]",
            RegexOptions.Compiled);

        // Phát hiện foreach — có thể tạo enumerator allocation nếu collection không tối ưu
        private static readonly Regex RxForeach = new(
            @"\bforeach\s*\(",
            RegexOptions.Compiled);

        // Phát hiện lambda với capture
        private static readonly Regex RxLambdaCapture = new(
            @"=>\s*\{[^}]*[a-zA-Z_]\w+\s*[;,\)]",
            RegexOptions.Compiled);

        // Phát hiện return new array
        private static readonly Regex RxReturnNewArray = new(
            @"\breturn\s+new\s+\w+\s*\[",
            RegexOptions.Compiled);

        public static List<Core.PerformanceIssue> Analyze(string filePath, string[] lines)
        {
            var issues = new List<Core.PerformanceIssue>();

            bool   insideLoopMethod = false;
            int    braceDepth       = 0;
            int    methodStartDepth = 0;
            string fileName         = Path.GetFileName(filePath);

            for (int i = 0; i < lines.Length; i++)
            {
                string line    = lines[i];
                string trimmed = line.Trim();

                // Phát hiện vào loop method
                if (RxLoopMethod.IsMatch(trimmed) && !trimmed.StartsWith("//"))
                {
                    insideLoopMethod = true;
                    methodStartDepth = braceDepth;
                }

                // Đếm brace để xác định ra khỏi method
                braceDepth += Count(line, '{') - Count(line, '}');
                if (insideLoopMethod && braceDepth <= methodStartDepth)
                    insideLoopMethod = false;

                if (!insideLoopMethod || trimmed.StartsWith("//")) continue;

                int   lineNo  = i + 1;
                string loc    = $"{fileName}:L{lineNo}";

                // --- Check: String concatenation ---
                if (RxStringConcat.IsMatch(trimmed))
                {
                    issues.Add(new Core.PerformanceIssue
                    {
                        Severity      = Core.IssueSeverity.Warning,
                        Title         = "String concatenation trong vòng lặp gameplay",
                        Description   = $"Nối chuỗi bằng toán tử `+` tạo ra đối tượng String mới mỗi frame, gây GC pressure.",
                        CodeLocation  = loc,
                        FixSuggestion = "Sử dụng StringBuilder.AppendLine() hoặc string.Format() một lần bên ngoài vòng lặp."
                    });
                }

                // --- Check: new allocation ---
                if (RxNewAlloc.IsMatch(trimmed) && !trimmed.Contains("//"))
                {
                    // Loại trừ new với built-in value types phổ biến
                    bool isValueType = Regex.IsMatch(trimmed,
                        @"\bnew\s+(Vector2|Vector3|Quaternion|Color|Rect|int|float|bool)\s*\(");
                    if (!isValueType)
                    {
                        issues.Add(new Core.PerformanceIssue
                        {
                            Severity      = Core.IssueSeverity.Error,
                            Title         = "Cấp phát đối tượng mới (new) trong vòng lặp gameplay",
                            Description   = $"Gọi `new` trong Update()/FixedUpdate() mỗi frame tích lũy rác bộ nhớ, " +
                                            $"kích hoạt GC và gây giật lag. Tại {loc}.",
                            CodeLocation  = loc,
                            FixSuggestion = "Khởi tạo đối tượng ở Awake()/Start() và tái sử dụng. Dùng Object Pooling cho vật thể tạm thời."
                        });
                    }
                }

                // --- Check: foreach ---
                if (RxForeach.IsMatch(trimmed))
                {
                    issues.Add(new Core.PerformanceIssue
                    {
                        Severity      = Core.IssueSeverity.Warning,
                        Title         = "foreach trong vòng lặp gameplay",
                        Description   = $"foreach trên List<T>/Array thường an toàn, nhưng trên Dictionary hoặc " +
                                        $"IEnumerable tạo ra Enumerator object trên heap.",
                        CodeLocation  = loc,
                        FixSuggestion = "Dùng vòng lặp for thông thường. Nếu cần foreach, đảm bảo collection implements " +
                                        "struct-based enumerator (List<T>, T[])."
                    });
                }

                // --- Check: return new array ---
                if (RxReturnNewArray.IsMatch(trimmed))
                {
                    issues.Add(new Core.PerformanceIssue
                    {
                        Severity      = Core.IssueSeverity.Error,
                        Title         = "Return mảng mới trong vòng lặp gameplay",
                        Description   = $"Trả về mảng mới mỗi frame tạo allocation không cần thiết.",
                        CodeLocation  = loc,
                        FixSuggestion = "Điền dữ liệu vào mảng có sẵn (pre-allocated buffer), truyền vào như tham số out/ref."
                    });
                }

                // --- Check: lambda capture ---
                if (RxLambdaCapture.IsMatch(trimmed))
                {
                    issues.Add(new Core.PerformanceIssue
                    {
                        Severity      = Core.IssueSeverity.Warning,
                        Title         = "Lambda với capture biến trong vòng lặp gameplay",
                        Description   = $"Định nghĩa lambda bắt (capture) biến cục bộ buộc trình biên dịch tạo Display Class object trên heap.",
                        CodeLocation  = loc,
                        FixSuggestion = "Cách ly lambda ra ngoài Update(), hoặc sử dụng named method thay vì anonymous function."
                    });
                }
            }

            return issues;
        }

        private static int Count(string s, char c)
        {
            int count = 0;
            foreach (char ch in s) if (ch == c) count++;
            return count;
        }
    }
}
