using UnityEngine;

namespace KspMp.Ui
{
    /// <summary>
    /// The one window. Alt+M toggles it; typing in chat locks KSP's keyboard shortcuts.
    ///
    /// It shows the sections that mean something where the player is standing - notices always, players always,
    /// the flight roles and the control buttons only in flight - so there is a single place to look for "what is
    /// actually going on" rather than a window per feature.
    /// </summary>
    internal sealed class HudWindow
    {
        private const float Width = 400f;
        private const string KeyboardLockId = "KspMp.chat";
        private static readonly int WindowId = "KspMp.Hud".GetHashCode();
        private readonly KspMpAddon _addon;
        private readonly ChatPanel _chat;
        private Rect _rect = new Rect(0, 60, Width, 0);
        private bool _keyboardLocked;
        private readonly System.Collections.Generic.List<KspMp.Systems.NoticeSystem.Notice> _noticeScratch = new System.Collections.Generic.List<KspMp.Systems.NoticeSystem.Notice>();
        // The addon is created before KSP has settled on a resolution, so anchoring to the right edge in the
        // field initialiser would put the window wherever the loading screen happened to be wide.
        private bool _placed;

        public bool Visible = true;

        public HudWindow(KspMpAddon addon)
        {
            _addon = addon;
            _chat = new ChatPanel(addon.Chat);
        }

        public void Draw()
        {
            var show = Visible && _addon.Network.IsConnected && HighLogic.LoadedSceneIsGame;
            if (!show)
            {
                SetKeyboardLock(false);
                return;
            }
            if (!_placed)
            {
                _placed = true;
                _rect.x = Theme.ScreenW - Width - 20;
            }
            Theme.Begin();
            _rect = Theme.Clamp(GUILayout.Window(WindowId, _rect, DrawContents,
                                                 "KspMp" + Theme.Tint("   Alt+M", Theme.Dim), GUILayout.Width(Width)));
            Theme.End();
            SetKeyboardLock(_chat.InputFocused);
        }

        private void DrawContents(int id)
        {
            var net = _addon.Network;

            GUILayout.Label(Theme.Dot(Theme.Accent) + "  <b>" + net.ServerName + "</b>", Theme.Value);

            // The numbers that say whether the session is healthy, small and side by side so they can be
            // taken in at a glance rather than read.
            var drift = _addon.TimeSync.DriftSeconds * 1000;
            GUILayout.BeginHorizontal();
            GUILayout.Label(net.PingMs + " ms", Theme.Chip);
            GUILayout.Label(Theme.Tint("drift ", Theme.Dim)
                            + Theme.Tint(drift.ToString("F0") + " ms", System.Math.Abs(drift) > 250 ? Theme.Warn : Theme.Dim), Theme.Chip);
            GUILayout.Label(Theme.Tint("warp ", Theme.Dim) + _addon.Warp.StatusText, Theme.Chip);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            DrawNotices();

            Theme.Separator();
            GUILayout.Label("PLAYERS", Theme.Head);

            foreach (var p in _addon.Players.Players)
            {
                var you = p.ClientId == net.ClientId;
                GUILayout.BeginHorizontal();
                GUILayout.Label(Theme.Dot(Theme.PlayerColour(p.ClientId)) + "  " + p.Name
                                + (string.IsNullOrEmpty(p.AvatarKerbalName) ? "" : Theme.Tint("  " + p.AvatarKerbalName, Theme.Dim)),
                                Theme.Value);
                GUILayout.FlexibleSpace();
                GUILayout.Label(you ? "you" : p.PingMs + " ms", Theme.Key);
                GUILayout.EndHorizontal();
                GUILayout.Label("      " + _addon.Presence.Describe(p.ClientId), Theme.Caption);
            }

            DrawBuilders();
            DrawFlight();

            Theme.Separator();
            GUILayout.Label("CHAT", Theme.Head);
            GUILayout.Space(4);
            _chat.Draw(140);
            Theme.DragHeader();
        }

