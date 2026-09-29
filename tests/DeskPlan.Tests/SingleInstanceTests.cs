using DeskPlan.Services;

namespace DeskPlan.Tests;

public class SingleInstanceTests
{
    [Fact]
    public void DuplicateNotifiesPrimaryAndExitsWithoutOwningLock()
    {
        var scope = Guid.NewGuid().ToString();
        using var first = new SingleInstanceService(scope);
        using var activated = new ManualResetEventSlim();
        Assert.True(first.IsPrimary);
        first.Listen(() => activated.Set());
        var thread = new Thread(() =>
        {
            using var duplicate = new SingleInstanceService(scope);
            Assert.False(duplicate.IsPrimary);
            duplicate.ActivateExisting();
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ActivationBeforeUiReadyIsRetained()
    {
        using var first = new SingleInstanceService(Guid.NewGuid().ToString());
        using var activated = new ManualResetEventSlim();
        first.ActivateExisting();
        first.Listen(() => activated.Set());
        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ShutdownReleasesOwnership()
    {
        var scope = Guid.NewGuid().ToString();
        using (var first = new SingleInstanceService(scope)) Assert.True(first.IsPrimary);
        using var restarted = new SingleInstanceService(scope);
        Assert.True(restarted.IsPrimary);
    }

    [Fact]
    public void SeparateTestDirectoryDoesNotBlockPersonalApp()
    {
        using var first = new SingleInstanceService(Guid.NewGuid().ToString());
        using var different = new SingleInstanceService(Guid.NewGuid().ToString());
        Assert.True(first.IsPrimary);
        Assert.True(different.IsPrimary);
    }
}
