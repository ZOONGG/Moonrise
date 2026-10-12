import java.nio.file.*;
import java.util.jar.JarFile;
import net.weavemc.loader.impl.util.*;
import net.weavemc.loader.impl.bootstrap.transformer.ModInitializerHook;
import net.weavemc.loader.impl.mixin.SandboxedMixinService;
import net.weavemc.loader.impl.shaded.asm.*;
import net.weavemc.loader.impl.shaded.asm.tree.*;

// Offline only: no premain, game main, imported initializer or account access.
public final class CurrentWeaveProbe {
    public static void main(String[] args) throws Exception {
        var mod = Path.of(args[0]).toRealPath();
        System.setProperty("weave.mods.directory", mod.getParent().toString());
        System.setProperty("weave.mods.override", mod.toString());
        System.getProperties().put("weave.game.info", java.util.Map.of("version", "1.8.9", "client", "lunar client"));
        var discovered = FileManager.INSTANCE.getMods();
        if (discovered.size() != 1 || !discovered.get(0).getFile().toPath().toRealPath().equals(mod))
            throw new AssertionError("Discovery did not isolate exactly the original mod");
        try (var jar = new JarFile(mod.toFile())) {
            var config = UtilityKt.fetchModConfig(jar, UtilityKt.getJSON());
            if (config.getEntryPoints().isEmpty() || config.getMixinConfigs().isEmpty())
                throw new AssertionError("Missing initializer or Mixin config");
            for (var entry : config.getEntryPoints())
                if (jar.getEntry(entry.replace('.', '/') + ".class") == null) throw new AssertionError(entry);
            for (var resource : config.getMixinConfigs())
                if (jar.getEntry(resource) == null) throw new AssertionError(resource);
            System.out.println("metadata-decoded namespace=" + config.getNamespace() + " initializers=" + config.getEntryPoints()
                + " configs=" + config.getMixinConfigs() + " dependencies=" + config.getDependencies());
        }
        if (!new SandboxedMixinService().getInitialPhase().toString().equals("PREINIT"))
            throw new AssertionError("Sandbox initial phase changed");
        // Exercise the actual loader's main hook on a synthetic game main, never execute it.
        var writer = new ClassWriter(0);
        writer.visit(52, Opcodes.ACC_PUBLIC, "net/minecraft/client/main/Main", null, "java/lang/Object", null);
        var method = writer.visitMethod(Opcodes.ACC_PUBLIC | Opcodes.ACC_STATIC, "main", "([Ljava/lang/String;)V", null, null);
        method.visitCode(); method.visitInsn(Opcodes.RETURN); method.visitMaxs(0, 1); method.visitEnd(); writer.visitEnd();
        var transformed = ModInitializerHook.INSTANCE.transform(CurrentWeaveProbe.class.getClassLoader(), "net/minecraft/client/main/Main", writer.toByteArray());
        var node = new ClassNode(); new ClassReader(transformed).accept(node, 0);
        long calls = node.methods.stream().flatMap(m -> java.util.Arrays.stream(m.instructions.toArray()))
            .filter(i -> i instanceof MethodInsnNode call && call.name.equals("initializeMods")).count();
        if (calls != 1) throw new AssertionError("Expected exactly one initializer dispatch");
        System.out.println("PASS discovery=1 sandbox=PREINIT initializer-dispatch=1; runtime initializer and mixin application remain untested");
    }
}
