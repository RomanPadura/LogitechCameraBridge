using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace LogitechCameraBridge.Wpf.Services;

public static class AppRestart
{
    private const string ParentOption = "--restart-parent";

    public static ProcessStartInfo CreateStartInfo(string processPath, string assemblyPath,
        IReadOnlyList<string> arguments, int parentProcessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parentProcessId);
        var start = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory
        };
        // Support both the normal apphost executable and explicit dotnet <application.dll> launches.
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
            start.ArgumentList.Add(assemblyPath);
        }
        for (int index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] == ParentOption)
            {
                index++; // Replace the previous restart handshake rather than accumulating it.
                continue;
            }
            start.ArgumentList.Add(arguments[index]);
        }
        start.ArgumentList.Add(ParentOption);
        start.ArgumentList.Add(parentProcessId.ToString(CultureInfo.InvariantCulture));
        return start;
    }

    public static int? GetParentProcessId(IReadOnlyList<string> arguments)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] != ParentOption)
                continue;
            if (index + 1 >= arguments.Count || !int.TryParse(arguments[index + 1],
                    NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0 || id == Environment.ProcessId)
                throw new ArgumentException("Invalid restart parent process ID.");
            return id;
        }
        return null;
    }

    public static async Task WaitForParentAsync(IReadOnlyList<string> arguments)
    {
        if (GetParentProcessId(arguments) is not { } id)
            return;
        Process parent;
        try { parent = Process.GetProcessById(id); }
        catch (ArgumentException) { return; } // The old instance has already exited.
        using (parent)
            await parent.WaitForExitAsync();
    }

    public static void StartReplacement()
    {
        var start = CreateStartInfo(Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine application executable path."),
            typeof(App).Assembly.Location, Environment.GetCommandLineArgs().Skip(1).ToArray(), Environment.ProcessId);
        using var replacement = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the replacement application.");
    }
}
