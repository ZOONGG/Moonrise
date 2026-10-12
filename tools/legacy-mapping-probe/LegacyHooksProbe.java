import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.jar.JarFile;
import kotlinx.serialization.json.Json;
import net.weavemc.loader.WeaveLoader;
import net.weavemc.loader.HookManager;
import net.weavemc.loader.api.Hook;
import org.objectweb.asm.ClassWriter;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.MethodNode;
import org.objectweb.asm.tree.InsnNode;
import org.objectweb.asm.tree.LdcInsnNode;

/** Offline contract probe. No game, initializer, agent, or generated JAR. */
public final class LegacyHooksProbe {
    public static void main(String[] args) throws Exception {
        WeaveLoader.ModConfig config;
        try (JarFile jar = new JarFile(args[0])) {
            String json = new String(jar.getInputStream(jar.getJarEntry("weave.mod.json")).readAllBytes(), StandardCharsets.UTF_8);
            config = Json.Default.decodeFromString(WeaveLoader.ModConfig.Companion.serializer(), json);
        }
        if (!config.getEntrypoints().isEmpty() || !config.getMixinConfigs().isEmpty() || config.getHooks().isEmpty())
            throw new AssertionError("Expected hooks-only metadata");
        var manager = HookManager.INSTANCE;
        var saved = new ArrayList<>(manager.getHooks());
        try {
            manager.getHooks().clear();
            for (String name : config.getHooks()) {
                Hook hook = (Hook) Class.forName(name).getConstructor().newInstance();
                if (hook.targets.length != 0) throw new AssertionError("Expected global hook: " + name);
                // Same registration and dispatch ABI as WeaveLoader.init / HookManager.transform.
                manager.getHooks().add(hook);
                hook.transform(new ClassNode(), () -> {});
                System.out.println("instantiated-and-dispatched=" + name);
            }
            ClassNode node = new ClassNode();
            node.version = Opcodes.V1_8;
            node.access = Opcodes.ACC_PUBLIC;
            node.name = "fixture/Neutral";
            node.superName = "java/lang/Object";
            MethodNode method = new MethodNode(Opcodes.ACC_PUBLIC | Opcodes.ACC_STATIC, "value", "()Ljava/lang/String;", null, null);
            method.instructions.add(new LdcInsnNode("neutral"));
            method.instructions.add(new InsnNode(Opcodes.ARETURN));
            node.methods.add(method);
            ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
            node.accept(writer);
            byte[] output = manager.transform(LegacyHooksProbe.class.getClassLoader(), node.name, writer.toByteArray());
            if (output == null) throw new AssertionError("Global hooks were not dispatched");
            new org.objectweb.asm.ClassReader(output);
            System.out.println("registered=" + manager.getHooks().size() + "; manager-dispatch=passed");

            // Exercise a positive transformation without loading any Lunar class.
            node.name = "fixture/Reach";
            method.access = Opcodes.ACC_PUBLIC;
            ((LdcInsnNode) method.instructions.getFirst()).cst = "[1.3 blocks]";
            MethodNode reach = new MethodNode(Opcodes.ACC_PUBLIC, "reach", "()D", null, null);
            reach.instructions.add(new LdcInsnNode(3.0d));
            reach.instructions.add(new InsnNode(Opcodes.DRETURN));
            node.methods.add(reach);
            writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
            node.accept(writer);
            output = manager.transform(LegacyHooksProbe.class.getClassLoader(), node.name, writer.toByteArray());
            ClassNode transformed = new ClassNode();
            new org.objectweb.asm.ClassReader(output).accept(transformed, 0);
            Object constant = ((LdcInsnNode) transformed.methods.stream().filter(m -> m.name.equals("reach")).findFirst().orElseThrow().instructions.getFirst()).cst;
            if (!Double.valueOf(300.0d).equals(constant)) throw new AssertionError("Reach hook did not apply");
            System.out.println("positive-reach-transform=passed; constant=300.0");
        } finally {
            manager.getHooks().clear();
            manager.getHooks().addAll(saved);
        }
    }
}
