using Onyx.ViewModels;

namespace Onyx.Tests.ViewModels;

public sealed class RelayCommandTests
{
    [Fact]
    public async Task Disabled_command_does_not_execute()
    {
        var calls = 0;
        var command = new RelayCommand(() => { calls++; return Task.CompletedTask; }, () => false);

        await command.ExecuteAsync();

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Running_command_rejects_duplicate_execution_and_reenables_after_completion()
    {
        var completion = new TaskCompletionSource();
        var calls = 0;
        var notifications = 0;
        var command = new RelayCommand(() => { calls++; return completion.Task; });
        command.CanExecuteChanged += (_, _) => notifications++;

        var running = command.ExecuteAsync();
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();
        Assert.Equal(1, calls);

        completion.SetResult();
        await running;

        Assert.True(command.CanExecute(null));
        Assert.Equal(2, notifications);
    }

    [Fact]
    public async Task Failed_command_can_be_retried()
    {
        var command = new RelayCommand(() => Task.FromException(new InvalidOperationException("failed")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync());

        Assert.True(command.CanExecute(null));
    }
}
