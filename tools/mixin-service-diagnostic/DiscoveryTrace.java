package dev.moonrise.diagnostic;

import java.io.*;
import java.lang.instrument.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.ProtectionDomain;
import java.util.*;
import org.objectweb.asm.*;
import org.objectweb.asm.tree.*;

/** Observes definitions/resources and prints otherwise discarded discovery failures only. */
public final class DiscoveryTrace implements ClassFileTransformer {
    private static final String SERVICE = "org/spongepowered/asm/service/MixinService";
    private final String output;
    private final PrintStream log;
    private final Set<String> providers = new HashSet<>();

    private DiscoveryTrace(String output) throws IOException {
        this.output = output;
        this.log = new PrintStream(new FileOutputStream(output, true), true, "UTF-8");
    }

    public static void install(Instrumentation instrumentation, String output) throws IOException {
        DiscoveryTrace trace = new DiscoveryTrace(output);
        trace.log.println("Mixin discovery diagnostic installed; no selection/validation changes");
        instrumentation.addTransformer(trace);
    }

    private String loader(ClassLoader loader) {
        return loader == null ? "bootstrap" : loader.getClass().getName() + "@" +
                Integer.toHexString(System.identityHashCode(loader)) + " name=" + loader.getName();
    }

    private void resources(ClassLoader loader, String name) throws IOException {
        Enumeration<URL> urls = loader.getResources(name);
        int count = 0;
        while (urls.hasMoreElements()) {
            count++;
            URL url = urls.nextElement();
            log.println("RESOURCE " + name + " " + url);
            if (name.startsWith("META-INF/services/")) {
                try (BufferedReader reader = new BufferedReader(new InputStreamReader(url.openStream(), StandardCharsets.UTF_8))) {
                    String line;
                    while ((line = reader.readLine()) != null) {
                        String provider = line.split("#", 2)[0].trim();
                        if (!provider.isEmpty()) {
                            log.println("PROVIDER " + provider);
                            providers.add(provider.replace('.', '/'));
                        }
                    }
                }
            }
        }
        log.println("RESOURCE COUNT " + name + " " + count);
    }

    @Override public synchronized byte[] transform(ClassLoader loader, String name, Class<?> previous,
            ProtectionDomain domain, byte[] bytes) {
        if (name == null) return null;
        boolean service = SERVICE.equals(name);
        if (!(service || name.startsWith("org/spongepowered/asm/service/") ||
                name.startsWith("net/weavemc/loader/mixins/") ||
                name.equals("net/weavemc/loader/WeaveLoader") ||
                name.equals("net/weavemc/loader/mappings/XSrgMapper") ||
                name.equals("org/spongepowered/asm/launch/MixinBootstrap") || providers.contains(name))) return null;
        try {
            log.println("DEFINE " + name + " loader=" + loader(loader) + " source=" +
                    (domain == null || domain.getCodeSource() == null ? "null" : domain.getCodeSource().getLocation()));
            if (!service) return null;
            for (ClassLoader cursor = loader; cursor != null; cursor = cursor.getParent())
                log.println("CHAIN " + loader(cursor));
            log.println("TCCL " + loader(Thread.currentThread().getContextClassLoader()));
            resources(loader, "META-INF/services/org.spongepowered.asm.service.IMixinService");
            resources(loader, "META-INF/services/org.spongepowered.asm.service.IMixinServiceBootstrap");
            resources(loader, "META-INF/services/org.spongepowered.asm.service.IGlobalPropertyService");
            resources(loader, "org/spongepowered/asm/service/IMixinService.class");
            resources(loader, "net/weavemc/loader/mixins/WeaveMixinService.class");
            resources(loader, "lunar/lunar_named_b5_1.8.9.xsrg");

            ClassNode node = new ClassNode();
            new ClassReader(bytes).accept(node, 0);
            int count = 0;
            for (MethodNode method : node.methods) {
                if (!method.name.equals("initService")) continue;
                Set<LabelNode> seen = new HashSet<>();
                for (TryCatchBlockNode handler : method.tryCatchBlocks) {
                    if (!"java/util/ServiceConfigurationError".equals(handler.type) || !seen.add(handler.handler)) continue;
                    // Preserve the throwable on the operand stack and the original catch body.
                    // Use only java.base types so no diagnostic class must enter the game loader.
                    InsnList trace = new InsnList();
                    trace.add(new InsnNode(Opcodes.DUP));
                    trace.add(new TypeInsnNode(Opcodes.NEW, "java/io/PrintStream"));
                    trace.add(new InsnNode(Opcodes.DUP));
                    trace.add(new TypeInsnNode(Opcodes.NEW, "java/io/FileOutputStream"));
                    trace.add(new InsnNode(Opcodes.DUP));
                    trace.add(new LdcInsnNode(output));
                    trace.add(new InsnNode(Opcodes.ICONST_1));
                    trace.add(new MethodInsnNode(Opcodes.INVOKESPECIAL, "java/io/FileOutputStream", "<init>", "(Ljava/lang/String;Z)V", false));
                    trace.add(new MethodInsnNode(Opcodes.INVOKESPECIAL, "java/io/PrintStream", "<init>", "(Ljava/io/OutputStream;)V", false));
                    trace.add(new InsnNode(Opcodes.DUP));
                    trace.add(new VarInsnNode(Opcodes.ASTORE, method.maxLocals++));
                    int stream = method.maxLocals - 1;
                    trace.add(new MethodInsnNode(Opcodes.INVOKEVIRTUAL, "java/lang/Throwable", "printStackTrace", "(Ljava/io/PrintStream;)V", false));
                    trace.add(new VarInsnNode(Opcodes.ALOAD, stream));
                    trace.add(new MethodInsnNode(Opcodes.INVOKEVIRTUAL, "java/io/PrintStream", "close", "()V", false));
                    AbstractInsnNode insertion = handler.handler;
                    while (insertion.getNext() instanceof FrameNode || insertion.getNext() instanceof LineNumberNode)
                        insertion = insertion.getNext();
                    method.instructions.insert(insertion, trace);
                    count++;
                }
            }
            if (count != 1) throw new IllegalStateException("Expected one discovery catch handler; got " + count);
            // Existing frames are retained; the temporary local is dead at all original frame boundaries.
            ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
            node.accept(writer);
            log.println("Instrumented initService catch handlers=" + count);
            return writer.toByteArray();
        } catch (Throwable error) {
            log.println("DIAGNOSTIC FAILURE");
            error.printStackTrace(log);
            return null;
        }
    }
}