        /// <summary>Notices first, because a notice is the only thing here that expects an answer.</summary>
        private void DrawNotices()
        {
            var notices = _addon.Notices;
            if (notices == null || notices.Notices.Count == 0) return;
            Theme.Separator();
            GUILayout.Label("NOTICES", Theme.Head);
            // A button's action may dismiss a notice; the list is walked from a copy so the pass stays
            // consistent with its layout pass.
            _noticeScratch.Clear();
            _noticeScratch.AddRange(notices.Notices);
            for (var i = 0; i < _noticeScratch.Count; i++)
            {
                var notice = _noticeScratch[i];
                var text = Theme.Tint(notice.Text, notice.Colour);
                if (notice.CountdownUntil >= 0) text += Theme.Tint("  " + (notice.CountdownText ?? "in") + " " + notice.SecondsLeft + "s", Theme.Dim);
                GUILayout.Label(text, Theme.Value);
                if (notice.Actions.Length == 0) continue;
                GUILayout.BeginHorizontal();
                for (var a = 0; a < notice.Actions.Length; a++)
                {
                    var action = notice.Actions[a];
                    // An action with nothing to run is a state label ("waiting for the launch..."), not a button.
                    if (action.OnClick == null) GUILayout.Label(Theme.Tint(action.Label, Theme.Dim), Theme.Chip);
                    // Run after the GUI pass: an action that loads a scene or saves the game from inside the
                    // window callback tears the scene down under the rest of this frame's OnGUI.
                    else if (GUILayout.Button(action.Label, action.Primary ? Theme.Primary : GUI.skin.button, GUILayout.Height(Theme.ControlHeight))) _addon.Defer(action.OnClick);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>Every open workbench, with a Join for the ones we could actually walk over to.</summary>
        private void DrawBuilders()
        {
            if (HighLogic.LoadedScene != GameScenes.EDITOR) return;
            var builders = _addon.Builders;
            var editor = _addon.Editor;
            if (builders == null || editor == null || builders.Sessions.Count == 0) return;

            Theme.Separator();
            GUILayout.Label("BUILDERS", Theme.Head);
            var me = _addon.Network.ClientId;
            var myFacility = EditorDriver.editorFacility == EditorFacility.SPH ? Shared.Protocol.EditorFacilityKind.Sph : Shared.Protocol.EditorFacilityKind.Vab;

            for (var i = 0; i < builders.Sessions.Count; i++)
            {
                var session = builders.Sessions[i];
                var mine = session.OwnerClientId == me;
                var guesting = !editor.OnOwnBench && editor.SessionOwner == session.OwnerClientId;
                var facility = session.Facility == Shared.Protocol.EditorFacilityKind.Sph ? "SPH" : "VAB";
                var craft = session.PartCount == 0 || string.IsNullOrEmpty(session.ShipName)
                    ? Theme.Tint("empty", Theme.Dim)
                    : "'" + session.ShipName + "'" + Theme.Tint("  " + session.PartCount + " parts", Theme.Dim);

                GUILayout.BeginHorizontal();
                GUILayout.Label(Theme.Dot(Theme.PlayerColour(session.OwnerClientId)) + "  " + (mine ? "your bench" : NameOf(session.OwnerClientId))
                                + Theme.Tint("  [" + facility + "]  ", Theme.Dim) + craft, Theme.Value);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (session.BuilderClientIds != null && session.BuilderClientIds.Length > 1)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(18);
                    for (var b = 0; b < session.BuilderClientIds.Length; b++)
                    {
                        var id = session.BuilderClientIds[b];
                        if (id == session.OwnerClientId) continue;
                        GUILayout.Label(Theme.Dot(Theme.PlayerColour(id)) + " " + NameOf(id), Theme.Chip);
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }

                GUILayout.BeginHorizontal();
                GUILayout.Space(18);
                if (guesting)
                {
                    if (GUILayout.Button("Leave", GUILayout.Height(Theme.ControlHeight))) editor.LeaveSession();
                }
                else if (mine)
                {
                    // Nothing to click on your own bench; the row is there so you can see it exists.
                }
                else if (session.Facility != myFacility)
                {
                    GUILayout.Label(Theme.Tint("in the " + facility, Theme.Dim), Theme.Chip);
                }
                else if (GUILayout.Button("Join", GUILayout.Height(Theme.ControlHeight)))
                {
                    // One click. Whatever is on your bench is set aside, not lost, and comes back when you
                    // leave; the "Join anyway / Cancel" step this used to have read as a button that did
                    // nothing to a player who had a pod down ("had to spam it").
                    Log.Info("Join clicked: " + NameOf(session.OwnerClientId) + "'s bench");
                    var owner = session.OwnerClientId;
                    _addon.Defer(() => editor.JoinSession(owner));
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>Who is flying what, and the buttons that move that around.</summary>
        private void DrawFlight()
        {
            if (!HighLogic.LoadedSceneIsFlight || FlightGlobals.fetch == null) return;
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return;
            var control = _addon.Control;
            var net = _addon.Network;

            Theme.Separator();
            GUILayout.Label("FLIGHT", Theme.Head);
            Row("Vessel", vessel.GetDisplayName());

            var pilot = control.PilotOf(vessel.id);
            Row("Pilot", pilot == 0
                ? Theme.Tint("nobody aboard", Theme.Dim)
                : Theme.Dot(Theme.PlayerColour(pilot)) + "  " + NameOf(pilot));

            if (control.TryGetRoles(vessel.id, out var roles) && roles.AboardClientIds != null && roles.AboardClientIds.Length > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Aboard", Theme.Key, GUILayout.Width(70));
                for (var i = 0; i < roles.AboardClientIds.Length; i++)
                {
                    var id = roles.AboardClientIds[i];
                    GUILayout.Label(Theme.Dot(Theme.PlayerColour(id)) + " " + NameOf(id), Theme.Chip);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            Row("You", Theme.Tint(control.RoleText, Theme.Warn));

            var tags = GUILayout.Toggle(_addon.Settings.ShowNametags, "  Nametags over other players");
            if (tags != _addon.Settings.ShowNametags) { _addon.Settings.ShowNametags = tags; _addon.Settings.Save(); }

            var mine = _addon.Vessels.IsMine(vessel.id);
            if (mine)
            {
                if (control.TryGetRoles(vessel.id, out var r) && r.AboardClientIds != null)
                {
                    GUILayout.BeginHorizontal();
                    for (var i = 0; i < r.AboardClientIds.Length; i++)
                    {
                        var id = r.AboardClientIds[i];
                        if (id == net.ClientId) continue;
                        if (GUILayout.Button("Give to " + NameOf(id), GUILayout.Height(Theme.ControlHeight))) control.GiveControl(vessel.id, id);
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
                var shared = control.SharedStickFor(vessel.id);
                var want = GUILayout.Toggle(shared, "  Shared stick (co-pilots may steer)");
                if (want != shared) control.SetSharedStick(vessel.id, want);
            }
            else if (control.IAmAboard(vessel.id))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Request control", Theme.Primary, GUILayout.Height(Theme.ControlHeight))) control.RequestControl(vessel.id);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        private static void Row(string key, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(key, Theme.Key, GUILayout.Width(70));
            GUILayout.Label(value, Theme.Value);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private string NameOf(int clientId) => _addon.Players.TryGet(clientId, out var p) ? p.Name : "#" + clientId;

        private void SetKeyboardLock(bool locked)
        {
            // Against the lock stack, not a cached flag: KSP clears every lock on a scene change, and typing in
            // chat after that used to stage the rocket.
            var has = InputLockManager.GetControlLock(KeyboardLockId) != ControlTypes.None;
            _keyboardLocked = locked;
            if (locked && !has) InputLockManager.SetControlLock(ControlTypes.KEYBOARDINPUT, KeyboardLockId);
            else if (!locked && has) InputLockManager.RemoveControlLock(KeyboardLockId);
        }
    }
}
