using UnityEngine;

namespace KspMp.Ui
{
    /// <summary>
    /// One dark skin shared by every KspMp window.
    ///
    /// KSP's own skin is a pale, ornate thing made for the game's dialogs. A mod window drawn in it washes out
    /// against a bright sky and reads as part of the game's furniture rather than as something separate. This
    /// derives a darker, flatter skin from it - keeping KSP's font, so it still belongs to the game - and adds
    /// the handful of styles the windows actually want: section panels, muted captions, a primary button, and
    /// status colours that mean the same thing everywhere.
    ///
    /// The backgrounds are drawn once into small textures and nine-sliced, which is what gives rounded corners
    /// and hairline borders without shipping any art. Two things about that are worth knowing. Building a
    /// texture per frame is the classic way a KSP mod leaks memory, so these are built once; and a texture made
    /// during one scene is destroyed when the next one loads unless it says otherwise, hence HideAndDontSave
    /// and the null check in Build that quietly rebuilds if it is ever destroyed anyway.
    /// </summary>
    internal static class Theme
    {
        /// <summary>Height of the title bar drawn into the window texture. Drag handles match it.</summary>
        public const int HeaderHeight = 26;

        /// <summary>One height for fields, buttons and the captions beside them, so rows line up.</summary>
        public const int ControlHeight = 26;

        public static readonly Color Ink    = Rgb(0xC9, 0xD2, 0xDD);
        public static readonly Color Dim    = Rgb(0x7B, 0x87, 0x97);
        public static readonly Color Accent = Rgb(0x4F, 0xD1, 0xA5);
        public static readonly Color Warn   = Rgb(0xE8, 0xB8, 0x4B);
        public static readonly Color Bad    = Rgb(0xFF, 0x6B, 0x6B);

        private static readonly Color WindowFill = Rgb(0x12, 0x15, 0x1A, 0.97f);
        private static readonly Color HeaderFill = Rgb(0x1B, 0x21, 0x2B, 0.98f);
        private static readonly Color PanelFill  = Rgb(0x19, 0x1E, 0x27, 0.85f);
        private static readonly Color FieldFill  = Rgb(0x0B, 0x0E, 0x13);
        private static readonly Color FieldHot   = Rgb(0x0F, 0x14, 0x1A);
        private static readonly Color ButtonFill = Rgb(0x22, 0x2A, 0x37);
        private static readonly Color ButtonHot  = Rgb(0x2E, 0x39, 0x49);
        private static readonly Color Line       = Rgb(0x2B, 0x33, 0x40);
        private static readonly Color LineHot    = Rgb(0x3E, 0x4A, 0x5C);
        private static readonly Color OnAccent   = Rgb(0x0B, 0x14, 0x11);

        private static GUISkin _skin;
        private static GUISkin _savedSkin;
        private static float _requested, _scale = 1f;
        private static int _scaledFor;
        private static Matrix4x4 _savedMatrix;
        private static Texture2D _window, _panel, _field, _fieldHot, _button, _buttonHot, _primary, _primaryHot, _rule, _thumb;

        /// <summary>Section headers, captions and status text, in the one place they can stay consistent.</summary>
        public static GUIStyle Head, Caption, Key, FieldKey, Value, Panel, Well, Primary, Danger, Rule1, Chip;

        public static GUISkin Skin { get { Build(); return _skin; } }

        /// <summary>
        /// How much larger than raw pixels the windows are drawn. IMGUI has no notion of display density, so
        /// without this a window is the same number of pixels on every screen and shrinks as resolution grows.
        /// </summary>
        public static float Scale
        {
            get
            {
                if (_requested > 0.01f) return _scale;
                // Worked out lazily and again after a resolution change: settings are read while KSP is still
                // on the loading screen, long before it has settled on the resolution it will actually use.
                if (_scaledFor != Screen.height)
                {
                    _scaledFor = Screen.height;
                    _scale = Mathf.Clamp(Mathf.Round(Screen.height / 1080f * 20f) / 20f, 1f, 2f);
                }
                return _scale;
            }
        }

        /// <summary>Screen size in the coordinates windows are placed in, which is not pixels once scaled.</summary>
        public static float ScreenW => Screen.width / Scale;
        public static float ScreenH => Screen.height / Scale;

        /// <summary>Applies a chosen size. Zero hands the decision back to the screen.</summary>
        public static void SetScale(float requested)
        {
            _requested = requested;
            _scaledFor = 0;
            if (requested > 0.01f) _scale = Mathf.Clamp(requested, 0.6f, 3f);
        }

