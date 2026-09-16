package moonrise.cosmetics;

import java.io.ByteArrayOutputStream;
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
import net.weavemc.loader.impl.shaded.asm.tree.LdcInsnNode;
import net.weavemc.loader.impl.shaded.asm.tree.MethodInsnNode;
import net.weavemc.loader.impl.shaded.asm.tree.MethodNode;

/** Keeps equipped cosmetics visible while Lunar reconnects its WebSocket. */
final class CosmeticsStabilityTransformer implements ClassFileTransformer {
    private static final long DEFAULT_RECONNECT_DELAY_MS = 15000L;
    private static final long FAST_RECONNECT_DELAY_MS = 1000L;
    private static final long DEFAULT_MAX_BACKOFF_MS = 120000L;
    private static final long MAX_BACKOFF_MS = 5000L;

    @Override
    public byte[] transform(
            ClassLoader loader,
            String className,
            Class<?> classBeingRedefined,
            ProtectionDomain protectionDomain,
            byte[] classfileBuffer) throws IllegalClassFormatException {
        if (className == null || !className.startsWith("com/moonsworth/lunar/client/")) {
            return null;
        }
        try {
            if (CosmeticsAgent.containsAscii(classfileBuffer, "getReconnectAuthenticatorJwt")
                    && CosmeticsAgent.containsAscii(classfileBuffer, "getReconnectDelay")) {
                return patchReconnectPolicy(classfileBuffer);
            }
            if (CosmeticsAgent.containsAscii(classfileBuffer, "Disconnected from the AssetServer")) {
                return preserveCosmeticsDuringReconnect(loader, classfileBuffer);
            }
        } catch (Exception ignored) {
            // Unknown Lunar layouts safely keep the original bytecode.
        }
        return null;
    }

    private static byte[] patchReconnectPolicy(byte[] classfileBuffer) {
        ClassNode node = read(classfileBuffer);
        boolean changed = false;
        for (FieldNode field : node.fields) {
            if (Long.valueOf(DEFAULT_RECONNECT_DELAY_MS).equals(field.value)) {
                field.value = Long.valueOf(FAST_RECONNECT_DELAY_MS);
                changed = true;
            } else if (Long.valueOf(DEFAULT_MAX_BACKOFF_MS).equals(field.value)) {
                field.value = Long.valueOf(MAX_BACKOFF_MS);
                changed = true;
            }
        }
        for (MethodNode method : node.methods) {
            for (AbstractInsnNode instruction = method.instructions.getFirst();
                 instruction != null;
                 instruction = instruction.getNext()) {
                if (!(instruction instanceof LdcInsnNode)) {
                    continue;
                }
                LdcInsnNode constant = (LdcInsnNode) instruction;
                if (Long.valueOf(DEFAULT_RECONNECT_DELAY_MS).equals(constant.cst)) {
                    constant.cst = Long.valueOf(FAST_RECONNECT_DELAY_MS);
                    changed = true;
                } else if (Long.valueOf(DEFAULT_MAX_BACKOFF_MS).equals(constant.cst)) {
                    constant.cst = Long.valueOf(MAX_BACKOFF_MS);
                    changed = true;
                }
            }
        }
        return changed ? write(node) : null;
    }

    private static byte[] preserveCosmeticsDuringReconnect(ClassLoader loader, byte[] classfileBuffer) {
        ClassNode node = read(classfileBuffer);
        MethodNode onClose = null;
        for (MethodNode method : node.methods) {
            if ("onClose".equals(method.name) && "(ILjava/lang/String;Z)V".equals(method.desc)) {
                onClose = method;
                break;
            }
        }
        if (onClose == null) {
            return null;
        }

        boolean changed = false;
        for (AbstractInsnNode instruction = onClose.instructions.getFirst(); instruction != null;) {
            AbstractInsnNode next = instruction.getNext();
            if (instruction instanceof MethodInsnNode) {
                MethodInsnNode call = (MethodInsnNode) instruction;
                if (isOwnershipVisibilityReset(call)) {
                    changed |= removeTwoArgumentSetter(onClose, call);
                } else if (isCosmeticsStoreReset(loader, call)) {
                    changed |= removeGetterChain(onClose, call);
                }
            }
            instruction = next;
        }
        return changed ? write(node) : null;
    }

