using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Optifunity.Editor.Module1
{
    /// <summary>
    /// Phát hiện hiện tượng Boxing — ép kiểu value type sang reference type/interface,
    /// gây cấp phát bộ nhớ trên Managed Heap không cần thiết.
    /// </summary>
    public static class BoxingAnalyzer
    {
        // Struct/value-type phổ biến của Unity và C#
        private static readonly string[] CommonValueTypes =
        {
            "int", "float", "double", "bool", "long", "byte", "short",
            "Vector2", "Vector3", "Vector4", "Quaternion", "Color", "Color32",
            "Rect", "RectInt", "Bounds", "BoundsInt", "Ray", "RaycastHit",
            "Matrix4x4", "Plane"
        };

        // Phát hiện cast explicit sang object: (object)myStruct
        private static readonly Regex RxExplicitObjectCast = new(
            @"\(object\)\s*[a-zA-Z_]\w*",
            RegexOptions.Compiled);

        // Phát hiện truyền struct vào hàm nhận object: SomeMethod(myVector)
        // Heuristic: biến hoa đầu nhưng không phải class method call
        private static readonly Regex RxObjectParamCall = new(
            @"\bstring\.Format\s*\([^)]*\)|Debug\.(Log|LogWarning|LogError)\s*\([^)]*\+",
            RegexOptions.Compiled);

        // Phát hiện dùng Enum với Dictionary<string, object> hay Hashtable
        private static readonly Regex RxEnumInHashtable = new(
            @"\bHashtable\b|\bArrayList\b|\bIDictionary\b",
            RegexOptions.Compiled);

        // Phát hiện so sánh enum với object: if (myEnum == someObject)
        private static readonly Regex RxEnumObjectCompare = new(
            @"\b(enum\s+\w+|\w+\s*==\s*null)\s",
            RegexOptions.Compiled);

        // Interface tham số — có thể nhận value type và boxing
        private static readonly Regex RxInterfaceParam = new(
            @":\s*I[A-Z][a-zA-Z]+\b",
            RegexOptions.Compiled);

        // Phát hiện struct implements interface và được dùng trực tiếp qua interface
        private static readonly Regex RxStructDef = new(
            @"\bstruct\s+\w+\s*:\s*I[A-Z]",
            RegexOptions.Compiled);

        public static List<Core.PerformanceIssue> Analyze(string filePath, string[] lines)
        {
            var issues   = new List<Core.PerformanceIssue>();
            string fileName = Path.GetFileName(filePath);

            // Track struct definitions with interfaces
            bool hasStructWithInterface = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line    = lines[i];
                string trimmed = line.Trim();
                if (trimmed.StartsWith("//")) continue;

                int    lineNo = i + 1;
                string loc    = $"{fileName}:L{lineNo}";

                // --- Check: Explicit object cast ---
                if (RxExplicitObjectCast.IsMatch(trimmed))
                {
                    issues.Add(new Core.PerformanceIssue
                    {
                        Severity      = Core.IssueSeverity.Error,
                        Title         = "Boxing: Cast value type sang object",
                        Description   = $"Ép kiểu tường minh sang `object` buộc CLR sao chép struct lên Managed Heap.",
                        CodeLocation  = loc,
                        FixSuggestion = "Tránh truyền struct vào các hàm nhận System.Object. Dùng generic methods <T> thay thế."
                    });
                }

                // --- Check: string.Format hoặc Debug.Log với struct concatenation ---
                if (RxObjectParamCall.IsMatch(trimmed))
                {
                    issues.Add(new Core.PerformanceIssue
                    {
                        Severity      = Core.IssueSeverity.Warning,
                        Title         = "Boxing tiềm ẩn: struct/value-type trong string.Format / Debug.Log",
                        Description   = $"Truyền value type (int, Vector3, enum...) vào Debug.Log/string.Format " +
                                        $"có thể gây boxing implicit.",
                        CodeLocation  = loc,
                        FixSuggestion = "Gọi .ToString() tường minh trước khi nối chuỗi: `myVector.ToString()` " +
                                        "hoặc dùng string interpolation $\"...\" có overload generic."
                    });
                }

                // --- Check: Non-generic collections (ArrayList, Hashtable) ---
                if (RxEnumInHashtable.IsMatch(trimmed))
                {
                    issues.Add(new Core.PerformanceIssue
                    {
                        Severity      = Core.IssueSeverity.Error,
                        Title         = "Non-generic collection gây boxing liên tục",
                        Description   = $"ArrayList, Hashtable lưu trữ System.Object — mọi value type thêm vào đều bị boxing.",
                        CodeLocation  = loc,
                        FixSuggestion = "Thay ArrayList → List<T>, Hashtable → Dictionary<K,V> với T/K/V là kiểu cụ thể."
                    });
                }

                // --- Check: Struct implementing interface ---
                if (RxStructDef.IsMatch(trimmed))
                {
                    hasStructWithInterface = true;
                    issues.Add(new Core.PerformanceIssue
                    {
                        Severity      = Core.IssueSeverity.Warning,
                        Title         = "Struct implement Interface — nguy cơ boxing khi dùng qua interface variable",
                        Description   = $"Khi struct được gán cho biến kiểu Interface (IMyInterface x = myStruct), " +
                                        $"CLR sẽ boxing struct đó lên heap.",
                        CodeLocation  = loc,
                        FixSuggestion = "Dùng struct qua generic constraint (where T : IMyInterface) để tránh boxing. " +
                                        "Tránh gán struct vào interface variable."
                    });
                }
            }

            return issues;
        }
    }
}
