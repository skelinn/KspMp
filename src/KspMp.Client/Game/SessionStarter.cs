using System;

namespace KspMp.Game
{
    /// <summary>Creates the local sandbox save that mirrors the server's universe and enters it.</summary>
    public static class SessionStarter
    {
        public const string SaveName = "KspMp";

        public static bool IsInMultiplayerSave => HighLogic.CurrentGame != null && HighLogic.SaveFolder == SaveName;

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
            var seeded = KspMpAddon.Instance.VesselProto.SeedFlightState(game);
            if (seeded > 0) Log.Info("Seeded the new save with " + seeded + " vessel(s) from the server");
            HighLogic.CurrentGame = game;
            GamePersistence.SaveGame(game, "persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            game.Start();
        }
    }
}
