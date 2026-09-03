#if NETFRAMEWORK
// KSP's Mono runtime ships no System.Runtime.Serialization.dll. LiteNetLib's NetSerializer references that assembly
// for exactly one thing: Attribute.IsDefined(property, typeof(IgnoreDataMemberAttribute)). For the net472 build the
// implicit framework reference is disabled (see the csproj) so this local type binds instead and nothing needs the
// missing assembly at runtime. KspMp does not use NetSerializer/NetPacketProcessor itself; only NatPunchModule does.
namespace System.Runtime.Serialization
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    internal sealed class IgnoreDataMemberAttribute : Attribute
    {
    }
}
#endif
