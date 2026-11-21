using System.Runtime.ExceptionServices;
using LogitechCameraBridge.Common;

namespace LogitechCameraBridge.Wpf.Tests;

public class SingleInstanceGuardTests
{
    [Fact]
    public void AnotherLaunchCannotAcquireUntilTheOwnerFinishesCleanup()
    {
        string name = Name();
        var owner = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(owner);
        try
        {
            RunOnOtherThread(() =>
            {
                using var duplicate = SingleInstanceGuard.TryAcquire(name);
                Assert.Null(duplicate);
            });
        }
        finally { owner.Dispose(); }

        // Normal shutdown/restart allows the next launch to take ownership.
        RunOnOtherThread(() =>
        {
            using var replacement = SingleInstanceGuard.TryAcquire(name);
            Assert.NotNull(replacement);
        });
        owner.Dispose(); // Idempotent disposal.
    }

    [Fact]
    public void CrashedOwnerDoesNotPermanentlyBlockTheApp()
    {
        string name = Name();
        using var abandoned = new Mutex(initiallyOwned: false, name);
        RunOnOtherThread(() => Assert.True(abandoned.WaitOne(0)));
        // The owning thread exited without releasing; Windows marks the mutex abandoned.
        using var recovered = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(recovered);
        RunOnOtherThread(() =>
        {
            using var duplicate = SingleInstanceGuard.TryAcquire(name);
            Assert.Null(duplicate);
        });
    }

    [Fact]
    public void UnrelatedInstanceNamesDoNotBlockEachOther()
    {
        using var first = SingleInstanceGuard.TryAcquire(Name());
        RunOnOtherThread(() =>
        {
            using var separate = SingleInstanceGuard.TryAcquire(Name());
            Assert.NotNull(separate);
        });
    }

    private static string Name() => $@"Local\LogitechCameraBridge.Tests.{Guid.NewGuid():N}";

    private static void RunOnOtherThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Mutex test worker did not finish.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
