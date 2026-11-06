namespace ValoTools.Services;

internal static class DebugConsole
{
    private static bool _enabled;

    public static void Initialize(bool enabled, Func<bool> allocateConsole)
    {
        if (!enabled || !allocateConsole()) return;
        _enabled = true;
        Console.Title = "ValoTools - Debug Console (tokens are hidden)";
        WriteLine("Debug console started. Riot request URLs/statuses are logged here.");
    }

    public static void WriteLine(string message)
    {
        if (_enabled) Console.WriteLine(message);
    }
}
