using Xunit;

namespace FantasyTale.Launcher.Core.Tests;

[CollectionDefinition("fixed local hosting ports", DisableParallelization = true)]
public sealed class FixedLocalHostingPortsDefinition;

// Native fixtures share process-name inspection and the runtime's sample lock;
// overlapping launches can make recovery observe an unrelated process exiting.
// Updater fixtures also copy and execute native images; serialize their process
// and pipe scenarios while managed unit tests remain parallel.
[CollectionDefinition("native hook runtime", DisableParallelization = true)]
public sealed class NativeHookRuntimeDefinition;
