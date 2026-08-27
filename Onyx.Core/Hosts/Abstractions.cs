namespace Onyx.Core.Hosts;

public enum Hive { LocalMachine, CurrentUser }

public interface IRegistry
{
    string? GetValue(Hive hive, string key, string name);
    IReadOnlyList<string> SubKeys(Hive hive, string key);
}

public interface IFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    IReadOnlyList<string> Directories(string path);
    IReadOnlyList<string> Files(string path);
    void CreateDirectory(string path);
    void Copy(string from, string to, bool overwrite);
    void DeleteFile(string path);
    void DeleteDirectory(string path);
}

public interface IKnownFolders
{
    string AppData { get; }
    string LocalAppData { get; }
    string Documents { get; }
    string UserProfile { get; }
    string? Variable(string name);
}
