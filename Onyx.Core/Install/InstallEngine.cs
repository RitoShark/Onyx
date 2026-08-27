using Onyx.Core.Catalog;
using Onyx.Core.Hosts;

namespace Onyx.Core.Install;

public interface IPayload
{
    string Root { get; }
}

public interface IRegistrar
{
    void Register(string dllPath);
    void Unregister(string dllPath);
}

public interface IChecksum
{
    string Sha256(string path);
    string ReadExpected(string sidecarPath);
}

public sealed record InstallJournal(IReadOnlyList<string> Written, IReadOnlyList<string> Registered)
{
    public static InstallJournal Empty => new([], []);
}

public sealed class InstallEngine(IFileSystem fs, IRegistrar registrar, IChecksum checksum)
{
    public InstallJournal Apply(InstallPlan plan, IPayload payload)
    {
        var written = new List<string>();
        var registered = new List<string>();

        try
        {
            foreach (var op in plan.Operations)
                switch (op.Verb)
                {
                    case StepVerb.Sha256:
                        Verify(payload, op);
                        break;
                    case StepVerb.Copy:
                        written.Add(CopyOne(payload, op));
                        break;
                    case StepVerb.CopyDir:
                        written.AddRange(CopyTree(payload, op));
                        break;
                    case StepVerb.Regsvr32:
                        registrar.Register(op.To!);
                        registered.Add(op.To!);
                        break;
                    default:
                        throw new PlanException($"Unhandled verb {op.Verb}.");
                }
        }
        catch
        {
            Revert(new InstallJournal(written, registered));
            throw;
        }

        return new InstallJournal(written, registered);
    }

    public void Revert(InstallJournal journal)
    {
        foreach (var dll in journal.Registered.Reverse())
        {
            try { registrar.Unregister(dll); }
            catch (Exception) { }
        }

        foreach (var file in journal.Written.Reverse())
        {
            try { if (fs.FileExists(file)) fs.DeleteFile(file); }
            catch (Exception) { }
        }
    }

    string CopyOne(IPayload payload, PlannedOperation op)
    {
        var from = Path.Combine(payload.Root, op.From!);
        if (!fs.FileExists(from))
            throw new PlanException($"Payload is missing '{op.From}'.");

        fs.CreateDirectory(Path.GetDirectoryName(op.To!)!);
        Copy(from, op.To!);
        return op.To!;
    }

    void Copy(string from, string to)
    {
        try
        {
            fs.Copy(from, to, overwrite: true);
        }
        catch (IOException e) when (IsSharingViolation(e))
        {
            throw new FileLockedException(to);
        }
        catch (UnauthorizedAccessException)
        {
            throw new FileLockedException(to);
        }
    }

    static bool IsSharingViolation(IOException e) =>
        (e.HResult & 0xFFFF) is 32 or 33;

    IReadOnlyList<string> CopyTree(IPayload payload, PlannedOperation op)
    {
        var from = op.From is null or "." ? payload.Root : Path.Combine(payload.Root, op.From);
        if (!fs.DirectoryExists(from))
            throw new PlanException($"Payload is missing directory '{op.From}'.");

        var written = new List<string>();
        Walk(from, op.To!, written);
        return written;
    }

    void Walk(string from, string to, List<string> written)
    {
        fs.CreateDirectory(to);

        foreach (var file in fs.Files(from))
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            Copy(file, target);
            written.Add(target);
        }

        foreach (var dir in fs.Directories(from))
            Walk(dir, Path.Combine(to, Path.GetFileName(dir)), written);
    }

    void Verify(IPayload payload, PlannedOperation op)
    {
        var sidecar = Path.Combine(payload.Root, op.From!);
        if (!fs.FileExists(sidecar)) return;

        var subject = sidecar[..^".sha256".Length];
        if (!fs.FileExists(subject))
            throw new PlanException($"Checksum names '{Path.GetFileName(subject)}', which the payload does not contain.");

        var expected = checksum.ReadExpected(sidecar);
        var actual = checksum.Sha256(subject);

        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new PlanException($"Checksum mismatch for {Path.GetFileName(subject)}.");
    }
}
