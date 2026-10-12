import java.nio.file.*;
import java.util.*;
import net.weavemc.loader.impl.bootstrap.Bootstrap;

/** Test-only BWH lifecycle through original Bootstrap; no Main.main/game loop. */
public final class BwhLifecycleBootstrap {
    public static void main(String[] args) throws Exception {
        Path root = Path.of(args[0]).toRealPath();
        try {
            DefinitionOnlyBootstrap.bootstrap(args, List.of(root.resolve("inputs/bwh.jar").toFile()));
            ClassLoader owner = Bootstrap.INSTANCE.getMinecraftBootstrapClassLoader();
            ApiDefinitionOnlyBootstrap.require(owner == DefinitionOnlyBootstrap.expected,
                "BWH genuine bootstrap owner");
            Class<?> loaderType = Class.forName("net.weavemc.loader.impl.WeaveLoader", false, owner);
            Object loader = ApiDefinitionOnlyBootstrap.invoke(loaderType.getMethod("getInstance"), null);
            ApiDefinitionOnlyBootstrap.require(loader.getClass() == loaderType &&
                loaderType.getClassLoader() == owner, "BWH real loader singleton identity");
            Class<?> entry = Class.forName("lunarweavebridge.compat.BwhPreInitBridge", false, owner);
            var started = entry.getDeclaredField("started");
            started.setAccessible(true);
            ApiDefinitionOnlyBootstrap.require(started.getBoolean(null),
                "original BWH complete preInit and remaining bridge initializers returned");
            var mods = (List<?>) ApiDefinitionOnlyBootstrap.field(loader, "mods");
            Set<String> ids = new HashSet<>();
            for (Object mod : mods) ids.add((String) ApiDefinitionOnlyBootstrap.get(mod, "getModId"));
            ApiDefinitionOnlyBootstrap.require(ids.size() == mods.size() &&
                ids.equals(Set.of("minecraft", "java", "net.weavemc.api.v1_8", "bwhelper")),
                "actual BWH-only registry");
            System.out.println("BWH_LIFECYCLE_READY original bridge started=true; full preInit returned; genuine bootstrap; BWH-only");
        } finally {
            // Verify unchanged guards after lifecycle, including a failed lifecycle.
            var guard = System.getSecurityManager();
            if (guard == null) throw new AssertionError("Missing lifecycle guard");
            try { guard.checkConnect("127.0.0.1", 9); throw new AssertionError("Network guard"); }
            catch (SecurityException expected) { System.out.println("PASS post-lifecycle network rejected"); }
            try { guard.checkLink("offline-forbidden-native"); throw new AssertionError("Native guard"); }
            catch (SecurityException expected) { System.out.println("PASS post-lifecycle native rejected"); }
            try { guard.checkExec("offline-forbidden-process"); throw new AssertionError("Process guard"); }
            catch (SecurityException expected) { System.out.println("PASS post-lifecycle process rejected"); }
            try { guard.checkWrite(root.getParent().resolve("forbidden-bwh-write").toString()); throw new AssertionError("Write guard"); }
            catch (SecurityException expected) { System.out.println("PASS post-lifecycle outside-root write rejected"); }
        }
        // Mod executor threads are confined to this disposable JVM; do not invoke
        // game shutdown or mutate executor/loader state to make lifecycle pass.
        System.exit(0);
    }
}
