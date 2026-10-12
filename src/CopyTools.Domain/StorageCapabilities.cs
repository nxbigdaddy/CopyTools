namespace CopyTools.Domain;

[Flags]
public enum StorageCapabilities
{
    None = 0,
    List = 1,
    Read = 2,
    Write = 4,
    Delete = 8,
    Move = 16,
    Resume = 32,
    Directories = 64,
    All = List | Read | Write | Delete | Move | Resume | Directories,
}
