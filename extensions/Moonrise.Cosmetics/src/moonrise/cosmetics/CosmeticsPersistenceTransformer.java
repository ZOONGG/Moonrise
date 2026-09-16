package moonrise.cosmetics;

import java.io.IOException;
import java.io.InputStream;
import java.lang.instrument.ClassFileTransformer;
import java.lang.instrument.IllegalClassFormatException;
import java.security.ProtectionDomain;
import net.weavemc.loader.impl.shaded.asm.ClassReader;
import net.weavemc.loader.impl.shaded.asm.ClassWriter;
import net.weavemc.loader.impl.shaded.asm.Opcodes;
import net.weavemc.loader.impl.shaded.asm.tree.AbstractInsnNode;
import net.weavemc.loader.impl.shaded.asm.tree.ClassNode;
import net.weavemc.loader.impl.shaded.asm.tree.FieldInsnNode;
import net.weavemc.loader.impl.shaded.asm.tree.FieldNode;
import net.weavemc.loader.impl.shaded.asm.tree.FrameNode;
import net.weavemc.loader.impl.shaded.asm.tree.InsnList;
import net.weavemc.loader.impl.shaded.asm.tree.LdcInsnNode;
import net.weavemc.loader.impl.shaded.asm.tree.MethodInsnNode;
import net.weavemc.loader.impl.shaded.asm.tree.MethodNode;
import net.weavemc.loader.impl.shaded.asm.tree.TypeInsnNode;
import net.weavemc.loader.impl.shaded.asm.tree.VarInsnNode;

/** Adds classloader-local capture and replay of Lunar v2 outfit requests. */
final class CosmeticsPersistenceTransformer implements ClassFileTransformer {
    static final String TARGET = "com/lunarclient/websocket/cosmetic/v2/CosmeticService$Stub";
    private static final String TEMPLATE = "moonrise/cosmetics/CosmeticsPersistenceTemplate";
    private static final String CALLBACK = "com/google/protobuf/RpcCallback";

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
            ClassNode target = new ClassNode();
            new ClassReader(classfileBuffer).accept(target, 0);
            ClassNode template = readTemplate();
            transplant(template, target);

            boolean login = false;
            boolean update = false;
            boolean select = false;
            for (MethodNode method : target.methods) {
                if ("login".equals(method.name) && method.desc.contains("LoginRequest;")) {
                    method.instructions.insert(wrapLoginCallback());
                    method.maxStack = Math.max(method.maxStack, 2);
                    login = true;
                } else if ("updateOutfit".equals(method.name)
                        && method.desc.contains("UpdateOutfitRequest;")) {
                    method.instructions.insert(captureRequest("updateOutfit"));
                    method.maxStack = Math.max(method.maxStack, 3);
                    update = true;
                } else if ("selectOutfit".equals(method.name)
                        && method.desc.contains("SelectOutfitRequest;")) {
                    method.instructions.insert(captureRequest("selectOutfit"));
                    method.maxStack = Math.max(method.maxStack, 3);
                    select = true;
                }
            }
            if (!login || !update || !select) {
                return null;
            }
            ClassWriter writer = new ClassWriter(0);
            target.accept(writer);
            CosmeticsAgent.writeProof("cosmetics-persistence-class.txt", className + "\n");
            return writer.toByteArray();
        } catch (RuntimeException | IOException exception) {
            return null;
        }
    }

    private static ClassNode readTemplate() throws IOException {
        InputStream input = CosmeticsPersistenceTransformer.class.getClassLoader()
                .getResourceAsStream(TEMPLATE + ".class");
        if (input == null) {
            throw new IOException("Persistence template is missing");
        }
        try (InputStream stream = input) {
            ClassNode template = new ClassNode();
            new ClassReader(stream).accept(template, 0);
            return template;
        }
    }

    private static void transplant(ClassNode template, ClassNode target) {
        if (!target.interfaces.contains("java/lang/reflect/InvocationHandler")) {
            target.interfaces.add("java/lang/reflect/InvocationHandler");
        }
        for (FieldNode field : template.fields) {
            if (field.name.startsWith("moonrise$")) {
                target.fields.add(new FieldNode(
                        field.access, field.name, field.desc, field.signature, field.value));
            }
        }
        for (MethodNode method : template.methods) {
            if (!method.name.startsWith("moonrise$")
                    && !"invoke".equals(method.name)) {
                continue;
            }
            MethodNode copy = new MethodNode(
                    method.access,
                    method.name,
                    method.desc,
                    method.signature,
                    method.exceptions == null
                            ? null
                            : method.exceptions.toArray(new String[0]));
            method.accept(copy);
            remapTemplateOwner(copy);
            target.methods.add(copy);
        }
    }

    private static void remapTemplateOwner(MethodNode method) {
        for (AbstractInsnNode instruction = method.instructions.getFirst();
             instruction != null;
             instruction = instruction.getNext()) {
            if (instruction instanceof MethodInsnNode) {
                MethodInsnNode call = (MethodInsnNode) instruction;
                if (TEMPLATE.equals(call.owner)) {
                    call.owner = TARGET;
                }
            } else if (instruction instanceof FieldInsnNode) {
                FieldInsnNode field = (FieldInsnNode) instruction;
                if (TEMPLATE.equals(field.owner)) {
                    field.owner = TARGET;
                }
            } else if (instruction instanceof TypeInsnNode) {
                TypeInsnNode type = (TypeInsnNode) instruction;
                if (TEMPLATE.equals(type.desc)) {
                    type.desc = TARGET;
                }
            } else if (instruction instanceof FrameNode) {
                FrameNode frame = (FrameNode) instruction;
                remapFrameTypes(frame.local);
                remapFrameTypes(frame.stack);
            }
        }
    }

    private static void remapFrameTypes(java.util.List<Object> types) {
        if (types == null) {
            return;
        }
        for (int index = 0; index < types.size(); index++) {
            if (TEMPLATE.equals(types.get(index))) {
                types.set(index, TARGET);
            }
        }
    }

    private static InsnList captureRequest(String operation) {
        InsnList instructions = new InsnList();
        instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));
        instructions.add(new LdcInsnNode(operation));
        instructions.add(new VarInsnNode(Opcodes.ALOAD, 2));
        instructions.add(new MethodInsnNode(
                Opcodes.INVOKESPECIAL,
                TARGET,
                "moonrise$capture",
                "(Ljava/lang/String;Ljava/lang/Object;)V",
                false));
        return instructions;
    }

    private static InsnList wrapLoginCallback() {
        InsnList instructions = new InsnList();
        instructions.add(new VarInsnNode(Opcodes.ALOAD, 0));
        instructions.add(new VarInsnNode(Opcodes.ALOAD, 3));
        instructions.add(new MethodInsnNode(
                Opcodes.INVOKESPECIAL,
                TARGET,
                "moonrise$wrapLoginCallback",
                "(Ljava/lang/Object;)Ljava/lang/Object;",
                false));
        instructions.add(new TypeInsnNode(Opcodes.CHECKCAST, CALLBACK));
        instructions.add(new VarInsnNode(Opcodes.ASTORE, 3));
        return instructions;
    }
}
