import java.net.*;
import java.nio.file.*;
import java.security.*;
import java.util.*;
import java.util.jar.*;
import net.weavemc.loader.impl.shaded.asm.*;
import net.weavemc.loader.impl.shaded.asm.commons.*;

/** Does not start either loader, Minecraft, or Raven.init(). */
public final class MixedWeaveProof {
    static String hash(Path path) throws Exception {
        return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(Files.readAllBytes(path)));
    }
    static class ApiLoader extends ClassLoader implements AutoCloseable {
        final JarFile legacy, raven, api;
        final Path facade;
        ApiLoader(Path old, Path mod, Path facade, Path api) throws Exception {
            super(MixedWeaveProof.class.getClassLoader());
            this.legacy = new JarFile(old.toFile()); this.raven = new JarFile(mod.toFile()); this.facade = facade;
            this.api = new JarFile(api.toFile());
        }
        byte[] classBytes(String name) throws ClassNotFoundException {
            String entry = name.replace('.', '/') + ".class";
            try {
                byte[] bytes;
                if (name.equals("net.weavemc.loader.api.ModInitializer") || (name.equals("net.weavemc.loader.api.event.EventBus") || name.startsWith("net.weavemc.loader.api.event.EventBus$")) || name.equals("net.minecraft.util.EnumChatFormatting") || name.startsWith("fixture.") || name.equals("org.lwjgl.input.Keyboard") || name.equals("net.minecraft.util.IChatComponent") || name.equals("net.minecraft.client.Minecraft"))
                    bytes = Files.readAllBytes(facade.resolve(entry));
                else {
                    JarFile source = name.startsWith("net.weavemc.loader.api.") ? legacy : name.startsWith("net.weavemc.api.") ? api : raven;
                    var found = source.getJarEntry(entry);
                    if (found == null) throw new ClassNotFoundException(name);
                    bytes = source.getInputStream(found).readAllBytes();
                    if (source == legacy || source == api) {
                        var writer = new ClassWriter(0);
                        var mapper = new Remapper(Opcodes.ASM9) {
                            @Override public String map(String type) {
                                if (name.equals("net.weavemc.loader.api.event.Event") && type.equals("java/lang/Object"))
                                    return "net/weavemc/api/event/Event";
                                if (type.startsWith("kotlin/")) return "net/weavemc/loader/impl/shaded/" + type;
                                return type;
                            }
                        };
                        new ClassReader(bytes).accept(new ClassRemapper(writer, mapper), 0);
                        bytes = writer.toByteArray();
                    }
                }
                return bytes;
            } catch (java.io.IOException error) { throw new ClassNotFoundException(name, error); }
        }
        @Override protected Class<?> findClass(String name) throws ClassNotFoundException {
            byte[] bytes = classBytes(name);
            return defineClass(name, bytes, 0, bytes.length);
        }
        @Override public java.io.InputStream getResourceAsStream(String name) {
            if (name.endsWith(".class")) {
                try { return new java.io.ByteArrayInputStream(classBytes(name.substring(0, name.length()-6).replace('/', '.'))); }
                catch (ClassNotFoundException ignored) { }
            }
            return super.getResourceAsStream(name);
        }
        Class<?> defineTarget(byte[] bytes) { return defineClass("net.minecraft.client.Minecraft", bytes, 0, bytes.length); }
        public void close() throws Exception { legacy.close(); raven.close(); api.close(); }
    }
    public static void main(String[] args) throws Exception {
        Path current = Path.of(args[0]), old = Path.of(args[1]), mod = Path.of(args[2]), facade = Path.of(args[3]);
        Path bwh = Path.of(args[4]);
        Path api = Path.of(args[5]);
        if (!hash(api).equals("6c1bc99be8b94de44af64d2e1ab08cacd2e1c5d8f2423c113041f02d3228d11a"))
            throw new IllegalStateException("Local Minecraft API SHA mismatch");
        if (!hash(current).equals("013b7b9a4ca01a473a1ffc0b031c843c70fe61616e9048fb326e03794c8457b7") ||
            !hash(old).equals("c4200d145c0d3d78f57eab4ddee3a64ff1a3176e65f2e2b15a31777a0142b524") ||
            !hash(mod).equals("c6edd2979121df4f7c2585abee8da0b84e3b7b5496b0b1a4a684edad179727a1"))
            throw new IllegalStateException("Unrecognised input SHA");
        if (!hash(bwh).equals("6da5b2a67e05e07deae2779b4aa3d1b70e5028ded227c41487ff783f5d609a57"))
            throw new IllegalStateException("Maintained BWH SHA mismatch");
        String before = hash(mod);
        String bwhBefore = hash(bwh);
        var references = new TreeSet<String>();
        var apiMembers = new TreeSet<String>();
        try (var jar = new JarFile(mod.toFile())) {
            int count = 0;
            for (var entry : Collections.list(jar.entries())) {
                if (!entry.getName().endsWith(".class")) continue;
                count++;
                new ClassReader(jar.getInputStream(entry).readAllBytes()).accept(new ClassRemapper(new ClassWriter(0), new Remapper(Opcodes.ASM9) {
                    @Override public String map(String name) { references.add(name); return name; }
                    @Override public String mapMethodName(String owner, String name, String desc) {
                        if (owner.startsWith("net/weavemc/")) apiMembers.add(owner + "." + name + desc);
                        return name;
                    }
                }), 0);
            }
            System.out.println("AUDIT original classes=" + count);
        }
        references.stream().filter(n -> n.startsWith("net/weavemc/")).forEach(n -> System.out.println("API " + n));
        references.stream().filter(n -> n.startsWith("org/") || n.startsWith("com/") || n.startsWith("io/") || n.startsWith("kotlin/") || n.startsWith("net/minecraftforge/"))
            .map(n -> n.substring(0, n.lastIndexOf('/'))).distinct().forEach(n -> System.out.println("DEPENDENCY-PACKAGE " + n));
        apiMembers.forEach(n -> System.out.println("MEMBER " + n));
        try (var loader = new ApiLoader(old, mod, facade, api)) {
            Object raven = loader.loadClass("me.pianopenguin471.RavenWeave").getConstructor().newInstance();
            if (!(raven instanceof net.weavemc.api.ModInitializer)) throw new AssertionError("Current ABI missing");
            ((net.weavemc.api.ModInitializer) raven).preInit(null);
            var commands = loader.loadClass("net.weavemc.loader.api.command.CommandBus").getDeclaredField("commands");
            commands.setAccessible(true);
            if (((List<?>) commands.get(null)).size() != 1) throw new AssertionError("Raven command not registered exactly once");
            System.out.println("PASS original Raven current-ABI preInit; GUICommand registered once");
            var bus = loader.loadClass("net.weavemc.loader.api.event.EventBus");
            var event = loader.loadClass("net.weavemc.loader.api.event.ChatSentEvent").getConstructor(String.class).newInstance("ordinary message");
            int[] calls = {0};
            java.util.function.Consumer<net.weavemc.api.event.Event> listener = e -> calls[0]++;
            net.weavemc.api.event.EventBus.subscribe(net.weavemc.api.event.Event.class, listener);
            var eventBase = loader.loadClass("net.weavemc.loader.api.event.Event");
            bus.getMethod("callEvent", eventBase).invoke(null, event);
            if (calls[0] != 1) throw new AssertionError("Legacy event did not traverse current bus exactly once");
            net.weavemc.api.event.EventBus.unsubscribe(listener);
            bus.getMethod("callEvent", eventBase).invoke(null, event);
            if (calls[0] != 1) throw new AssertionError("Unsubscribe failed");
            Object probe = loader.loadClass("fixture.ProbeListener").getConstructor().newInstance();
            bus.getMethod("subscribe", Object.class).invoke(null, probe);
            probe.getClass().getMethod("registerCommand").invoke(probe);
            Object chat = event.getClass().getConstructor(String.class).newInstance("/moonriseproof alpha");
            bus.getMethod("callEvent", eventBase).invoke(null, chat);
            if (probe.getClass().getField("calls").getInt(probe) != 1 ||
                !"alpha".equals(probe.getClass().getField("argument").get(probe)) ||
                !(Boolean) chat.getClass().getMethod("isCancelled").invoke(chat))
                throw new AssertionError("Legacy command arguments/cancellation/annotation dispatch failed");
            bus.getMethod("unsubscribe", Object.class).invoke(null, probe);
            bus.getMethod("callEvent", eventBase).invoke(null, event);
            if (probe.getClass().getField("calls").getInt(probe) != 1) throw new AssertionError("Object unsubscribe failed");
            System.out.println("PASS legacy annotation listener; command argument slicing; cancellation; object unsubscribe");
            bus.getMethod("unsubscribe", Object.class).invoke(null, raven);
            loader.loadClass("fixture.RoutingProof").getMethod("run").invoke(null);
            bus.getMethod("unsubscribe", Object.class).invoke(null, raven);
            try { loader.loadClass("net.weavemc.loader.WeaveLoader"); throw new AssertionError("Legacy runtime leaked"); }
            catch (ClassNotFoundException expected) { }
            System.out.println("PASS legacy event -> actual current EventBus; unsubscribe; legacy loader excluded");
        }
        System.getProperties().put("weave.game.info", Map.of("version", "1.8.9", "client", "lunar client"));
        LoaderDiscoveryBoundary.run(mod, bwh, Path.of(args[7]));
        var sandbox = new net.weavemc.loader.impl.mixin.SandboxedMixinLoader(
            MixedWeaveProof.class.getClassLoader(),
            net.weavemc.loader.impl.shaded.mappings.ClasspathLoaders.INSTANCE.fromLoader(MixedWeaveProof.class.getClassLoader()));
        try (var jar = new JarFile(mod.toFile())) {
            var config = jar.getJarEntry("ravenweave.mixins.json");
            sandbox.injectResource(config.getName(), jar.getInputStream(config).readAllBytes());
        }
        sandbox.getState().initialize();
        sandbox.getState().registerMixin("ravenweave", "ravenweave.mixins.json");
        var mixins = sandbox.loadClass("net.weavemc.loader.impl.shaded.spongepowered.asm.mixin.Mixins");
        if (((Set<?>) mixins.getMethod("getConfigs").invoke(null)).size() != 1) throw new AssertionError("Mixin registration failed");
        System.out.println("PASS original Mixin config registered in actual current sandbox (targets NOT applied)");
        try (var jar = new JarFile(bwh.toFile())) {
            var config = jar.getJarEntry("bwhelper.mixins.json");
            sandbox.injectResource(config.getName(), jar.getInputStream(config).readAllBytes());
        }
        sandbox.getState().registerMixin("bwhelper", "bwhelper.mixins.json");
        if (((Set<?>) mixins.getMethod("getConfigs").invoke(null)).size() != 2) throw new AssertionError("Shared config registration failed");
        System.out.println("PASS Raven + maintained BWH config registration in one current sandbox (no target application)");
        originalMixinProof(old, mod, facade, api);
        try { FullMixinPreparation.run(old, mod, facade, api, bwh, Path.of(args[6]), Path.of(args[7]), args.length < 9 || !Boolean.parseBoolean(args[8]), Arrays.stream(args).skip(9).map(Path::of).toList()); }
        finally {
            if (!before.equals(hash(mod)) || !bwhBefore.equals(hash(bwh))) throw new AssertionError("Archive changed during full application");
            System.out.println("PASS full application original Raven/BWH hashes unchanged (including failure path)");
        }
        if (!before.equals(hash(mod))) throw new AssertionError("Original modified");
        if (!bwhBefore.equals(hash(bwh))) throw new AssertionError("Maintained BWH modified");
        System.out.println("PASS Raven SHA unchanged; no output mod JAR");
        if (Boolean.getBoolean("moonrise.proof.requireDiscovery"))
            throw new IllegalStateException("BLOCKED: actual WeaveLoader discovery/loader-wide identity not certified; metadata parser and preparation PASS are partial evidence only");
        System.out.println("SCOPE: regression proof only; actual WeaveLoader discovery remains BLOCKED");
    }
    static void originalMixinProof(Path old, Path mod, Path facade, Path api) throws Exception {
        try (var game = new ApiLoader(old, mod, facade, api)) {
            var rawProvider = net.weavemc.loader.impl.shaded.mappings.ClasspathLoaders.INSTANCE.fromLoader(game);
            // Kotlin synthetic accessor is hidden from javac; use the pinned loader's own relocator.
            Remapper relocation = (Remapper) net.weavemc.loader.impl.util.MappingsKt.class
                .getMethod("access$createRelocationRemapper").invoke(null);
            net.weavemc.loader.impl.shaded.kotlin.jvm.functions.Function1<String, byte[]> provider = name -> {
                byte[] raw = rawProvider.invoke(name);
                if (raw == null) return null;
                if (!name.startsWith("me/pianopenguin471/") && !name.startsWith("ravenweave/")) return raw;
                var writer = new ClassWriter(0);
                new ClassReader(raw).accept(new ClassRemapper(writer, relocation), 0);
                return writer.toByteArray();
            };
            var sandbox = new net.weavemc.loader.impl.mixin.SandboxedMixinLoader(game, provider);
            for (String name : List.of("net.weavemc.loader.api.event.Event", "net.weavemc.loader.api.event.EventBus", "net.weavemc.loader.api.ModInitializer")) {
                byte[] definition = game.classBytes(name);
                byte[] visible = provider.invoke(name.replace('.', '/'));
                if (!Arrays.equals(definition, visible)) throw new AssertionError("Facade definition/provider mismatch: " + name);
                game.loadClass(name);
            }
            if (!Arrays.equals(game.classBytes("me.pianopenguin471.mixins.MinecraftMixin"),
                game.raven.getInputStream(game.raven.getJarEntry("me/pianopenguin471/mixins/MinecraftMixin.class")).readAllBytes()))
                throw new AssertionError("Original Mixin bytes changed before standard relocation");
            System.out.println("PASS shared game definitions/sandbox facade bytes; original MinecraftMixin read unchanged");
            // Bounded offline selection, not a replacement mod config or archive.
            sandbox.injectResource("offline-original-minecraft.json", ("{\"required\":true,\"minVersion\":\"0.8\",\"compatibilityLevel\":\"JAVA_17\",\"package\":\"me.pianopenguin471.mixins\",\"mixins\":[\"MinecraftMixin\"],\"injectors\":{\"defaultRequire\":1}}").getBytes(java.nio.charset.StandardCharsets.UTF_8));
            sandbox.getState().initialize();
            sandbox.getState().registerMixin("offline-original", "offline-original-minecraft.json");
            byte[] input = game.classBytes("net.minecraft.client.Minecraft");
            byte[] output = sandbox.getState().transform("net.minecraft.client.Minecraft", input);
            if (Arrays.equals(input, output)) throw new AssertionError("Original Mixin did not transform target");
            var node = new net.weavemc.loader.impl.shaded.asm.tree.ClassNode();
            new ClassReader(output).accept(node, 0);
            for (var method : node.methods)
                new net.weavemc.loader.impl.shaded.asm.tree.analysis.Analyzer<>(new net.weavemc.loader.impl.shaded.asm.tree.analysis.BasicVerifier()).analyze(node.name, method);
            var diagnostics = new java.io.StringWriter();
            net.weavemc.loader.impl.shaded.asm.util.CheckClassAdapter.verify(new ClassReader(output), game, false, new java.io.PrintWriter(diagnostics));
            if (!diagnostics.toString().isBlank()) throw new AssertionError("ASM verification failed: " + diagnostics);
            Class<?> target = game.defineTarget(output);
            Object fixture = target.getConstructor().newInstance();
            int[] events = {0};
            java.util.function.Consumer<net.weavemc.api.event.Event> listener = event -> {
                if (event.getClass().getName().equals("ravenweave.client.event.impl.GameLoopEvent")) {
                    try { if (target.getField("ticks").getInt(fixture) != events[0]) throw new AssertionError("Injection was not HEAD"); }
                    catch (ReflectiveOperationException e) { throw new AssertionError(e); }
                    events[0]++;
                }
            };
            net.weavemc.api.event.EventBus.subscribe(net.weavemc.api.event.Event.class, listener);
            try { target.getMethod("runTick").invoke(fixture); target.getMethod("runTick").invoke(fixture); }
            finally { net.weavemc.api.event.EventBus.unsubscribe(listener); }
            if (events[0] != 2 || target.getField("ticks").getInt(fixture) != 2) throw new AssertionError("Original injected behavior failed");
            System.out.println("PASS original Raven MinecraftMixin: named runTick HEAD; ASM BasicVerifier; JVM definition/invocation; two original GameLoopEvents");
        }
    }
}


