using LogitechCameraBridge.Wpf.Services;
using System.IO;
using Velopack;
using Velopack.Sources;
using Velopack.Locators;

namespace LogitechCameraBridge.Wpf.Tests;

public sealed class AppUpdaterTests
{
    [Fact]
    public async Task UninstalledAppDoesNotContactSource()
    {
        var manager = new FakeManager { Installed = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AppUpdater(() => manager).DownloadUpdateAsync(default));
        Assert.False(manager.Checked);
    }

    [Fact]
    public async Task CurrentVersionDoesNotDownloadOrRequestRestart()
    {
        var manager = new FakeManager();
        Assert.Null(await new AppUpdater(() => manager).DownloadUpdateAsync(default));
        Assert.True(manager.Checked);
        Assert.False(manager.Downloaded);
    }

    [Fact]
    public async Task NewVersionDownloadsBeforeReturningApplyAction()
    {
        var manager = new FakeManager { Update = new UpdateInfo(new VelopackAsset(), false) };
        var apply = await new AppUpdater(() => manager).DownloadUpdateAsync(default);
        Assert.True(manager.Downloaded);
        Assert.NotNull(apply);
        // Do not execute the action: the application owns cleanup and process exit.
    }

    [Fact]
    public async Task FailedDownloadDoesNotReturnApplyAction()
    {
        var manager = new FakeManager
        {
            Update = new UpdateInfo(new VelopackAsset(), false),
            FailDownload = true
        };
        await Assert.ThrowsAsync<IOException>(() =>
            new AppUpdater(() => manager).DownloadUpdateAsync(default));
    }

    [Fact]
    public async Task ExitDuringCheckPreventsDownloadAndApply()
    {
        using var cancellation = new CancellationTokenSource();
        var manager = new FakeManager
        {
            Update = new UpdateInfo(new VelopackAsset(), false),
            AfterCheck = cancellation.Cancel
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AppUpdater(() => manager).DownloadUpdateAsync(cancellation.Token));
        Assert.False(manager.Downloaded);
    }

    private sealed class FakeManager() : UpdateManager(new SimpleWebSource("https://example.invalid"),
        locator: new TestVelopackLocator("UpdateTests", "1.0.0", Path.GetTempPath()))
    {
        public bool Installed { get; init; } = true;
        public override bool IsInstalled => Installed;
        public UpdateInfo? Update { get; init; }
        public bool Checked { get; private set; }
        public bool Downloaded { get; private set; }
        public bool FailDownload { get; init; }
        public Action? AfterCheck { get; init; }

        public override Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            Checked = true;
            AfterCheck?.Invoke();
            return Task.FromResult(Update);
        }

        public override Task DownloadUpdatesAsync(UpdateInfo updates, Action<int>? progress = null,
            CancellationToken cancelToken = default)
        {
            Assert.Same(Update, updates);
            if (FailDownload)
                throw new IOException("Download interrupted.");
            Downloaded = true;
            return Task.CompletedTask;
        }
    }
}
