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
    /// Building together in the VAB and SPH.
    ///
    /// Opening an editor opens your own workbench, private until somebody joins it. The Builders list in the HUD
    /// shows every open bench and offers a Join button; joining stashes whatever you were building and puts the
    /// other player's craft in front of you, and leaving gives you yours back. Everyone in one session works on
    /// one craft: after any local change it is sent up as a snapshot, and snapshots from the others are loaded
    /// into the local editor. A change built on a stale revision is refused by the server, which sends the
    /// current craft back instead.
    /// </summary>
    public sealed class EditorSystem : SystemBase
    {
        /// <summary>
        /// How long after an edit the bench goes out. This used to be 0.4 s, and that window is where edits were
        /// lost: a snapshot from the other builder applied inside it replaced the craft and destroyed whatever
        /// had just been put down but not yet sent. Now a put-down part is on the wire on the next frame or so.
        /// </summary>
        public const float SendDebounceSeconds = 0.05f;

        /// <summary>
        /// Never more often than this. A part action slider (fuel, thrust limiter) fires the modified event
        /// on every step while the part stays in the ship, and each step would otherwise be a full craft
        /// snapshot; four a second is plenty for the other builder to follow a drag.
        /// </summary>
        public const float MinSendIntervalSeconds = 0.25f;
        private float _lastSentAt = -10f;
        public const float PresenceIntervalSeconds = 0.1f;

        private readonly Dictionary<int, EditorPresenceMsg> _others = new Dictionary<int, EditorPresenceMsg>();
        private readonly Dictionary<int, float> _othersSeenAt = new Dictionary<int, float>();
        private readonly List<int> _stale = new List<int>();
        private EditorFacilityKind _facility;
        /// <summary>Whose bench we are on: 0 (or our own client id) while we are on our own.</summary>
        private int _sessionOwner;
        /// <summary>What we were building before joining somebody else's bench, restored when we leave.</summary>
        private ConfigNode _stash;
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
        /// <summary>Whose bench we are on (0 = our own).</summary>
        public int SessionOwner => _sessionOwner;
        public bool OnOwnBench => _sessionOwner == 0 || _sessionOwner == Net.ClientId;
        public int SnapshotsSent { get; private set; }
        public int SnapshotsApplied { get; private set; }
        public IReadOnlyDictionary<int, EditorPresenceMsg> Others => _others;

        public override bool ShouldRun(GameScenes scene, bool connected) => connected && scene == GameScenes.EDITOR;

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.EditorSnapshot, OnSnapshot);
            Net.RegisterHandler(MessageId.EditorPresence, OnPresence);
            GameEvents.onEditorShipModified.Add(OnShipModified);
            GameEvents.onEditorShipCrewModified.Add(OnCrewModified);
            GameEvents.onEditorRestart.Add(OnEditorRestart);
            GameEvents.onEditorLoad.Add(OnEditorLoad);
            _facility = EditorDriver.editorFacility == EditorFacility.SPH ? EditorFacilityKind.Sph : EditorFacilityKind.Vab;
            _revision = 0;
            _sessionOwner = 0;
            _stash = null;
            _others.Clear();
            _lastSentHash = "";
            Net.Send(MessageId.EditorJoin, new EditorJoinMsg { Facility = _facility }, Channel.Control, Delivery.ReliableOrdered);
            _joined = true;
            Log.Info("Opened our own " + _facility + " workbench");
        }

        protected override void OnDeactivate()
        {
            if (_joined) Net.Send(MessageId.EditorLeave, new EditorLeaveMsg(), Channel.Control, Delivery.ReliableOrdered);
            _joined = false;
            Net.UnregisterHandler(MessageId.EditorSnapshot, OnSnapshot);
            Net.UnregisterHandler(MessageId.EditorPresence, OnPresence);
            GameEvents.onEditorShipModified.Remove(OnShipModified);
            GameEvents.onEditorShipCrewModified.Remove(OnCrewModified);
            GameEvents.onEditorRestart.Remove(OnEditorRestart);
            GameEvents.onEditorLoad.Remove(OnEditorLoad);
            _others.Clear();
            _sessionOwner = 0;
            _stash = null;
        }

        public override void Update()
        {
            if (!_joined) return;
            var now = Time.realtimeSinceStartup;
            // Never share the bench while a part is in hand. A held part is detached from the ship, so a
            // snapshot taken mid-drag is the craft with that part missing: the other builder watches it vanish,
            // their own copy of it is destroyed, and when they in turn pick something up the same happens
            // back. The drop or the delete fires onEditorShipModified again, and that is when it goes out.
            // A builder who left the bench (or the game) sends no more cursors; without this their last one,
            // part in hand, stayed painted on the bench for the rest of the session.
            if (_others.Count > 0)
            {
                _stale.Clear();
                foreach (var pair in _othersSeenAt) if (now - pair.Value > 10f) _stale.Add(pair.Key);
                for (var i = 0; i < _stale.Count; i++) { _others.Remove(_stale[i]); _othersSeenAt.Remove(_stale[i]); }
            }
            if (_dirtyAt >= 0 && now - _dirtyAt >= SendDebounceSeconds && now - _lastSentAt >= MinSendIntervalSeconds && HeldPart() == null)
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

        /// <summary>
        /// The part in the player's hand, or null. KSP's selectedPart is not the same thing: it keeps pointing at
        /// a part after it has been attached, so "selected" alone would read as holding for the rest of the
        /// session and nothing would ever be shared again after the first placement. A part in the hand is
        /// detached from the ship; a selected part that the ship contains has been put down.
        /// </summary>
        internal static Part HeldPart()
        {
            var editor = EditorLogic.fetch;
            var selected = EditorLogic.SelectedPart;
            if (editor == null || selected == null) return null;
            return editor.ship != null && editor.ship.Contains(selected) ? null : selected;
        }

        // ---- local changes going out ----

        private void OnCrewModified(VesselCrewManifest manifest)
        {
            if (Applying || !_joined) return;
            _dirtyAt = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// Who sits where, as text: one line per seated kerbal, "part name#nth part of that name|seat|kerbal".
        /// Part ids are no use across machines - KSP renumbers parts as it loads a craft - so a seat is named by
        /// the part's type and its rank among parts of that type, which survives the load for any craft that
        /// does not have two identical crewed parts (and merely swaps seats between those when it does).
        /// </summary>
        private static string ManifestText()
        {
            var manifest = ShipConstruction.ShipManifest;
            if (manifest == null || manifest.PartManifests == null) return "";
            var sb = new StringBuilder();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < manifest.PartManifests.Count; i++)
            {
                var pm = manifest.PartManifests[i];
                if (pm == null || pm.PartInfo == null) continue;
                var name = pm.PartInfo.name;
                seen.TryGetValue(name, out var nth);
                seen[name] = nth + 1;
                var crew = pm.GetPartCrew();
                if (crew == null) continue;
                for (var seat = 0; seat < crew.Length; seat++)
                    if (crew[seat] != null && !string.IsNullOrEmpty(crew[seat].name))
                        sb.Append(name).Append('#').Append(nth).Append('|').Append(seat).Append('|').Append(crew[seat].name).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>The other builder's seating, applied to our crew tab. Runs after ReplaceWorkbench, under Applying.</summary>
        private static int ApplyManifestText(string text)
        {
            var manifest = ShipConstruction.ShipManifest;
            if (manifest == null || manifest.PartManifests == null || HighLogic.CurrentGame == null) return 0;
            var roster = HighLogic.CurrentGame.CrewRoster;
            // Empty every seat first; the sender's list is the whole truth.
            for (var i = 0; i < manifest.PartManifests.Count; i++)
            {
                var pm = manifest.PartManifests[i];
                var crew = pm != null ? pm.GetPartCrew() : null;
                if (crew == null) continue;
                for (var seat = 0; seat < crew.Length; seat++)
                    if (crew[seat] != null) pm.RemoveCrewFromSeat(seat);
            }
            var seated = 0;
            var lines = (text ?? "").Split('\n');
            for (var l = 0; l < lines.Length; l++)
            {
                var line = lines[l];
                if (line.Length == 0) continue;
                var bar1 = line.IndexOf('|');
                var bar2 = bar1 >= 0 ? line.IndexOf('|', bar1 + 1) : -1;
                var hash = line.IndexOf('#');
                if (bar1 < 0 || bar2 < 0 || hash < 0 || hash > bar1) continue;
                var name = line.Substring(0, hash);
                if (!int.TryParse(line.Substring(hash + 1, bar1 - hash - 1), out var nth)) continue;
                if (!int.TryParse(line.Substring(bar1 + 1, bar2 - bar1 - 1), out var seat)) continue;
                var kerbal = line.Substring(bar2 + 1);
                if (!roster.Exists(kerbal)) continue;
                var pcm = roster[kerbal];
                if (pcm.rosterStatus != ProtoCrewMember.RosterStatus.Available) continue;   // flying, dead: not seatable here
                var found = 0;
                for (var i = 0; i < manifest.PartManifests.Count; i++)
                {
                    var pm = manifest.PartManifests[i];
                    if (pm == null || pm.PartInfo == null || pm.PartInfo.name != name) continue;
                    if (found++ != nth) continue;
                    var crew = pm.GetPartCrew();
                    if (crew == null || seat < 0 || seat >= crew.Length) break;
                    pm.AddCrewToSeat(pcm, seat);
                    seated++;
                    break;
                }
            }
            if (KSP.UI.CrewAssignmentDialog.Instance != null)
                KSP.UI.CrewAssignmentDialog.Instance.RefreshCrewLists(manifest, false, true);
            return seated;
        }

        private void OnShipModified(ShipConstruct ship)
        {
            if (Applying || !_joined) return;
            _dirtyAt = Time.realtimeSinceStartup;
            var held = HeldPart();
            if (held != null) Log.Info("Bench changed while holding " + held.partInfo.title + "; it goes out when that is put down");
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
                var manifestText = ManifestText();
                var hash = HashOf(text + "\n" + manifestText);
                if (hash == _lastSentHash)   // nothing actually changed (KSP fires the event generously)
                {
                    Log.Info("Nothing new to share: the craft reads the same as what was last sent");
                    return;
                }
                _lastSentHash = hash;
                _lastSentAt = Time.realtimeSinceStartup;

                var raw = Encoding.UTF8.GetBytes(text);
                var craft = DeflateCodec.Compress(raw, 0, raw.Length);
                var manifestRaw = Encoding.UTF8.GetBytes(manifestText);
                var manifestBytes = manifestRaw.Length == 0 ? Array.Empty<byte>() : DeflateCodec.Compress(manifestRaw, 0, manifestRaw.Length);
                Net.Send(MessageId.EditorSnapshot, new EditorSnapshotMsg
                {
                    Facility = _facility,
                    Revision = _revision,
                    ShipName = editor.ship.shipName,
                    PartCount = editor.ship.parts != null ? editor.ship.parts.Count : 0,
                    CraftDeflated = craft,
                    ManifestDeflated = manifestBytes,
                    SessionOwnerClientId = _sessionOwner,
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
                SessionOwnerClientId = _sessionOwner,
            }, Channel.State, Delivery.Sequenced);
        }

        /// <summary>Called by the launch patch so everyone else leaves the shared bench, and so a player whose
        /// kerbal is seated hears about it before the vessel reaches them.</summary>
        public void AnnounceLaunch(string shipName, string site, string[] aboardKerbals)
        {
            if (!_joined) return;
            Net.Send(MessageId.EditorLaunch, new EditorLaunchMsg { Facility = _facility, ShipName = shipName, LaunchSite = site, AboardKerbals = aboardKerbals, SessionOwnerClientId = _sessionOwner }, Channel.Control, Delivery.ReliableOrdered);
            Log.Info("Announced the launch of " + shipName + " from " + site + " with " + (aboardKerbals != null ? aboardKerbals.Length : 0) + " kerbal(s) aboard");
        }

        // ---- remote changes coming in ----

        private void OnSnapshot(NetDataReader body)
        {
            var msg = Envelope.Read<EditorSnapshotMsg>(body);
            if (!IsOurSession(msg.SessionOwnerClientId)) return;
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
                // An edit made but not yet sent is about to be replaced by the other builder's craft. There is
                // no merging in a whole-craft model, so the honest thing is to say so, loudly enough to notice.
                var pendingEdit = _dirtyAt >= 0 && HeldPart() == null;   // a held part is kept, so nothing is lost
                ReplaceWorkbench(editor, ship);
                if (msg.ManifestDeflated != null && msg.ManifestDeflated.Length > 0)
                {
                    try
                    {
                        var seated = ApplyManifestText(Encoding.UTF8.GetString(DeflateCodec.Decompress(msg.ManifestDeflated, 0, msg.ManifestDeflated.Length)));
                        Log.Info("Applied the other builder's seating: " + seated + " kerbal(s) in their seats");
                    }
                    catch (Exception e)
                    {
                        Log.Exception("Applying the shared crew seating", e);
                    }
                }
                if (pendingEdit)
                {
                    _dirtyAt = -1f;
                    var who = NameOf(msg.FromClientId);
                    Log.Warn("The bench from " + who + " arrived while our last change was still going out; that change is gone");
                    ScreenMessages.PostScreenMessage(who + "'s change arrived on top of yours - check your last edit", 5f, ScreenMessageStyle.UPPER_CENTER);
                }
                // Hash what SendSnapshot would hash, not the bytes that arrived. KSP renumbers parts and
                // reorders them as it loads a craft, so the text we received and the text we would write back
                // out differ for the very same ship. Storing the received text here meant the guard never
                // matched, every applied craft was echoed back as if it were a local edit, and a part the
                // other player had just deleted reappeared on the next round trip.
                _lastSentHash = LocalCraftHash();
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

        /// <summary>
        /// Puts <paramref name="ship"/> on the workbench the way KSP's own craft load does.
        ///
        /// The obvious swap - <c>editor.ship.Clear(); editor.ship = ship;</c> - does not work, because
        /// <see cref="ShipConstruct.Clear"/> only empties a list (ShipConstruct.cs:2799). The previous craft's
        /// Part objects stay in the scene: drawn and clickable, but belonging to no ship. A part the other
        /// builder deleted lingers as a ghost, clicking a ghost does nothing, and a fresh set piles up with every
        /// snapshot applied - which is why the player who receives more snapshots ends up unable to delete
        /// anything at all. KSP's on_shipLoaded (EditorLogic.cs:6591-6640) destroys every part that is not in the
        /// new ship, drops the selection, re-layers and resets the crew tab; this does the same.
        /// </summary>
        internal static void ReplaceWorkbench(EditorLogic editor, ShipConstruct ship)
        {
            if (editor == null || ship == null) return;

            // SetBackup() copies these two fields back into the ship (EditorLogic.cs:7568), so they have to say
            // the incoming craft's name before it runs. Otherwise every applied craft is silently renamed to
            // whatever the receiving player had typed, and the rename goes back out as if it were a local edit.
            if (editor.shipNameField != null) editor.shipNameField.text = ship.shipName;
            if (editor.shipDescriptionField != null) editor.shipDescriptionField.text = ship.shipDescription;

            var previous = editor.ship;
            if (previous != null && previous.vesselDeltaV != null)
            {
                UnityEngine.Object.Destroy(previous.vesselDeltaV);
                previous.vesselDeltaV = null;
            }

            var partCount = ship.parts != null ? ship.parts.Count : 0;
            editor.ship = ship;
            editor.rootPart = partCount > 0 ? ship.parts[0].localRoot : null;

            // Whatever this player is holding is not in any ship - picking a part up detaches it - so the
            // sweep below would destroy it out of their hand. Keep the held part and everything hanging off it;
            // they will attach it to the new craft when they let go, and that attach is what gets shared.
            var held = HeldPart();
            var keep = new HashSet<Part>();
            if (held != null) CollectSubtree(held, keep);

            var strays = 0;
            var inShip = new HashSet<Part>();
            if (ship.parts != null) for (var i = 0; i < ship.parts.Count; i++) if (ship.parts[i] != null) inShip.Add(ship.parts[i]);
            var all = Part.allParts.ToArray();
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i] == null || inShip.Contains(all[i]) || keep.Contains(all[i])) continue;
                strays++;
                UnityEngine.Object.Destroy(all[i].gameObject);
            }
            if (held == null) editor.selectedPart = null;
            if (editor.rootPart != null) editor.rootPart.gameObject.SetLayerRecursive(0, filterTranslucent: true, ignoreLayersMask: 2097152);

            // The stage/dV readout is rebuilt per craft (EditorLogic.cs:1492); without it the engineer's report
            // and the staging list keep describing the craft that just left.
            if (partCount > 0 && ship.vesselDeltaV == null) ship.vesselDeltaV = VesselDeltaV.Create(ship);

            editor.SetBackup();
            // SetBackup early-returns on an empty craft (EditorLogic.cs:7537), so ShipConfig would still hold the
            // old ship and the crew tab below would be reset against it.
            if (partCount == 0) ShipConstruction.ShipConfig = ship.SaveShip();

            // The editor is a state machine, and an empty bench sits in "pick a pod": the parts list greys out
            // everything that cannot be a root part and most of the editor is locked (EditorLogic.cs:3140-3145),
            // and only leaving that state for idle lifts the lock and the filter (EditorLogic.cs:3275-3280).
            // A craft arriving on an empty bench has to make that transition the way KSP's own load does, or the
            // player who joined is told a strut "cannot be the first part placed" on a bench with fifty parts on
            // it. The reverse holds when the other builder deletes the pod.
            // This runs after SetBackup because on_shipLoaded resets the crew tab against ShipConfig, which an
            // empty bench has never filled in (that was a NullReferenceException in the first test).
            try
            {
                var fsm = editor.fsm;
                if (fsm != null && fsm.Started)
                {
                    if (editor.rootPart != null && fsm.CurrentState == editor.st_podSelect)
                    {
                        fsm.RunEvent(editor.on_shipLoaded);
                        Log.Info("Left the pick-a-pod state: the bench has a root part now");
                    }
                    else if (editor.rootPart == null && fsm.CurrentState == editor.st_idle)
                    {
                        // Only from idle: with a part in hand (st_place) the drop itself decides what happens.
                        fsm.RunEvent(editor.on_podDeleted);
                        Log.Info("Back in the pick-a-pod state: the bench is empty");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Exception("Moving the editor out of or into pick-a-pod", e);
            }



            var manifest = ShipConstruction.ShipManifest;
            var keptCrew = manifest != null && manifest.CrewCount > 0;
            if (keptCrew) editor.RefreshCrewAssignment(ShipConstruction.ShipConfig, editor.GetPartExistsFilter());
            else editor.ResetCrewAssignment(ShipConstruction.ShipConfig, allowAutoHire: false);

            Log.Info("Replaced the workbench: destroyed " + strays + " stray part(s), " + partCount + " part(s) now, crew " + (keptCrew ? "kept" : "reset")
                     + (held != null ? ", kept the " + keep.Count + " part(s) in hand" : ""));
        }

        private static void CollectSubtree(Part part, HashSet<Part> into)
        {
            if (part == null || !into.Add(part)) return;
            if (part.children != null)
                for (var i = 0; i < part.children.Count; i++) CollectSubtree(part.children[i], into);
            // Symmetry copies of a held part are live parts in no ship and not its children (EditorLogic
            // DuplicatePart); destroying them out from under EditorLogic left it dereferencing dead objects
            // every frame with the part stuck in the hand.
            if (part.symmetryCounterparts != null)
                for (var i = 0; i < part.symmetryCounterparts.Count; i++) CollectSubtree(part.symmetryCounterparts[i], into);
        }

        private static string HashOf(string craftText) =>
            craftText == null ? "" : craftText.Length + ":" + craftText.GetHashCode();

        /// <summary>The workbench as we would send it, which is the only form worth comparing against.</summary>
        private static string LocalCraftHash()
        {
            var editor = EditorLogic.fetch;
            if (editor == null || editor.ship == null) return "";
            try
            {
                var node = editor.ship.SaveShip();
                return node == null ? "" : HashOf(ProtoCodec.ToText(node) + "\n" + ManifestText());
            }
            catch (Exception e)
            {
                Log.Exception("Hashing the workbench", e);
                return "";
            }
        }

        private void OnPresence(NetDataReader body)
        {
            var msg = Envelope.Read<EditorPresenceMsg>(body);
            if (msg.ClientId == 0 || msg.ClientId == Net.ClientId || !IsOurSession(msg.SessionOwnerClientId)) return;
            _others[msg.ClientId] = msg;
            _othersSeenAt[msg.ClientId] = Time.realtimeSinceStartup;
        }

        /// <summary>A message addressed to bench <paramref name="owner"/>: is that the bench we are standing at?</summary>
        private bool IsOurSession(int owner) =>
            owner == _sessionOwner || (OnOwnBench && (owner == 0 || owner == Net.ClientId));

        /// <summary>
        /// Go and build on somebody else's bench. Whatever is on ours is stashed rather than thrown away, and
        /// comes back when we leave; the craft we are joining arrives as an ordinary snapshot.
        /// </summary>
        public void JoinSession(int ownerClientId)
        {
            if (!_joined || ownerClientId == Net.ClientId || ownerClientId == _sessionOwner) return;
            _stash = StashWorkbench();
            _sessionOwner = ownerClientId;
            _revision = 0;
            _lastSentHash = "";
            _others.Clear();
            _dirtyAt = -1f;
            // Start from an empty bench: the craft we are joining arrives as a snapshot, and if that bench is
            // empty nothing arrives at all - our own craft would have stayed on screen and gone out as the
            // first edit on their bench.
            var editor = EditorLogic.fetch;
            if (editor != null)
            {
                try { Applying = true; ReplaceWorkbench(editor, new ShipConstruct()); _lastSentHash = LocalCraftHash(); }
                catch (Exception e) { Log.Exception("Clearing the bench to join", e); }
                finally { Applying = false; }
            }
            Net.Send(MessageId.EditorSessionJoin, new EditorSessionJoinMsg { OwnerClientId = ownerClientId }, Channel.Control, Delivery.ReliableOrdered);
            Log.Info("Joining " + NameOf(ownerClientId) + "'s workbench" + (_stash != null ? " (ours is stashed)" : ""));
        }

        /// <summary>Back to our own bench, with whatever we had stashed when we left it.</summary>
        public void LeaveSession()
        {
            if (!_joined || OnOwnBench) return;
            Log.Info("Leaving " + NameOf(_sessionOwner) + "'s workbench");
            _sessionOwner = 0;
            _revision = 0;
            _lastSentHash = "";
            _others.Clear();
            Net.Send(MessageId.EditorSessionJoin, new EditorSessionJoinMsg { OwnerClientId = 0 }, Channel.Control, Delivery.ReliableOrdered);
            RestoreStash();
        }

        /// <summary>True when leaving our bench would lose work, so the UI can ask first.</summary>
        public bool OwnBenchHasParts()
        {
            var editor = EditorLogic.fetch;
            return editor != null && editor.ship != null && editor.ship.parts != null && editor.ship.parts.Count > 0;
        }

        private ConfigNode StashWorkbench()
        {
            var editor = EditorLogic.fetch;
            if (editor == null || editor.ship == null || editor.ship.parts == null || editor.ship.parts.Count == 0) return null;
            try
            {
                return editor.ship.SaveShip();
            }
            catch (Exception e)
            {
                Log.Exception("Stashing our craft", e);
                return null;
            }
        }

        private void RestoreStash()
        {
            var editor = EditorLogic.fetch;
            if (editor == null) return;
            try
            {
                Applying = true;
                var ship = new ShipConstruct();
                if (_stash != null && ship.LoadShip(_stash.GetNode("ShipConstruct") ?? _stash))
                {
                    ReplaceWorkbench(editor, ship);
                    Log.Info("Restored our own craft: " + ship.shipName + ", " + (ship.parts != null ? ship.parts.Count : 0) + " part(s)");
                }
                else
                {
                    ReplaceWorkbench(editor, new ShipConstruct());
                    Log.Info("Back on our own empty workbench");
                }
                _lastSentHash = LocalCraftHash();
            }
            catch (Exception e)
            {
                Log.Exception("Restoring our craft", e);
            }
            finally
            {
                Applying = false;
                _stash = null;
            }
            // Our bench is ours again and the server has nothing on it, so send what is there now.
            _dirtyAt = Time.realtimeSinceStartup;
        }

        /// <summary>The bench we were visiting is gone (its owner left the editor): keep the craft, on our own.</summary>
        public void OnSessionLost()
        {
            if (OnOwnBench) return;
            var who = NameOf(_sessionOwner);
            _sessionOwner = 0;
            _revision = 0;
            _lastSentHash = "";
            _others.Clear();
            // Their craft went with them (launched, or they left); ours comes back out of the stash, the same
            // as when we leave on purpose. Throwing the stash away here lost the guest's own craft for good.
            var hadStash = _stash != null;
            RestoreStash();
            Addon.Notices.Post("bench-lost", who + "'s workbench is gone; " + (hadStash ? "your own craft is back on your bench" : "you are back on your own empty bench"), Ui.Theme.Warn);
        }

        /// <summary>The workbench was launched out from under us; start again from an empty revision.</summary>
        public void OnRemoteLaunch(int sessionOwnerClientId)
        {
            // Only the bench we are standing at: any launch anywhere used to reset every builder's revision,
            // so their next edit was refused as stale and overwritten.
            if (!_joined || !IsOurSession(sessionOwnerClientId)) return;
            _revision = 0;
            _lastSentHash = "";
        }

        private string NameOf(int clientId) => Addon.Players.TryGet(clientId, out var p) ? p.Name : "#" + clientId;

        /// <summary>Draws the other builders' cursors and what they are holding.</summary>
        public void DrawOverlay()
        {
            if (!Active || _others.Count == 0 || EditorLogic.fetch == null || EditorLogic.fetch.editorCamera == null) return;
            // Plain text over the editor is unreadable against a pale hull or the bright floor, so the label
            // gets the same dark chip the windows use, and each builder keeps the colour they have everywhere else.
            Ui.Theme.Ensure();
            if (_labelStyle == null)
                _labelStyle = new GUIStyle(Ui.Theme.Chip) { fontSize = 12, richText = true, alignment = TextAnchor.MiddleCenter };
            var camera = EditorLogic.fetch.editorCamera;
            foreach (var pair in _others)
            {
                var presence = pair.Value;
                if (!presence.Holding) continue;
                var world = new Vector3(presence.CursorX, presence.CursorY, presence.CursorZ);
                var screen = camera.WorldToScreenPoint(world);
                if (screen.z <= 0) continue;
                var text = Ui.Theme.Tint(NameOf(pair.Key), Ui.Theme.PlayerColour(pair.Key))
                           + Ui.Theme.Tint("  " + presence.HeldPartName, Ui.Theme.Ink);
                var size = _labelStyle.CalcSize(new GUIContent(text));
                var rect = new Rect(screen.x - size.x / 2f, Screen.height - screen.y - 12, size.x, size.y);
                GUI.Label(rect, text, _labelStyle);
            }
        }
    }
}
