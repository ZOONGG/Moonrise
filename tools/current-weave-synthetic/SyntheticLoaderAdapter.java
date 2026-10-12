package dev.moonrise.current;
import java.lang.instrument.*;
import java.net.*;
import java.security.*;
import net.weavemc.loader.impl.shaded.asm.*;
import net.weavemc.loader.impl.shaded.asm.tree.*;

public final class SyntheticLoaderAdapter implements ClassFileTransformer {
    static final String GENESIS = "com/moonsworth/lunar/genesis/ORCOIHCCICICIHRCCCCHHHRICHHICI";
    private final URL self;
    private SyntheticLoaderAdapter(URL self) { this.self = self; }
    public static void install(Instrumentation inst, URL self) { inst.addTransformer(new SyntheticLoaderAdapter(self)); }
    public byte[] transform(ClassLoader loader, String name, Class<?> old, ProtectionDomain domain, byte[] bytes) {
        try {
            if (GENESIS.equals(name)) {
                var result = adapt(bytes);
                System.err.println("[Moonrise current] synthetic class loading bridge installed");
                return result;
            }
            if ("net/weavemc/loader/impl/WeaveLoader".equals(name) && loader != null && loader.getClass().getName().replace('.', '/').equals(GENESIS)) {
                loader.getClass().getMethod("addURL", URL.class).invoke(loader, self);
            }
        } catch (Exception failure) {
            // Transformer exceptions are ignored by Instrumentation; do not silently continue.
            failure.printStackTrace(System.err); Runtime.getRuntime().halt(86);
        }
        return null;
    }
    static byte[] adapt(byte[] bytes) throws Exception {
        var target = new ClassNode(); new ClassReader(bytes).accept(target, 0);
        if (!target.superName.equals("java/net/URLClassLoader")) throw new IllegalStateException("Unsupported game loader superclass");
        var template = new ClassNode();
        try (var stream = SyntheticLoaderAdapter.class.getResourceAsStream("SyntheticLoaderMethods.class")) {
            new ClassReader(stream).accept(template, 0);
        }
        for (var method : template.methods) if (method.name.startsWith("moonrise$")) {
            if (target.methods.stream().anyMatch(m -> m.name.equals(method.name))) throw new IllegalStateException("Duplicate synthetic bridge");
            for (var insn : method.instructions) {
                if (insn instanceof MethodInsnNode call && call.owner.equals(template.name)) call.owner = target.name;
                if (insn instanceof FrameNode frame && frame.local != null)
                    frame.local.replaceAll(x -> template.name.equals(x) ? target.name : x);
            }
            target.methods.add(method);
        }
        int matches = 0;
        for (var method : target.methods) if (method.name.equals("loadClass") && method.desc.equals("(Ljava/lang/String;Z)Ljava/lang/Class;")) {
            var code = new InsnList(); var original = new LabelNode();
            code.add(new VarInsnNode(Opcodes.ALOAD, 1)); code.add(new LdcInsnNode(SyntheticResolver.PREFIX));
            code.add(new MethodInsnNode(Opcodes.INVOKEVIRTUAL, "java/lang/String", "startsWith", "(Ljava/lang/String;)Z", false));
            code.add(new JumpInsnNode(Opcodes.IFEQ, original));
            code.add(new VarInsnNode(Opcodes.ALOAD, 0)); code.add(new VarInsnNode(Opcodes.ALOAD, 1)); code.add(new VarInsnNode(Opcodes.ILOAD, 2));
            code.add(new MethodInsnNode(Opcodes.INVOKESPECIAL, target.name, "moonrise$loadSynthetic", "(Ljava/lang/String;Z)Ljava/lang/Class;", false));
            code.add(new InsnNode(Opcodes.ARETURN)); code.add(original); code.add(new FrameNode(Opcodes.F_SAME, 0, null, 0, null));
            method.instructions.insert(code); matches++;
        }
        if (matches != 1) throw new IllegalStateException("Unsupported game loader loadClass boundary");
        var writer = new ClassWriter(ClassWriter.COMPUTE_MAXS); target.accept(writer); return writer.toByteArray();
    }
}
