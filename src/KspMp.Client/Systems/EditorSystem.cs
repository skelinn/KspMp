using System;
using System.Collections.Generic;
using System.Text;
using KSP.UI.Screens;
using KspMp.Shared.Codec;
using KspMp.Shared.Protocol;
using KspMp.Vessels;
using LiteNetLib.Utils;
using UnityEngine;

namespace KspMp.Systems
{
    /// <summary>
    /// Building together in the VAB and SPH. Everyone in the same facility works on one craft: after any local
    /// change the craft is sent up as a snapshot, and snapshots from the others are loaded into the local editor.
    /// A change built on a stale revision is refused by the server, which sends the current craft back instead.
    /// </summary>
    public sealed class EditorSystem : SystemBase
    {
        public const float SendDebounceSeconds = 0.4f;
        public const float PresenceIntervalSeconds = 0.1f;

        private readonly Dictionary<int, EditorPresenceMsg> _others = new Dictionary<int, EditorPresenceMsg>();
        private EditorFacilityKind _facility;
        private bool _joined;
        private int _revision;
        private float _dirtyAt = -1f;
        private float _nextPresenceAt;
        private string _lastSentHash = "";
        private GUIStyle _labelStyle;

        public EditorSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "Editor";
        /// <summary>True while a remote craft is being loaded, so our own editor events do not echo back.</summary>
        public bool Applying { get; private set; }
        public int Revision => _revision;
        public int BuilderCount => _others.Count + 1;
        public int SnapshotsSent { get; private set; }
        public int SnapshotsApplied { get; private set; }
        public IReadOnlyDictionary<int, EditorPresenceMsg> Others => _others;

