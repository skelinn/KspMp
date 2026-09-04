using System;

namespace KspMp.Game
{
    /// <summary>Creates the local sandbox save that mirrors the server's universe and enters it.</summary>
    public static class SessionStarter
    {
        public const string SaveName = "KspMp";

        public static bool IsInMultiplayerSave => HighLogic.CurrentGame != null && HighLogic.SaveFolder == SaveName;

        /// <summary>Puts our avatar into the first seat of a launch manifest (replacing whoever was there).</summary>
        public static bool SeatAvatar(VesselCrewManifest manifest)
        {
            var roster = KspMpAddon.Instance.Roster;
            if (manifest == null || !roster.HasAvatar || HighLogic.CurrentGame == null || !HighLogic.CurrentGame.CrewRoster.Exists(roster.AvatarName)) return false;
            var avatar = HighLogic.CurrentGame.CrewRoster[roster.AvatarName];
            if (avatar.rosterStatus == ProtoCrewMember.RosterStatus.Assigned)
            {
                Log.Warn("Not seating " + avatar.name + ": already assigned to a vessel");
                return false;
            }
            var crewable = manifest.GetCrewableParts();
            if (crewable == null || crewable.Count == 0) return false;
            foreach (var pm in crewable)
            {
                var crew = pm.GetPartCrew();
                for (var i = 0; i < crew.Length; i++)
                    if (crew[i] != null && crew[i].name == avatar.name) return true; // already aboard
            }
            var first = crewable[0];
            first.RemoveCrewFromSeat(0);
            first.AddCrewToSeat(avatar, 0);
            return true;
        }

        /// <summary>Seats the named kerbals (they must be in the roster) into the next free seats of the manifest.</summary>
        public static int SeatCrew(VesselCrewManifest manifest, string[] names)
        {
            if (manifest == null || names == null || names.Length == 0 || HighLogic.CurrentGame == null) return 0;
            var roster = HighLogic.CurrentGame.CrewRoster;
            var seated = 0;
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (!roster.Exists(name))
                {
                    // Seat a friend's Kerbal before they join: create it as an ordinary kerbal; their claim adopts it.
                    var created = new ProtoCrewMember(ProtoCrewMember.KerbalType.Crew, name);
                    KerbalRoster.SetExperienceTrait(created, "Pilot");
                    created.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                    roster.AddCrewMember(created);
                    Log.Info("Created kerbal " + name + " to seat them (not in the roster yet)");
                }
                var kerbal = roster[name];
                if (kerbal.rosterStatus == ProtoCrewMember.RosterStatus.Assigned) { Log.Warn("Not seating " + name + ": already assigned to a vessel"); continue; }
                var done = false;
                foreach (var pm in manifest.GetCrewableParts())
                {
                    var crew = pm.GetPartCrew();
                    for (var i = 0; i < crew.Length && !done; i++)
                    {
                        if (crew[i] != null && crew[i].name == name) done = true;
                    }
                    if (done) break;
                    for (var i = 0; i < crew.Length; i++)
                    {
                        if (crew[i] != null) continue;
                        pm.AddCrewToSeat(kerbal, i);
                        done = true;
                        seated++;
                        break;
                    }
                    if (done) break;
                }
                if (!done)
                {
                    // No free seat: bump the last non-avatar occupant of the first crewable part.
                    var pm = manifest.GetCrewableParts()[0];
                    var crew = pm.GetPartCrew();
                    for (var i = crew.Length - 1; i >= 1; i--)
                    {
                        if (crew[i] == null) continue;
                        pm.RemoveCrewFromSeat(i);
                        pm.AddCrewToSeat(kerbal, i);
                        seated++;
                        break;
                    }
                }
            }
            return seated;
        }

        public static void EnterGame(double universalTime)
        {
            Log.Info("Starting multiplayer sandbox at UT " + universalTime.ToString("F1"));
            var parameters = GameParameters.GetDefaultParameters(global::Game.Modes.SANDBOX, GameParameters.Preset.Normal);
            parameters.Flight.CanQuickLoad = false;
            parameters.Flight.CanRestart = false;
            parameters.Flight.CanLeaveToEditor = false;

            // Same path the stock "Start new game" button takes.
            var game = GamePersistence.CreateNewGame(SaveName, global::Game.Modes.SANDBOX, parameters, "Squad/Flags/default", GameScenes.SPACECENTER, EditorFacility.None);
            // A brand-new Game has no flight state yet (KSP creates it lazily); we need it to carry the server's UT.
            if (game.flightState == null) game.flightState = new FlightState();
            game.flightState.universalTime = universalTime;
            KspMpAddon.Instance.Roster.SeedGame(game);
            var seeded = KspMpAddon.Instance.VesselProto.SeedFlightState(game);
            if (seeded > 0) Log.Info("Seeded the new save with " + seeded + " vessel(s) from the server");
            HighLogic.CurrentGame = game;
            GamePersistence.SaveGame(game, "persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            game.Start();
        }
    }
}
