import java.io.*;
import java.net.*;
import java.nio.file.*;
import java.security.Permission;
import java.util.*;
import java.util.jar.*;
import net.weavemc.loader.impl.WeaveLoader;
import net.weavemc.loader.impl.bootstrap.transformer.URLClassLoaderAccessor;

/** Runs the unchanged constructor; any bootstrap failure is a BLOCKED result. */
public final class OfflineBootstrap {
    static final class Owner extends URLClassLoader implements URLClassLoaderAccessor {
        Owner() { super(new URL[0], OfflineBootstrap.class.getClassLoader()); }
        public ClassLoader getWeaveBacking() { return this; }
        public void addWeaveURL(URL url) {
            OfflineMixinBytes.auditArchive(url);
            addURL(url); System.out.println("REGISTER URL " + url);
        }
    }
    @SuppressWarnings({"removal", "unchecked"})
    public static void main(String[] args) throws Exception {
        Path root = Path.of(args[0]).toRealPath();
        Path raven = Path.of(args[1]), bwh = Path.of(args[2]);
        System.setProperty("moonrise.proof.raven", raven.toString());
        System.setSecurityManager(new SecurityManager() {
            public void checkPermission(Permission permission) {
                if (permission.getName().equals("setSecurityManager")) throw new SecurityException("Guard removal denied");
                if (permission instanceof java.io.FilePermission &&
                    (permission.getActions().contains("write") || permission.getActions().contains("delete")))
                    checkWrite(permission.getName());
            }
            public void checkWrite(String file) {
                try {
                    Path path = Path.of(file).toAbsolutePath().normalize();
                    Path ancestor = path;
                    while (!Files.exists(ancestor)) ancestor = ancestor.getParent();
                    if (!path.startsWith(root) || !ancestor.toRealPath().startsWith(root))
                        throw new SecurityException("Write outside isolated root denied: " + path);
                } catch (IOException e) { throw new SecurityException(e); }
            }
            public void checkDelete(String file) { checkWrite(file); }
            public void checkWrite(FileDescriptor descriptor) {
                if (descriptor != FileDescriptor.out && descriptor != FileDescriptor.err)
                    throw new SecurityException("Unbounded descriptor write denied");
            }
            public void checkConnect(String host, int port) { throw new SecurityException("Offline network denied"); }
            public void checkListen(int port) { throw new SecurityException("Offline listen denied"); }
            public void checkExec(String command) { throw new SecurityException("Child process denied"); }
        });
        for (String property : List.of("user.home", "user.dir", "java.io.tmpdir", "weave.repo.local.path", "weave.vanilla.jar.path"))
            if (!Path.of(System.getProperty(property)).toAbsolutePath().normalize().startsWith(root) ||
                (Files.exists(Path.of(System.getProperty(property))) &&
                 !Path.of(System.getProperty(property)).toRealPath().startsWith(root)))
                throw new AssertionError("Unisolated property " + property);
        try { Files.writeString(root.getParent().resolve("forbidden-probe"), "no");
            throw new AssertionError("Guard failed");
        } catch (SecurityException expected) { System.out.println("PASS outside-root write rejected"); }
        try { new Socket("127.0.0.1", 9); throw new AssertionError("Network guard failed"); }
        catch (SecurityException expected) { System.out.println("PASS network connection rejected before connect"); }
        Files.writeString(root.resolve("logs/guard-local-write.txt"), "Isolated write allowed\n");
        if (OfflineBootstrapAgent.instrumentation == null) throw new AssertionError("No Instrumentation");
        System.out.println("PASS real Instrumentation=" + OfflineBootstrapAgent.instrumentation.getClass().getName());
        var field = MetadataReadInterception.class.getDeclaredField("legacyInputs");
        field.setAccessible(true);
        try (var jar = new JarFile(raven.toFile())) {
            ((Map<Path, byte[]>)field.get(null)).put(raven.toRealPath(),
                jar.getInputStream(jar.getJarEntry("weave.mod.json")).readAllBytes());
        }
        Class<?> utility = Class.forName("net.weavemc.loader.impl.util.UtilityKt");
        if (!OfflineBootstrapAgent.intercepted || utility.getClassLoader() != WeaveLoader.class.getClassLoader())
            throw new AssertionError("Actual loader interception not installed");
        net.weavemc.loader.impl.util.UtilityKt.setGameInfo();
        Path selectedVanilla = net.weavemc.loader.impl.util.FileManager.INSTANCE.getVANILLA_MINECRAFT_JAR().toPath().toRealPath();
        if (!selectedVanilla.equals(root.resolve("inputs/vanilla.jar").toRealPath()))
            throw new AssertionError("Loader selected unexpected vanilla input");
        System.out.println("PASS actual FileManager vanilla input=" + selectedVanilla);
        Path mappingCache = net.weavemc.internals.MappingsRetrieval.INSTANCE.mappingsCache(
            net.weavemc.internals.MappingsType.MERGED, "1.8.9").toRealPath();
        if (!mappingCache.equals(root.resolve("home/.weave/.cache/mappings/merged_1.8.9/mappings.tiny").toRealPath()))
            throw new AssertionError("Unexpected merged mapping lookup");
        var mappings = net.weavemc.internals.MappingsRetrieval.INSTANCE
            .loadMergedWeaveMappings("1.8.9", selectedVanilla.toFile()).getMappings();
        if (!mappings.getNamespaces().equals(List.of("official", "mcp-srg", "mcp-named")) || mappings.getClasses().isEmpty())
            throw new AssertionError("Incompatible merged mappings");
        System.out.println("PASS actual loader merged cache=" + mappingCache + "; namespaces=" +
            mappings.getNamespaces() + "; classes=" + mappings.getClasses().size());
        OfflineMixinBytes.initialize(raven, bwh);
        OfflineMixinBytes.initializeGame(root.resolve("inputs/game.jar"));
        Class.forName("net.weavemc.loader.impl.mixin.SandboxedMixinLoader");
        if (!OfflineMixinBytes.installed) throw new AssertionError("Sandbox byte boundary not installed");
        System.out.println("CONSTRUCTOR begin; original validation/lifecycle retained");
        try (var owner = new Owner()) {
            verifyGuava(root, owner);
            OfflineLegacyCommand.verify(owner);
            // SafeTransformer exits the JVM on the unchanged BWH blocker, bypassing finally.
            // Read the real registry during shutdown; never replay Raven preInit/register.
            if (!Boolean.getBoolean("moonrise.proof.traceMixinBytesOnly"))
                Runtime.getRuntime().addShutdownHook(new Thread(() -> {
                    try { OfflineLegacyCommand.verifyRegistration(); }
                    catch (Throwable error) { error.printStackTrace(System.out); }
                }, "offline-command-registry-evidence"));
            new WeaveLoader(owner, OfflineBootstrapAgent.instrumentation, List.of(raven.toFile(), bwh.toFile()));
            System.out.println("Constructor returned; offline discovery still not certified");
        } catch (Throwable error) {
            System.out.println("BLOCKED first constructor failure");
            error.printStackTrace(System.out);
        } finally {
            for (String name : List.of("adaptedReads", "originalReads")) {
                var counter = MetadataReadInterception.class.getDeclaredField(name); counter.setAccessible(true);
                System.out.println("METADATA " + name + "=" + counter.getInt(null));
            }
            try { Files.writeString(root.getParent().resolve("forbidden-post-bootstrap"), "no");
                throw new AssertionError("Post-bootstrap guard failed");
            } catch (SecurityException expected) { System.out.println("PASS post-bootstrap outside-root write rejected"); }
            try { new Socket("127.0.0.1", 9); throw new AssertionError("Post-bootstrap network guard failed"); }
            catch (SecurityException expected) { System.out.println("PASS post-bootstrap network connection rejected before connect"); }
        }
        System.exit(2); // Constructor return alone cannot certify discovery/game lifecycle.
    }
    static void verifyGuava(Path root, ClassLoader backing) throws Exception {
        Set<String> definitions = new HashSet<>();
        try (var jar = new JarFile(root.resolve("inputs/guava.jar").toFile())) {
            var entries = jar.entries();
            while (entries.hasMoreElements()) {
                String name = entries.nextElement().getName();
                if (name.endsWith(".class") && !definitions.add(name))
                    throw new AssertionError("Duplicate Guava archive entry: " + name);
            }
        }
        // Include future appended mod/API sources as well as current JVM search entries.
        try (var paths = Files.walk(root.resolve("inputs"))) {
            for (Path path : paths.filter(p -> p.toString().endsWith(".jar")).toList()) {
                if (path.getFileName().toString().equals("guava.jar")) continue;
                try (var jar = new JarFile(path.toFile())) {
                    for (String name : definitions) if (jar.getJarEntry(name) != null)
                        throw new AssertionError("Conflicting Guava definition: " + path + "!/" + name);
                }
            }
        }
        ClassLoader actual = OfflineBootstrap.class.getClassLoader();
        for (String name : definitions) {
            var resources = Collections.list(actual.getResources(name));
            if (resources.size() != 1 || !resources.get(0).toString().contains("/inputs/guava.jar!/"))
                throw new AssertionError("Guava resource identity: " + name + " " + resources);
        }
        Class<?> maps = Class.forName("com.google.common.collect.Maps", false, actual);
        if (maps.getClassLoader() != actual || Class.forName(maps.getName(), false, backing) != maps ||
            Class.forName(maps.getName(), false, actual) != maps)
            throw new AssertionError("Guava Maps split identity");
        System.out.println("PASS Guava unique definitions; Maps single/repeated identity; owner=" +
            maps.getClassLoader() + "; classes=" + definitions.size() + "; source=" +
            maps.getProtectionDomain().getCodeSource().getLocation());
        String bus = "net.weavemc.loader.api.command.CommandBus";
        System.out.println("LEGACY CommandBus resources=" + Collections.list(actual.getResources(bus.replace('.', '/') + ".class")));
        try {
            Class<?> type = Class.forName(bus, false, actual);
            System.out.println("LEGACY CommandBus available; owner=" + type.getClassLoader() + "; register=" +
                type.getDeclaredMethod("register", Class.forName("net.weavemc.loader.api.command.Command", false, actual)));
        } catch (ClassNotFoundException missing) {
            System.out.println("LEGACY CommandBus ABI unavailable: " + missing);
        }
    }
}
