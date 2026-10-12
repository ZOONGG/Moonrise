import java.util.*;
import com.google.gson.*;
import net.weavemc.loader.impl.shaded.asm.*;
import net.weavemc.loader.impl.shaded.asm.tree.*;

/** Read-only evidence for the original selector; never changes annotations or target bytes. */
public final class RavenYawSelectorEvidence {
    static void run(FullMixinPreparation.RealLoader game,
                    net.weavemc.loader.impl.mixin.SandboxedMixinLoader sandbox, List<?> handles) throws Exception {
        String owner = "net.minecraft.client.renderer.entity.RendererLivingEntity";
        byte[] bytes = game.classBytes(owner);
        var mixin = new ClassNode();
        new ClassReader(game.classBytes("me.pianopenguin471.mixins.RendererLivingEntityMixin")).accept(mixin, 0);
        var handler = mixin.methods.stream().filter(m -> m.name.equals("changeYaw")).findFirst().orElseThrow();
        for (var annotation : handler.visibleAnnotations)
            System.out.println("YAW original " + handler.name + handler.desc + " " + annotation.desc + " " + annotationText(annotation));
        var config = JsonParser.parseString(new String(game.raven.getInputStream(
            game.raven.getJarEntry("ravenweave.mixins.json")).readAllBytes(), java.nio.charset.StandardCharsets.UTF_8));
        System.out.println("YAW original config=" + config);
        int references = 0;
        for (var entry : Collections.list(game.raven.entries())) {
            if (!entry.getName().endsWith(".class")) continue;
            var candidate = new ClassNode();
            new ClassReader(game.raven.getInputStream(entry).readAllBytes()).accept(candidate, 0);
            for (var candidateMethod : candidate.methods)
                for (var insn : candidateMethod.instructions)
                    if (insn instanceof MethodInsnNode call && call.owner.equals(mixin.name) &&
                        call.name.equals(handler.name) && call.desc.equals(handler.desc)) references++;
        }
        System.out.println("YAW original archive direct handler references=" + references);
        for (Object handle : handles) {
            Object preparedConfig = handle.getClass().getMethod("getConfig").invoke(handle);
            var required = preparedConfig.getClass().getDeclaredMethod("getDefaultRequiredInjections");
            required.setAccessible(true);
            System.out.println("YAW prepared config=" + handle + " defaultRequire=" + required.invoke(preparedConfig));
        }

        // Use the sandbox's actual Mixin Locals implementation and its ASM identity.
        String asm = "net.weavemc.loader.impl.shaded.asm.";
        Class<?> nodeClass = sandbox.loadClass(asm + "tree.ClassNode");
        Object node = nodeClass.getConstructor().newInstance();
        Class<?> reader = sandbox.loadClass(asm + "ClassReader");
        reader.getMethod("accept", sandbox.loadClass(asm + "ClassVisitor"), int.class)
            .invoke(reader.getConstructor(byte[].class).newInstance((Object)bytes), node, 0);
        Class<?> methodClass = sandbox.loadClass(asm + "tree.MethodNode");
        Object method = null;
        for (Object candidate : (List<?>)nodeClass.getField("methods").get(node))
            if (methodClass.getField("name").get(candidate).equals("doRender") &&
                methodClass.getField("desc").get(candidate).equals("(Lnet/minecraft/entity/EntityLivingBase;DDDFF)V")) method = candidate;
        if (method == null) throw new AssertionError("Original yaw target method absent");
        Object insns = methodClass.getField("instructions").get(method);
        Object head = insns.getClass().getMethod("getFirst").invoke(insns);
        Class<?> insnClass = sandbox.loadClass(asm + "tree.AbstractInsnNode");
        Class<?> localsClass = sandbox.loadClass("net.weavemc.loader.impl.shaded.spongepowered.asm.util.Locals");
        // FabricUtil's default compatibility (no fabric-compat decoration in original config).
        Object[] locals = (Object[])localsClass.getMethod("getLocalsAt", nodeClass, methodClass, insnClass, int.class)
            .invoke(null, node, method, head, 10000);
        int floatOrdinal = 0;
        for (int slot = 0; slot < locals.length; slot++) {
            Object local = locals[slot];
            if (local == null) continue;
            String desc = (String)local.getClass().getField("desc").get(local);
            System.out.println("YAW HEAD slot=" + slot + " desc=" + desc +
                (desc.equals("F") ? " floatOrdinal=" + floatOrdinal++ : ""));
        }
        if (floatOrdinal != 2) throw new AssertionError("Yaw HEAD local structure changed");
        System.out.println("YAW HEAD ordinal=4 absent; actual Mixin Locals has only float ordinals 0,1");
        var target = new ClassNode(); new ClassReader(bytes).accept(target, 0);
        var render = target.methods.stream().filter(m -> m.name.equals("doRender") &&
            m.desc.equals("(Lnet/minecraft/entity/EntityLivingBase;DDDFF)V")).findFirst().orElseThrow();
        for (var local : render.localVariables)
            if (local.desc.equals("F")) System.out.println("YAW LVT float slot=" + local.index +
                " startInsn=" + render.instructions.indexOf(local.start) + " endInsn=" + render.instructions.indexOf(local.end));
    }

    static String annotationText(AnnotationNode annotation) {
        var values = new ArrayList<String>();
        for (int i = 0; i < annotation.values.size(); i += 2) {
            Object value = annotation.values.get(i + 1);
            values.add(annotation.values.get(i) + "=" +
                (value instanceof AnnotationNode nested ? annotationText(nested) : value));
        }
        return values.toString();
    }
}
