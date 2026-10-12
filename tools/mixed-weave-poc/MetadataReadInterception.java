import java.io.*;
import java.nio.file.*;
import java.util.*;
import java.util.jar.JarFile;
import java.util.zip.ZipEntry;
import net.weavemc.loader.impl.shaded.asm.*;
import net.weavemc.loader.impl.shaded.asm.tree.*;
import net.weavemc.internals.ModConfig;

/** Disposable load-time experiment. No agent, archive output or loader bootstrap. */
public final class MetadataReadInterception {
    private static final String UTILITY = "net.weavemc.loader.impl.util.UtilityKt";
    private static final Map<Path, byte[]> legacyInputs = new HashMap<>();
    private static int adaptedReads, originalReads;

    public static InputStream metadataStream(JarFile jar, ZipEntry entry) throws IOException {
        if (!entry.getName().equals("weave.mod.json")) throw new AssertionError("Unexpected read boundary");
        boolean actualParser = StackWalker.getInstance().walk(frames -> frames.anyMatch(f ->
            f.getClassName().equals(UTILITY) && f.getMethodName().equals("fetchModConfig")));
        if (!actualParser) throw new AssertionError("Hook called outside actual parser");
        byte[] expected = legacyInputs.get(Path.of(jar.getName()).toRealPath());
        if (Boolean.getBoolean("moonrise.proof.isolatedBootstrap")) {
            boolean registration = StackWalker.getInstance().walk(frames -> frames.anyMatch(f ->
                f.getClassName().equals("net.weavemc.loader.impl.WeaveLoader") && f.getMethodName().equals("registerAsMod")));
            System.out.println("METADATA_READ jar=" + jar.getName() + "; registerAsMod=" + registration +
                "; legacyAdaptation=" + (expected != null));
        }
        if (expected == null) {
            originalReads++;
            return jar.getInputStream(entry); // Current input keeps the original stream, parser and serializer.
        }
        byte[] raw;
        try (var stream = jar.getInputStream(entry)) { raw = stream.readAllBytes(); }
        if (!Arrays.equals(expected, raw)) throw new AssertionError("Legacy input changed after authorization");
        adaptedReads++;
        return new ByteArrayInputStream(LoaderDiscoveryBoundary.normalize(raw, "mcp-named", "1.8.9"));
    }

    static byte[] adapt(byte[] original) {
        var reader = new ClassReader(original);
        var node = new ClassNode(); reader.accept(node, 0);
        if (!node.name.equals(UTILITY.replace('.', '/'))) throw new AssertionError("Unexpected adaptation target");
        int replaced = 0;
        for (var method : node.methods) {
            if (!method.name.equals("fetchModConfig") || !method.desc.equals(
                "(Ljava/util/jar/JarFile;Lnet/weavemc/loader/impl/shaded/kotlinx/serialization/json/Json;)Lnet/weavemc/internals/ModConfig;")) continue;
            for (var insn : method.instructions) {
                if (insn instanceof MethodInsnNode call && call.getOpcode() == Opcodes.INVOKEVIRTUAL &&
                    call.owner.equals("java/util/jar/JarFile") && call.name.equals("getInputStream") &&
                    call.desc.equals("(Ljava/util/zip/ZipEntry;)Ljava/io/InputStream;")) {
                    call.setOpcode(Opcodes.INVOKESTATIC);
                    call.owner = "MetadataReadInterception";
                    call.name = "metadataStream";
                    call.desc = "(Ljava/util/jar/JarFile;Ljava/util/zip/ZipEntry;)Ljava/io/InputStream;";
                    replaced++;
                }
            }
        }
        if (replaced != 1) throw new AssertionError("Pinned metadata read boundary changed: " + replaced);
        // Same stack inputs/output; retain existing frames and max stack. No other method is edited.
        var writer = new ClassWriter(reader, 0); node.accept(writer); return writer.toByteArray();
    }

    static final class IsolatedUtility extends ClassLoader {
        IsolatedUtility() { super(MetadataReadInterception.class.getClassLoader()); }
        @Override protected Class<?> loadClass(String name, boolean resolve) throws ClassNotFoundException {
            if (!name.equals(UTILITY)) return super.loadClass(name, resolve);
            synchronized (getClassLoadingLock(name)) {
                Class<?> type = findLoadedClass(name);
                if (type == null) {
                    try (var input = getParent().getResourceAsStream(name.replace('.', '/') + ".class")) {
                        if (input == null) throw new ClassNotFoundException(name);
                        byte[] bytes = adapt(input.readAllBytes());
                        type = defineClass(name, bytes, 0, bytes.length);
                    } catch (IOException error) { throw new ClassNotFoundException(name, error); }
                }
                if (resolve) resolveClass(type);
                return type;
            }
        }
    }

