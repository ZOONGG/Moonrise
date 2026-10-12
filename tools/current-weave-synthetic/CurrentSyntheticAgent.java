package dev.moonrise.current;
import java.lang.instrument.*;
import java.net.*;
import java.nio.file.*;
public final class CurrentSyntheticAgent {
    public static void premain(String loader, Instrumentation instrumentation) throws Exception {
        var self = CurrentSyntheticAgent.class.getProtectionDomain().getCodeSource().getLocation();
        var isolated = new URLClassLoader("MoonriseCurrentSynthetic", new URL[]{self, Path.of(loader).toUri().toURL()}, ClassLoader.getPlatformClassLoader());
        isolated.loadClass("dev.moonrise.current.SyntheticLoaderAdapter")
            .getMethod("install", Instrumentation.class, URL.class).invoke(null, instrumentation, self);
    }
}
