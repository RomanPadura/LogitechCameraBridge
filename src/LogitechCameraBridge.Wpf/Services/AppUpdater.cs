using Velopack;
using Velopack.Sources;

namespace LogitechCameraBridge.Wpf.Services;

public sealed class AppUpdater(Func<UpdateManager>? managerFactory = null)
{
    public const string RepositoryUrl = "https://github.com/RomanPadura/LogitechCameraBridge";
    public const string TokenVariable = "LOGITECH_CAMERA_BRIDGE_GITHUB_TOKEN";

    // Construct only when requested, so normal startup never contacts GitHub.
    public async Task<Action?> DownloadUpdateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manager = managerFactory?.Invoke() ?? CreateManager();
        if (!manager.IsInstalled)
            throw new InvalidOperationException("Install the application using the release Setup.exe before checking for updates.");

        var update = await manager.CheckForUpdatesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (update is null)
            return null;

        await manager.DownloadUpdatesAsync(update, cancelToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // The caller first releases devices and flushes logs, then starts the updater and exits.
        return () => manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: true);
    }

    private static UpdateManager CreateManager()
    {
        string? token = Environment.GetEnvironmentVariable(TokenVariable);
        if (string.IsNullOrWhiteSpace(token))
            token = Environment.GetEnvironmentVariable(TokenVariable, EnvironmentVariableTarget.User);
        return new UpdateManager(new GithubSource(RepositoryUrl,
            string.IsNullOrWhiteSpace(token) ? null : token, prerelease: false));
    }
}
