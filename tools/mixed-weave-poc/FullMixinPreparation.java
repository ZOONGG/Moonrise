import java.nio.file.*;
import java.util.*;
import java.util.jar.*;
import com.google.gson.*;
import net.weavemc.loader.impl.shaded.asm.*;
import net.weavemc.loader.impl.shaded.asm.commons.*;
import net.weavemc.loader.impl.shaded.asm.tree.*;

/** Full original configurations; no game fixtures and no game initialization. */
public final class FullMixinPreparation {
    static class RealLoader extends MixedWeaveProof.ApiLoader {
        final List<JarFile> inputs = new ArrayList<>();
        final JarFile maintained;
        final List<JarFile> dependencies = new ArrayList<>();
        final Map<Path, String> dependencyHashes = new LinkedHashMap<>();
        RealLoader(Path old, Path mod, Path facade, Path api, Path bwh, Path cache, List<Path> dependencyPaths) throws Exception {
            super(old, mod, facade, api);
            maintained = new JarFile(bwh.toFile());
            try (var paths = Files.list(cache)) {
                for (Path path : paths.filter(p -> p.toString().endsWith(".cache")).sorted().toList()) {
                    inputs.add(new JarFile(path.toFile()));
                    System.out.println("NAMED-INPUT " + path + " SHA256=" + MixedWeaveProof.hash(path));
                }
            }
            for (Path path : dependencyPaths) {
                var jar = new JarFile(path.toFile());
                dependencies.add(jar);
                for (var entry : Collections.list(jar.entries()))
                    if (entry.getName().endsWith(".class") && (entry.getName().startsWith("net/minecraft/") ||
                        entry.getName().startsWith("net/weavemc/") || entry.getName().startsWith("fixture/")))
                        throw new AssertionError("Dependency contains protected class namespace: " + path + " " + entry.getName());
                String hash = MixedWeaveProof.hash(path);
                dependencyHashes.put(path, hash);
                System.out.println("DEPENDENCY-INPUT " + path + " SHA256=" + hash);
            }
        }
        @Override byte[] classBytes(String name) throws ClassNotFoundException {
            String entry = name.replace('.', '/') + ".class";
            try {
                if (name.startsWith("net.minecraft.")) {
                    for (JarFile jar : inputs) {
                        var found = jar.getJarEntry(entry);
                        if (found != null) return jar.getInputStream(found).readAllBytes();
                    }
                } else if (!name.startsWith("fixture.") && !name.startsWith("net.weavemc.")) {
                    var found = maintained.getJarEntry(entry);
                    if (found != null) return maintained.getInputStream(found).readAllBytes();
                }
            } catch (java.io.IOException e) { throw new ClassNotFoundException(name, e); }
            if (name.startsWith("net.minecraft.") || name.startsWith("fixture."))
                throw new ClassNotFoundException("Real offline input absent: " + name);
            if (!name.startsWith("net.weavemc.")) try {
                for (JarFile jar : dependencies) {
                    var found = jar.getJarEntry(entry);
                    if (found != null) return jar.getInputStream(found).readAllBytes();
                }
            } catch (java.io.IOException e) { throw new ClassNotFoundException(name, e); }
            if (name.startsWith("org.lwjgl.")) throw new ClassNotFoundException("Real offline dependency absent: " + name);
            return super.classBytes(name);
        }
        @Override public void close() throws Exception {
            for (JarFile jar : dependencies) jar.close();
            for (JarFile jar : inputs) jar.close();
            maintained.close();
            super.close();
            for (var entry : dependencyHashes.entrySet())
                if (!entry.getValue().equals(MixedWeaveProof.hash(entry.getKey()))) throw new AssertionError("Dependency archive changed: " + entry.getKey());
            System.out.println("PASS real dependency hashes unchanged count=" + dependencyHashes.size());
        }
    }
    static void run(Path old, Path mod, Path facade, Path api, Path bwh, Path cache, Path mappings, boolean application, List<Path> dependencies) throws Exception {
        System.out.println("FULL preparation (no fixtures); mappings=" + mappings + " SHA256=" + MixedWeaveProof.hash(mappings));
        if (!Files.readAllLines(mappings).get(0).contains("mcp-named")) throw new AssertionError("Named mapping namespace absent");
        try (var game = new RealLoader(old, mod, facade, api, bwh, cache, dependencies)) {
            Remapper relocation = (Remapper) net.weavemc.loader.impl.util.MappingsKt.class
                .getMethod("access$createRelocationRemapper").invoke(null);
            Set<String> modClasses = new HashSet<>();
            try (var other = new JarFile(bwh.toFile())) {
                for (JarFile jar : List.of(game.raven, other))
                    for (var entry : Collections.list(jar.entries()))
                        if (entry.getName().endsWith(".class")) modClasses.add(entry.getName().substring(0, entry.getName().length()-6));
            }
            net.weavemc.loader.impl.shaded.kotlin.jvm.functions.Function1<String, byte[]> provider = name -> {
                byte[] bytes;
                try { bytes = game.classBytes(name.replace('/', '.')); }
                catch (ClassNotFoundException absent) {
                    try (var resource = FullMixinPreparation.class.getClassLoader().getResourceAsStream(name + ".class")) {
                        if (resource == null) { System.out.println("MISSING " + name); return null; }
                        return resource.readAllBytes();
                    } catch (java.io.IOException e) { throw new IllegalStateException(e); }
                }
                if (!modClasses.contains(name)) return bytes;
                var writer = new ClassWriter(0);
                new ClassReader(bytes).accept(new ClassRemapper(writer, relocation), 0);
                return writer.toByteArray();
            };
            var sandbox = new net.weavemc.loader.impl.mixin.SandboxedMixinLoader(game, provider);
            for (String name : List.of("net.weavemc.loader.api.event.Event", "net.weavemc.loader.api.event.EventBus", "net.weavemc.loader.api.ModInitializer")) {
                if (!Arrays.equals(game.classBytes(name), provider.invoke(name.replace('.', '/'))))
                    throw new AssertionError("Facade visibility mismatch: " + name);
                game.loadClass(name);
            }
            System.out.println("PASS full sandbox facade provider/definition equality");
            for (String name : List.of("net.weavemc.loader.api.ModInitializer", "net.weavemc.loader.api.event.Event",
                "net.weavemc.loader.api.event.EventBus", "net.weavemc.loader.api.command.Command",
                "net.weavemc.loader.api.command.CommandBus", "net.weavemc.api.ModInitializer",
                "net.weavemc.api.event.Event", "net.weavemc.api.event.EventBus")) {
                Class<?> definition = game.loadClass(name);
                // Sandbox's loadClass is its isolated Mixin implementation loader, not the game definition owner.
                try {
                    Class<?> isolated = sandbox.loadClass(name);
                    System.out.println("SANDBOX-CLASS-LOOKUP " + name + " sameIdentity=" + (isolated == definition));
                } catch (Exception absent) {
                    if (!(absent instanceof ClassNotFoundException)) throw absent;
                    System.out.println("SANDBOX-CLASS-LOOKUP " + name + " unavailable=" + absent.getMessage());
                }
                if (name.startsWith("net.weavemc.api.") && definition != Class.forName(name))
                    throw new AssertionError("Split current loader API identity: " + name);
                byte[] definedBytes;
                if (name.startsWith("net.weavemc.api.")) {
                    try (var resource = definition.getClassLoader().getResourceAsStream(name.replace('.', '/') + ".class")) {
                        definedBytes = resource.readAllBytes();
                    }
                } else definedBytes = game.classBytes(name);
                if (!Arrays.equals(definedBytes, provider.invoke(name.replace('.', '/'))))
                    throw new AssertionError("Identity provider byte mismatch: " + name);
                System.out.println("IDENTITY offline game definition/provider " + name + " owner=" + definition.getClassLoader());
            }
            System.out.println("PASS offline game API owner/provider equality; sandbox loadClass identity and real WeaveLoader lifecycle identity NOT certified");
            sandbox.getState().initialize();
            var configs = new ArrayList<String>();
            var expected = new TreeMap<String, Set<String>>();
            var expectedCounts = new ArrayList<Integer>();
            for (Path path : List.of(mod, bwh)) try (var jar = new JarFile(path.toFile())) {
                var metadata = JsonParser.parseString(new String(jar.getInputStream(jar.getJarEntry("weave.mod.json")).readAllBytes(), java.nio.charset.StandardCharsets.UTF_8)).getAsJsonObject();
                for (var declared : metadata.getAsJsonArray("mixinConfigs")) {
                    String resource = declared.getAsString();
                    byte[] bytes = jar.getInputStream(jar.getJarEntry(resource)).readAllBytes();
                    var config = JsonParser.parseString(new String(bytes, java.nio.charset.StandardCharsets.UTF_8)).getAsJsonObject();
                    String pkg = config.get("package").getAsString();
                    int count = 0;
                    for (String section : List.of("mixins", "client", "server")) {
                        if (!config.has(section)) continue;
                        for (var mixin : config.getAsJsonArray(section)) {
                            count++;
                            String name = pkg.replace('.', '/') + "/" + mixin.getAsString().replace('.', '/');
                            byte[] raw = jar.getInputStream(jar.getJarEntry(name + ".class")).readAllBytes();
                            if (!Arrays.equals(raw, game.classBytes(name.replace('/', '.')))) throw new AssertionError("Mod input changed: " + name);
                            var node = new ClassNode(); new ClassReader(provider.invoke(name)).accept(node, 0);
                            var annotations = node.invisibleAnnotations == null ? List.<AnnotationNode>of() : node.invisibleAnnotations;
                            boolean relocated = false;
                            for (var annotation : annotations) {
                                if (!annotation.desc.equals("Lnet/weavemc/loader/impl/shaded/spongepowered/asm/mixin/Mixin;")) continue;
                                relocated = true;
                                for (int i=0; i<annotation.values.size(); i+=2) {
                                    if (!Set.of("value", "targets").contains(annotation.values.get(i))) continue;
                                    for (Object target : (List<?>) annotation.values.get(i+1)) {
                                        String targetName = target instanceof Type ? ((Type)target).getInternalName() : target.toString().replace('.', '/');
                                        byte[] targetBytes = provider.invoke(targetName);
                                        expected.computeIfAbsent(targetName, k -> new TreeSet<>()).add(name.replace('/', '.'));
                                        System.out.println("RESOLUTION " + resource + " " + name + " -> " + targetName + " " + (targetBytes == null ? "ABSENT" : "REAL bytes=" + targetBytes.length));
                                    }
                                }
                            }
                            if (!relocated) throw new AssertionError("Mixin annotation relocation failed: " + name);
                        }
                    }
                    System.out.println("DISCOVERED " + resource + " mixins=" + count + " original bytes/relocation verified");
                    sandbox.injectResource(resource, bytes);
                    sandbox.getState().registerMixin("offline-" + configs.size(), resource);
                    configs.add(resource);
                    expectedCounts.add(count);
                }
            }
            try {
                Class<?> mixinsApi = sandbox.loadClass("net.weavemc.loader.impl.shaded.spongepowered.asm.mixin.Mixins");
                var handles = new ArrayList<>((Set<?>) mixinsApi.getMethod("getConfigs").invoke(null));
                if (handles.size() != configs.size()) throw new AssertionError("Shared config count mismatch");
                var targets = sandbox.getState().findTargets(sandbox.getState().getTransformer$weave_loader());
                System.out.println("DECLARED targets=" + new TreeSet<>(targets));
                // findTargets only discovers annotations; a non-target transform forces MixinProcessor selection/preparation.
                byte[] object = provider.invoke("java/lang/Object");
                sandbox.getState().transform("java/lang/Object", object);
                for (Object handle : handles) {
                    Object config = handle.getClass().getMethod("getConfig").invoke(handle);
                    for (String fieldName : List.of("prepared", "mixins", "pendingMixins", "mixinMapping")) {
                        var field = config.getClass().getDeclaredField(fieldName); field.setAccessible(true);
                        Object value = field.get(config);
                        System.out.println("PREPARE-STATE " + handle + " " + fieldName + "=" + value);
                        if (fieldName.equals("prepared") && !Boolean.TRUE.equals(value)) throw new AssertionError("Config not prepared");
                        if (fieldName.equals("mixins") && ((List<?>)value).isEmpty()) throw new AssertionError("No prepared mixins");
                        if (fieldName.equals("mixins")) {
                            int index = -1;
                            for (int i=0; i<configs.size(); i++) if (handle.toString().endsWith("/" + configs.get(i))) index = i;
                            if (index < 0 || ((List<?>)value).size() != expectedCounts.get(index)) throw new AssertionError("Incomplete preparation");
                        }
                        if (fieldName.equals("pendingMixins") && !((List<?>)value).isEmpty()) throw new AssertionError("Pending mixins remain");
                    }
                }
                System.out.println("PREPARED full configs=" + configs);
                if (application) {
                    RavenYawSelectorEvidence.run(game, sandbox, handles);
                    applyAll(sandbox, game, expected);
                }
            } catch (Throwable blocker) {
                System.out.println("BLOCKER full shared preparation/application");
                blocker.printStackTrace(System.out);
                throw new IllegalStateException("Full preparation/application failed; see blocker above", blocker);
            }
        }
    }

