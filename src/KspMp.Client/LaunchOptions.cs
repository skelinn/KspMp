using System;

namespace KspMp
{
    /// <summary>
    /// Command-line options for scripted sessions and quick testing:
    ///   -kspmp-connect host[:port]   connect as soon as the main menu is ready
    ///   -kspmp-name Name             player name for this run (not saved)
    ///   -kspmp-enter                 enter the game right after the server accepts us
    ///   -kspmp-say "text"            send one chat message after joining
    ///   -kspmp-debug                 show the debug window
    ///   -kspmp-launch "Ships/VAB/Kerbal X.craft"   launch a craft (path relative to the KSP folder) once in the space center
    ///   -kspmp-site LaunchPad|Runway  launch site for -kspmp-launch (default from the craft folder)
    ///   -kspmp-fly N                 N seconds after launch: SAS on, full throttle, stage once
    ///   -kspmp-warp I:D:S            D seconds after flight starts request warp index I, cancel it S seconds later
    ///   -kspmp-avatar "Name:Trait"   claim this Kerbal on first join (Trait = Pilot, Engineer or Scientist)
    ///   -kspmp-crew "Name,Name"      seat these kerbals after the avatar on -kspmp-launch
    ///   -kspmp-stage D               D seconds after entering flight: press space once
    ///   -kspmp-input D:S             D seconds after entering flight: hold pitch 0.3 and throttle 0.8 for S seconds
    ///   -kspmp-toggle Group:D        D seconds after entering flight: toggle an action group (Light, Gear, RCS, Brakes, ...)
    ///   -kspmp-partevent Name:D      D seconds after entering flight: fire a part-menu action by name on the active vessel
    /// </summary>
    public sealed class LaunchOptions
    {
        public string ConnectHost;
        public int ConnectPort = 7777;
        public string PlayerName;
        public bool EnterGame;
        public string Say;
        public bool Debug;
        public string LaunchCraft;
        public string LaunchSite;
        public float FlyAfterSeconds = -1f;
        public string AvatarName;
        public string AvatarTrait = "Pilot";
        public string[] ExtraCrew = new string[0];
        public float StageAfterSeconds = -1f;
        public float InputAfterSeconds = -1f;
        public float InputDurationSeconds = 10f;
        public string ToggleGroup;
        public float ToggleAfterSeconds = -1f;
        public string PartEventName;
        public float PartEventAfterSeconds = -1f;
        public int WarpIndex = -1;
        public float WarpAfterSeconds = 30f;
        public float WarpDurationSeconds = 30f;

        public bool AutoConnect => !string.IsNullOrEmpty(ConnectHost);

        public static LaunchOptions Parse(string[] args)
        {
            var options = new LaunchOptions();
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-kspmp-connect" when i + 1 < args.Length:
                    {
                        var target = args[++i];
                        var colon = target.LastIndexOf(':');
                        if (colon > 0 && int.TryParse(target.Substring(colon + 1), out var port))
                        {
                            options.ConnectHost = target.Substring(0, colon);
                            options.ConnectPort = port;
                        }
                        else options.ConnectHost = target;
                        break;
                    }
                    case "-kspmp-name" when i + 1 < args.Length:
                        options.PlayerName = args[++i];
                        break;
                    case "-kspmp-enter":
                        options.EnterGame = true;
                        break;
                    case "-kspmp-say" when i + 1 < args.Length:
                        options.Say = args[++i];
                        break;
                    case "-kspmp-debug":
                        options.Debug = true;
                        break;
                    case "-kspmp-launch" when i + 1 < args.Length:
                        options.LaunchCraft = args[++i];
                        break;
                    case "-kspmp-site" when i + 1 < args.Length:
                        options.LaunchSite = args[++i];
                        break;
                    case "-kspmp-avatar" when i + 1 < args.Length:
                    {
                        var spec = args[++i];
                        var colon = spec.LastIndexOf(':');
                        options.AvatarName = colon > 0 ? spec.Substring(0, colon) : spec;
                        if (colon > 0) options.AvatarTrait = spec.Substring(colon + 1);
                        break;
                    }
                    case "-kspmp-crew" when i + 1 < args.Length:
                        options.ExtraCrew = args[++i].Split(',');
                        for (var k = 0; k < options.ExtraCrew.Length; k++) options.ExtraCrew[k] = options.ExtraCrew[k].Trim();
                        break;
                    case "-kspmp-stage" when i + 1 < args.Length:
                        if (float.TryParse(args[++i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var stageAfter)) options.StageAfterSeconds = stageAfter;
                        break;
                    case "-kspmp-input" when i + 1 < args.Length:
                    {
                        var parts = args[++i].Split(':');
                        if (parts.Length > 0 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var after)) options.InputAfterSeconds = after;
                        if (parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var duration)) options.InputDurationSeconds = duration;
                        break;
                    }
                    case "-kspmp-toggle" when i + 1 < args.Length:
                    {
                        var spec = args[++i];
                        var colon = spec.LastIndexOf(':');
                        options.ToggleGroup = colon > 0 ? spec.Substring(0, colon) : spec;
                        if (colon > 0 && float.TryParse(spec.Substring(colon + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var toggleAfter)) options.ToggleAfterSeconds = toggleAfter;
                        break;
                    }
                    case "-kspmp-partevent" when i + 1 < args.Length:
                    {
                        var spec = args[++i];
                        var colon = spec.LastIndexOf(':');
                        options.PartEventName = colon > 0 ? spec.Substring(0, colon) : spec;
                        if (colon > 0 && float.TryParse(spec.Substring(colon + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var eventAfter)) options.PartEventAfterSeconds = eventAfter;
                        break;
                    }
                    case "-kspmp-warp" when i + 1 < args.Length:
                    {
                        var parts = args[++i].Split(':');
                        if (parts.Length > 0 && int.TryParse(parts[0], out var index)) options.WarpIndex = index;
                        if (parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var delay)) options.WarpAfterSeconds = delay;
                        if (parts.Length > 2 && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var duration)) options.WarpDurationSeconds = duration;
                        break;
                    }
                    case "-kspmp-fly" when i + 1 < args.Length:
                        if (float.TryParse(args[++i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds)) options.FlyAfterSeconds = seconds;
                        break;
                }
            }
            return options;
        }
    }
}
