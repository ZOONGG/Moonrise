package dev.moonrise.current;
import java.util.*;
import net.weavemc.loader.impl.WeaveLoader;
import net.weavemc.loader.impl.mixin.*;
import net.weavemc.loader.impl.util.MappingsHandler;
import net.weavemc.loader.impl.shaded.asm.ClassReader;
import net.weavemc.loader.impl.shaded.asm.ClassWriter;
import net.weavemc.loader.impl.shaded.asm.commons.ClassRemapper;
import net.weavemc.loader.impl.shaded.asm.commons.Remapper;

public final class SyntheticResolver {
    public static final String PREFIX = "net.weavemc.loader.impl.shaded.spongepowered.asm.synthetic.";
    private static Object field(Object value, String name) throws ReflectiveOperationException {
        var f = value.getClass().getDeclaredField(name); f.setAccessible(true); return f.get(value);
    }
    public static byte[] generate(ClassLoader game, String name) throws ClassNotFoundException {
        if (!name.startsWith(PREFIX)) return null;
        try {
            var instance = WeaveLoader.getInstance();
            if (instance == null || WeaveLoader.class.getClassLoader() != game)
                throw new IllegalStateException("Current Weave synthetic request from wrong game loader");
            @SuppressWarnings("unchecked") var sandboxes = (Map<String, SandboxedMixinLoader>)field(instance, "mixinInstances");
            var mappings = MappingsHandler.INSTANCE;
            return generateRegistered(sandboxes, name, (namespace, bytes) ->
                remapExecutable(bytes, mappings.mapper(namespace, mappings.getEnvironmentRuntimeNamespace())));
        } catch (ReflectiveOperationException | RuntimeException failure) {
            throw new ClassNotFoundException("Current Weave synthetic generation failed: " + name, failure);
        }
    }
    static byte[] remapExecutable(byte[] bytes, Remapper mapper) {
        // MappingsHandler.remap is a metadata reader (SKIP_CODE), not an executable-class remapper.
        var writer = new ClassWriter(0);
        new ClassReader(bytes).accept(new ClassRemapper(writer, mapper), 0);
        return writer.toByteArray();
    }
    static byte[] generateRegistered(Map<String, SandboxedMixinLoader> sandboxes, String name,
        java.util.function.BiFunction<String, byte[], byte[]> remap) throws ReflectiveOperationException {
            SandboxedMixinState owner = null; String namespace = null;
            for (var entry : sandboxes.entrySet()) {
                var state = entry.getValue().getState();
                var registry = field(state.getTransformer$weave_loader(), "syntheticClassRegistry");
                if (((Map<?, ?>)field(registry, "classes")).containsKey(name.replace('.', '/'))) {
                    if (owner != null) throw new IllegalStateException("Ambiguous synthetic class owner: " + name);
                    owner = state; namespace = entry.getKey();
                }
            }
            if (owner == null) return null;
            byte[] bytes = generateOwned(owner, name);
            bytes = remap.apply(namespace, bytes);
            if (!new ClassReader(bytes).getClassName().equals(name.replace('.', '/')))
                throw new IllegalStateException("Generated synthetic class name mismatch");
            System.err.println("[Moonrise current] synthetic-generated name=" + name + " namespace=" + namespace);
            return bytes;
    }
    static byte[] generateOwned(SandboxedMixinState state, String name) {
        byte[] bytes = state.generateClass(state.getTransformer$weave_loader(), name);
        if (bytes == null) throw new IllegalStateException("Registered synthetic class produced no bytes: " + name);
        return bytes;
    }
}
