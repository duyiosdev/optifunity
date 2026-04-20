using UnityEditor;
using UnityEngine;

namespace Optifunity.Editor.UI
{
    /// <summary>
    /// Tập trung định nghĩa GUIStyle và màu sắc cho toàn bộ Editor UI của Optifunity.
    /// </summary>
    public static class OptifunityStyles
    {
        // ─── Colors ───────────────────────────────────────────────────────────
        public static readonly Color ColorError    = new(0.95f, 0.25f, 0.25f);
        public static readonly Color ColorWarning  = new(1.00f, 0.78f, 0.20f);
        public static readonly Color ColorInfo     = new(0.30f, 0.65f, 1.00f);
        public static readonly Color ColorSuccess  = new(0.30f, 0.90f, 0.50f);

        public static readonly Color BgDark        = new(0.13f, 0.14f, 0.16f);
        public static readonly Color BgCard        = new(0.18f, 0.20f, 0.23f);
        public static readonly Color BgCardHover   = new(0.22f, 0.25f, 0.29f);
        public static readonly Color BgHeader      = new(0.10f, 0.12f, 0.15f);
        public static readonly Color AccentBlue    = new(0.25f, 0.55f, 0.95f);
        public static readonly Color AccentPurple  = new(0.55f, 0.30f, 0.95f);
        public static readonly Color TextPrimary   = new(0.92f, 0.94f, 0.98f);
        public static readonly Color TextSecondary = new(0.60f, 0.65f, 0.75f);

        // ─── Lazy-loaded Styles ───────────────────────────────────────────────
        private static GUIStyle _styleTitle;
        private static GUIStyle _styleSubtitle;
        private static GUIStyle _styleCard;
        private static GUIStyle _styleLabelError;
        private static GUIStyle _styleLabelWarning;
        private static GUIStyle _styleLabelInfo;
        private static GUIStyle _styleLabelSuccess;
        private static GUIStyle _styleHeader;
        private static GUIStyle _styleIssueTitle;
        private static GUIStyle _styleIssueDesc;
        private static GUIStyle _styleBadge;
        private static GUIStyle _styleButtonAutoFix;
        private static GUIStyle _styleSeparator;
        private static Texture2D _texCard;
        private static Texture2D _texHeader;
        private static Texture2D _texDark;
        private static Texture2D _texError;
        private static Texture2D _texWarning;
        private static Texture2D _texInfo;
        private static Texture2D _texSuccess;

        public static GUIStyle StyleTitle => _styleTitle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 20,
            alignment = TextAnchor.MiddleLeft,
            normal    = { textColor = TextPrimary },
            margin    = new RectOffset(4, 4, 8, 4)
        };

        public static GUIStyle StyleSubtitle => _styleSubtitle ??= new GUIStyle(EditorStyles.label)
        {
            fontSize  = 11,
            alignment = TextAnchor.MiddleLeft,
            normal    = { textColor = TextSecondary },
            margin    = new RectOffset(4, 4, 0, 4)
        };

        public static GUIStyle StyleHeader => _styleHeader ??= new GUIStyle()
        {
            fontSize  = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal    = { textColor = TextPrimary, background = TexHeader },
            padding   = new RectOffset(10, 0, 8, 8),
            margin    = new RectOffset(0, 0, 8, 4)
        };

        public static GUIStyle StyleCard => _styleCard ??= new GUIStyle()
        {
            normal  = { background = TexCard },
            padding = new RectOffset(10, 10, 8, 8),
            margin  = new RectOffset(4, 4, 3, 3)
        };

        public static GUIStyle StyleIssueTitle => _styleIssueTitle ??= new GUIStyle(EditorStyles.label)
        {
            fontSize  = 11,
            fontStyle = FontStyle.Bold,
            wordWrap  = true,
            normal    = { textColor = TextPrimary }
        };

        public static GUIStyle StyleIssueDesc => _styleIssueDesc ??= new GUIStyle(EditorStyles.label)
        {
            fontSize  = 10,
            wordWrap  = true,
            normal    = { textColor = TextSecondary }
        };

