namespace Onyx.Core.Hosts;

public sealed class HostRegistry(IReadOnlyList<IHostDetector> detectors)
{
    public static HostRegistry Standard(IRegistry registry, IFileSystem fs, IKnownFolders folders) =>
        new([
            new PhotoshopDetector(registry, fs),
            new PaintNetDetector(registry, fs, folders),
            new GimpDetector(fs, folders),
            new MayaDetector(registry, fs, folders),
            new BlenderDetector(fs, folders),
            new ThumbnailHostDetector(folders)
        ]);

    public IReadOnlyList<HostInstance> DetectAll() =>
        detectors.SelectMany(d => d.Detect()).ToList();

    public IReadOnlyList<HostInstance> For(string hostId) =>
        detectors.Where(d => d.HostId == hostId).SelectMany(d => d.Detect()).ToList();
}
