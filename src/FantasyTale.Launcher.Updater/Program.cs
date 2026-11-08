using FantasyTale.Launcher.Core.Updates;

if (args.Length is 3 or 4 && args[0] == "--repair" && (args.Length == 3 || args[3] == "--silent"))
    return await LauncherUpdateHandoff.RepairOfflineAsync(args[1], args[2], showErrors: args.Length == 3);
return args.Length == 1 ? await LauncherUpdateHandoff.RunAsync(args[0]) : 2;