        public static GUIStyle StyleLabelError => _styleLabelError ??= MakeColorLabel(ColorError, true);
        public static GUIStyle StyleLabelWarning => _styleLabelWarning ??= MakeColorLabel(ColorWarning, true);
        public static GUIStyle StyleLabelInfo    => _styleLabelInfo    ??= MakeColorLabel(ColorInfo, false);
        public static GUIStyle StyleLabelSuccess => _styleLabelSuccess ??= MakeColorLabel(ColorSuccess, false);

        public static GUIStyle StyleButtonAutoFix => _styleButtonAutoFix ??= new GUIStyle(GUI.skin.button)
        {
            fontSize  = 10,
            fontStyle = FontStyle.Bold,
            normal    = { textColor = Color.white, background = MakeTexture(AccentBlue) },
            hover     = { textColor = Color.white, background = MakeTexture(AccentBlue * 1.15f) },
            padding   = new RectOffset(8, 8, 4, 4),
            margin    = new RectOffset(2, 2, 2, 2)
        };

        // ─── Textures ─────────────────────────────────────────────────────────
        public static Texture2D TexCard    => _texCard    ??= MakeTexture(BgCard);
        public static Texture2D TexHeader  => _texHeader  ??= MakeTexture(BgHeader);
        public static Texture2D TexDark    => _texDark    ??= MakeTexture(BgDark);
        public static Texture2D TexError   => _texError   ??= MakeTexture(ColorError   * 0.35f);
        public static Texture2D TexWarning => _texWarning ??= MakeTexture(ColorWarning * 0.35f);
        public static Texture2D TexInfo    => _texInfo    ??= MakeTexture(ColorInfo    * 0.35f);
        public static Texture2D TexSuccess => _texSuccess ??= MakeTexture(ColorSuccess * 0.35f);

        // ─── Health Score Gradient ────────────────────────────────────────────
        public static Color GetHealthColor(int score)
        {
            if (score >= 80) return ColorSuccess;
            if (score >= 50) return ColorWarning;
            return ColorError;
        }

        public static string GetSeverityIcon(Core.IssueSeverity severity) => severity switch
        {
            Core.IssueSeverity.Error   => "●",
            Core.IssueSeverity.Warning => "◆",
            Core.IssueSeverity.Info    => "○",
            _ => "○"
        };

        public static GUIStyle GetSeverityStyle(Core.IssueSeverity severity) => severity switch
        {
            Core.IssueSeverity.Error   => StyleLabelError,
            Core.IssueSeverity.Warning => StyleLabelWarning,
            Core.IssueSeverity.Info    => StyleLabelInfo,
            _ => StyleLabelInfo
        };

        public static Texture2D GetSeverityBg(Core.IssueSeverity severity) => severity switch
        {
            Core.IssueSeverity.Error   => TexError,
            Core.IssueSeverity.Warning => TexWarning,
            Core.IssueSeverity.Info    => TexInfo,
            _ => TexCard
        };

        // ─── Helpers ──────────────────────────────────────────────────────────
        private static GUIStyle MakeColorLabel(Color color, bool bold)
        {
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 10,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                normal    = { textColor = color }
            };
            return style;
        }

        public static Texture2D MakeTexture(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        /// <summary>Vẽ separator line</summary>
        public static void DrawSeparator(float thickness = 1f)
        {
            var rect = GUILayoutUtility.GetRect(0, thickness, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.3f, 0.35f, 0.4f, 0.6f));
            GUILayout.Space(2);
        }

        /// <summary>Vẽ badge badge (pill shape) với màu sắc</summary>
        public static void DrawBadge(string text, Color color)
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal       = { textColor = color, background = MakeTexture(color * 0.25f) },
                padding      = new RectOffset(6, 6, 2, 2),
                margin       = new RectOffset(2, 2, 0, 0),
                border       = new RectOffset(4, 4, 4, 4),
                fontSize     = 9,
                fontStyle    = FontStyle.Bold,
                alignment    = TextAnchor.MiddleCenter
            };
            GUILayout.Label(text, style, GUILayout.ExpandWidth(false));
        }
    }
}
