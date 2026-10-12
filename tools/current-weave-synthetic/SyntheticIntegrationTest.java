package dev.moonrise.current;
import java.net.*;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;
import java.lang.reflect.*;
import java.util.jar.*;
import com.moonsworth.lunar.ichor.*;
import net.weavemc.loader.impl.mixin.*;
import net.weavemc.loader.impl.shaded.mappings.ClasspathLoaders;
import net.weavemc.loader.impl.shaded.asm.*;
import net.weavemc.loader.impl.shaded.asm.tree.*;

public final class SyntheticIntegrationTest {
    public static byte[] generated;
    public static String generatedName;
    // Fixture provider only; never packaged in the adapter.
    public static byte[] generate(ClassLoader loader, String name) { return name.equals(generatedName) ? generated : null; }
    public static void main(String[] args) throws Exception {
        System.getProperties().put("weave.game.info", Map.of("version", "1.8.9", "client", "lunar client"));
        var sandbox = new SandboxedMixinLoader(SyntheticIntegrationTest.class.getClassLoader(), ClasspathLoaders.INSTANCE.fromLoader(SyntheticIntegrationTest.class.getClassLoader()));
        var state = sandbox.getState(); state.initialize();
        var transformer = state.getTransformer$weave_loader();
        var getExtensions = transformer.getClass().getDeclaredMethod("getExtensions"); getExtensions.setAccessible(true);
        var extensions = getExtensions.invoke(transformer);
        var generatorType = sandbox.loadClass("net.weavemc.loader.impl.shaded.spongepowered.asm.mixin.injection.invoke.arg.ArgsClassGenerator");
        var generator = extensions.getClass().getMethod("getGenerator", Class.class).invoke(extensions, generatorType);
        var mixinType = sandbox.loadClass("net.weavemc.loader.impl.shaded.spongepowered.asm.mixin.extensibility.IMixinInfo");
        var mixin = java.lang.reflect.Proxy.newProxyInstance(sandbox, new Class[]{mixinType}, (p,m,a) -> m.getName().equals("toString") ? "offline-fixture" : null);
        var info = generatorType.getMethod("getArgsClass", String.class, mixinType).invoke(generator, "(IDLjava/lang/String;)V", mixin);
        var infoType = sandbox.loadClass("net.weavemc.loader.impl.shaded.spongepowered.asm.service.ISyntheticClassInfo");
        generatedName = ((String)infoType.getMethod("getName").invoke(info)).replace('/', '.');
        generated = SyntheticResolver.generateRegistered(Map.of("fixture-named", sandbox), generatedName, (namespace, bytes) -> {
            if (!namespace.equals("fixture-named")) throw new AssertionError("Wrong remap namespace"); return bytes;
        });
        if (SyntheticResolver.generateRegistered(Map.of(), generatedName, (n,b) -> b) != null) throw new AssertionError("Missing registry accepted");
        try {
            SyntheticResolver.generateRegistered(Map.of("one",sandbox,"two",sandbox), generatedName, (n,b) -> b);
            throw new AssertionError("Ambiguous owner accepted");
        } catch (IllegalStateException expected) { }
        if (!new ClassReader(generated).getClassName().equals(generatedName.replace('.', '/'))) throw new AssertionError("Wrong generated name");
        var identity = new net.weavemc.loader.impl.shaded.asm.commons.Remapper(Opcodes.ASM9) {};
        byte[] broken = net.weavemc.loader.impl.util.MappingsHandler.INSTANCE.remap(generated, identity);
        var defining = new ClassLoader(SyntheticIntegrationTest.class.getClassLoader()) {
            Class<?> define(byte[] bytes) { return defineClass(null, bytes, 0, bytes.length); }
        };
        try {
            defining.define(broken);
            throw new AssertionError("Old production remap unexpectedly retained executable code");
        } catch (ClassFormatError expected) {
            if (!expected.getMessage().contains("Absent Code attribute")) throw expected;
            System.out.println("PASS reproduced live ClassFormatError using actual Weave MappingsHandler.remap");
        }
        generated = SyntheticResolver.remapExecutable(generated, identity);
        var fixtureType = new ClassWriter(0);
        fixtureType.visit(52, Opcodes.ACC_PUBLIC, "fixture/NamedValue", null, "java/lang/Object", null); fixtureType.visitEnd();
        sandbox.injectResource("fixture/NamedValue.class", fixtureType.toByteArray());
        var typedInfo = generatorType.getMethod("getArgsClass", String.class, mixinType).invoke(generator, "(ILfixture/NamedValue;)V", mixin);
        var typedName = ((String)infoType.getMethod("getName").invoke(typedInfo)).replace('/', '.');
        var remapped = SyntheticResolver.generateRegistered(Map.of("fixture-named", sandbox), typedName, (namespace, bytes) -> {
            return SyntheticResolver.remapExecutable(bytes,
                new net.weavemc.loader.impl.shaded.asm.commons.SimpleRemapper(Opcodes.ASM9, "fixture/NamedValue", "java/lang/String"));
        });
        var typedNode = new ClassNode(); new ClassReader(remapped).accept(typedNode, 0);
        if (typedNode.methods.stream().noneMatch(m -> m.name.equals("of") && m.desc.startsWith("(ILjava/lang/String;)")))
            throw new AssertionError("Generated object descriptors were not remapped to runtime namespace");
        var typedClass = defining.define(remapped);
        var typedValue = typedClass.getMethod("of", int.class, String.class).invoke(null, 3, "runtime");
        if (!typedClass.getMethod("$1").invoke(typedValue).equals("runtime")) throw new AssertionError("Remapped Args body failed");
        try { SyntheticResolver.generateOwned(state, SyntheticResolver.PREFIX + "args.Args$999"); throw new AssertionError("Unknown generation accepted"); }
        catch (IllegalStateException expected) { }
        byte[] original;
        try (var jar = new JarFile(args[0])) { original = jar.getInputStream(jar.getJarEntry(SyntheticLoaderAdapter.GENESIS + ".class")).readAllBytes(); }
        byte[] adapted = SyntheticLoaderAdapter.adapt(original);
        // Same composition as live premain ordering: our bridge, then untouched Weave's loader accessor transform.
        adapted = net.weavemc.loader.impl.bootstrap.transformer.URLClassLoaderTransformer.INSTANCE.transform(
            SyntheticIntegrationTest.class.getClassLoader(), SyntheticLoaderAdapter.GENESIS, adapted);
        var node = new ClassNode(); new ClassReader(adapted).accept(node, 0);
        for (var method : node.methods) for (var insn : method.instructions)
            if (insn instanceof LdcInsnNode constant && "dev.moonrise.current.SyntheticResolver".equals(constant.cst))
                constant.cst = "dev.moonrise.current.SyntheticFixture";
        var writer = new ClassWriter(0); node.accept(writer);
        var holder = new ClassLoader(SyntheticIntegrationTest.class.getClassLoader()) {
            Class<?> define(byte[] b) { return defineClass(null, b, 0, b.length); }
            protected Class<?> loadClass(String name, boolean resolve) throws ClassNotFoundException {
                if (name.equals("dev.moonrise.current.SyntheticResolver") || name.equals("dev.moonrise.current.SyntheticFixture"))
                    throw new ClassNotFoundException("Genesis defining loader deliberately cannot see adapter helper: " + name);
                return super.loadClass(name, resolve);
            }
        };
        var loaderType = holder.define(writer.toByteArray());
        var stage = new OOIOHHHCIICRRRCICOIIIRRRICOCIC() {
            public String name(){return "offline";} public boolean hasMixinRuntime(){return false;}
            public boolean shouldUseParentAsMixinRuntime(){return false;} public boolean shouldUseClassBytes(){return false;}
        };
        var pipeline = new ROHIOOCICRORHRCIHHHHHHRICCCRRC(List.of(stage), null, SyntheticIntegrationTest.class.getClassLoader());
        var game = (URLClassLoader)loaderType.getConstructor(URL[].class, ROHIOOCICRORHRCIHHHHHHRICCCRRC.class).newInstance(new URL[]{SyntheticIntegrationTest.class.getProtectionDomain().getCodeSource().getLocation()}, pipeline);
        loaderType.getMethod("CCIIROCRRHHROIRHRRIHOCRHIHIOHO", Set.class).invoke(game,
            Set.of("dev.moonrise.current.SyntheticResolver", "dev.moonrise.current.SyntheticFixture", "net.weavemc.loader.impl.shaded.spongepowered.asm.mixin.injection.invoke.arg.Args",
                "net.weavemc.loader.impl.shaded.spongepowered.asm.mixin.injection.invoke.arg.ArgumentCountException",
                "net.weavemc.loader.impl.shaded.spongepowered.asm.mixin.injection.invoke.arg.ArgumentIndexOutOfBoundsException"));
        var resolver = Class.forName("dev.moonrise.current.SyntheticResolver", false, game);
        if (resolver.getClassLoader() != game) throw new AssertionError("Resolver must belong to game loader");
        var provider = Class.forName("dev.moonrise.current.SyntheticFixture", false, game);
        if (provider.getClassLoader() != game) throw new AssertionError("Fixture must belong to game loader");
        provider.getField("generated").set(null, generated);
        provider.getField("generatedName").set(null, generatedName);
        // The exact untouched Genesis previously cannot find these registered synthetic bytes.
        var baseline = new com.moonsworth.lunar.genesis.ORCOIHCCICICIHRCCCCHHHRICHHICI(new URL[0], pipeline);
        try { baseline.loadClass(generatedName); throw new AssertionError("Baseline unexpectedly resolved synthetic"); }
        catch (ClassNotFoundException expected) { }
        var loaded = game.loadClass(generatedName);
        if (loaded.getClassLoader() != game || loaded != game.loadClass(generatedName)) throw new AssertionError("Wrong/duplicate definition");
        Object value = loaded.getMethod("of", int.class, double.class, String.class).invoke(null, 7, 2.5, "fixture");
        if (!loaded.getMethod("$0").invoke(value).equals(7)) throw new AssertionError("Broken generated Args semantics");
        var pool = Executors.newFixedThreadPool(4);
        try {
            var futures = new ArrayList<Future<Class<?>>>();
            for (int i = 0; i < 20; i++) futures.add(pool.submit(() -> game.loadClass(generatedName)));
            for (var future : futures) if (future.get() != loaded) throw new AssertionError("Concurrent duplicate");
        } finally { pool.shutdown(); }
        try { game.loadClass(SyntheticResolver.PREFIX + "args.Args$999"); throw new AssertionError("Unknown accepted"); }
        catch (ClassNotFoundException expected) { }
        if (game.loadClass("java.lang.String") != String.class) throw new AssertionError("Ordinary delegation changed");
        game.close(); baseline.close();
        System.out.println("PASS actual 1.3.4 registered Args generation + actual Genesis baseline failure/adapted definition, invocation, duplicate/concurrent/unknown/delegation");
    }
}
