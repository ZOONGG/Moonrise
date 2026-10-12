package dev.moonrise.mixed;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.jar.JarFile;
import java.util.zip.ZipEntry;
import net.weavemc.loader.impl.shaded.asm.*;
import net.weavemc.loader.impl.shaded.asm.commons.*;

public final class MixedRuntime {
    public static final String RAVEN = "c6edd2979121df4f7c2585abee8da0b84e3b7b5496b0b1a4a684edad179727a1";
    private static final Set<String> ravenClasses = new HashSet<>();
    private static Remapper relocation;
    public static synchronized InputStream metadataStream(JarFile jar, ZipEntry entry) throws Exception {
        if (!entry.getName().equals("weave.mod.json")) throw new IllegalStateException("Unexpected metadata entry");
        byte[] original;
        try (var input = jar.getInputStream(entry)) { original = input.readAllBytes(); }
        Properties selection = new Properties();
        try (var config = MixedRuntime.class.getResourceAsStream("/moonrise-mixed/selection.properties")) {
            if (config == null) return legacyExperimentMetadata(jar, original);
            selection.load(config);
        }
        for (int i = 0; i < Integer.parseInt(selection.getProperty("count")); i++) {
            byte[] expected = Base64.getDecoder().decode(selection.getProperty(i + ".original"));
            if (!Arrays.equals(original, expected)) continue;
            validateArchive(jar, selection.getProperty(i + ".hash"));
            initializeRelocation(jar);
            System.out.println("[Moonrise mixed] verified legacy metadata adapted in memory: " + selection.getProperty(i + ".hash"));
            return new ByteArrayInputStream(Base64.getDecoder().decode(selection.getProperty(i + ".current")));
        }
        return new ByteArrayInputStream(original);
    }
    private static InputStream legacyExperimentMetadata(JarFile jar, byte[] original) throws Exception {
        byte[] expected;
        try (var input = MixedRuntime.class.getResourceAsStream("/moonrise-mixed/raven-original.json")) { expected = input.readAllBytes(); }
        if (!Arrays.equals(original, expected)) return new ByteArrayInputStream(original);
        validateArchive(jar, RAVEN);
        initializeRelocation(jar);
        System.out.println("[Moonrise mixed] original legacy metadata adapted in memory; known injector findings preserved");
        return MixedRuntime.class.getResourceAsStream("/moonrise-mixed/raven-current.json");
    }
    private static void validateArchive(JarFile jar, String hash) throws Exception {
        Path archive = Path.of(jar.getName()).toRealPath();
        boolean originalArchive = HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(Files.readAllBytes(archive))).equals(hash);
        Path namedCache = Path.of(System.getProperty("user.home"), ".weave", ".cache", "jars", "mcp-named_1.8.9");
        boolean loaderCache = Files.isDirectory(namedCache) && archive.getParent().equals(namedCache.toRealPath())
            && archive.getFileName().toString().endsWith(".cache")
            && StackWalker.getInstance().walk(frames -> frames.anyMatch(frame -> frame.getClassName().equals("net.weavemc.loader.impl.WeaveLoader") && frame.getMethodName().equals("registerAsMod")));
        if (!originalArchive && !loaderCache)
            throw new IllegalStateException("Verified legacy metadata outside original/actual Weave registration cache");
    }
    private static void initializeRelocation(JarFile jar) throws Exception {
        for (var item : Collections.list(jar.entries())) if (item.getName().endsWith(".class"))
            ravenClasses.add(item.getName().substring(0, item.getName().length() - 6));
        relocation = (Remapper) net.weavemc.loader.impl.util.MappingsKt.class.getMethod("access$createRelocationRemapper").invoke(null);
    }
    public static synchronized byte[] mixinBytes(byte[] raw, String name) {
        if (raw == null || !ravenClasses.contains(name.replace('.', '/'))) return raw;
        if (relocation == null) throw new IllegalStateException("Raven relocation not initialized");
        var writer = new ClassWriter(0);
        new ClassReader(raw).accept(new ClassRemapper(writer, relocation), 0);
        return writer.toByteArray();
    }
    public static Object findMethodDiagnostic(Object info, Object method, int flags, Object node) throws Exception {
        Object result = info.getClass().getMethod("findMethod", method.getClass(), int.class).invoke(info, method, flags);
        // The real sandbox cached runtime member names before its named transform.
        // Resolve only the observed mismatch through Weave's genuine mappings;
        // reuse the existing Method (and its frames), never manufacture metadata.
        String target = (String) node.getClass().getField("name").get(node);
        String name = (String) method.getClass().getField("name").get(method);
        String desc = (String) method.getClass().getField("desc").get(method);
        if (result == null && target.equals("net/minecraft/client/renderer/entity/RendererLivingEntity")
            && name.equals("doRender") && desc.equals("(Lnet/minecraft/entity/EntityLivingBase;DDDFF)V")) {
            var mappings = net.weavemc.loader.impl.util.MappingsHandler.INSTANCE;
            String runtimeName = mappings.mapper("mcp-named", "official")
                .mapMethodName(target, name, desc);
            if (!runtimeName.equals("a")) throw new IllegalStateException("Observed renderer mapping changed: " + runtimeName);
            result = info.getClass().getMethod("findMethod", String.class, String.class, int.class)
                .invoke(info, runtimeName, desc, flags);
            if (result != null) System.out.println("[Moonrise mixed] resolved existing renderer LVT metadata via actual Weave mapping: " + name + " -> " + runtimeName);
        }
        if (result == null) {
            System.err.println("[Moonrise mixed] missing LVT metadata target=" + node.getClass().getField("name").get(node)
                + " method=" + method.getClass().getField("name").get(method) + method.getClass().getField("desc").get(method)
                + " flags=" + flags);
            var methods = info.getClass().getDeclaredField("methods"); methods.setAccessible(true);
            for (Object cached : (Iterable<?>) methods.get(info)) {
                var type = cached.getClass();
                System.err.println("[Moonrise mixed] cached method=" + type.getMethod("getName").invoke(cached)
                    + type.getMethod("getDesc").invoke(cached));
            }
        }
        return result; // Unresolved metadata still follows the original failure path.
    }
}
