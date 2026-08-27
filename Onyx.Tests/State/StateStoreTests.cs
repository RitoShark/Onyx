using Onyx.Core.Install;
using Onyx.Core.State;

namespace Onyx.Tests.State;

public class StateStoreTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"onyx-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Round_trips_a_record()
    {
        var store = StateStore.Load(_path);
        store.Record("aventurine-blender", "4.3", "3.1.5", new InstallJournal([@"C:\a\b.py"], [@"C:\a\c.dll"]));
        store.Save();

        var record = StateStore.Load(_path).Find("aventurine-blender", "4.3");

        Assert.NotNull(record);
        Assert.Equal("3.1.5", record.Tag);
        Assert.Equal([@"C:\a\b.py"], record.Written);
        Assert.Equal([@"C:\a\c.dll"], record.Registered);
    }

    [Fact]
    public void A_record_converts_straight_back_into_a_journal_for_uninstall()
    {
        var store = StateStore.Load(_path);
        store.Record("p", "i", "v1", new InstallJournal([@"C:\a"], [@"C:\b"]));

        var journal = store.Find("p", "i")!.AsJournal();

        Assert.Equal([@"C:\a"], journal.Written);
        Assert.Equal([@"C:\b"], journal.Registered);
    }

    [Fact]
    public void Records_are_keyed_by_plugin_and_instance()
    {
        var store = StateStore.Load(_path);
        store.Record("aventurine-blender", "4.1", "3.1.4", InstallJournal.Empty);
        store.Record("aventurine-blender", "4.3", "3.1.5", InstallJournal.Empty);

        Assert.Equal("3.1.4", store.Find("aventurine-blender", "4.1")!.Tag);
        Assert.Equal("3.1.5", store.Find("aventurine-blender", "4.3")!.Tag);
    }

    [Fact]
    public void Recording_the_same_key_twice_replaces_it()
    {
        var store = StateStore.Load(_path);
        store.Record("aventurine-blender", "4.3", "3.1.4", InstallJournal.Empty);
        store.Record("aventurine-blender", "4.3", "3.1.5", InstallJournal.Empty);

        Assert.Equal("3.1.5", store.Find("aventurine-blender", "4.3")!.Tag);
        Assert.Single(store.All);
    }

    [Fact]
    public void Forget_removes_a_record()
    {
        var store = StateStore.Load(_path);
        store.Record("aventurine-blender", "4.3", "3.1.5", InstallJournal.Empty);
        store.Forget("aventurine-blender", "4.3");

        Assert.Null(store.Find("aventurine-blender", "4.3"));
    }

    [Fact]
    public void A_missing_file_loads_as_empty()
    {
        Assert.Empty(StateStore.Load(_path).All);
    }

    [Fact]
    public void A_corrupt_file_loads_as_empty_rather_than_throwing()
    {
        File.WriteAllText(_path, "{ not json");
        Assert.Empty(StateStore.Load(_path).All);
    }

    [Fact]
    public void The_default_path_sits_under_the_ritoshark_folder()
    {
        Assert.Equal(@"C:\local\RitoShark\Onyx\state.json", StateStore.DefaultPath(@"C:\local"));
    }
}
