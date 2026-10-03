namespace Moonrise.Models;

public enum PackageKind
{
    WeaveMod,
    JavaAgent,

    // Legacy index values retained only so old metadata can be read and discarded safely.
    // New imports never produce these kinds and the UI never exposes them.
    Ambiguous,
    Unclassified
}
