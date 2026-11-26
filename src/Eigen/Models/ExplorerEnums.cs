namespace Eigen.Models;

public enum ExplorerViewMode
{
    Details,
    Tiles,
    Compact
}

public enum SortField
{
    Name,
    DateModified,
    Type,
    Size
}

public enum EigenThemeMode
{
    Dark,
    Light,
    System
}

public enum ConflictAction
{
    Ask,
    KeepBoth,
    Replace,
    Skip
}

public enum PreviewKind
{
    None,
    Image,
    Text,
    Folder,
    Video,
    Generic
}

public enum IconPackStyle
{
    Auto,
    Native,
    NeonWire,
    GlassFrost,
    RetroPixel,
    DuotoneMinimal,
    TactileBrutal
}
