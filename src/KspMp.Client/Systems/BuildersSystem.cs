using System.Collections.Generic;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;

namespace KspMp.Systems
{
    /// <summary>
    /// Who is building where. Kept outside <see cref="EditorSystem"/> because that only runs inside an editor,
    /// and the players list wants to say "in the VAB, building 'Rocket'" from the launch pad as much as from the
    /// next bench along.
    /// </summary>
    public sealed class BuildersSystem : SystemBase
    {
        private EditorSessionInfo[] _sessions = new EditorSessionInfo[0];

        public BuildersSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "Builders";
        public override bool ShouldRun(GameScenes scene, bool connected) => connected;
        public IReadOnlyList<EditorSessionInfo> Sessions => _sessions;

        protected override void OnActivate() => Net.RegisterHandler(MessageId.EditorSessionList, OnList);

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.EditorSessionList, OnList);
            _sessions = new EditorSessionInfo[0];
        }

        public bool TryGetSession(int ownerClientId, out EditorSessionInfo session)
        {
            for (var i = 0; i < _sessions.Length; i++)
                if (_sessions[i].OwnerClientId == ownerClientId) { session = _sessions[i]; return true; }
            session = default(EditorSessionInfo);
            return false;
        }

        /// <summary>The session a player is building in, own or guest.</summary>
        public bool TryGetSessionOf(int clientId, out EditorSessionInfo session)
        {
            for (var i = 0; i < _sessions.Length; i++)
            {
                var builders = _sessions[i].BuilderClientIds;
                if (builders == null) continue;
                for (var b = 0; b < builders.Length; b++)
                    if (builders[b] == clientId) { session = _sessions[i]; return true; }
            }
            session = default(EditorSessionInfo);
            return false;
        }

        /// <summary>A line for the players list: what this player is building, and with whom.</summary>
        public string Describe(int clientId)
        {
            if (!TryGetSessionOf(clientId, out var session)) return "";
            var facility = session.Facility == EditorFacilityKind.Sph ? "SPH" : "VAB";
            if (session.OwnerClientId != clientId)
                return "building with " + NameOf(session.OwnerClientId) + " in the " + facility;
            var craft = string.IsNullOrEmpty(session.ShipName) || session.PartCount == 0
                ? "an empty workbench"
                : "'" + session.ShipName + "' (" + session.PartCount + " part" + (session.PartCount == 1 ? "" : "s") + ")";
            var guests = session.BuilderClientIds != null ? session.BuilderClientIds.Length - 1 : 0;
            return "in the " + facility + ", building " + craft + (guests > 0 ? " with " + guests + " other" + (guests == 1 ? "" : "s") : "");
        }

        private void OnList(NetDataReader body)
        {
            var msg = Envelope.Read<EditorSessionListMsg>(body);
            _sessions = msg.Sessions ?? new EditorSessionInfo[0];

            // If the bench we were visiting has gone, its owner walked out of the editor and we keep the craft.
            var editor = Addon.Editor;
            if (editor != null && editor.Active && !editor.OnOwnBench && !TryGetSession(editor.SessionOwner, out _))
                editor.OnSessionLost();
        }

        private string NameOf(int clientId) => Addon.Players.TryGet(clientId, out var p) ? p.Name : "#" + clientId;
    }
}
