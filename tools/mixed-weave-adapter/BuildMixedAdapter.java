package dev.moonrise.mixed;

import java.nio.file.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.jar.*;
import net.weavemc.loader.impl.shaded.asm.*;
import net.weavemc.loader.impl.shaded.asm.commons.*;

/** Build-time extraction of legacy ABI only; no legacy runtime classes. */
public final class BuildMixedAdapter {
    public static void main(String[] args) throws Exception {
        Path legacy = Path.of(args[0]), raven = Path.of(args[1]), output = Path.of(args[2]);
        check(legacy, "c4200d145c0d3d78f57eab4ddee3a64ff1a3176e65f2e2b15a31777a0142b524");
        check(raven, MixedRuntime.RAVEN);
        try (var jar = new JarFile(legacy.toFile())) {
            for (var entry : Collections.list(jar.entries())) {
                String name = entry.getName();
                if (!name.endsWith(".class") || !(name.startsWith("net/weavemc/loader/api/event/") || name.startsWith("net/weavemc/loader/api/command/"))) continue;
                if (name.startsWith("net/weavemc/loader/api/event/EventBus")) continue;
                var writer = new ClassWriter(0);
                var remapper = new Remapper(Opcodes.ASM9) {
                    public String map(String type) {
                        if (name.equals("net/weavemc/loader/api/event/Event.class") && type.equals("java/lang/Object")) return "net/weavemc/api/event/Event";
                        return type.startsWith("kotlin/") ? "net/weavemc/loader/impl/shaded/" + type : type;
                    }
                };
                try (var input = jar.getInputStream(entry)) { new ClassReader(input.readAllBytes()).accept(new ClassRemapper(writer, remapper), 0); }
                Path target = output.resolve(name); Files.createDirectories(target.getParent()); Files.write(target, writer.toByteArray());
            }
        }
        Path resources = output.resolve("moonrise-mixed"); Files.createDirectories(resources);
        try (var jar = new JarFile(raven.toFile()); var input = jar.getInputStream(jar.getJarEntry("weave.mod.json"))) {
            Files.write(resources.resolve("raven-original.json"), input.readAllBytes());
        }
        Files.writeString(resources.resolve("raven-current.json"), "{\"name\":\"RavenWeave\",\"modId\":\"ravenweave\",\"namespace\":\"mcp-named\",\"compiledFor\":\"1.8.9\",\"entryPoints\":[\"me.pianopenguin471.RavenWeave\"],\"mixinConfigs\":[\"ravenweave.mixins.json\"],\"hooks\":[],\"tweakers\":[],\"accessWideners\":[],\"dependencies\":[]}");
        Files.writeString(output.resolve("weave.mod.json"), "{\"name\":\"Moonrise Experimental Raven ABI\",\"modId\":\"moonrise-mixed-raven-abi\",\"namespace\":\"mcp-named\",\"compiledFor\":\"1.8.9\",\"entryPoints\":[],\"mixinConfigs\":[],\"hooks\":[],\"tweakers\":[],\"accessWideners\":[],\"dependencies\":[]}");
    }
    static void check(Path path, String expected) throws Exception {
        if (!HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(Files.readAllBytes(path))).equals(expected))
            throw new IllegalStateException("Unrecognized build input: " + path.getFileName());
    }
}
