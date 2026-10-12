import java.nio.file.*;
import java.util.*;
import java.util.jar.*;
import net.weavemc.loader.impl.shaded.asm.*;
import net.weavemc.loader.impl.shaded.asm.commons.*;
import net.weavemc.loader.impl.shaded.asm.tree.*;

/** Test-only provider boundary; legacy membership comes from archive entries. */
public final class OfflineMixinBytes {
    private static final Map<String, byte[]> legacy = new HashMap<>(), current = new HashMap<>();
    private static final Set<String> seen = new HashSet<>();
    private static Remapper relocation;
    static boolean installed;
    private static final Map<String, byte[]> game = new HashMap<>();
    static void initializeGame(Path path) throws Exception {
        if (!System.getProperty("weave.namespace.environment.runtime").equals("mcp-named") ||
            !System.getProperty("weave.namespace.environment.classpath").equals("mcp-named"))
            throw new AssertionError("Offline game provider requires named context");
        read(path, game);
        for (var entry : game.entrySet()) {
            if (!entry.getKey().startsWith("net/minecraft/") ||
                !new ClassReader(entry.getValue()).getClassName().equals(entry.getKey()))
                throw new AssertionError("Unexpected named game archive class: " + entry.getKey());
        }
        for (String name : List.of("net/minecraft/entity/Entity", "net/minecraft/client/gui/GuiScreen"))
            if (!game.containsKey(name)) throw new AssertionError("Named game input absent: " + name);
        System.out.println("PASS named game archive classes=" + game.size());
    }
    static void initialize(Path old, Path maintained) throws Exception {
        read(old, legacy); read(maintained, current);
        for (String name : legacy.keySet())
            if (current.containsKey(name)) throw new AssertionError("Ambiguous class ownership: " + name);
        relocation = (Remapper) net.weavemc.loader.impl.util.MappingsKt.class
            .getMethod("access$createRelocationRemapper").invoke(null);
        if (relocation == null) throw new AssertionError("Pinned relocation unavailable");
        String expected = Type.getDescriptor(Class.forName("net.weavemc.loader.impl.shaded.spongepowered.asm.mixin.Mixin"));
        if (!expected.equals("L" + relocation.map("org/spongepowered/asm/mixin/Mixin") + ";"))
            throw new AssertionError("Relocator/runtime descriptor mismatch");
        System.out.println("EXPECTED runtime annotation=" + expected);
        legacy.forEach((name, bytes) -> {
            if (!annotations(bytes).isEmpty()) System.out.println("ORIGINAL legacy " + name + " annotations=" + annotations(bytes));
        });
    }
    private static void read(Path path, Map<String, byte[]> classes) throws Exception {
        try (var jar = new JarFile(path.toFile())) {
            for (var entry : Collections.list(jar.entries())) if (entry.getName().endsWith(".class")) {
                try (var stream = jar.getInputStream(entry)) {
                    classes.put(entry.getName().substring(0, entry.getName().length() - 6), stream.readAllBytes());
                }
            }
        }
    }
    static void auditArchive(java.net.URL url) {
        try (var jar = new JarFile(Path.of(url.toURI()).toFile())) {
            int matches = 0;
            for (String name : legacy.keySet()) {
                var entry = jar.getJarEntry(name + ".class");
                if (entry == null) continue;
                matches++;
                try (var stream = jar.getInputStream(entry)) {
                    byte[] bytes = stream.readAllBytes();
                    if (!Arrays.equals(bytes, legacy.get(name))) throw new AssertionError("Legacy archive entry changed: " + name);
                    if (!annotations(bytes).isEmpty()) System.out.println("REGISTERED archive " + name + " annotations=" + annotations(bytes));
                }
            }
            System.out.println("ARCHIVE path=" + url + "; legacyClasses=" + matches);
        } catch (Exception error) { throw new AssertionError("Archive audit failed", error); }
    }
    private static List<String> annotations(byte[] bytes) {
        var node = new ClassNode(); new ClassReader(bytes).accept(node, ClassReader.SKIP_CODE);
        var result = new ArrayList<String>();
        if (node.visibleAnnotations != null) for (var a : node.visibleAnnotations) result.add(a.desc);
        if (node.invisibleAnnotations != null) for (var a : node.invisibleAnnotations) result.add(a.desc);
        return result;
    }
    public static byte[] supply(byte[] raw, String name, ClassLoader sandbox) {
        String key = name.replace('.', '/');
        if (key.startsWith("net/minecraft/")) {
            // The real loader's provider assumes official backing names. This
            // offline backing loader instead owns already-named definitions.
            byte[] named = game.get(key);
            if (named == null) throw new AssertionError("Named game class absent: " + key);
            try {
                if (Collections.list(sandbox.getParent().getResources(key + ".class")).size() != 1)
                    throw new AssertionError("Duplicate game resource owner: " + key);
            } catch (java.io.IOException error) { throw new AssertionError(error); }
            try (var stream = sandbox.getParent().getResourceAsStream(key + ".class")) {
                if (stream == null || !Arrays.equals(named, stream.readAllBytes()))
                    throw new AssertionError("Game owner/resource mismatch: " + key);
            } catch (java.io.IOException error) { throw new AssertionError(error); }
            if (seen.add(key)) System.out.println("PASS SANDBOX named game " + key +
                "; archive/resource/provider equality; source=" + sandbox.getParent().getResource(key + ".class"));
            return named;
        }
        if (raw == null) {
            if (name.startsWith("net/minecraft/") && seen.add(name))
                System.out.println("SANDBOX missing target " + name + "; resource=" + sandbox.getParent().getResource(name + ".class"));
            return null;
        }
        byte[] original = legacy.get(key);
        if (original == null) {
            if (current.containsKey(key)) {
                if (!Arrays.equals(raw, current.get(key))) throw new AssertionError("Current provider bytes changed: " + key);
                if (seen.add(key)) System.out.println("PASS current provider/archive equality " + key + " annotations=" + annotations(raw));
            }
            return raw;
        }
        boolean enabled = !Boolean.getBoolean("moonrise.proof.traceMixinBytesOnly");
        byte[] output = raw;
        if (enabled) {
            var writer = new ClassWriter(0);
            new ClassReader(raw).accept(new ClassRemapper(writer, relocation), 0);
            output = writer.toByteArray();
            var expectedAnnotations = new ArrayList<String>();
            for (String descriptor : annotations(raw)) expectedAnnotations.add(relocation.mapDesc(descriptor));
            if (!annotations(output).equals(expectedAnnotations)) throw new AssertionError("Annotation relocation mismatch: " + key);
        }
        if (seen.add(key)) System.out.println("SANDBOX legacy " + key + "; source=" + sandbox.getParent().getResource(key + ".class") +
            "; originalEquality=" + Arrays.equals(original, raw) + "; incoming=" + annotations(raw) +
            "; supplied=" + annotations(output) + "; relocation=" + enabled);
        return output;
    }
    static byte[] intercept(byte[] bytes) {
        var reader = new ClassReader(bytes); var node = new ClassNode(); reader.accept(node, 0);
        int returns = 0;
        for (var method : node.methods) if (method.name.equals("getClassBytes$weave_loader") && method.desc.equals("(Ljava/lang/String;)[B")) {
            for (var instruction : method.instructions.toArray()) if (instruction.getOpcode() == Opcodes.ARETURN) {
                var hook = new InsnList();
                hook.add(new VarInsnNode(Opcodes.ALOAD, 1));
                hook.add(new VarInsnNode(Opcodes.ALOAD, 0));
                hook.add(new MethodInsnNode(Opcodes.INVOKESTATIC, "OfflineMixinBytes", "supply",
                    "([BLjava/lang/String;Ljava/lang/ClassLoader;)[B", false));
                method.instructions.insertBefore(instruction, hook); returns++;
                method.maxStack += 2;
            }
        }
        if (returns != 1) throw new AssertionError("Pinned sandbox byte boundary changed: " + returns);
        var writer = new ClassWriter(reader, 0); node.accept(writer); installed = true;
        return writer.toByteArray();
    }
}
