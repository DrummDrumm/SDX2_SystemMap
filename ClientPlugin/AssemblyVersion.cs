// Pulsar compiles the hub build from source without MSBuild, so the version in Directory.Build.props never reaches it.
// Keep this in step with <Version> there. Local builds (LOCAL_BUILD) get theirs from the SDK.
#if !LOCAL_BUILD
[assembly: System.Reflection.AssemblyVersion("0.1.0")]
#endif
