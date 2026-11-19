using System.Runtime.CompilerServices;

// The renderer assemblies the destination view (DestinationView.cs) reaches into. Keep this list in sync with the
// <Publicize> entries in SDX2_SystemMap.csproj: local builds publicize them with Krafs.Publicizer, and Pulsar
// publicizes the assemblies named here when it compiles the plugin on the player's PC.
[assembly: IgnoresAccessChecksTo("VRage.Render")]
[assembly: IgnoresAccessChecksTo("VRage.Render11")]

// Krafs.Publicizer generates this attribute class in local builds (LOCAL_BUILD); Pulsar's build needs it here.
#if !LOCAL_BUILD
namespace System.Runtime.CompilerServices
{
    [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class IgnoresAccessChecksToAttribute : System.Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName) { AssemblyName = assemblyName; }
        public string AssemblyName { get; }
    }
}
#endif
