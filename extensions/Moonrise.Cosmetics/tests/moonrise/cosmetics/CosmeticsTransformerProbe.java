package moonrise.cosmetics;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.lang.instrument.ClassFileTransformer;
import java.lang.reflect.Constructor;
import java.net.URL;
import java.net.URLClassLoader;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.Enumeration;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

import net.weavemc.loader.impl.shaded.asm.ClassReader;
import net.weavemc.loader.impl.shaded.asm.tree.AbstractInsnNode;
import net.weavemc.loader.impl.shaded.asm.tree.ClassNode;
import net.weavemc.loader.impl.shaded.asm.tree.FieldNode;
import net.weavemc.loader.impl.shaded.asm.tree.LdcInsnNode;
import net.weavemc.loader.impl.shaded.asm.tree.MethodInsnNode;
import net.weavemc.loader.impl.shaded.asm.tree.MethodNode;
import net.weavemc.loader.impl.shaded.asm.tree.analysis.Analyzer;
import net.weavemc.loader.impl.shaded.asm.tree.analysis.BasicVerifier;

/** Structural offline check against the currently installed Lunar JAR. */
public final class CosmeticsTransformerProbe {
    private CosmeticsTransformerProbe() {
    }

    public static void main(String[] arguments) throws Exception {
        if (arguments.length != 2) {
            throw new IllegalArgumentException("Expected Lunar JAR and agent JAR paths");
        }
        Path lunarJar = Paths.get(arguments[0]);
        Path agentJar = Paths.get(arguments[1]);
        int reconnectPolicies = 0;
        int assetClients = 0;
        int unlockResponses = 0;
        int persistenceStubs = 0;
        int debugConfigurations = 0;

        try (URLClassLoader loader = new URLClassLoader(
                new URL[] {lunarJar.toUri().toURL(), agentJar.toUri().toURL()},
                CosmeticsTransformerProbe.class.getClassLoader());
             ZipFile zip = new ZipFile(lunarJar.toFile())) {
            CosmeticsStabilityTransformer transformer = new CosmeticsStabilityTransformer();
            CosmeticsUnlockTransformer unlockTransformer = new CosmeticsUnlockTransformer();
            CosmeticsPersistenceTransformer persistenceTransformer =
                    new CosmeticsPersistenceTransformer();
            Constructor<?> debugConstructor = Class.forName(
                    "moonrise.cosmetics.CosmeticsAgent$DebugConfigurationTransformer")
                    .getDeclaredConstructor();
            debugConstructor.setAccessible(true);
            ClassFileTransformer debugTransformer =
                    (ClassFileTransformer) debugConstructor.newInstance();
            Enumeration<? extends ZipEntry> entries = zip.entries();
            while (entries.hasMoreElements()) {
                ZipEntry entry = entries.nextElement();
                if ((CosmeticsUnlockTransformer.TARGET + ".class").equals(entry.getName())) {
                    byte[] transformed = unlockTransformer.transform(
                            loader,
                            CosmeticsUnlockTransformer.TARGET,
                            null,
                            null,
                            read(zip, entry));
                    if (transformed == null) {
                        throw new IllegalStateException("Unlock transformer skipped v2 LoginResponse");
                    }
                    verifyBytecode(transformed);
                    verifyUnlockGetter(transformed);
                    unlockResponses++;
                    continue;
                }
                if ((CosmeticsPersistenceTransformer.TARGET + ".class").equals(entry.getName())) {
                    byte[] transformed = persistenceTransformer.transform(
                            loader,
                            CosmeticsPersistenceTransformer.TARGET,
                            null,
                            null,
                            read(zip, entry));
                    if (transformed == null) {
                        throw new IllegalStateException(
                                "Persistence transformer skipped v2 CosmeticService.Stub");
                    }
                    verifyBytecode(transformed);
                    verifyPersistenceHooks(transformed);
                    persistenceStubs++;
                    continue;
                }
                if (!entry.getName().startsWith("com/moonsworth/lunar/client/")
                        || !entry.getName().endsWith(".class")) {
                    continue;
                }
                byte[] original = read(zip, entry);
                byte[] debugTransformed = debugTransformer.transform(
                        loader,
                        entry.getName().substring(0, entry.getName().length() - 6),
                        null,
                        null,
                        original);
                if (debugTransformed != null) {
                    verifyBytecode(debugTransformed);
                    System.out.println("cosmetics-debug-configuration=" + entry.getName());
                    debugConfigurations++;
                }
                boolean reconnectPolicy =
                        CosmeticsAgent.containsAscii(original, "getReconnectAuthenticatorJwt")
                                && CosmeticsAgent.containsAscii(original, "getReconnectDelay");
                boolean assetClient = CosmeticsAgent.containsAscii(
                        original, "Disconnected from the AssetServer");
                if (!reconnectPolicy && !assetClient) {
                    continue;
                }

                byte[] transformed = transformer.transform(
                        loader,
                        entry.getName().substring(0, entry.getName().length() - 6),
                        null,
                        null,
                        original);
                if (transformed == null) {
                    throw new IllegalStateException("Transformer skipped " + entry.getName());
                }
                verifyBytecode(transformed);
                if (reconnectPolicy) {
                    verifyReconnectConstants(transformed);
                    reconnectPolicies++;
                }
                if (assetClient) {
                    assetClients++;
                }
            }
        }

        if (reconnectPolicies != 1 || assetClients != 1 || unlockResponses != 1
                || persistenceStubs != 1
                || debugConfigurations != 1) {
            throw new IllegalStateException(
                    "Unexpected Lunar layout: reconnect=" + reconnectPolicies
                            + ", assets=" + assetClients
                            + ", unlock=" + unlockResponses
                            + ", persistence=" + persistenceStubs
                            + ", debug=" + debugConfigurations);
        }
        System.out.println("cosmetics-stability-transformer=verified");
    }

