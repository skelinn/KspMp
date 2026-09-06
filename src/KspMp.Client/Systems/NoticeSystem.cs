using System;
using System.Collections.Generic;
using UnityEngine;

namespace KspMp.Systems
{
    /// <summary>
    /// The short-lived things a player needs to be told: somebody launched with your kerbal aboard, control was
    /// handed to you, a request for the stick. A notice can carry buttons and a countdown, which is what makes it
    /// different from a chat line - the join-flight invite is a notice with an "Enter flight" button and ten
    /// seconds on the clock.
    ///
    /// Runs in every scene, connected or not, so a notice raised in the VAB survives the walk to the space centre.
    /// </summary>
    public sealed class NoticeSystem : SystemBase
    {
        public sealed class Action
        {
            public string Label;
            public System.Action OnClick;
            public bool Primary;
        }

        public sealed class Notice
        {
            public string Key;
            public string Text;
            public Color Colour;
            public Action[] Actions = new Action[0];
            public float ExpiresAt;
            /// <summary>Set to have the notice show "... (7s)" and fire <see cref="OnCountdown"/> when it reaches zero.</summary>
            public string CountdownText;
            public float CountdownUntil = -1f;
            public System.Action OnCountdown;

            public int SecondsLeft => CountdownUntil < 0 ? 0 : Mathf.Max(0, Mathf.CeilToInt(CountdownUntil - Time.realtimeSinceStartup));
        }

        private readonly List<Notice> _notices = new List<Notice>();

        public NoticeSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "Notices";
        public override bool ShouldRun(GameScenes scene, bool connected) => true;
        public IReadOnlyList<Notice> Notices => _notices;

        /// <summary>
        /// Posts a notice. A repeated <paramref name="key"/> replaces the notice already showing rather than
        /// stacking another copy, so a repeated invite does not bury the screen.
        /// </summary>
        public Notice Post(string key, string text, Color colour, Action[] actions = null, float ttlSeconds = 15f)
        {
            // The same notice re-posted with the same words (its buttons refreshed, say) is not news: it is
            // replaced without being announced again in the log, on screen and in chat.
            var repeat = false;
            for (var i = 0; i < _notices.Count && !repeat; i++) repeat = _notices[i].Key == key && _notices[i].Text == text;
            Dismiss(key);
            var notice = new Notice
            {
                Key = key,
                Text = text,
                Colour = colour,
                Actions = actions ?? new Action[0],
                ExpiresAt = ttlSeconds > 0 ? Time.realtimeSinceStartup + ttlSeconds : float.MaxValue,
            };
            _notices.Add(notice);
            if (repeat) return notice;
            Log.Info("Notice: " + text);
            try
            {
                ScreenMessages.PostScreenMessage("[KspMp] " + text, Mathf.Min(ttlSeconds > 0 ? ttlSeconds : 10f, 10f), ScreenMessageStyle.UPPER_CENTER);
            }
            catch (Exception e)
            {
                Log.Exception("Showing a screen message", e);
            }
            Addon.Chat?.AddLocal(text);
            return notice;
        }

        public void Dismiss(string key)
        {
            for (var i = _notices.Count - 1; i >= 0; i--)
                if (_notices[i].Key == key) _notices.RemoveAt(i);
        }

        public bool Has(string key)
        {
            for (var i = 0; i < _notices.Count; i++) if (_notices[i].Key == key) return true;
            return false;
        }

        public override void Update()
        {
            var now = Time.realtimeSinceStartup;
            for (var i = _notices.Count - 1; i >= 0; i--)
            {
                var notice = _notices[i];
                if (notice.CountdownUntil >= 0 && now >= notice.CountdownUntil)
                {
                    var fire = notice.OnCountdown;
                    _notices.RemoveAt(i);
                    try { fire?.Invoke(); }
                    catch (Exception e) { Log.Exception("Running a notice countdown", e); }
                    continue;
                }
                if (now >= notice.ExpiresAt) _notices.RemoveAt(i);
            }
        }

        protected override void OnDeactivate() => _notices.Clear();
    }
}
