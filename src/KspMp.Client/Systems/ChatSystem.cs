using System;
using System.Collections.Generic;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;

namespace KspMp.Systems
{
    public sealed class ChatSystem : SystemBase
    {
        public struct Line
        {
            public DateTime Time;
            public int FromClientId;
            public string From;
            public string Text;

            public bool IsServer => FromClientId == 0;
            public bool IsLocal => FromClientId < 0;
        }

        public const int MaxLines = 200;

        private readonly List<Line> _lines = new List<Line>();

        public ChatSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "Chat";
        public IReadOnlyList<Line> Lines => _lines;
        public int Unread { get; set; }
        /// <summary>Incremented on every new line so UI can auto-scroll.</summary>
        public int Revision { get; private set; }

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.Chat, OnChat);
            AddLocal("Connected to " + Net.ServerName + ".");
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.Chat, OnChat);
            AddLocal("Disconnected.");
        }

        public void Send(string text)
        {
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0 || !Net.IsConnected) return;
            Net.Send(MessageId.Chat, new ChatMsg { FromClientId = 0, FromName = string.Empty, Text = text }, Channel.ChatMod, Delivery.ReliableOrdered);
        }

        public void AddLocal(string text) => Add(new Line { Time = DateTime.Now, FromClientId = -1, From = "KspMp", Text = text });

        private void OnChat(NetDataReader body)
        {
            var msg = Envelope.Read<ChatMsg>(body);
            Log.Info("Chat: " + (msg.FromClientId == 0 ? "[server]" : msg.FromName + "#" + msg.FromClientId) + ": " + msg.Text);
            Add(new Line { Time = DateTime.Now, FromClientId = msg.FromClientId, From = msg.FromName, Text = msg.Text });
            if (msg.FromClientId != Net.ClientId) Unread++;
        }

        private void Add(Line line)
        {
            _lines.Add(line);
            if (_lines.Count > MaxLines) _lines.RemoveAt(0);
            Revision++;
        }
    }
}
