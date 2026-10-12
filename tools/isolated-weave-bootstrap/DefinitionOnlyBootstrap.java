import java.lang.instrument.*;
import java.lang.reflect.*;
import java.nio.file.*;
import java.security.Permission;
import java.io.*;
import java.net.*;
import java.util.*;
import net.weavemc.loader.impl.bootstrap.Bootstrap;
import net.weavemc.loader.impl.bootstrap.transformer.URLClassLoaderTransformer;

/** Test-only genuine bootstrap; no game or mod lifecycle invocation. */
public final class DefinitionOnlyBootstrap {
    static Instrumentation real;
    static ClassFileTransformer original;
    static ClassLoader expected;
    static ClassLoader callbackOwner;
    static byte[] mainInput;
    static boolean inputEqual;
    static int registrations, removals, callbacks;
    public static void premain(String args, Instrumentation inst) { real = inst; }
    @SuppressWarnings("removal")
    public static void main(String[] args) throws Exception {
        bootstrap(args, List.of());
    }
    @SuppressWarnings("removal")
    static void bootstrap(String[] args, List<File> mods) throws Exception {
        Path root = Path.of(args[0]).toRealPath();
        installGuard(root);
        net.weavemc.loader.impl.util.UtilityKt.setGameInfo();
        if (real == null || Bootstrap.INSTANCE.getMinecraftBootstrapClassLoader() != null)
            throw new AssertionError("Initial bootstrap state");
        Instrumentation observed = (Instrumentation) java.lang.reflect.Proxy.newProxyInstance(
            Instrumentation.class.getClassLoader(), new Class<?>[]{Instrumentation.class}, (proxy, method, values) -> {
                try {
                    Object result = method.invoke(real, values);
                    if (method.getName().equals("addTransformer") && values[0].getClass().getName().equals(
                            "net.weavemc.loader.impl.bootstrap.Bootstrap$bootstrap$1")) {
                        original = (ClassFileTransformer) values[0]; registrations++;
                        System.out.println("REGISTER genuine bootstrap transformer");
                    }
                    if (method.getName().equals("removeTransformer") && values[0] == original) {
                        if (!Boolean.TRUE.equals(result) || Bootstrap.INSTANCE.getMinecraftBootstrapClassLoader() != expected ||
                            !StackWalker.getInstance().walk(s -> s.anyMatch(f -> f.getClassName().equals(
                                "net.weavemc.loader.impl.bootstrap.Bootstrap$bootstrap$1") && f.getMethodName().equals("transform"))))
                            throw new AssertionError("Original callback sequence");
                        removals++;
                        System.out.println("ORIGINAL transform stack; removal=true; field identity=true");
                    }
                    return result;
                } catch (InvocationTargetException e) { throw e.getCause(); }
            });
        real.addTransformer(URLClassLoaderTransformer.INSTANCE);
        Bootstrap.INSTANCE.bootstrap(observed, mods);
        Class<?> wrapping = Class.forName("net.weavemc.loader.impl.bootstrap.transformer.ApplicationWrapper$WrappingLoader",
            false, DefinitionOnlyBootstrap.class.getClassLoader());
        expected = (ClassLoader) wrapping.getConstructor().newInstance();
        if (!(expected instanceof net.weavemc.loader.impl.bootstrap.transformer.URLClassLoaderAccessor))
            throw new AssertionError("Original URL transformer missing");
        try (var jar = new java.util.jar.JarFile(root.resolve("inputs/game.jar").toFile());
             var stream = jar.getInputStream(jar.getJarEntry("net/minecraft/client/main/Main.class"))) {
            mainInput = stream.readAllBytes();
        }
        real.addTransformer(new ClassFileTransformer() {
            public byte[] transform(ClassLoader loader, String name, Class<?> type,
                    java.security.ProtectionDomain domain, byte[] bytes) {
                if (name.equals("net/minecraft/client/main/Main")) {
                    callbacks++;
                    callbackOwner = loader;
                    inputEqual = Arrays.equals(bytes, mainInput);
                    System.out.println("MAIN callback owner=" + loader);
                }
                return null;
            }
        });
        Class<?> main = Class.forName("net.minecraft.client.main.Main", false, expected);
        if (main.getClassLoader() != expected || Class.forName(main.getName(), false, expected) != main ||
            Bootstrap.INSTANCE.getMinecraftBootstrapClassLoader() != expected ||
            callbackOwner != expected || !inputEqual || registrations != 1 || removals != 1 || callbacks != 1)
            throw new AssertionError("Definition identity/counts");
        System.out.println((mods.isEmpty() ? "PASS definition-only bootstrap; Main owner=" : "PASS offline lifecycle bootstrap; Main owner=") + main.getClassLoader() +
            "; field identity=true; original Main bytes=true; registrations=1; original callback removals=1; Main callbacks=1");
        System.out.println("PASS pinned sole-writer audit + single original callback => one normal assignment");
    }
    @SuppressWarnings("removal")
    static void installGuard(Path root) throws Exception {
        System.setSecurityManager(new SecurityManager() {
            public void checkPermission(Permission p) {
                if (p.getName().equals("setSecurityManager")) throw new SecurityException("Guard removal denied");
                if (p instanceof java.io.FilePermission && (p.getActions().contains("write") || p.getActions().contains("delete"))) checkWrite(p.getName());
            }
            public void checkWrite(String file) {
                try {
                    Path path = Path.of(file).toAbsolutePath().normalize(), ancestor = path;
                    while (!Files.exists(ancestor)) ancestor = ancestor.getParent();
                    if (!path.startsWith(root) || !ancestor.toRealPath().startsWith(root)) throw new SecurityException("Outside-root write denied");
                } catch (IOException e) { throw new SecurityException(e); }
            }
            public void checkDelete(String file) { checkWrite(file); }
            public void checkWrite(FileDescriptor fd) {
                if (fd != FileDescriptor.out && fd != FileDescriptor.err) throw new SecurityException("Descriptor write denied");
            }
            public void checkConnect(String host, int port) { throw new SecurityException("Offline network denied"); }
            public void checkListen(int port) { throw new SecurityException("Offline listen denied"); }
            public void checkExec(String command) { throw new SecurityException("Child process denied"); }
            public void checkLink(String library) { throw new SecurityException("Native load denied"); }
        });
        for (String property : List.of("user.home", "user.dir", "java.io.tmpdir", "weave.repo.local.path", "weave.vanilla.jar.path"))
            if (!Path.of(System.getProperty(property)).toRealPath().startsWith(root)) throw new AssertionError("Unisolated property");
        try { Files.writeString(root.getParent().resolve("forbidden-probe"), "no"); throw new AssertionError("Write guard"); }
        catch (SecurityException expected) { System.out.println("PASS outside-root write rejected"); }
        try { System.getSecurityManager().checkConnect("127.0.0.1", 9); throw new AssertionError("Network guard"); }
        catch (SecurityException expected) { System.out.println("PASS network connection rejected"); }
    }
}
