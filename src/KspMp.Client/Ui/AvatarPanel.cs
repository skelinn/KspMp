using KspMp.Systems;
using UnityEngine;

namespace KspMp.Ui
{
    /// <summary>First-join form: pick your Kerbal's name and trait.</summary>
    internal sealed class AvatarPanel
    {
        private static readonly string[] Traits = { "Pilot", "Engineer", "Scientist" };
        private readonly KspMpAddon _addon;
        private string _name;
        private int _trait;

        public AvatarPanel(KspMpAddon addon)
        {
            _addon = addon;
            _name = string.IsNullOrEmpty(addon.Settings.AvatarKerbalName) ? addon.Settings.PlayerName + " Kerman" : addon.Settings.AvatarKerbalName;
        }

        public void Draw()
        {
            var roster = _addon.Roster;
            GUILayout.Label("<b>Choose your Kerbal</b>  (you will be this Kerbal in the world)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(60));
            _name = GUILayout.TextField(_name, 40);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Trait", GUILayout.Width(60));
            _trait = GUILayout.SelectionGrid(_trait, Traits, 3);
            GUILayout.EndHorizontal();
            GUI.enabled = !roster.ClaimPending;
            if (GUILayout.Button(roster.ClaimPending ? "Claiming ..." : "Claim this Kerbal")) roster.Claim(_name, Traits[_trait]);
            GUI.enabled = true;
            if (!string.IsNullOrEmpty(roster.ClaimError)) GUILayout.Label("<color=#ff8080>" + roster.ClaimError + "</color>");
        }
    }
}
