namespace Moonrise.Services;

public static class CatalogTrust
{
    // Populate only after the production Ed25519 public key has been verified
    // through a separate trusted channel. Empty means fail closed in production.
    public const string ProductionEd25519PublicKeyBase64 = "";
}
