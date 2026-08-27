using Onyx.Core.Processes;

namespace Onyx.Tests.Processes;

public class ProcessGuardTests
{
    sealed class FakeProcessTable : IProcessTable
    {
        public List<RunningProcess> Processes { get; } = [];
        public List<int> CloseRequests { get; } = [];
        public bool CloseActuallyExits { get; set; } = true;

        public IReadOnlyList<RunningProcess> Running(IReadOnlyList<string> names) =>
            Processes.Where(p => names.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).ToList();

        public void RequestClose(int pid)
        {
            CloseRequests.Add(pid);
            if (CloseActuallyExits) Processes.RemoveAll(p => p.Pid == pid);
        }
    }

    [Fact]
    public void Reports_a_running_host()
    {
        var table = new FakeProcessTable();
        table.Processes.Add(new RunningProcess(42, "Photoshop"));

        Assert.Single(new ProcessGuard(table).Check("photoshop"));
    }

    [Fact]
    public void Reports_nothing_when_the_host_is_closed()
    {
        Assert.Empty(new ProcessGuard(new FakeProcessTable()).Check("photoshop"));
    }

    [Fact]
    public void An_unrelated_process_does_not_block_an_install()
    {
        var table = new FakeProcessTable();
        table.Processes.Add(new RunningProcess(42, "chrome"));

        Assert.Empty(new ProcessGuard(table).Check("blender"));
    }

    [Fact]
    public void The_thumbnail_host_is_not_guarded_because_explorer_always_runs()
    {
        Assert.Empty(ProcessGuard.ProcessNamesFor("thumbnails"));
    }

    [Fact]
    public void Every_host_that_can_hold_unsaved_work_has_a_guard()
    {
        foreach (var host in new[] { "blender", "maya", "gimp", "paintnet", "photoshop" })
            Assert.NotEmpty(ProcessGuard.ProcessNamesFor(host));
    }

    [Fact]
    public void An_unknown_host_has_no_guard_rather_than_throwing()
    {
        Assert.Empty(ProcessGuard.ProcessNamesFor("nonesuch"));
    }

    [Fact]
    public async Task Close_requests_a_graceful_exit_and_succeeds_when_the_host_quits()
    {
        var table = new FakeProcessTable();
        table.Processes.Add(new RunningProcess(42, "blender"));

        var closed = await new ProcessGuard(table).CloseAndWaitAsync("blender", TimeSpan.FromSeconds(1), default);

        Assert.True(closed);
        Assert.Equal([42], table.CloseRequests);
    }

    [Fact]
    public async Task Close_asks_every_instance_of_the_host_to_quit()
    {
        var table = new FakeProcessTable();
        table.Processes.Add(new RunningProcess(1, "blender"));
        table.Processes.Add(new RunningProcess(2, "blender"));

        await new ProcessGuard(table).CloseAndWaitAsync("blender", TimeSpan.FromSeconds(1), default);

        Assert.Equal([1, 2], table.CloseRequests);
    }

    [Fact]
    public async Task Close_returns_false_rather_than_killing_a_host_that_refuses()
    {
        var table = new FakeProcessTable { CloseActuallyExits = false };
        table.Processes.Add(new RunningProcess(42, "blender"));

        var closed = await new ProcessGuard(table).CloseAndWaitAsync("blender", TimeSpan.FromMilliseconds(200), default);

        Assert.False(closed);
        Assert.Single(table.Processes);
    }
}