        /// <summary>
        /// Opens a themed drawing pass: the dark skin, at the chosen size. Always paired with
        /// <see cref="End"/>, which puts back what was there - KSP and other mods draw in the same OnGUI.
        /// </summary>
        public static void Begin()
        {
            _savedSkin = GUI.skin;
            _savedMatrix = GUI.matrix;
            GUI.skin = Skin;
            if (Scale != 1f)
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Scale, Scale, 1f));
        }

        public static void End()
        {
            GUI.matrix = _savedMatrix;
            GUI.skin = _savedSkin;
        }

        /// <summary>Makes sure the styles exist, for code that reads them without going through the skin.</summary>
        public static void Ensure() => Build();

        /// <summary>Rich-text colouring, so a coloured word does not need a hex literal at every call site.</summary>
        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        public static string Tint(string text, Color c) => "<color=" + Hex(c) + ">" + text + "</color>";

        /// <summary>A status dot. Colour carries the meaning; the shape only makes it findable.</summary>
        public static string Dot(Color c) => Tint("●", c);

        /// <summary>
        /// Colours picked so that the same player is the same colour everywhere: their dot in the player
        /// list, their name in chat, and the label over their cursor in the shared editor. With three or four
        /// people building at once that is the difference between reading every label and glancing at one.
        /// </summary>
        private static readonly Color[] Palette =
        {
            Rgb(0x4F, 0xD1, 0xA5), Rgb(0x6E, 0xA8, 0xFF), Rgb(0xE8, 0xB8, 0x4B), Rgb(0xFF, 0x8F, 0xA3),
            Rgb(0xC0, 0x8C, 0xFF), Rgb(0x6F, 0xE3, 0xE1), Rgb(0xF2, 0x9E, 0x5B), Rgb(0xA8, 0xD8, 0x66),
        };

        public static Color PlayerColour(int clientId) =>
            clientId <= 0 ? Dim : Palette[clientId % Palette.Length];

        // ---------------------------------------------------------------- layout helpers

        /// <summary>Opens a titled panel. Always paired with <see cref="EndSection"/>.</summary>
        public static void BeginSection(string title)
        {
            Build();
            GUILayout.BeginVertical(Panel);
            if (!string.IsNullOrEmpty(title)) GUILayout.Label(title, Head);
        }

        public static void EndSection()
        {
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        /// <summary>A hairline. Cheaper to read than a blank gap when rows are dense.</summary>
        public static void Separator()
        {
            Build();
            GUILayout.Space(3);
            GUILayout.Label(GUIContent.none, Rule1, GUILayout.Height(1));
            GUILayout.Space(3);
        }

        /// <summary>A label and its value on one line, with the labels lined up down the column.</summary>
        public static void Row(string key, string value, float keyWidth = 96f)
        {
            Build();
            GUILayout.BeginHorizontal();
            GUILayout.Label(key, Key, GUILayout.Width(keyWidth));
            GUILayout.Label(value, Value);
            GUILayout.EndHorizontal();
        }

        /// <summary>Only the title bar drags, so text in the body can be selected without the window moving.</summary>
        public static void DragHeader() => GUI.DragWindow(new Rect(0, 0, 100000, HeaderHeight));

        /// <summary>
        /// Keeps enough of a window on screen to grab hold of. A window dragged off the edge, or stranded by
        /// a resolution change, is otherwise gone for good - the position is only in memory, so the only way
        /// back is to restart the game.
        /// </summary>
        public static Rect Clamp(Rect r)
        {
            const float Grip = 90f;
            r.x = Mathf.Clamp(r.x, Grip - Mathf.Max(r.width, Grip), ScreenW - Grip);
            r.y = Mathf.Clamp(r.y, 0f, Mathf.Max(0f, ScreenH - HeaderHeight));
            return r;
        }

        // ---------------------------------------------------------------- construction

        private static void Build()
        {
            // Unity reports a destroyed object as null, so this also covers a texture lost to a scene load.
            if (_skin != null && _window != null && _panel != null) return;

            _window     = WindowBox(64, 8, HeaderHeight);
            _panel      = RoundedBox(24, 5, PanelFill, Line, 1f);
            _field      = RoundedBox(20, 4, FieldFill, Line, 1f);
            _fieldHot   = RoundedBox(20, 4, FieldHot, Accent, 1f);
            _button     = RoundedBox(20, 4, ButtonFill, Line, 1f);
            _buttonHot  = RoundedBox(20, 4, ButtonHot, LineHot, 1f);
            _primary    = RoundedBox(20, 4, Rgb(0x2C, 0x7A, 0x63), Accent, 1f);
            _primaryHot = RoundedBox(20, 4, Rgb(0x3A, 0x9C, 0x7F), Accent, 1f);
            _rule       = Solid(Line);
            _thumb      = RoundedBox(12, 4, Rgb(0x39, 0x45, 0x55), Rgb(0x4A, 0x59, 0x6C), 1f);

            var basis = HighLogic.Skin != null ? HighLogic.Skin : GUI.skin;
            _skin = Object.Instantiate(basis);
            _skin.name = "KspMpDark";
            _skin.hideFlags = HideFlags.HideAndDontSave;

            // Window. The title sits in the bar drawn into the texture, which is why the content offset pulls
            // it back up out of the padding that keeps the body clear of it.
            var w = _skin.window;
            w.normal.background = w.onNormal.background = _window;
            w.border = new RectOffset(9, 9, HeaderHeight + 2, 9);
            w.padding = new RectOffset(12, 12, HeaderHeight + 8, 10);
            w.margin = new RectOffset(0, 0, 0, 0);
            w.overflow = new RectOffset(0, 0, 0, 0);
            w.alignment = TextAnchor.UpperLeft;
            w.contentOffset = new Vector2(2, -(HeaderHeight + 3));
            w.fontSize = 13;
            w.fontStyle = FontStyle.Bold;
            w.richText = true;
            w.normal.textColor = w.onNormal.textColor = Ink;

            var label = _skin.label;
            Paint(label, Ink);
            label.richText = true;
            label.wordWrap = true;
            label.fontSize = 13;
            label.padding = new RectOffset(0, 0, 3, 3);
            label.margin = new RectOffset(2, 2, 1, 1);

            var button = _skin.button;
            button.normal.background = button.focused.background = _button;
            button.hover.background = _buttonHot;
            button.active.background = button.onActive.background = _primaryHot;
            button.onNormal.background = button.onHover.background = button.onFocused.background = _primary;
            button.border = new RectOffset(5, 5, 5, 5);
            button.padding = new RectOffset(12, 12, 6, 7);
            button.margin = new RectOffset(2, 2, 3, 3);
            button.overflow = new RectOffset(0, 0, 0, 0);
            button.richText = true;
            button.fontSize = 13;
            button.alignment = TextAnchor.MiddleCenter;
            button.normal.textColor = button.focused.textColor = Ink;
            button.hover.textColor = Color.white;
            button.active.textColor = button.onActive.textColor = OnAccent;
            button.onNormal.textColor = button.onHover.textColor = button.onFocused.textColor = Color.white;

            Dress(_skin.textField);
            Dress(_skin.textArea);
            // Every control on a row is the same height, which is what lets a caption beside a field line up
            // with the text inside it instead of floating above it.
            _skin.textField.fixedHeight = ControlHeight;
            button.fixedHeight = ControlHeight;

            var box = _skin.box;
            box.normal.background = box.onNormal.background = _panel;
            box.border = new RectOffset(6, 6, 6, 6);
            box.padding = new RectOffset(10, 10, 8, 10);
            box.margin = new RectOffset(0, 0, 2, 2);
            box.normal.textColor = Ink;

            var scroll = _skin.verticalScrollbar;
            scroll.normal.background = _rule;
            scroll.border = new RectOffset(0, 0, 0, 0);
            scroll.fixedWidth = 8;
            scroll.margin = new RectOffset(4, 0, 0, 0);
            var thumb = _skin.verticalScrollbarThumb;
            thumb.normal.background = thumb.hover.background = thumb.active.background = _thumb;
            thumb.border = new RectOffset(5, 5, 5, 5);
            thumb.fixedWidth = 8;
            Blank(_skin.verticalScrollbarUpButton);
            Blank(_skin.verticalScrollbarDownButton);
            _skin.scrollView.normal.background = null;

            Head = new GUIStyle(label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                wordWrap = false,
                margin = new RectOffset(0, 0, 0, 4),
            };
            Head.normal.textColor = Accent;

            Caption = new GUIStyle(label) { fontSize = 11, wordWrap = true, margin = new RectOffset(2, 2, 0, 4) };
            Caption.normal.textColor = Dim;

            Key = new GUIStyle(label) { fontSize = 12, wordWrap = false };
            Key.normal.textColor = Dim;

            FieldKey = new GUIStyle(Key) { alignment = TextAnchor.MiddleLeft, fixedHeight = ControlHeight };

            Value = new GUIStyle(label) { fontSize = 12, wordWrap = true };

            Danger = new GUIStyle(label) { fontSize = 12, wordWrap = true };
            Danger.normal.textColor = Bad;

            Panel = new GUIStyle(box);

            // An inset well, for content that scrolls inside a panel rather than sitting on it.
            Well = new GUIStyle
            {
                border = new RectOffset(5, 5, 5, 5),
                padding = new RectOffset(7, 4, 5, 5),
                margin = new RectOffset(0, 0, 2, 2),
            };
            Well.normal.background = _field;

            Primary = new GUIStyle(button) { fontStyle = FontStyle.Bold, fixedHeight = ControlHeight + 4 };
            Primary.normal.background = Primary.focused.background = _primary;
            Primary.hover.background = _primaryHot;
            Primary.normal.textColor = Primary.focused.textColor = Color.white;
            Primary.hover.textColor = Color.white;

            Chip = new GUIStyle(label)
            {
                fontSize = 11,
                wordWrap = false,
                padding = new RectOffset(7, 7, 3, 3),
                margin = new RectOffset(0, 4, 2, 2),
                border = new RectOffset(5, 5, 5, 5),
                alignment = TextAnchor.MiddleCenter,
            };
            Chip.normal.background = _field;
            Chip.normal.textColor = Dim;

            Rule1 = new GUIStyle { margin = new RectOffset(0, 0, 0, 0), padding = new RectOffset(0, 0, 0, 0) };
            Rule1.normal.background = _rule;
        }

        private static void Dress(GUIStyle field)
        {
            if (field == null) return;
            field.normal.background = field.hover.background = _field;
            field.focused.background = field.onFocused.background = _fieldHot;
            field.border = new RectOffset(5, 5, 5, 5);
            field.padding = new RectOffset(8, 8, 5, 6);
            field.margin = new RectOffset(2, 2, 3, 3);
            field.overflow = new RectOffset(0, 0, 0, 0);
            field.fontSize = 13;
            field.alignment = TextAnchor.MiddleLeft;
            Paint(field, Ink);
            field.focused.textColor = field.onFocused.textColor = Color.white;
        }

        private static void Blank(GUIStyle style)
        {
            if (style == null) return;
            style.normal.background = style.hover.background = style.active.background = null;
            style.fixedHeight = 0;
        }

        private static void Paint(GUIStyle style, Color c)
        {
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = c;
            style.onNormal.textColor = style.onHover.textColor = style.onActive.textColor = style.onFocused.textColor = c;
        }

        // ---------------------------------------------------------------- drawing

        private static Color Rgb(int r, int g, int b, float a = 1f) => new Color(r / 255f, g / 255f, b / 255f, a);

        private static Texture2D Solid(Color c)
        {
            var tex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            tex.SetPixel(0, 0, c);
            return Finish(tex);
        }

        /// <summary>
        /// A rounded rectangle with a one-pixel border, for nine-slicing. Coverage is worked out from the
        /// distance to the rounded boundary, which antialiases the corners for free.
        /// </summary>
        private static Texture2D RoundedBox(int size, int radius, Color fill, Color border, float borderWidth)
        {
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                    tex.SetPixel(x, y, Shade(x, y, size, radius, borderWidth, fill, border));
            return Finish(tex);
        }

        /// <summary>
        /// As above, with a title bar baked into the top rows and a hairline under it. Nine-slicing keeps that
        /// band at its drawn height whatever size the window ends up, which is why it can live in the texture
        /// rather than being drawn as a separate strip every frame.
        /// </summary>
        private static Texture2D WindowBox(int size, int radius, int headerHeight)
        {
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var fromTop = size - 1 - y;
                    var fill = fromTop < headerHeight ? HeaderFill
                             : fromTop == headerHeight ? Line
                             : WindowFill;
                    tex.SetPixel(x, y, Shade(x, y, size, radius, 1f, fill, Line));
                }
            return Finish(tex);
        }

        private static Color Shade(int x, int y, int size, int radius, float borderWidth, Color fill, Color border)
        {
            float px = x + 0.5f, py = y + 0.5f;
            var cx = Mathf.Clamp(px, radius, size - radius);
            var cy = Mathf.Clamp(py, radius, size - radius);
            var dist = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            var inside = Mathf.Clamp01(radius - dist + 0.5f);
            var onEdge = borderWidth <= 0f ? 0f : Mathf.Clamp01(dist - (radius - borderWidth) + 0.5f);
            var c = Color.Lerp(fill, border, onEdge);
            c.a *= inside;
            return c;
        }

        private static Texture2D Finish(Texture2D tex)
        {
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Point;   // the borders are one pixel; blurring them muddies every edge
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.Apply();
            return tex;
        }
    }
}
