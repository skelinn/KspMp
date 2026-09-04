using KspMp.Vessels;

namespace KspMp.Roster
{
    /// <summary>ProtoCrewMember &lt;-&gt; ConfigNode text, plus field copying onto an existing roster entry.</summary>
    public static class KerbalCodec
    {
        public static string ToText(ProtoCrewMember kerbal)
        {
            var node = new ConfigNode("KERBAL");
            kerbal.Save(node);
            return ProtoCodec.ToText(node);
        }

        public static ProtoCrewMember Parse(string text, global::Game.Modes mode)
        {
            var parsed = ConfigNode.Parse(text);
            if (parsed == null) return null;
            var node = parsed.GetNode("KERBAL") ?? parsed;
            return new ProtoCrewMember(mode, node);
        }

        /// <summary>Copies everything except placement (which vessel snapshots decide) onto an existing kerbal.</summary>
        public static void CopyInto(ProtoCrewMember target, ProtoCrewMember source)
        {
            target.courage = source.courage;
            target.stupidity = source.stupidity;
            target.isBadass = source.isBadass;
            target.veteran = source.veteran;
            target.hasToured = source.hasToured;
            target.experience = source.experience;
            target.experienceLevel = source.experienceLevel;
            if (target.gender != source.gender) target.gender = source.gender;
            if (target.type != source.type) target.type = source.type;
            if (target.trait != source.trait) KerbalRoster.SetExperienceTrait(target, source.trait);
            target.inactiveTimeEnd = source.inactiveTimeEnd;
            if (target.inactive != source.inactive) target.inactive = source.inactive;
            if (target.rosterStatus != source.rosterStatus) target.rosterStatus = source.rosterStatus;
        }
    }
}
