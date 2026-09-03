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
            if (_lineStyle == null) _lineStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true, fontSize = 12 };

            if (_chat.Revision != _seenRevision)
            {
                _seenRevision = _chat.Revision;
                _scroll.y = float.MaxValue;
            }

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(height));
            foreach (var line in _chat.Lines)
            {
                var prefix = line.IsLocal ? "<i>" : line.IsServer ? "<color=#ffd966>" : "<b>";
                var suffix = line.IsLocal ? "</i>" : line.IsServer ? "</color>" : "</b>";
                var text = line.IsLocal ? prefix + line.Text + suffix : prefix + line.From + ":" + suffix + " " + line.Text;
                GUILayout.Label(text, _lineStyle);
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(InputControlName);
            _input = GUILayout.TextField(_input, 500);
            var send = GUILayout.Button("Send", GUILayout.Width(60));
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
