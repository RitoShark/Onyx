using Onyx.Core.Catalog;
using Onyx.Core.Install;
using Onyx.Core.Releases;

namespace Onyx.Tests.Install;

public class InstallEngineTests
{
    sealed class FakePayload(string root) : IPayload
    {
        public string Root => root;
    }

    sealed class FakeRegistrar : IRegistrar
    {
        public List<string> Registered { get; } = [];
        public bool Fail { get; set; }

        public void Register(string dllPath)
        {
            if (Fail) throw new InvalidOperationException("regsvr32 failed");
            Registered.Add(dllPath);
        }

        public void Unregister(string dllPath) => Registered.Remove(dllPath);
    }

    sealed class FakeChecksum : IChecksum
    {
        public string Expected { get; set; } = "abc";
        public string Actual { get; set; } = "abc";
        public string Sha256(string path) => Actual;
        public string ReadExpected(string sidecarPath) => Expected;
    }

    static InstallPlan Plan(params PlannedOperation[] ops) =>
        new("p", "i", "v1", new ReleaseAsset("a.zip", "https://x/a.zip", 1), [], ops);

    static InstallEngine Engine(FakeFileSystem fs, FakeRegistrar? registrar = null, FakeChecksum? checksum = null) =>
        new(fs, registrar ?? new FakeRegistrar(), checksum ?? new FakeChecksum());

    [Fact]
    public void Copies_a_file_and_records_it_in_the_journal()
    {
        var fs = new FakeFileSystem().WithFile(@"C:\payload\RitoTex.8bi", [1, 2, 3]);

        var journal = Engine(fs).Apply(
            Plan(new PlannedOperation(StepVerb.Copy, "RitoTex.8bi", @"C:\ps\Plug-ins\RitoTex.8bi")),
            new FakePayload(@"C:\payload"));

        Assert.True(fs.FileExists(@"C:\ps\Plug-ins\RitoTex.8bi"));
        Assert.Equal([@"C:\ps\Plug-ins\RitoTex.8bi"], journal.Written);
    }

    [Fact]
    public void Copies_a_directory_tree_recursively()
    {
        var fs = new FakeFileSystem()
            .WithFile(@"C:\payload\Aventurine\__init__.py")
            .WithFile(@"C:\payload\Aventurine\io\import_skn.py");

        var journal = Engine(fs).Apply(
            Plan(new PlannedOperation(StepVerb.CopyDir, "Aventurine", @"C:\cfg\scripts\addons\Aventurine")),
            new FakePayload(@"C:\payload"));

        Assert.True(fs.FileExists(@"C:\cfg\scripts\addons\Aventurine\__init__.py"));
        Assert.True(fs.FileExists(@"C:\cfg\scripts\addons\Aventurine\io\import_skn.py"));
        Assert.Equal(2, journal.Written.Count);
    }

    [Fact]
    public void A_dot_source_copies_the_payload_root()
    {
        var fs = new FakeFileSystem().WithFile(@"C:\payload\TexFileType.dll");

        Engine(fs).Apply(
            Plan(new PlannedOperation(StepVerb.CopyDir, ".", @"C:\pdn\FileTypes")),
            new FakePayload(@"C:\payload"));

        Assert.True(fs.FileExists(@"C:\pdn\FileTypes\TexFileType.dll"));
    }

    [Fact]
    public void Registers_a_dll_and_records_it()
    {
        var fs = new FakeFileSystem().WithFile(@"C:\payload\TexThumbnailProvider.dll");
        var registrar = new FakeRegistrar();

        var journal = Engine(fs, registrar).Apply(
            Plan(
                new PlannedOperation(StepVerb.Copy, "TexThumbnailProvider.dll", @"C:\local\TexThumbnailProvider.dll"),
                new PlannedOperation(StepVerb.Regsvr32, null, @"C:\local\TexThumbnailProvider.dll")),
            new FakePayload(@"C:\payload"));

        Assert.Equal([@"C:\local\TexThumbnailProvider.dll"], registrar.Registered);
        Assert.Equal([@"C:\local\TexThumbnailProvider.dll"], journal.Registered);
    }

    [Fact]
    public void A_failing_step_leaves_the_filesystem_as_it_started()
    {
        var fs = new FakeFileSystem().WithFile(@"C:\payload\TexThumbnailProvider.dll");
        var registrar = new FakeRegistrar { Fail = true };

        Assert.Throws<InvalidOperationException>(() => Engine(fs, registrar).Apply(
            Plan(
                new PlannedOperation(StepVerb.Copy, "TexThumbnailProvider.dll", @"C:\local\TexThumbnailProvider.dll"),
                new PlannedOperation(StepVerb.Regsvr32, null, @"C:\local\TexThumbnailProvider.dll")),
            new FakePayload(@"C:\payload")));

        Assert.False(fs.FileExists(@"C:\local\TexThumbnailProvider.dll"));
    }

