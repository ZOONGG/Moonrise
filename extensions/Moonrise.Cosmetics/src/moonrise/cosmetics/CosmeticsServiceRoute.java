package moonrise.cosmetics;

/** Ensures Lunar's current cosmetics protocol uses its compatible official services. */
final class CosmeticsServiceRoute {
    private static final String AUTHENTICATOR_OVERRIDE = "serviceOverrideAuthenticator";
    private static final String ASSET_SERVER_OVERRIDE = "serviceOverrideAssetServer";
    private static final String API_OVERRIDE = "serviceOverrideApi";
    private static final String STYNGR_OVERRIDE = "serviceOverrideStyngr";

    private CosmeticsServiceRoute() {
    }

    static CosmeticsServiceRoute official() {
        return new CosmeticsServiceRoute();
    }

    void apply() {
        System.clearProperty(AUTHENTICATOR_OVERRIDE);
        System.clearProperty(ASSET_SERVER_OVERRIDE);
        System.clearProperty(API_OVERRIDE);
        System.clearProperty(STYNGR_OVERRIDE);
    }
}
