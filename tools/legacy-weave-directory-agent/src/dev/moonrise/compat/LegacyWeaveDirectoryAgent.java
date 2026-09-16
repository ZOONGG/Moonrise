package dev.moonrise.compat;

import java.lang.instrument.ClassFileTransformer;
import java.lang.instrument.Instrumentation;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.security.ProtectionDomain;

/**
 * Launch-only adapter for the official Weave Loader 0.2.x.
 *
 * <p>Weave 0.2.x ignores the modern {@code weave.mods.directory} property and
 * reads {@code user.home/.weave/mods}. Moonrise must not redirect the real Java
 * home or write outside its launch session, so this agent redirects only the
 * legacy loader's property lookup to a private property derived from the
 * already validated launch directory.</p>
 */
public final class LegacyWeaveDirectoryAgent {
    private static final String LEGACY_LOADER_CLASS = "net/weavemc/loader/WeaveLoader";
    private static final byte[] SOURCE_PROPERTY = "user.home".getBytes(StandardCharsets.UTF_8);
    private static final byte[] TARGET_PROPERTY = "weave.dir".getBytes(StandardCharsets.UTF_8);

    private LegacyWeaveDirectoryAgent() {
    }

    public static void premain(String arguments, Instrumentation instrumentation) {
        String configuredMods = System.getProperty("weave.mods.directory");
        if (configuredMods == null || configuredMods.isBlank()) {
            throw new IllegalStateException("Moonrise legacy Weave adapter has no mod directory.");
        }

        Path mods = Path.of(configuredMods).toAbsolutePath().normalize();
        Path weaveDirectory = mods.getParent();
        Path launchHome = weaveDirectory == null ? null : weaveDirectory.getParent();
        if (launchHome == null || !"mods".equalsIgnoreCase(fileName(mods)) ||
                !".weave".equalsIgnoreCase(fileName(weaveDirectory))) {
            throw new IllegalStateException("Moonrise legacy Weave adapter received an unsafe mod directory.");
        }

        System.setProperty("weave.dir", launchHome.toString());
        instrumentation.addTransformer(new LegacyLoaderTransformer());
    }

    private static String fileName(Path path) {
        Path name = path.getFileName();
        return name == null ? "" : name.toString();
    }

    private static final class LegacyLoaderTransformer implements ClassFileTransformer {
        @Override
        public byte[] transform(
                Module module,
                ClassLoader loader,
                String className,
                Class<?> classBeingRedefined,
                ProtectionDomain protectionDomain,
                byte[] classfileBuffer) {
            if (!LEGACY_LOADER_CLASS.equals(className)) {
                return null;
            }

            byte[] transformed = classfileBuffer.clone();
            int replacements = 0;
            for (int offset = 0; offset <= transformed.length - SOURCE_PROPERTY.length; offset++) {
                if (!matches(transformed, offset, SOURCE_PROPERTY)) {
                    continue;
                }
                System.arraycopy(TARGET_PROPERTY, 0, transformed, offset, TARGET_PROPERTY.length);
                replacements++;
            }
            if (replacements != 1) {
                throw new IllegalStateException(
                        "Unsupported Weave Loader 0.2.x bytecode: expected one user.home reference, found " + replacements + ".");
            }
            return transformed;
        }

        private static boolean matches(byte[] bytes, int offset, byte[] expected) {
            for (int index = 0; index < expected.length; index++) {
                if (bytes[offset + index] != expected[index]) {
                    return false;
                }
            }
            return true;
        }
    }
}