    static void applyAll(net.weavemc.loader.impl.mixin.SandboxedMixinLoader sandbox, RealLoader game,
                         Map<String, Set<String>> expected) throws Exception {
        int verified = 0, transformed = 0;
        Set<String> applied = new TreeSet<>();
        List<String> failures = new ArrayList<>();
        Map<String, byte[]> outputs = new TreeMap<>();
        if (expected.size() != 18 || expected.values().stream().flatMap(Set::stream).distinct().count() != 21)
            throw new AssertionError("Full target/Mixin inventory changed");
        for (var entry : expected.entrySet()) {
            String target = entry.getKey();
            System.out.println("APPLICATION target=" + target + " expected=" + entry.getValue());
            try {
                byte[] original = game.classBytes(target.replace('/', '.'));
                byte[] output = sandbox.getState().transform(target.replace('/', '.'), original);
                if (Arrays.equals(original, output)) throw new AssertionError("Unchanged target");
                var node = new ClassNode(); new ClassReader(output).accept(node, 0);
                Set<String> evidence = new TreeSet<>();
                Map<String, Set<String>> matched = new TreeMap<>();
                for (MethodNode method : node.methods) {
                    String owner = null;
                    for (AnnotationNode annotation : method.visibleAnnotations == null ? List.<AnnotationNode>of() : method.visibleAnnotations) {
                        if (annotation.desc.endsWith("/MixinMerged;"))
                            for (int i=0; i<annotation.values.size(); i+=2)
                                if (annotation.values.get(i).equals("mixin")) owner = annotation.values.get(i+1).toString();
                    }
                    if (owner == null) continue;
                    var source = new ClassNode(); new ClassReader(game.classBytes(owner)).accept(source, 0);
                    boolean injector = false;
                    for (MethodNode declared : source.methods) {
                        if (!method.desc.equals(declared.desc.replace("org/spongepowered/", "net/weavemc/loader/impl/shaded/spongepowered/")) ||
                            !(method.name.equals(declared.name) || method.name.endsWith("$" + declared.name))) continue;
                        matched.computeIfAbsent(owner, k -> new HashSet<>()).add(declared.name + declared.desc);
                        var annotations = new ArrayList<AnnotationNode>();
                        if (declared.visibleAnnotations != null) annotations.addAll(declared.visibleAnnotations);
                        if (declared.invisibleAnnotations != null) annotations.addAll(declared.invisibleAnnotations);
                        for (AnnotationNode annotation : annotations)
                            if (annotation.desc.contains("/mixin/injection/") && !annotation.desc.endsWith("/Coerce;")) injector = true;
                        if (annotations.stream().anyMatch(a -> a.desc.endsWith("/Accessor;"))) {
                            boolean fieldAccess = false;
                            for (var insn : method.instructions) if (insn instanceof FieldInsnNode field && field.owner.equals(target) &&
                                node.fields.stream().anyMatch(f -> f.name.equals(field.name) && f.desc.equals(field.desc))) fieldAccess = true;
                            if (!fieldAccess) throw new AssertionError("Accessor has no real target field access: " + owner + "." + declared.name);
                            System.out.println("ACCESSOR verified " + owner + "." + declared.name);
                        }
                    }
                    int calls = 0;
                    for (MethodNode caller : node.methods) for (var insn : caller.instructions)
                        if (insn instanceof MethodInsnNode call && call.owner.equals(target) && call.name.equals(method.name) && call.desc.equals(method.desc)) calls++;
                    System.out.println("MERGED " + owner + " " + method.name + method.desc + " injector=" + injector + " callsites=" + calls);
                    if (injector && calls == 0) throw new AssertionError("Injector merged without callsite: " + owner + "." + method.name);
                    evidence.add(owner);
                }
                for (String mixin : entry.getValue()) {
                    var source = new ClassNode(); new ClassReader(game.classBytes(mixin)).accept(source, 0);
                    for (MethodNode declared : source.methods) {
                        var annotations = new ArrayList<AnnotationNode>();
                        if (declared.visibleAnnotations != null) annotations.addAll(declared.visibleAnnotations);
                        if (declared.invisibleAnnotations != null) annotations.addAll(declared.invisibleAnnotations);
                        boolean required = annotations.stream().anyMatch(a -> a.desc.contains("/mixin/injection/") || a.desc.endsWith("/Accessor;") || a.desc.endsWith("/Invoker;") || a.desc.endsWith("/Overwrite;"));
                        if (required && !matched.getOrDefault(mixin, Set.of()).contains(declared.name + declared.desc))
                            throw new AssertionError("Declared operation not present in output: " + mixin + "." + declared.name + declared.desc);
                    }
                }
                if (!evidence.containsAll(entry.getValue())) throw new AssertionError("Missing application evidence: expected=" + entry.getValue() + " actual=" + evidence);
                transformed++; applied.addAll(entry.getValue());
                for (MethodNode method : node.methods)
                    if ((method.access & (Opcodes.ACC_ABSTRACT | Opcodes.ACC_NATIVE)) == 0)
                        new net.weavemc.loader.impl.shaded.asm.tree.analysis.Analyzer<>(new net.weavemc.loader.impl.shaded.asm.tree.analysis.BasicVerifier()).analyze(node.name, method);
                new ClassReader(output).accept(new net.weavemc.loader.impl.shaded.asm.util.CheckClassAdapter(new ClassWriter(0), true), 0);
                verified++;
                outputs.put(target.replace('/', '.'), output);
                System.out.println("PASS APPLICATION " + target + " mixins=" + evidence + " BasicVerifier/CheckClassAdapter");
                var diagnostics = new java.io.StringWriter();
                try { net.weavemc.loader.impl.shaded.asm.util.CheckClassAdapter.verify(new ClassReader(output), game, false, new java.io.PrintWriter(diagnostics)); }
                catch (LinkageError missing) { diagnostics.append(missing.toString()); }
                System.out.println("DEPENDENCY-VERIFICATION " + target + " " + (diagnostics.toString().isBlank() ? "PASS" : diagnostics.toString()));
            } catch (Throwable failure) {
                failures.add(target);
                System.out.println("BLOCKER APPLICATION " + target + " expected=" + entry.getValue());
                failure.printStackTrace(System.out);
            }
        }
        // One definition owner for the complete available hierarchy avoids split target identities.
        ClassLoader definitions = new ClassLoader(game.getParent()) {
            @Override protected Class<?> findClass(String name) throws ClassNotFoundException {
                byte[] bytes = outputs.containsKey(name) ? outputs.get(name) : game.classBytes(name);
                return defineClass(name, bytes, 0, bytes.length);
            }
        };
        for (String name : outputs.keySet()) try {
            Class<?> type = Class.forName(name, false, definitions);
            type.getDeclaredMethods(); type.getDeclaredConstructors();
            System.out.println("JVM-VERIFICATION PASS " + name + " shared definition/member enumeration; no initialization");
        } catch (LinkageError dependency) {
            System.out.println("JVM-VERIFICATION LIMITED " + name + " " + dependency);
        }
        System.out.println("APPLICATION SUMMARY targets=" + expected.size() + " appliedTargets=" + transformed + " verifiedTargets=" + verified + " appliedMixins=" + applied.size() + " failures=" + failures);
        if (!failures.isEmpty()) throw new IllegalStateException("Full application failures: " + failures);
    }
}
