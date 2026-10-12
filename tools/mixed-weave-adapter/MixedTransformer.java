package dev.moonrise.mixed;

import java.lang.instrument.*;
import java.net.URL;
import java.security.ProtectionDomain;
import net.weavemc.loader.impl.shaded.asm.*;
import net.weavemc.loader.impl.shaded.asm.tree.*;

public final class MixedTransformer implements ClassFileTransformer {
    private final URL self;
    private MixedTransformer(URL self) { this.self = self; }
    public static void install(Instrumentation inst, URL self) { inst.addTransformer(new MixedTransformer(self)); }
    public byte[] transform(ClassLoader owner, String name, Class<?> old, ProtectionDomain domain, byte[] bytes) {
        try {
            if (name.equals("net/weavemc/loader/impl/WeaveLoader")) {
                // ModInitializerHook legitimately resolves its system-side class before bootstrap.
                if (owner == ClassLoader.getSystemClassLoader()) return null;
                if (owner == null || !owner.getClass().getName().equals("com.moonsworth.lunar.genesis.ORCOIHCCICICIHRCCCCHHHRICHHICI"))
                    throw new IllegalStateException("Mixed experiment requires actual Genesis game loader");
                owner.getClass().getMethod("addURL", URL.class).invoke(owner, self);
                System.out.println("[Moonrise mixed] adapter added to actual Weave owner");
            }
            boolean metadata = name.equals("net/weavemc/loader/impl/util/UtilityKt");
            boolean sandbox = name.equals("net/weavemc/loader/impl/mixin/SandboxedMixinLoader");
            boolean locals = name.equals("net/weavemc/loader/impl/shaded/spongepowered/asm/util/Locals");
            if (!metadata && !sandbox && !locals) return null;
            var reader = new ClassReader(bytes); var node = new ClassNode(); reader.accept(node, 0);
            int matches = 0;
            for (var method : node.methods) {
                if (locals) for (var instruction : method.instructions.toArray())
                    if (instruction instanceof MethodInsnNode call && call.owner.endsWith("/mixin/transformer/ClassInfo")
                        && call.name.equals("findMethod") && call.desc.startsWith("(Lnet/weavemc/loader/impl/shaded/asm/tree/MethodNode;")) {
                        method.instructions.insertBefore(call, new VarInsnNode(Opcodes.ALOAD, 0));
                        String resultType = Type.getReturnType(call.desc).getInternalName();
                        call.setOpcode(Opcodes.INVOKESTATIC); call.owner = "dev/moonrise/mixed/MixedRuntime";
                        call.name = "findMethodDiagnostic";
                        call.desc = "(Ljava/lang/Object;Ljava/lang/Object;ILjava/lang/Object;)Ljava/lang/Object;";
                        method.instructions.insert(call, new TypeInsnNode(Opcodes.CHECKCAST, resultType));
                        method.maxStack++; matches++;
                    }
                if (metadata && method.name.equals("fetchModConfig") && method.desc.equals(
                    "(Ljava/util/jar/JarFile;Lnet/weavemc/loader/impl/shaded/kotlinx/serialization/json/Json;)Lnet/weavemc/internals/ModConfig;")) {
                    for (var instruction : method.instructions.toArray())
                        if (instruction instanceof MethodInsnNode call && call.getOpcode() == Opcodes.INVOKEVIRTUAL
                            && call.owner.equals("java/util/jar/JarFile") && call.name.equals("getInputStream")
                            && call.desc.equals("(Ljava/util/zip/ZipEntry;)Ljava/io/InputStream;")) {
                            call.setOpcode(Opcodes.INVOKESTATIC); call.owner = "dev/moonrise/mixed/MixedRuntime";
                            call.name = "metadataStream";
                            call.desc = "(Ljava/util/jar/JarFile;Ljava/util/zip/ZipEntry;)Ljava/io/InputStream;"; matches++;
                        }
                }
                if (sandbox && method.name.equals("getClassBytes$weave_loader") && method.desc.equals("(Ljava/lang/String;)[B"))
                    for (var instruction : method.instructions.toArray()) if (instruction.getOpcode() == Opcodes.ARETURN) {
                        var hook = new InsnList(); hook.add(new VarInsnNode(Opcodes.ALOAD, 1));
                        hook.add(new MethodInsnNode(Opcodes.INVOKESTATIC, "dev/moonrise/mixed/MixedRuntime", "mixinBytes", "([BLjava/lang/String;)[B", false));
                        method.instructions.insertBefore(instruction, hook); method.maxStack++; matches++;
                    }
            }
            if (matches != (locals ? 2 : 1)) throw new IllegalStateException("Pinned mixed boundary changed: " + name + " matches=" + matches);
            var writer = new ClassWriter(reader, 0); node.accept(writer);
            System.out.println("[Moonrise mixed] installed " + name); return writer.toByteArray();
        } catch (Throwable failure) {
            failure.printStackTrace(System.err); Runtime.getRuntime().halt(87); return null;
        }
    }
}
