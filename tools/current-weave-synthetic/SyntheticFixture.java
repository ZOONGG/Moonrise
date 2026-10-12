package dev.moonrise.current;
// Offline byte provider only; excluded from the embedded adapter.
public final class SyntheticFixture {
    public static byte[] generated;
    public static String generatedName;
    public static byte[] generate(ClassLoader loader, String name) {
        return name.equals(generatedName) ? generated : null;
    }
}
