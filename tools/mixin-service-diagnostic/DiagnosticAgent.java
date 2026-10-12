package dev.moonrise.diagnostic;

import java.lang.instrument.Instrumentation;
import java.net.URL;
import java.net.URLClassLoader;
import java.nio.file.Paths;

/** Dev-only entrypoint. ASM remains isolated from both the system and game loaders. */
public final class DiagnosticAgent {
    public static void premain(String options, Instrumentation instrumentation) throws Exception {
        String[] paths = options.split("\\|", -1);
        if (paths.length != 2) throw new IllegalArgumentException("Expected untouched Weave JAR|diagnostic log");
        URL self = DiagnosticAgent.class.getProtectionDomain().getCodeSource().getLocation();
        URLClassLoader isolated = new URLClassLoader("MoonriseMixinDiagnostic",
                new URL[] { self, Paths.get(paths[0]).toUri().toURL() }, ClassLoader.getPlatformClassLoader());
        isolated.loadClass("dev.moonrise.diagnostic.DiscoveryTrace")
                .getMethod("install", Instrumentation.class, String.class)
                .invoke(null, instrumentation, paths[1]);
    }
}