    [Fact]
    public void A_missing_payload_file_fails_before_anything_is_written()
    {
        var fs = new FakeFileSystem().WithFile(@"C:\payload\one.txt");

        Assert.Throws<PlanException>(() => Engine(fs).Apply(
            Plan(
                new PlannedOperation(StepVerb.Copy, "one.txt", @"C:\target\one.txt"),
                new PlannedOperation(StepVerb.Copy, "missing.txt", @"C:\target\missing.txt")),
            new FakePayload(@"C:\payload")));

        Assert.False(fs.FileExists(@"C:\target\one.txt"));
    }

    [Fact]
    public void A_checksum_mismatch_stops_the_install_before_any_write()
    {
        var fs = new FakeFileSystem()
            .WithFile(@"C:\payload\a.dll")
            .WithFile(@"C:\payload\a.dll.sha256");
        var checksum = new FakeChecksum { Expected = "aaaa", Actual = "bbbb" };

        Assert.Throws<PlanException>(() => Engine(fs, checksum: checksum).Apply(
            Plan(
                new PlannedOperation(StepVerb.Copy, "a.dll", @"C:\local\a.dll"),
                new PlannedOperation(StepVerb.Sha256, "a.dll.sha256", null)),
            new FakePayload(@"C:\payload")));

        Assert.False(fs.FileExists(@"C:\local\a.dll"));
    }

    [Fact]
    public void A_matching_checksum_lets_the_install_through()
    {
        var fs = new FakeFileSystem()
            .WithFile(@"C:\payload\a.dll")
            .WithFile(@"C:\payload\a.dll.sha256");
        var checksum = new FakeChecksum { Expected = "dead", Actual = "DEAD" };

        Engine(fs, checksum: checksum).Apply(
            Plan(
                new PlannedOperation(StepVerb.Copy, "a.dll", @"C:\local\a.dll"),
                new PlannedOperation(StepVerb.Sha256, "a.dll.sha256", null)),
            new FakePayload(@"C:\payload"));

        Assert.True(fs.FileExists(@"C:\local\a.dll"));
    }

    [Fact]
    public void Revert_removes_written_files_and_unregisters()
    {
        var fs = new FakeFileSystem().WithFile(@"C:\payload\a.dll");
        var registrar = new FakeRegistrar();
        var engine = Engine(fs, registrar);

        var journal = engine.Apply(
            Plan(
                new PlannedOperation(StepVerb.Copy, "a.dll", @"C:\local\a.dll"),
                new PlannedOperation(StepVerb.Regsvr32, null, @"C:\local\a.dll")),
            new FakePayload(@"C:\payload"));

        engine.Revert(journal);

        Assert.False(fs.FileExists(@"C:\local\a.dll"));
        Assert.Empty(registrar.Registered);
    }

    [Fact]
    public void Revert_of_an_empty_journal_does_nothing()
    {
        Engine(new FakeFileSystem()).Revert(InstallJournal.Empty);
    }

    sealed class LockedFileSystem(FakeFileSystem inner, string lockedTarget) : Core.Hosts.IFileSystem
    {
        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
        public bool FileExists(string path) => inner.FileExists(path);
        public IReadOnlyList<string> Directories(string path) => inner.Directories(path);
        public IReadOnlyList<string> Files(string path) => inner.Files(path);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public void DeleteFile(string path) => inner.DeleteFile(path);
        public void DeleteDirectory(string path) => inner.DeleteDirectory(path);

        public void Copy(string from, string to, bool overwrite)
        {
            if (string.Equals(to, lockedTarget, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The process cannot access the file", unchecked((int)0x80070020));

            inner.Copy(from, to, overwrite);
        }
    }

    [Fact]
    public void A_locked_target_surfaces_as_a_FileLockedException_naming_the_file()
    {
        var inner = new FakeFileSystem().WithFile(@"C:\payload\TexThumbnailProvider.dll");
        var fs = new LockedFileSystem(inner, @"C:\local\TexThumbnailProvider.dll");
        var engine = new InstallEngine(fs, new FakeRegistrar(), new FakeChecksum());

        var ex = Assert.Throws<FileLockedException>(() => engine.Apply(
            Plan(new PlannedOperation(StepVerb.Copy, "TexThumbnailProvider.dll", @"C:\local\TexThumbnailProvider.dll")),
            new FakePayload(@"C:\payload")));

        Assert.Equal(@"C:\local\TexThumbnailProvider.dll", ex.Path);
        Assert.Contains("TexThumbnailProvider.dll", ex.Message);
    }
}
