using System.Collections.Generic;
using UnityEditor;

namespace Optifunity.Editor.Module5
{
    /// <summary>
    /// Xây dựng bản đồ ánh xạ nhanh từ Tên Class (do Profiler xuất ra) 
    /// tới đường dẫn file C# tương ứng để phục vụ tính năng "Locate Asset".
    /// </summary>
    public static class EditorCodeLocator
    {
        private static Dictionary<string, string> _scriptLookup;
        private static bool _isInitialized = false;

        public static void Initialize()
        {
            if (_isInitialized) return;

            _scriptLookup = new Dictionary<string, string>();
            string[] guids = AssetDatabase.FindAssets("t:MonoScript");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null)
                {
                    var clsName = script.GetClass()?.Name;
                    if (clsName != null && !_scriptLookup.ContainsKey(clsName))
                    {
                        _scriptLookup[clsName] = path;
                    }
                    else
                    {
                        // Fallback theo tên file nếu không parse được class
                        string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                        if (!_scriptLookup.ContainsKey(fileName))
                        {
                            _scriptLookup[fileName] = path;
                        }
                    }
                }
            }

            _isInitialized = true;
        }

        public static void ForceRebuild()
        {
            _isInitialized = false;
            Initialize();
        }

        /// <summary>
        /// Thử tìm định vị đường dẫn file từ tên Sample của Profiler.
        /// Thường Profiler sẽ trả về "ClassName.MethodName()".
        /// </summary>
        public static string FindScriptPath(string profilerSampleName)
        {
            if (!_isInitialized) Initialize();

            // Extract class name from something like "MyScript.Update()" -> "MyScript"
            int dotIndex = profilerSampleName.IndexOf('.');
            string candidate = dotIndex > 0 
                ? profilerSampleName.Substring(0, dotIndex) 
                : profilerSampleName;
                
            // Remove namespaces if present (ex: "MyNamespace.MyClass.Method" -> "MyClass")
            int lastDotBeforeMethod = candidate.LastIndexOf('.');
            if(lastDotBeforeMethod > 0 && dotIndex != lastDotBeforeMethod)
            {
                 candidate = profilerSampleName.Substring(lastDotBeforeMethod + 1, dotIndex - lastDotBeforeMethod - 1);
            }

            // Cleanup any trailing parentheses just in case
            int parenIndex = candidate.IndexOf('(');
            if (parenIndex > 0)
                candidate = candidate.Substring(0, parenIndex);

            candidate = candidate.Trim();

            if (_scriptLookup.TryGetValue(candidate, out string path))
            {
                return path;
            }

            // --- Try Shaders ---
            string[] shaders = AssetDatabase.FindAssets(candidate + " t:Shader");
            if (shaders.Length > 0) return AssetDatabase.GUIDToAssetPath(shaders[0]);

            // Fallback: Tìm full string
            if (_scriptLookup.TryGetValue(profilerSampleName, out string exactPath))
            {
                return exactPath;
            }

            return null; // Không tìm thấy (có thể là code C++ của Unity Engine)
        }
    }
}