        public override bool ShouldRun(GameScenes scene, bool connected) => connected && scene == GameScenes.EDITOR;

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.EditorSnapshot, OnSnapshot);
            Net.RegisterHandler(MessageId.EditorPresence, OnPresence);
            GameEvents.onEditorShipModified.Add(OnShipModified);
            GameEvents.onEditorRestart.Add(OnEditorRestart);
            GameEvents.onEditorLoad.Add(OnEditorLoad);
            _facility = EditorDriver.editorFacility == EditorFacility.SPH ? EditorFacilityKind.Sph : EditorFacilityKind.Vab;
            _revision = 0;
            _others.Clear();
            _lastSentHash = "";
            Net.Send(MessageId.EditorJoin, new EditorJoinMsg { Facility = _facility }, Channel.Control, Delivery.ReliableOrdered);
            _joined = true;
            Log.Info("Joined the shared " + _facility + " workbench");
        }

        protected override void OnDeactivate()
        {
            if (_joined) Net.Send(MessageId.EditorLeave, new EditorLeaveMsg(), Channel.Control, Delivery.ReliableOrdered);
            _joined = false;
            Net.UnregisterHandler(MessageId.EditorSnapshot, OnSnapshot);
            Net.UnregisterHandler(MessageId.EditorPresence, OnPresence);
            GameEvents.onEditorShipModified.Remove(OnShipModified);
            GameEvents.onEditorRestart.Remove(OnEditorRestart);
            GameEvents.onEditorLoad.Remove(OnEditorLoad);
            _others.Clear();
        }

        public override void Update()
        {
            if (!_joined) return;
            var now = Time.realtimeSinceStartup;
            if (_dirtyAt >= 0 && now - _dirtyAt >= SendDebounceSeconds)
            {
                _dirtyAt = -1f;
                SendSnapshot();
            }
            if (now >= _nextPresenceAt)
            {
                _nextPresenceAt = now + PresenceIntervalSeconds;
                SendPresence();
            }
        }

        // ---- local changes going out ----

        private void OnShipModified(ShipConstruct ship)
        {
            if (Applying || !_joined) return;
            _dirtyAt = Time.realtimeSinceStartup;
        }

        private void OnEditorRestart()
        {
            if (Applying || !_joined) return;
            _dirtyAt = Time.realtimeSinceStartup;
        }

        private void OnEditorLoad(ShipConstruct ship, CraftBrowserDialog.LoadType type)
        {
            if (Applying || !_joined) return;
            Log.Info("Loaded a craft into the shared workbench; sharing it");
            _dirtyAt = Time.realtimeSinceStartup;
        }

        private void SendSnapshot()
        {
            var editor = EditorLogic.fetch;
            if (editor == null || editor.ship == null) return;
            try
            {
                var node = editor.ship.SaveShip();
                if (node == null) return;
                var text = ProtoCodec.ToText(node);
                var hash = text.Length + ":" + text.GetHashCode();
                if (hash == _lastSentHash) return;   // nothing actually changed (KSP fires the event generously)
                _lastSentHash = hash;

                var raw = Encoding.UTF8.GetBytes(text);
                var craft = DeflateCodec.Compress(raw, 0, raw.Length);
                Net.Send(MessageId.EditorSnapshot, new EditorSnapshotMsg
                {
                    Facility = _facility,
                    Revision = _revision,
                    ShipName = editor.ship.shipName,
                    PartCount = editor.ship.parts != null ? editor.ship.parts.Count : 0,
                    CraftDeflated = craft,
                    ManifestDeflated = Array.Empty<byte>(),
                }, Channel.Bulk, Delivery.ReliableOrdered);
                SnapshotsSent++;
                Log.Info("Shared the craft: " + editor.ship.shipName + ", " + (editor.ship.parts != null ? editor.ship.parts.Count : 0) + " part(s), revision " + _revision + " (" + craft.Length + " bytes)");
            }
            catch (Exception e)
            {
                Log.Exception("Sharing the craft", e);
            }
        }

        private void SendPresence()
        {
            var editor = EditorLogic.fetch;
            if (editor == null) return;
            var held = EditorLogic.SelectedPart;
            var cursor = held != null ? held.transform.position : Vector3.zero;
            Net.Send(MessageId.EditorPresence, new EditorPresenceMsg
            {
                Facility = _facility,
                Holding = held != null,
                HeldPartName = held != null && held.partInfo != null ? held.partInfo.title : string.Empty,
                CursorX = cursor.x, CursorY = cursor.y, CursorZ = cursor.z,
            }, Channel.State, Delivery.Sequenced);
        }

        /// <summary>Called by the launch patch so everyone else leaves the shared bench.</summary>
        public void AnnounceLaunch(string shipName, string site)
        {
            if (!_joined) return;
            Net.Send(MessageId.EditorLaunch, new EditorLaunchMsg { Facility = _facility, ShipName = shipName, LaunchSite = site }, Channel.Control, Delivery.ReliableOrdered);
            Log.Info("Announced the launch of " + shipName + " from " + site);
        }

        // ---- remote changes coming in ----

        private void OnSnapshot(NetDataReader body)
        {
            var msg = Envelope.Read<EditorSnapshotMsg>(body);
            _revision = msg.Revision;
            if (msg.CraftDeflated == null || msg.CraftDeflated.Length == 0) return;   // our own accepted revision
            var editor = EditorLogic.fetch;
            if (editor == null) return;

            try
            {
                Applying = true;
                var text = Encoding.UTF8.GetString(DeflateCodec.Decompress(msg.CraftDeflated, 0, msg.CraftDeflated.Length));
                var node = ConfigNode.Parse(text);
                if (node == null) return;
                var shipNode = node.GetNode("ShipConstruct") ?? node;

                var ship = new ShipConstruct();
                if (!ship.LoadShip(shipNode))
                {
                    Log.Warn("Could not load the shared craft (revision " + msg.Revision + ")");
                    return;
                }
                editor.ship.Clear();
                EditorLogic.fetch.ship = ship;
                editor.SetBackup();
                GameEvents.onEditorShipModified.Fire(ship);
                _lastSentHash = text.Length + ":" + text.GetHashCode();
                SnapshotsApplied++;
                Log.Info("Applied the shared craft from " + NameOf(msg.FromClientId) + ": " + msg.ShipName + ", " + msg.PartCount + " part(s), revision " + msg.Revision);
            }
            catch (Exception e)
            {
                Log.Exception("Applying the shared craft", e);
            }
            finally
            {
                Applying = false;
            }
        }

        private void OnPresence(NetDataReader body)
        {
            var msg = Envelope.Read<EditorPresenceMsg>(body);
            if (msg.ClientId == 0 || msg.ClientId == Net.ClientId) return;
            _others[msg.ClientId] = msg;
        }

        /// <summary>The workbench was launched out from under us; start again from an empty revision.</summary>
        public void OnRemoteLaunch()
        {
            _revision = 0;
            _lastSentHash = "";
        }

        private string NameOf(int clientId) => Addon.Players.TryGet(clientId, out var p) ? p.Name : "#" + clientId;

        /// <summary>Draws the other builders' cursors and what they are holding.</summary>
        public void DrawOverlay()
        {
            if (!Active || _others.Count == 0 || EditorLogic.fetch == null || EditorLogic.fetch.editorCamera == null) return;
            if (_labelStyle == null) _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, alignment = TextAnchor.MiddleCenter };
            var camera = EditorLogic.fetch.editorCamera;
            foreach (var pair in _others)
            {
                var presence = pair.Value;
                if (!presence.Holding) continue;
                var world = new Vector3(presence.CursorX, presence.CursorY, presence.CursorZ);
                var screen = camera.WorldToScreenPoint(world);
                if (screen.z <= 0) continue;
                var rect = new Rect(screen.x - 90, Screen.height - screen.y - 12, 180, 24);
                GUI.Label(rect, "<color=#ffd966>" + NameOf(pair.Key) + ": " + presence.HeldPartName + "</color>", _labelStyle);
            }
        }
    }
}
