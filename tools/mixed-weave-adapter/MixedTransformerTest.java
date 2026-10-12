package dev.moonrise.mixed;

import java.util.Arrays;

/** Regression for actual ModInitializerHook system-side lookup observed in Lunar. */
public final class MixedTransformerTest {
    public static void main(String[] args) throws Exception {
        var constructor = MixedTransformer.class.getDeclaredConstructor(java.net.URL.class);
        constructor.setAccessible(true);
        var transformer = constructor.newInstance(MixedTransformerTest.class.getProtectionDomain().getCodeSource().getLocation());
        byte[] original;
        try (var input = MixedTransformerTest.class.getResourceAsStream("/net/weavemc/loader/impl/WeaveLoader.class")) {
            original = input.readAllBytes();
        }
        byte[] snapshot = original.clone();
        if (transformer.transform(ClassLoader.getSystemClassLoader(), "net/weavemc/loader/impl/WeaveLoader", null, null, original) != null)
            throw new AssertionError("System-side Weave definition must remain unchanged");
        if (!Arrays.equals(original, snapshot)) throw new AssertionError("Original bytes mutated");
        System.out.println("PASS original system-side WeaveLoader definition accepted unchanged");
        String localsName = "net/weavemc/loader/impl/shaded/spongepowered/asm/util/Locals";
        byte[] locals;
        try (var input = MixedTransformerTest.class.getResourceAsStream("/" + localsName + ".class")) { locals = input.readAllBytes(); }
        byte[] observed = transformer.transform(ClassLoader.getSystemClassLoader(), localsName, null, null, locals);
        var node = new net.weavemc.loader.impl.shaded.asm.tree.ClassNode();
        new net.weavemc.loader.impl.shaded.asm.ClassReader(observed).accept(node, 0);
        for (var method : node.methods) if ((method.access & (0x0400 | 0x0100)) == 0)
            new net.weavemc.loader.impl.shaded.asm.tree.analysis.Analyzer<>(new net.weavemc.loader.impl.shaded.asm.tree.analysis.BasicVerifier()).analyze(node.name, method);
        var defining = new ClassLoader(MixedTransformerTest.class.getClassLoader()) {
            Class<?> define(byte[] bytes) { return defineClass(null, bytes, 0, bytes.length); }
        };
        defining.define(observed).getDeclaredMethods();
        System.out.println("PASS actual Locals diagnostic bytecode ASM verification and JVM definition");
        var lines = java.nio.file.Files.readAllLines(java.nio.file.Path.of(System.getProperty("user.home"),
            ".weave", ".cache", "mappings", "merged_1.8.9", "mappings.tiny"));
        var mappings = net.weavemc.loader.impl.shaded.mappings.format.TinyMappingsV2Format.INSTANCE.detect(lines)
            ? net.weavemc.loader.impl.shaded.mappings.format.TinyMappingsV2Format.INSTANCE.parse(lines)
            : net.weavemc.loader.impl.shaded.mappings.format.TinyMappingsV1Format.INSTANCE.parse(lines);
        var mapper = new net.weavemc.loader.impl.shaded.mappings.remap.MappingsRemapper(mappings,
            "mcp-named", "official", name -> null);
        if (!mapper.mapMethodName("net/minecraft/client/renderer/entity/RendererLivingEntity", "doRender",
            "(Lnet/minecraft/entity/EntityLivingBase;DDDFF)V").equals("a"))
            throw new AssertionError("Real renderer mapping does not match observed ClassInfo member");
        System.out.println("PASS genuine cached Weave mappings resolve named doRender to official a");
    }
}
