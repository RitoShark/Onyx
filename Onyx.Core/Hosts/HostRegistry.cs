namespace Onyx.Core.Hosts;

public sealed class HostRegistry(IReadOnlyList<IHostDetector> detectors, HostOverrides? overrides = null)
{
    readonly HostOverrides _overrides = overrides ?? HostOverrides.Empty();

    public static HostRegistry Standard(
        IRegistry registry, IFileSystem fs, IKnownFolders folders, HostOverrides? overrides = null) =>
        new([
            new PhotoshopDetector(registry, fs),
            new PaintNetDetector(registry, fs, folders),
            new GimpDetector(fs, folders),
            new MayaDetector(registry, fs, folders),
            new BlenderDetector(fs, folders),
            new ThumbnailHostDetector(folders),
            new LtkThumbnailHostDetector(folders),
            new HematiteHostDetector(folders)
        ], overrides);

    public HostOverrides Overrides => _overrides;

    public IReadOnlyList<HostInstance> DetectAll() =>
        detectors.SelectMany(d => For(d.HostId)).ToList();

    public IReadOnlyList<HostInstance> For(string hostId)
    {
        var found = detectors
            .Where(d => d.HostId == hostId)
            .SelectMany(d => d.Detect())
            .Select(Redirect)
            .ToList();

        var manual = _overrides.Get(hostId, HostOverrides.ManualInstance);
        if (manual is not null && found.All(h => h.InstanceId != HostOverrides.ManualInstance))
            found.Add(new HostInstance(hostId, HostOverrides.ManualInstance, "Chosen folder", manual));

        return found;
    }

    HostInstance Redirect(HostInstance host)
    {
        var replacement = _overrides.Get(host.HostId, host.InstanceId);
        return replacement is null ? host : host with { Path = replacement };
    }
}
