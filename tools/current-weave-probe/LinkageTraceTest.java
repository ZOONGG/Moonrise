package dev.moonrise.diagnostic;
import java.lang.instrument.*;
import java.lang.reflect.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.atomic.AtomicReference;

public final class LinkageTraceTest {
    public static void main(String[] args) throws Exception {
        var captured = new AtomicReference<ClassFileTransformer>();
        var instrumentation = (Instrumentation)Proxy.newProxyInstance(LinkageTraceTest.class.getClassLoader(),
            new Class<?>[]{Instrumentation.class}, (p, method, values) -> {
                if (method.getName().equals("addTransformer")) { captured.set((ClassFileTransformer)values[0]); return null; }
                if (method.getName().equals("getAllLoadedClasses")) return new Class<?>[0];
                throw new AssertionError("Unexpected instrumentation mutation: " + method.getName());
            });
        var output = Path.of(args[0]);
        LinkageTrace.premain(output.toString(), instrumentation);
        var trace = captured.get();
        var bytes = "net/weavemc/loader/impl/shaded/spongepowered/asm/synthetic/args/Args$1".getBytes(StandardCharsets.ISO_8859_1);
        for (int i = 0; i < 700; i++)
            if (trace.transform(LinkageTraceTest.class.getClassLoader(), "net/optifine/reflect/Reflector", null, null, bytes) != null)
                throw new AssertionError("Diagnostic modified class bytes");
        if (trace.transform(null, null, null, null, bytes) != null) throw new AssertionError();
        var text = Files.readString(output);
        if (!text.contains("Args$1") || Files.readAllLines(output).size() != 600) throw new AssertionError("Missing refs/unbounded output");
        // Verify reflection contract against the untouched actual 1.3.4 JAR, without initialization.
        for (var spec : new String[][] {
            {"net.weavemc.loader.impl.WeaveLoader", "INSTANCE", "mixinInstances"},
            {"net.weavemc.loader.impl.mixin.SandboxedMixinLoader", "state", "injectedResources"},
            {"net.weavemc.loader.impl.mixin.SandboxedMixinState", "transformer"},
            {"net.weavemc.loader.impl.shaded.spongepowered.asm.mixin.transformer.MixinTransformer", "syntheticClassRegistry"},
            {"net.weavemc.loader.impl.shaded.spongepowered.asm.mixin.transformer.SyntheticClassRegistry", "classes"}}) {
            var type = Class.forName(spec[0], false, LinkageTraceTest.class.getClassLoader());
            for (int i = 1; i < spec.length; i++) type.getDeclaredField(spec[i]);
        }
        System.out.println("PASS observational transformer, synthetic refs, 600-line bound, actual 1.3.4 reflection contract");
    }
}
