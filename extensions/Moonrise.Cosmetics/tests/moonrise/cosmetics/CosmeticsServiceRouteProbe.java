package moonrise.cosmetics;

/** Verifies that stale community overrides cannot break Lunar cosmetics login. */
public final class CosmeticsServiceRouteProbe {
    private CosmeticsServiceRouteProbe() {
    }

    public static void main(String[] arguments) {
        System.setProperty("serviceOverrideAuthenticator", "wss://stale.invalid/ws");
        System.setProperty("serviceOverrideAssetServer", "wss://stale.invalid/ws");
        System.setProperty("serviceOverrideApi", "https://stale.invalid/api");
        System.setProperty("serviceOverrideStyngr", "https://stale.invalid/music");
        CosmeticsServiceRoute.official().apply();

        assertCleared("serviceOverrideAuthenticator");
        assertCleared("serviceOverrideAssetServer");
        assertCleared("serviceOverrideApi");
        assertCleared("serviceOverrideStyngr");
        System.out.println("cosmetics-service-route=verified");
    }

    private static void assertCleared(String property) {
        if (System.getProperty(property) != null) {
            throw new IllegalStateException(property + " is still overridden");
        }
    }
}
