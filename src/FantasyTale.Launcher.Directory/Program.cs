using FantasyTale.Launcher.DirectoryService;

var configuration = new ConfigurationBuilder().AddEnvironmentVariables("FT_DIRECTORY_").AddCommandLine(args).Build();
var options = configuration.Get<DirectoryOptions>() ?? new DirectoryOptions();
await using var server = await DirectoryHttp.StartAsync(options);
await server.WaitForShutdownAsync();
