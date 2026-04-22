using System;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Optifunity.Editor.Module5
{
    /// <summary>
    /// Helper class to access internal Profiler APIs safely across Unity versions.
    ///
    /// Root cause of InvalidCastException: selectedFrameIndex on ProfilerWindow
    /// may return a boxed Unity internal struct (not a plain long/int), so
    /// Convert.ToInt32(object) itself throws InvalidCastException.
    ///
    /// Fix: Use pattern matching `is` to safely unbox any numeric type,
    /// then fall back to string parsing as last resort.
    /// </summary>
    public static class ProfilerHelper
    {
        private static readonly Type         _profilerDriverType;
        private static readonly Type         _profilerWindowType;
        private static readonly PropertyInfo _selectedFrameProp;     // ProfilerDriver.selectedFrame
        private static readonly PropertyInfo _windowFrameProp;       // ProfilerWindow.selectedFrameIndex
        private static readonly PropertyInfo _firstFrameProp;        // ProfilerDriver.firstFrameIndex
        private static readonly PropertyInfo _lastFrameProp;         // ProfilerDriver.lastFrameIndex

        static ProfilerHelper()
        {
            _profilerDriverType = typeof(ProfilerDriver);

            _profilerWindowType = Type.GetType("UnityEditor.ProfilerWindow, UnityEditor.CoreModule")
                               ?? Type.GetType("UnityEditor.ProfilerWindow, UnityEditor");

            const BindingFlags STATIC   = BindingFlags.Public | BindingFlags.Static   | BindingFlags.NonPublic;
            const BindingFlags INSTANCE = BindingFlags.Public | BindingFlags.Instance  | BindingFlags.NonPublic;

            _selectedFrameProp = _profilerDriverType.GetProperty("selectedFrame", STATIC);
            _firstFrameProp    = _profilerDriverType.GetProperty("firstFrameIndex", STATIC);
            _lastFrameProp     = _profilerDriverType.GetProperty("lastFrameIndex",  STATIC);

            if (_profilerWindowType != null)
                _windowFrameProp = _profilerWindowType.GetProperty("selectedFrameIndex", INSTANCE);
        }

        // ─── Public Getters ────────────────────────────────────────────────────

        public static int GetSelectedFrame()
        {
            // Method 1: ProfilerDriver.selectedFrame (pre-2021.1 or still present internally)
            if (_selectedFrameProp != null)
            {
                int v = SafeGetInt(_selectedFrameProp, null);
                if (v >= 0) return v;
            }

            // Method 2: ProfilerWindow.selectedFrameIndex (Unity 2021.1+, returns long)
            if (_profilerWindowType != null && _windowFrameProp != null)
            {
                try
                {
                    var wins = Resources.FindObjectsOfTypeAll(_profilerWindowType);
                    if (wins != null && wins.Length > 0)
                    {
                        int v = SafeGetInt(_windowFrameProp, wins[0]);
                        if (v >= 0) return v;
                    }
                }
                catch { /* window access failed */ }
            }

            return -1;
        }

        public static int GetFirstFrame() => SafeGetInt(_firstFrameProp, null);
        public static int GetLastFrame()  => SafeGetInt(_lastFrameProp,  null);

        public static void SetSelectedFrame(int frameIndex)
        {
            if (frameIndex < 0) return;

            // Method 1: ProfilerDriver.selectedFrame
            if (_selectedFrameProp != null && _selectedFrameProp.CanWrite)
            {
                try { _selectedFrameProp.SetValue(null, frameIndex); return; }
                catch { /* fall through */ }
            }

            // Method 2: ProfilerWindow.selectedFrameIndex
            if (_profilerWindowType != null && _windowFrameProp != null)
            {
                try
                {
                    var wins = Resources.FindObjectsOfTypeAll(_profilerWindowType);
                    if (wins != null && wins.Length > 0)
                    {
                        // Match declared type exactly to avoid TargetException
                        Type   pt  = _windowFrameProp.PropertyType;
                        object val = pt == typeof(long) ? (object)(long)frameIndex : (object)frameIndex;
                        _windowFrameProp.SetValue(wins[0], val);
                        (wins[0] as EditorWindow)?.Repaint();
                    }
                }
                catch { /* no Profiler window open */ }
            }
        }

        // ─── Private Helper ────────────────────────────────────────────────────

        /// <summary>
        /// Safely read an integer property via reflection.
        /// Handles: int, long, uint, short, byte + any type whose ToString() parses to int.
        /// Returns -1 on any failure.
        /// </summary>
        private static int SafeGetInt(PropertyInfo prop, object target)
        {
            if (prop == null) return -1;
            try
            {
                object raw = prop.GetValue(target);
                if (raw == null) return -1;

                // Pattern match: avoids InvalidCastException entirely
                if (raw is int    i)  return i;
                if (raw is long   l)  return (int)Math.Max(-1, Math.Min(int.MaxValue, l));
                if (raw is uint   u)  return (int)u;
                if (raw is short  s)  return s;
                if (raw is ushort us) return us;
                if (raw is byte   b)  return b;

                // Last resort: parse from string representation
                if (int.TryParse(raw.ToString(), out int parsed)) return parsed;
            }
            catch { /* reflection or access error — return -1 */ }

            return -1;
        }
    }
}
