package dev.moonrise.diagnostic;

import java.io.*;
import java.lang.instrument.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.ProtectionDomain;
import java.util.*;

/** Read-only diagnostic: never generates, defines, retransforms or repairs a class. */
public final class LinkageTrace implements ClassFileTransformer {
    private final Instrumentation instrumentation;
    private final PrintStream log;
    private int lines;
    private LinkageTrace(Instrumentation instrumentation, PrintStream log) {
        this.instrumentation = instrumentation; this.log = log;
    }
    public static void premain(String output, Instrumentation instrumentation) throws Exception {
        var stream = new PrintStream(Files.newOutputStream(Path.of(output), StandardOpenOption.CREATE_NEW), true, StandardCharsets.UTF_8);
        var trace = new LinkageTrace(instrumentation, stream);
        trace.write("installed pid=" + ProcessHandle.current().pid() + " observational-only");
        instrumentation.addTransformer(trace);
    }
    private synchronized void write(String text) {
        if (lines++ < 600) log.println(java.time.Instant.now() + " " + text);
    }
    static Object field(Object object, String name) throws ReflectiveOperationException {
        var type = object instanceof Class<?> c ? c : object.getClass();
        var field = type.getDeclaredField(name); field.setAccessible(true);
        return field.get(object instanceof Class<?> ? null : object);
    }
    static String identity(ClassLoader loader) {
        return loader == null ? "bootstrap" : loader.getClass().getName() + "@" + Integer.toHexString(System.identityHashCode(loader));
    }
    // Called only once game startup has reached OptiFine; don't initialize Weave early.
    private void snapshot() throws ReflectiveOperationException {
        for (var type : instrumentation.getAllLoadedClasses()) {
            if (!type.getName().equals("net.weavemc.loader.impl.WeaveLoader")) continue;
            var instance = field(type, "INSTANCE");
            if (instance == null) { write("WeaveLoader INSTANCE absent"); continue; }
            var sandboxes = (Map<?, ?>)field(instance, "mixinInstances");
            for (var entry : sandboxes.entrySet()) {
                var sandbox = entry.getValue();
                var state = field(sandbox, "state");
                var transformer = field(state, "transformer");
                var registry = field(transformer, "syntheticClassRegistry");
                write("sandbox=" + entry.getKey() + " loader=" + identity((ClassLoader)sandbox)
                    + " synthetic-registry=" + ((Map<?, ?>)field(registry, "classes")).keySet()
                    + " injected-resources=" + ((Map<?, ?>)field(sandbox, "injectedResources")).keySet());
            }
        }
    }
    public byte[] transform(ClassLoader loader, String name, Class<?> redefining, ProtectionDomain domain, byte[] bytes) {
        if (name == null) return null;
        try {
            if (name.contains("/synthetic/") || name.equals("net/optifine/reflect/Reflector")
                || name.equals("net/minecraft/client/settings/GameSettings") || name.equals("net/minecraft/client/main/Main")) {
                write("definition=" + name + " loader=" + identity(loader) + " parent=" + identity(loader == null ? null : loader.getParent()));
                // Constant-pool strings only, no command lines, system properties or account data.
                var raw = new String(bytes, StandardCharsets.ISO_8859_1);
                var matcher = java.util.regex.Pattern.compile("(?:net/weavemc/loader/impl/shaded/spongepowered|org/spongepowered)/asm/synthetic/[A-Za-z0-9_/$]+").matcher(raw);
                var refs = new TreeSet<String>(); while (matcher.find()) refs.add(matcher.group());
                write("incoming-synthetic-references=" + refs);
                if (name.equals("net/optifine/reflect/Reflector")) snapshot();
            }
        } catch (ReflectiveOperationException | RuntimeException failure) {
            write("snapshot-unavailable=" + failure.getClass().getName());
        }
        return null;
    }
}
