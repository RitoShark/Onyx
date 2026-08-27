using Onyx.Core.Hosts;

namespace Onyx.Core.Install;

public interface IPresenceProbe
{
    string PluginId { get; }
    string? DetectInstalled(string hostPath);
}

public sealed class ThumbnailPresenceProbe(IRegistry registry, IFileSystem fs) : IPresenceProbe
{
    public const string Clsid = "{243B3EEC-8FD0-44CD-95AD-BEAFDCE52CBF}";

    public string PluginId => "tex-thumbnails";

    public string? DetectInstalled(string hostPath)
    {
        var dll = registry.GetValue(
            Hive.CurrentUser, $@"Software\Classes\CLSID\{Clsid}\InProcServer32", "");

        return dll is not null && fs.FileExists(dll) ? dll : null;
    }
}

