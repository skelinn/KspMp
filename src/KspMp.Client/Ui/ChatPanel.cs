using KspMp.Systems;
using UnityEngine;

namespace KspMp.Ui
{
    /// <summary>Chat log + input box shared by the lobby and the in-game HUD.</summary>
    internal sealed class ChatPanel
    {
        public const string InputControlName = "KspMp.ChatInput";

        private readonly ChatSystem _chat;
        private string _input = string.Empty;
        private Vector2 _scroll;
        private int _seenRevision = -1;
        private GUIStyle _lineStyle;

        public ChatPanel(ChatSystem chat)
        {
            _chat = chat;
        }

        public bool InputFocused => GUI.GetNameOfFocusedControl() == InputControlName;

        public void Draw(float height)
        {
            Theme.Ensure();
            if (_lineStyle == null)
                _lineStyle = new GUIStyle(Theme.Value) { wordWrap = true, richText = true, fontSize = 12, padding = new RectOffset(0, 0, 2, 2) };

            if (_chat.Revision != _seenRevision)
            {
                _seenRevision = _chat.Revision;
                _scroll.y = float.MaxValue;
            }

            // The well is passed as the scroll view's own background so the log reads as sunk into the panel
            // rather than floating on it. No horizontal bar: the lines wrap, so there is never anything to the side.
            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar,
                                                Theme.Well, GUILayout.Height(height));
            if (_chat.Lines.Count == 0) GUILayout.Label("Nothing said yet.", Theme.Caption);
            foreach (var line in _chat.Lines)
            {
                string text;
                if (line.IsLocal) text = Theme.Tint(line.Text, Theme.Dim);
                else if (line.IsServer) text = Theme.Tint(line.Text, Theme.Warn);
                else text = "<b>" + Theme.Tint(line.From, Theme.PlayerColour(line.FromClientId)) + "</b>  " + line.Text;
                GUILayout.Label(text, _lineStyle);
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(InputControlName);
            _input = GUILayout.TextField(_input, 500);
            var send = GUILayout.Button("Send", GUILayout.Width(66));
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && InputFocused)
            {
                send = true;
                e.Use();
            }
            GUILayout.EndHorizontal();

            if (send && _input.Trim().Length > 0)
            {
                _chat.Send(_input);
                _input = string.Empty;
            }
            _chat.Unread = 0;
        }
    }
}
