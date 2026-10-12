package dev.moonrise.contract;

import java.lang.instrument.Instrumentation;
import java.net.URL;
import java.net.URLClassLoader;
import java.nio.file.Path;

/** Development only: isolated ASM/Weave, no returned transformed class bytes. */
public final class GenesisContractAgent {
    public static void premain(String options, Instrumentation inst) throws Exception {
        String[] p = options.split("\\|", -1);
        if (p.length != 3) throw new IllegalArgumentException("Weave JAR|evidence directory|marker JAR");
        URL self = GenesisContractAgent.class.getProtectionDomain().getCodeSource().getLocation();
        URLClassLoader isolated = new URLClassLoader("MoonriseGenesisContract",
            new URL[]{self, Path.of(p[0]).toUri().toURL()}, ClassLoader.getPlatformClassLoader());
        isolated.loadClass("dev.moonrise.contract.GenesisContractTrace")
            .getMethod("install", Instrumentation.class, String.class, String.class)
            .invoke(null, inst, p[1], p[2]);
    }
}
