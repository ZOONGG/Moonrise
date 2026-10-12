package moonrise.cosmetics;

import java.lang.instrument.ClassFileTransformer;
import java.lang.instrument.IllegalClassFormatException;
import java.lang.instrument.Instrumentation;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.nio.file.StandardOpenOption;
import java.security.ProtectionDomain;

import net.weavemc.loader.impl.shaded.asm.ClassReader;
import net.weavemc.loader.impl.shaded.asm.ClassWriter;
import net.weavemc.loader.impl.shaded.asm.Opcodes;
import net.weavemc.loader.impl.shaded.asm.tree.ClassNode;
import net.weavemc.loader.impl.shaded.asm.tree.FieldInsnNode;
import net.weavemc.loader.impl.shaded.asm.tree.FieldNode;
import net.weavemc.loader.impl.shaded.asm.tree.InsnNode;
import net.weavemc.loader.impl.shaded.asm.tree.MethodNode;

/**
 * Optional shared cosmetics agent for Moonrise.
 *
 * This implementation is independently built and has no launcher dependency.
 */
public final class CosmeticsAgent {
    private static final CosmeticsServiceRoute SERVICE_ROUTE =
            CosmeticsServiceRoute.official();

    private CosmeticsAgent() {
    }

    public static void premain(String arguments, Instrumentation instrumentation) {
        SERVICE_ROUTE.apply();
        System.setProperty("moonrise.cosmetics.agent", "active");
        System.setProperty("moonrise.cosmetics.visibility", "local");
        instrumentation.addTransformer(new CosmeticsUnlockTransformer(), false);
        instrumentation.addTransformer(new CosmeticsPersistenceTransformer(), false);
        instrumentation.addTransformer(new CosmeticsStabilityTransformer(), false);
        writeProof(
                "cosmetics-agent.ready",
                "cosmetics-services=official\n"
                        + "cosmetics-visibility=local\n"
                        + "cosmetics-unlock=all\n"
                        + "cosmetics-reconnect=stable\n"
                        + "cosmetics-state=local\n");
        // Keep the official production/debug flag unchanged. Debug mode
        // requires ProfileData, which is absent from production Ichor builds.
    }

    static void writeProof(String fileName, String value) {
        String proofDirectory = System.getProperty("moonrise.proofDir");
        if (proofDirectory == null || proofDirectory.length() == 0) {
            return;
        }
        try {
            Path directory = Paths.get(proofDirectory);
            Files.createDirectories(directory);
            Files.write(
                    directory.resolve(fileName),
                    value.getBytes(StandardCharsets.UTF_8),
                    StandardOpenOption.CREATE,
                    StandardOpenOption.TRUNCATE_EXISTING,
                    StandardOpenOption.WRITE);
        } catch (Exception ignored) {
        }
    }

    static boolean containsAscii(byte[] value, String marker) {
        byte[] expected = marker.getBytes(StandardCharsets.US_ASCII);
        outer:
        for (int offset = 0; offset <= value.length - expected.length; offset++) {
            for (int index = 0; index < expected.length; index++) {
                if (value[offset + index] != expected[index]) {
                    continue outer;
                }
            }
            return true;
        }
        return false;
    }
}