    private static byte[] read(ZipFile zip, ZipEntry entry) throws Exception {
        try (InputStream input = zip.getInputStream(entry)) {
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = input.read(buffer)) >= 0) {
                output.write(buffer, 0, count);
            }
            return output.toByteArray();
        }
    }

    private static void verifyBytecode(byte[] transformed) throws Exception {
        ClassNode node = new ClassNode();
        new ClassReader(transformed).accept(node, 0);
        for (MethodNode method : node.methods) {
            new Analyzer<>(new BasicVerifier()).analyze(node.name, method);
        }
    }

    private static void verifyReconnectConstants(byte[] transformed) {
        ClassNode node = new ClassNode();
        new ClassReader(transformed).accept(node, 0);
        boolean fastDelay = false;
        boolean cappedBackoff = false;
        boolean oldDelay = false;
        for (FieldNode field : node.fields) {
            fastDelay |= Long.valueOf(1000L).equals(field.value);
            cappedBackoff |= Long.valueOf(5000L).equals(field.value);
            oldDelay |= Long.valueOf(15000L).equals(field.value)
                    || Long.valueOf(120000L).equals(field.value);
        }
        for (MethodNode method : node.methods) {
            for (AbstractInsnNode instruction = method.instructions.getFirst();
                 instruction != null;
                 instruction = instruction.getNext()) {
                if (instruction instanceof LdcInsnNode) {
                    Object value = ((LdcInsnNode) instruction).cst;
                    fastDelay |= Long.valueOf(1000L).equals(value);
                    cappedBackoff |= Long.valueOf(5000L).equals(value);
                    oldDelay |= Long.valueOf(15000L).equals(value)
                            || Long.valueOf(120000L).equals(value);
                }
            }
        }
        if (!fastDelay || !cappedBackoff || oldDelay) {
            throw new IllegalStateException("Reconnect constants were not patched");
        }
    }

    private static void verifyUnlockGetter(byte[] transformed) {
        ClassNode node = new ClassNode();
        new ClassReader(transformed).accept(node, 0);
        for (MethodNode method : node.methods) {
            if (!"getHasAllCosmeticsFlag".equals(method.name) || !"()Z".equals(method.desc)) {
                continue;
            }
            AbstractInsnNode first = method.instructions.getFirst();
            AbstractInsnNode second = first == null ? null : first.getNext();
            if (first != null
                    && first.getOpcode() == net.weavemc.loader.impl.shaded.asm.Opcodes.ICONST_1
                    && second != null
                    && second.getOpcode() == net.weavemc.loader.impl.shaded.asm.Opcodes.IRETURN
                    && second.getNext() == null) {
                System.out.println("cosmetics-v2-login-unlock=verified");
                return;
            }
        }
        throw new IllegalStateException("All-cosmetics getter was not replaced safely");
    }

    private static void verifyPersistenceHooks(byte[] transformed) {
        ClassNode node = new ClassNode();
        new ClassReader(transformed).accept(node, 0);
        boolean login = false;
        boolean update = false;
        boolean select = false;
        boolean restore = false;
        boolean invocationHandler = node.interfaces.contains("java/lang/reflect/InvocationHandler");
        for (MethodNode method : node.methods) {
            restore |= "moonrise$mergeLoginResponse".equals(method.name);
            for (AbstractInsnNode instruction = method.instructions.getFirst();
                 instruction != null;
                 instruction = instruction.getNext()) {
                if (!(instruction instanceof MethodInsnNode)) {
                    continue;
                }
                MethodInsnNode call = (MethodInsnNode) instruction;
                if (call.owner.startsWith("moonrise/cosmetics/")) {
                    throw new IllegalStateException(
                            "Genesis bytecode depends on agent classloader: " + call.owner);
                }
                if (!CosmeticsPersistenceTransformer.TARGET.equals(call.owner)) {
                    continue;
                }
                login |= "login".equals(method.name)
                        && "moonrise$wrapLoginCallback".equals(call.name);
                update |= "updateOutfit".equals(method.name)
                        && "moonrise$capture".equals(call.name);
                select |= "selectOutfit".equals(method.name)
                        && "moonrise$capture".equals(call.name);
            }
        }
        if (!login || !update || !select || !restore || !invocationHandler) {
            throw new IllegalStateException(
                    "Missing classloader-local persistence hooks: login=" + login
                            + ", update=" + update
                            + ", select=" + select
                            + ", restore=" + restore
                            + ", invocationHandler=" + invocationHandler);
        }
        System.out.println("cosmetics-local-persistence=verified");
    }
}
