package moonrise.cosmetics;

import java.lang.instrument.ClassFileTransformer;
import java.lang.instrument.IllegalClassFormatException;
import java.security.ProtectionDomain;

import net.weavemc.loader.impl.shaded.asm.ClassReader;
import net.weavemc.loader.impl.shaded.asm.ClassWriter;
import net.weavemc.loader.impl.shaded.asm.Opcodes;
import net.weavemc.loader.impl.shaded.asm.tree.ClassNode;
import net.weavemc.loader.impl.shaded.asm.tree.InsnNode;
import net.weavemc.loader.impl.shaded.asm.tree.MethodNode;

/** Makes Lunar's v2 login response expose its built-in all-cosmetics mode. */
final class CosmeticsUnlockTransformer implements ClassFileTransformer {
    static final String TARGET = "com/lunarclient/websocket/cosmetic/v2/LoginResponse";

    @Override
    public byte[] transform(
            ClassLoader loader,
            String className,
            Class<?> classBeingRedefined,
            ProtectionDomain protectionDomain,
            byte[] classfileBuffer) throws IllegalClassFormatException {
        if (!TARGET.equals(className)) {
            return null;
        }
        try {
            ClassNode node = new ClassNode();
            new ClassReader(classfileBuffer).accept(node, 0);
            for (MethodNode method : node.methods) {
                if (!"getHasAllCosmeticsFlag".equals(method.name)
                        || !"()Z".equals(method.desc)) {
                    continue;
                }
                method.instructions.clear();
                method.tryCatchBlocks.clear();
                if (method.localVariables != null) {
                    method.localVariables.clear();
                }
                method.instructions.add(new InsnNode(Opcodes.ICONST_1));
                method.instructions.add(new InsnNode(Opcodes.IRETURN));
                method.maxStack = 1;
                method.maxLocals = 1;
                ClassWriter writer = new ClassWriter(0);
                node.accept(writer);
                return writer.toByteArray();
            }
        } catch (RuntimeException ignored) {
            // Unknown Lunar layouts safely keep the original bytecode.
        }
        return null;
    }
}