    private static boolean isOwnershipVisibilityReset(MethodInsnNode call) {
        if (call.getOpcode() != Opcodes.INVOKEVIRTUAL || !"()V".equals(call.desc)) {
            return false;
        }
        AbstractInsnNode value = previousReal(call);
        return value instanceof FieldInsnNode
                && value.getOpcode() == Opcodes.GETSTATIC
                && ((FieldInsnNode) value).owner.startsWith(
                        "com/lunarclient/websocket/cosmetic/v2/CosmeticOwnershipVisibility");
    }

    private static boolean removeTwoArgumentSetter(MethodNode method, MethodInsnNode call) {
        AbstractInsnNode value = previousReal(call);
        AbstractInsnNode receiver = previousReal(value);
        if (value == null || receiver == null || receiver.getOpcode() != Opcodes.ALOAD) {
            return false;
        }
        method.instructions.remove(receiver);
        method.instructions.remove(value);
        method.instructions.remove(call);
        return true;
    }

    private static boolean isCosmeticsStoreReset(ClassLoader loader, MethodInsnNode call) {
        if (call.getOpcode() != Opcodes.INVOKEVIRTUAL || !"()V".equals(call.desc)) {
            return false;
        }
        AbstractInsnNode getterNode = previousReal(call);
        if (!(getterNode instanceof MethodInsnNode)) {
            return false;
        }
        MethodInsnNode getter = (MethodInsnNode) getterNode;
        if (getter.getOpcode() != Opcodes.INVOKEVIRTUAL
                || !getter.desc.startsWith("()L")
                || !getter.desc.endsWith(";")) {
            return false;
        }
        String returnedClass = getter.desc.substring(3, getter.desc.length() - 1);
        byte[] target = readClass(loader, returnedClass);
        return target != null
                && CosmeticsAgent.containsAscii(target, "com/lunarclient/websocket/cosmetic/");
    }

    private static boolean removeGetterChain(MethodNode method, MethodInsnNode reset) {
        AbstractInsnNode getter = previousReal(reset);
        AbstractInsnNode field = previousReal(getter);
        AbstractInsnNode receiver = previousReal(field);
        if (!(getter instanceof MethodInsnNode)
                || !(field instanceof FieldInsnNode)
                || receiver == null
                || field.getOpcode() != Opcodes.GETFIELD
                || receiver.getOpcode() != Opcodes.ALOAD) {
            return false;
        }
        method.instructions.remove(receiver);
        method.instructions.remove(field);
        method.instructions.remove(getter);
        method.instructions.remove(reset);
        return true;
    }

    private static AbstractInsnNode previousReal(AbstractInsnNode instruction) {
        AbstractInsnNode current = instruction == null ? null : instruction.getPrevious();
        while (current != null && current.getOpcode() < 0) {
            current = current.getPrevious();
        }
        return current;
    }

    private static byte[] readClass(ClassLoader loader, String internalName) {
        if (loader == null) {
            return null;
        }
        try (InputStream input = loader.getResourceAsStream(internalName + ".class")) {
            if (input == null) {
                return null;
            }
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = input.read(buffer)) >= 0) {
                output.write(buffer, 0, count);
            }
            return output.toByteArray();
        } catch (Exception ignored) {
            return null;
        }
    }

    private static ClassNode read(byte[] classfileBuffer) {
        ClassNode node = new ClassNode();
        new ClassReader(classfileBuffer).accept(node, 0);
        return node;
    }

    private static byte[] write(ClassNode node) {
        ClassWriter writer = new ClassWriter(0);
        node.accept(writer);
        return writer.toByteArray();
    }

}
