using FantasyTale.Launcher.Core.Instances;

namespace FantasyTale.Launcher.Core.Stores;

internal static class StorageFailure
{
    internal static string Code(Exception error)
    {
        for (var depth = 0; depth < 8 && error is not null; depth++, error = error.InnerException!)
        {
            if (error is InstanceBusyException) return "busy";
            if (error is UnauthorizedAccessException) return "access";
            if (error is not IOException) continue;
            var code = error.HResult & 0xffff;
            if (code is 39 or 112) return "space";
            if (code is 32 or 33) return "busy";
            if (code == 5) return "access";
        }
        return "disk";
    }
}
