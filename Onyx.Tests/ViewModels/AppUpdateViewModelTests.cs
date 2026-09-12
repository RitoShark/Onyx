using Onyx.Core.Releases;
using Onyx.ViewModels;

namespace Onyx.Tests.ViewModels;

public sealed class AppUpdateViewModelTests
{
    [Fact]
    public async Task Offline_check_offers_retry_and_never_claims_up_to_date()
    {
        var model = new AppUpdateViewModel(_ => throw new HttpRequestException());

        await model.CheckAsync();

        Assert.Equal("Update check unavailable", model.Status);
        Assert.Equal("Check for updates", model.ActionLabel);
        Assert.True(model.ActionCommand.CanExecute(null));
    }

    [Fact]
    public async Task New_release_offers_download()
    {
        var model = new AppUpdateViewModel(_ => Task.FromResult<IReadOnlyList<Release>>(
            [new Release("v99.0.0", DateTimeOffset.UtcNow, false, "", [])]));

        await model.CheckAsync();

        Assert.Equal("Update available", model.Status);
        Assert.Equal("Download v99.0.0", model.ActionLabel);
    }

    [Fact]
    public async Task Empty_releases_are_reported_explicitly()
    {
        var model = new AppUpdateViewModel(_ => Task.FromResult<IReadOnlyList<Release>>([]));

        await model.CheckAsync();

        Assert.Equal("No published version found", model.Status);
    }

    [Fact]
    public async Task Concurrent_checks_share_the_running_request()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<Release>>();
        var calls = 0;
        var model = new AppUpdateViewModel(_ => { calls++; return completion.Task; });

        var running = model.CheckAsync();
        await model.CheckAsync();
        Assert.Equal(1, calls);
        Assert.False(model.ActionCommand.CanExecute(null));
        completion.SetResult([]);
        await running;
        Assert.True(model.ActionCommand.CanExecute(null));
    }
}
