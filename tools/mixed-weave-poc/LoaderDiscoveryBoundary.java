import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import java.util.jar.*;
import java.util.zip.ZipEntry;
import com.google.gson.*;
import net.weavemc.loader.impl.shaded.asm.*;
import net.weavemc.loader.impl.shaded.asm.tree.*;
import net.weavemc.loader.impl.util.UtilityKt;

/** Actual current parser evidence, explicitly NOT a replacement for registerAsMod. */
public final class LoaderDiscoveryBoundary {
    static byte[] normalize(byte[] original, String namespace, String version) {
        JsonObject metadata = JsonParser.parseString(new String(original, StandardCharsets.UTF_8)).getAsJsonObject();
        if (!metadata.has("entrypoints")) return original; // Current metadata passes through byte-for-byte.
        if (metadata.has("entryPoints")) throw new IllegalArgumentException("Ambiguous entrypoint generations");
        JsonObject result = metadata.deepCopy();
        result.add("entryPoints", result.remove("entrypoints"));
        JsonObject about = result.remove("about").getAsJsonObject();
        if (!result.has("name")) result.add("name", about.get("name"));
        if (!result.has("modId")) {
            String id = result.get("name").getAsString().toLowerCase(Locale.ROOT).replaceAll("[^a-z0-9_-]+", "-");
            if (id.isEmpty()) throw new IllegalArgumentException("Missing legacy mod identity");
            result.addProperty("modId", id);
        }
        for (String field : List.of("mixinConfigs", "hooks", "tweakers", "accessWideners", "dependencies"))
            if (!result.has(field)) result.add(field, new JsonArray());
        // Explicit named offline input context, not an inference from missing legacy metadata.
        if (!result.has("namespace")) result.addProperty("namespace", namespace);
        if (!result.has("compiledFor")) result.addProperty("compiledFor", version);
        return result.toString().getBytes(StandardCharsets.UTF_8);
    }
    static final class MetadataView extends JarFile {
        final byte[] adapted;
        MetadataView(Path path, byte[] adapted) throws IOException { super(path.toFile()); this.adapted = adapted; }
        @Override public InputStream getInputStream(ZipEntry entry) throws IOException {
            return entry.getName().equals("weave.mod.json") ? new ByteArrayInputStream(adapted) : super.getInputStream(entry);
        }
    }
    static ClassNode node(String name) throws IOException {
        try (var input = LoaderDiscoveryBoundary.class.getClassLoader().getResourceAsStream(name + ".class")) {
            if (input == null) throw new IOException(name);
            var node = new ClassNode(); new ClassReader(input.readAllBytes()).accept(node, 0); return node;
        }
    }
    static void run(Path raven, Path bwh, Path mappings) throws Exception {
        if (!Files.readAllLines(mappings).get(0).contains("mcp-named")) throw new AssertionError("Named context missing");
        for (Path path : List.of(raven, bwh)) {
            String before = MixedWeaveProof.hash(path);
            try (var original = new JarFile(path.toFile())) {
                byte[] raw = original.getInputStream(original.getJarEntry("weave.mod.json")).readAllBytes();
                boolean legacy = JsonParser.parseString(new String(raw, StandardCharsets.UTF_8)).getAsJsonObject().has("entrypoints");
                if (legacy) {
                    try {
                        UtilityKt.fetchModConfig(original, UtilityKt.getJSON());
                        throw new AssertionError("Legacy metadata unexpectedly accepted unadapted");
                    } catch (net.weavemc.loader.impl.shaded.kotlinx.serialization.SerializationException expected) {
                        System.out.println("EXPECTED real current parser rejects original legacy metadata: " + expected);
                    }
                }
                byte[] adapted = normalize(raw, "mcp-named", "1.8.9");
                if (!legacy && !Arrays.equals(raw, adapted)) throw new AssertionError("Current metadata changed");
                try (var view = new MetadataView(path, adapted)) {
                    var config = UtilityKt.fetchModConfig(view, UtilityKt.getJSON());
                    var source = JsonParser.parseString(new String(raw, StandardCharsets.UTF_8)).getAsJsonObject();
                    String entryKey = legacy ? "entrypoints" : "entryPoints";
                    if (!config.getEntryPoints().equals(strings(source.getAsJsonArray(entryKey))) ||
                        !config.getMixinConfigs().equals(strings(source.getAsJsonArray("mixinConfigs"))) ||
                        !config.getNamespace().equals("mcp-named") || !config.getCompiledFor().equals("1.8.9"))
                        throw new AssertionError("Metadata semantics lost");
                    for (String resource : config.getMixinConfigs())
                        if (!Arrays.equals(original.getInputStream(original.getJarEntry(resource)).readAllBytes(),
                            view.getInputStream(view.getJarEntry(resource)).readAllBytes())) throw new AssertionError("Config changed");
                    System.out.println("PASS actual current fetchModConfig, in-memory view: " + config);
                }
            } finally {
                if (!before.equals(MixedWeaveProof.hash(path))) throw new AssertionError("Archive changed");
            }
        }
        var loader = node("net/weavemc/loader/impl/WeaveLoader");
        var register = loader.methods.stream().filter(m -> m.name.equals("registerAsMod")).findFirst().orElseThrow();
        boolean opensJar = false, staticParser = false;
        for (var insn : register.instructions) {
            if (insn instanceof TypeInsnNode t && t.getOpcode() == Opcodes.NEW && t.desc.equals("java/util/jar/JarFile")) opensJar = true;
            if (insn instanceof MethodInsnNode m && m.owner.equals("net/weavemc/loader/impl/util/UtilityKt") && m.name.equals("configOrFatal")) staticParser = true;
        }
        if (!opensJar || !staticParser) throw new AssertionError("Pinned discovery boundary changed");
        var constructor = loader.methods.stream().filter(m -> m.name.equals("<init>")).findFirst().orElseThrow();
        List<String> bootstrap = calls(constructor);
        for (String required : List.of("java/lang/instrument/Instrumentation.addTransformer",
            "net/weavemc/loader/impl/WeaveLoader.addMinecraftApi", "net/weavemc/loader/impl/WeaveLoader.finalize"))
            if (!bootstrap.contains(required)) throw new AssertionError("Bootstrap boundary changed: " + required);
        List<String> registration = calls(register);
        int parserIndex = registration.indexOf("net/weavemc/loader/impl/util/UtilityKt.configOrFatal");
        for (String required : List.of("net/weavemc/loader/impl/bootstrap/transformer/URLClassLoaderAccessor.addWeaveURL",
            "net/weavemc/loader/impl/util/UtilityKt.getCacheManager")) {
            int index = registration.indexOf(required);
            if (index < 0 || index >= parserIndex) throw new AssertionError("Registration side-effect boundary changed");
        }
        if (!registration.contains("java/lang/instrument/Instrumentation.appendToSystemClassLoaderSearch"))
            throw new AssertionError("Real instrumentation requirement changed");
        var finalize = loader.methods.stream().filter(m -> m.name.equals("finalize")).findFirst().orElseThrow();
        if (!calls(finalize).contains("net/weavemc/loader/impl/WeaveLoader.verifyDependencies"))
            throw new AssertionError("Dependency validation boundary changed");
        System.out.println("PASS pinned constructor/registration bootstrap blockers and dependency validation asserted; no bootstrap bypass");
        var init = loader.methods.stream().filter(m -> m.name.equals("initializeMods")).findFirst().orElseThrow();
        boolean currentInit = false, systemLookup = false;
        for (var insn : init.instructions) if (insn instanceof MethodInsnNode m) {
            if (m.name.equals("preInit")) throw new AssertionError("Current init repeats preInit");
            if (m.owner.equals("net/weavemc/api/ModInitializer") && m.name.equals("init")) currentInit = true;
            if (m.owner.equals("java/lang/Class") && m.name.equals("forName") && m.desc.equals("(Ljava/lang/String;)Ljava/lang/Class;")) systemLookup = true;
        }
        if (!currentInit || !systemLookup) throw new AssertionError("Lifecycle lookup changed");
        System.out.println("PASS actual initializeMods bytecode calls init, never preInit; no Raven.init execution");
        MetadataReadInterception.run(raven, bwh);
        System.out.println("BLOCKED loader-wide identity: initializeMods uses Class.forName(String) in loader's defining classloader; offline child game owner alone cannot satisfy that lookup.");
    }
    static List<String> strings(JsonArray array) {
        var result = new ArrayList<String>(); for (var value : array) result.add(value.getAsString()); return result;
    }
    static List<String> calls(MethodNode method) {
        var result = new ArrayList<String>();
        for (var instruction : method.instructions)
            if (instruction instanceof MethodInsnNode call) result.add(call.owner + "." + call.name);
        return result;
    }
}
