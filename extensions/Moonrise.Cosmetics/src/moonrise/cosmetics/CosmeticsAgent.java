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
        writeProof(
                "cosmetics-agent.ready",
                "cosmetics-services=official\n"
                        + "cosmetics-visibility=local\n"
                        + "cosmetics-unlock=all\n"
                        + "cosmetics-reconnect=stable\n"
                        + "cosmetics-state=local\n");
        instrumentation.addTransformer(new CosmeticsUnlockTransformer(), false);
        instrumentation.addTransformer(new CosmeticsPersistenceTransformer(), false);
        instrumentation.addTransformer(new CosmeticsStabilityTransformer(), false);
        instrumentation.addTransformer(new DebugConfigurationTransformer(), false);
    }

    /** Enables Lunar's production cosmetics code path without naming its class. */
    private static final class DebugConfigurationTransformer implements ClassFileTransformer {
        @Override
        public byte[] transform(
                ClassLoader loader,
                String className,
                Class<?> classBeingRedefined,
                ProtectionDomain protectionDomain,
                byte[] classfileBuffer) throws IllegalClassFormatException {
            if (className == null || className.startsWith("java/") || className.startsWith("javax/")) {
                return null;
            }

            try {
                ClassNode node = new ClassNode();
                new ClassReader(classfileBuffer).accept(node, 0);
                FieldNode productionField = findProductionField(node, classfileBuffer);
                if (productionField == null) {
                    return null;
                }

                MethodNode initializer = null;
                for (MethodNode method : node.methods) {
                    if ("<clinit>".equals(method.name) && "()V".equals(method.desc)) {
                        initializer = method;
                        break;
                    }
                }
                if (initializer == null) {
                    initializer = new MethodNode(Opcodes.ACC_STATIC, "<clinit>", "()V", null, null);
                    initializer.instructions.add(new InsnNode(Opcodes.ICONST_0));
                    initializer.instructions.add(new FieldInsnNode(
                            Opcodes.PUTSTATIC, node.name, productionField.name, "Z"));
                    initializer.instructions.add(new InsnNode(Opcodes.RETURN));
                    node.methods.add(initializer);
                } else {
                    for (net.weavemc.loader.impl.shaded.asm.tree.AbstractInsnNode instruction =
                                 initializer.instructions.getFirst();
                         instruction != null;
                         instruction = instruction.getNext()) {
                        if (instruction.getOpcode() == Opcodes.RETURN) {
                            initializer.instructions.insertBefore(instruction, new InsnNode(Opcodes.ICONST_0));
                            initializer.instructions.insertBefore(instruction, new FieldInsnNode(
                                    Opcodes.PUTSTATIC, node.name, productionField.name, "Z"));
                        }
                    }
                }

                ClassWriter writer = new ClassWriter(0);
                node.accept(writer);
                writeProof("cosmetics-debug-class.txt", className + "\n");
                return writer.toByteArray();
            } catch (RuntimeException ignored) {
                return null;
            }
        }

        private static FieldNode findProductionField(ClassNode node, byte[] classfileBuffer) {
            int staticStrings = 0;
            int staticBooleans = 0;
            int staticFields = 0;
            FieldNode booleanField = null;
            for (FieldNode field : node.fields) {
                if ((field.access & Opcodes.ACC_STATIC) == 0) {
                    continue;
                }
                staticFields++;
                if ("Ljava/lang/String;".equals(field.desc)) {
                    staticStrings++;
                } else if ("Z".equals(field.desc)) {
                    staticBooleans++;
                    booleanField = field;
                }
            }

            if (containsAscii(classfileBuffer, "serviceOverrideAssetServer")
                    && staticBooleans == 1) {
                return booleanField;
            }

            boolean currentGenesisLayout = node.name.startsWith("com/moonsworth/lunar/genesis/")
                    && staticFields == 8
                    && staticStrings == 7
                    && staticBooleans == 1;
            if (currentGenesisLayout) {
                return booleanField;
            }

            boolean layoutMatches = staticBooleans == 1
                    && ((staticFields == 6 && staticStrings == 5)
                    || (staticFields == 8 && staticStrings == 7));
            if (!layoutMatches || node.innerClasses == null) {
                return null;
            }
            for (net.weavemc.loader.impl.shaded.asm.tree.InnerClassNode inner : node.innerClasses) {
                if (inner.name != null && (inner.access & Opcodes.ACC_ENUM) != 0) {
                    return booleanField;
                }
            }
            return null;
        }
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