    static void run(Path raven, Path bwh) throws Exception {
        String ravenHash = MixedWeaveProof.hash(raven), bwhHash = MixedWeaveProof.hash(bwh);
        legacyInputs.clear(); adaptedReads = originalReads = 0;
        try {
            try (var jar = new JarFile(raven.toFile())) {
                legacyInputs.put(raven.toRealPath(), jar.getInputStream(jar.getJarEntry("weave.mod.json")).readAllBytes());
            }
            var isolated = new IsolatedUtility();
            var utility = isolated.loadClass(UTILITY);
            var configOrFatal = utility.getMethod("configOrFatal", JarFile.class);
            if (utility == net.weavemc.loader.impl.util.UtilityKt.class) throw new AssertionError("Isolation failed");
            for (String api : List.of("net.weavemc.api.ModInitializer", "net.weavemc.api.event.Event",
                "net.weavemc.api.event.EventBus", "net.weavemc.internals.ModConfig")) {
                Class<?> parent = Class.forName(api, false, isolated.getParent());
                if (isolated.loadClass(api) != parent) throw new AssertionError("API boundary changed: " + api);
                System.out.println("OWNER interception " + api + " = " + parent.getClassLoader());
            }
            System.out.println("OWNER experimental UtilityKt = " + utility.getClassLoader() +
                "; actual WeaveLoader remains " + net.weavemc.loader.impl.WeaveLoader.class.getClassLoader());
            for (Path path : List.of(raven, bwh)) {
                try (var concrete = new JarFile(path.toFile()); var baseline = new LoaderDiscoveryBoundary.MetadataView(path,
                    LoaderDiscoveryBoundary.normalize(concrete.getInputStream(concrete.getJarEntry("weave.mod.json")).readAllBytes(), "mcp-named", "1.8.9"))) {
                    var expected = net.weavemc.loader.impl.util.UtilityKt.fetchModConfig(baseline,
                        net.weavemc.loader.impl.util.UtilityKt.getJSON());
                    var actual = (ModConfig) configOrFatal.invoke(null, concrete);
                    if (!expected.equals(actual)) throw new AssertionError("Intercepted config differs from proven normalizer");
                    System.out.println("PASS intercepted actual configOrFatal -> fetchModConfig, concrete JarFile: " + actual);
                }
            }
            // Direct parser negative control avoids configOrFatal's genuine fatal-error process exit.
            var fetch = utility.getMethod("fetchModConfig", JarFile.class,
                net.weavemc.loader.impl.shaded.kotlinx.serialization.json.Json.class);
            try (var invalid = new LoaderDiscoveryBoundary.MetadataView(bwh, "{}".getBytes(java.nio.charset.StandardCharsets.UTF_8))) {
                try {
                    fetch.invoke(null, invalid, utility.getMethod("getJSON").invoke(null));
                    throw new AssertionError("Current schema validation was disabled");
                } catch (java.lang.reflect.InvocationTargetException error) {
                    if (!(error.getCause() instanceof net.weavemc.loader.impl.shaded.kotlinx.serialization.SerializationException)) throw error;
                    System.out.println("PASS intercepted parser retains strict current-schema rejection: " + error.getCause().getClass().getSimpleName());
                }
            }
            if (adaptedReads != 1 || originalReads != 2) throw new AssertionError("Read counts " + adaptedReads + "/" + originalReads);
            System.out.println("PASS one legacy adaptation and one untouched current stream; actual parser/serializer retained");
            System.out.println("BLOCKED actual registerAsMod: no safely constructed WeaveLoader with real Instrumentation. " +
                "Constructor installs InjectionHandler and executes addMinecraftApi/finalize; registration mutates classloader/cache before metadata. " +
                "Child UtilityKt interception is proven but is NOT installed in the parent WeaveLoader definition. No discovery claimed.");
        } finally {
            legacyInputs.clear();
            if (!ravenHash.equals(MixedWeaveProof.hash(raven)) || !bwhHash.equals(MixedWeaveProof.hash(bwh)))
                throw new AssertionError("Input archive changed during interception");
            System.out.println("PASS interception Raven/BWH hashes unchanged (including failure path)");
        }
    }
}
