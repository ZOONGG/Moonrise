package dev.moonrise.mapping;
import java.io.*;
import java.net.*;
import java.nio.file.*;
import java.lang.instrument.*;
import java.security.*;
import java.util.*;
import java.util.jar.*;
import org.objectweb.asm.*;
import org.objectweb.asm.tree.*;
import org.cadixdev.lorenz.*;
import org.cadixdev.lorenz.io.*;

public final class LegacyMappingAdapter implements ClassFileTransformer {
    public static final String RESOURCE = "lunar/lunar_named_b5_1.8.9.xsrg";
    private static final String READER = "com.moonsworth.lunar.ICCRRCCIOOIRICIHHCIOROHHROOHHR.RCHICCCIHRCRCHCIRRIIRRHICORHHO.IHIRRHORRRRHICCOHOHORCHIHHHIOO";
    private final Path jar;
    private final byte[] expected;
    private boolean installed;
    private LegacyMappingAdapter(Path jar, byte[] expected) { this.jar=jar; this.expected=expected; }
    public static void install(Instrumentation inst, String mapping, String output) throws Exception {
        byte[] bytes=convert(Path.of(mapping));
        Path jar=Path.of(output);
        StartupTrace.stage("resource-jar-create-begin");
        try (var stream=new JarOutputStream(Files.newOutputStream(jar, StandardOpenOption.CREATE_NEW))) {
            stream.putNextEntry(new JarEntry(RESOURCE)); stream.write(bytes); stream.closeEntry();
        }
        StartupTrace.stage("resource-jar-created");
        inst.addTransformer(new LegacyMappingAdapter(jar,bytes));
    }
    public static byte[] convert(Path mapping) throws Exception {
        StartupTrace.stage("kin-source-open-begin");
        try (var jar=new JarFile(mapping.toFile())) {
            var entries=jar.stream().filter(e->e.getName().equals("lunar/lunar_named_b5_1.8.9.kin")).toList();
            if(entries.size()!=1) throw new IllegalStateException("Missing/duplicate pinned KIN mapping");
            StartupTrace.stage("kin-source-verified; matching-entries=1");
            MappingSet set;
            try(var input=jar.getInputStream(entries.get(0)); var reader=(MappingsReader)Class.forName(READER).getConstructor(InputStream.class).newInstance(input)) { set=reader.read(); }
            if(!set.getClassMapping("ave").orElseThrow().getFullDeobfuscatedName().equals("net/minecraft/client/Minecraft")) throw new IllegalStateException("Namespace direction mismatch");
            StartupTrace.stage("xsrg-generation-begin");
            var text=new StringWriter();
            try(var writer=new org.cadixdev.lorenz.io.srg.xsrg.XSrgWriter(text)) { writer.write(set); }
            MappingSet restored;
            try(var reader=new org.cadixdev.lorenz.io.srg.xsrg.XSrgReader(new StringReader(text.toString()))) { restored=reader.read(); }
            if(!semantics(set).equals(semantics(restored))) throw new IllegalStateException("Lossy XSRG descriptors/direction");
            var keys=new HashMap<String,String>();
            for(String line:text.toString().split("\\R")) {
                if(line.isEmpty()) continue;
                String[] p=line.split(" ");
                if(!(p[0].equals("CL:") && p.length==3 || (p[0].equals("MD:") || p[0].equals("FD:")) && p.length==5)) throw new IllegalStateException("Invalid legacy XSRG record");
                String key=p[0]+p[1]+(p[0].equals("MD:")?p[2]:"");
                String value=p[0].equals("CL:")?p[2]:p[3];
                String previous=keys.putIfAbsent(key,value);
                if(previous!=null && !previous.equals(value)) throw new IllegalStateException("Conflicting legacy mapping key "+key);
            }
            return text.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8);
        }
    }
    private static Set<String> semantics(MappingSet set) {
        var out=new TreeSet<String>();
        for(var c:set.getTopLevelClassMappings()) collect(c,out);
        return out;
    }
    private static void collect(org.cadixdev.lorenz.model.ClassMapping<?,?> c, Set<String> out) {
        out.add("CL: "+c.getFullObfuscatedName()+" "+c.getFullDeobfuscatedName());
        for(var f:c.getFieldMappings()) out.add("FD: "+f.getFullObfuscatedName()+" "+f.getType().orElseThrow()+" "+f.getFullDeobfuscatedName()+" "+f.getDeobfuscatedSignature().getType().orElseThrow());
        for(var m:c.getMethodMappings()) out.add("MD: "+m.getFullObfuscatedName()+" "+m.getObfuscatedDescriptor()+" "+m.getFullDeobfuscatedName()+" "+m.getDeobfuscatedDescriptor());
        for(var inner:c.getInnerClassMappings()) collect(inner,out);
    }
    static void verifyResource(ClassLoader loader, Path jar, byte[] expected) throws Exception {
        var urls=Collections.list(loader.getResources(RESOURCE));
        var unique=new LinkedHashSet<String>();
        for(var url:urls) unique.add(url.toExternalForm());
        StartupTrace.stage("xsrg-visibility; loader="+loader.getClass().getName()+"; raw="+urls.size()+"; unique="+unique.size());
        String wanted="jar:"+jar.toUri().toURL().toExternalForm()+"!/"+RESOURCE;
        if(unique.size()!=1 || !unique.contains(wanted))
            throw new IllegalStateException("Expected exactly one XSRG origin; raw="+urls.size()+"; unique="+unique.size()+"; origins="+unique);
        if(!wanted.equals(loader.getResource(RESOURCE).toExternalForm())) throw new IllegalStateException("XSRG lookup origin mismatch");
        for(var url:urls) try(var input=url.openStream()) {
            if(!Arrays.equals(expected,input.readAllBytes())) throw new IllegalStateException("XSRG enumerated content mismatch");
        }
        try(var input=loader.getResourceAsStream(RESOURCE)) {
            if(input==null || !Arrays.equals(expected,input.readAllBytes())) throw new IllegalStateException("Genesis XSRG stream mismatch");
        }
    }
    public synchronized byte[] transform(ClassLoader loader,String name,Class<?> old,ProtectionDomain domain,byte[] bytes) {
        if(!Set.of("net/weavemc/loader/mixins/WeaveMixinTransformer", "net/weavemc/loader/WeaveLoader", "net/weavemc/loader/mixins/WeaveMixinService").contains(name==null?"":name)) return null;
        try {
            if(loader==null || !"Genesis".equals(loader.getName())) throw new IllegalStateException("Unexpected defining loader for "+name);
            if(name.equals("net/weavemc/loader/mixins/WeaveMixinTransformer")) return currentMixinEnvironment(bytes);
            if(name.equals("net/weavemc/loader/WeaveLoader")) return observe(bytes,false);
            if(installed || loader==null || !"Genesis".equals(loader.getName())) throw new IllegalStateException("Unsupported/duplicate Mixin bootstrap loader");
            try(var input=loader.getResourceAsStream("ave.class")) {
                if(input==null || !HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(input.readAllBytes())).equals("430a17aa1911bcd0251aa4dcecb013b6c5b251bb5d0122c7305de0e36dc33e82"))
                    throw new IllegalStateException("Minecraft 1.8.9 vanilla-obfuscated Genesis bytecode contract mismatch");
            }
            if(loader.getResource(RESOURCE)!=null || loader.getResources(RESOURCE).hasMoreElements()) throw new IllegalStateException("Existing/conflicting XSRG resource");
            StartupTrace.stage("genesis-resource-attach-begin");
            loader.getClass().getMethod("addURL",URL.class).invoke(loader,jar.toUri().toURL());
            verifyResource(loader, jar, expected);
            /* Stream is checked again at the construction boundary. */
            try(var input=loader.getResourceAsStream(RESOURCE)) {
                if(input==null || !Arrays.equals(expected,input.readAllBytes())) throw new IllegalStateException("Genesis XSRG content mismatch");
            }
            installed=true;
            StartupTrace.stage("genesis-resource-attached-verified");
            System.err.println("[Moonrise mapping] XSRG FOUND; content verified through actual Genesis loader BEFORE WeaveMixinService construction");
            return observe(bytes,true);
        } catch(Throwable failure) {
            StartupTrace.failure(failure);
            System.err.println("[Moonrise mapping] FAIL CLOSED: "+failure); failure.printStackTrace(System.err);
            // Instrumentation ignores transformer exceptions. Terminate instead of continuing without the contract.
            Runtime.getRuntime().halt(86);
        }
        return null;
    }
    private static byte[] currentMixinEnvironment(byte[] bytes) {
        var node=new ClassNode(); new ClassReader(bytes).accept(node,0);
        int matched=0;
        for(var method:node.methods) for(var insn:method.instructions) {
            if(insn instanceof MethodInsnNode call && call.owner.equals("org/spongepowered/asm/mixin/MixinEnvironment") &&
                    call.name.equals("getDefaultEnvironment") && call.desc.equals("()Lorg/spongepowered/asm/mixin/MixinEnvironment;")) {
                call.name="getCurrentEnvironment"; matched++;
            }
        }
        if(matched!=1) throw new IllegalStateException("Pinned Weave Mixin environment boundary mismatch: "+matched);
        StartupTrace.stage("mixin-current-environment-adapted");
        var writer=new ClassWriter(0); node.accept(writer); return writer.toByteArray();
    }
    // Evidence only: preserve original control flow and rethrow the same init failure.
    private static InsnList marker(String stage) {
        var code=new InsnList();
        code.add(new FieldInsnNode(Opcodes.GETSTATIC,"java/lang/System","err","Ljava/io/PrintStream;"));
        code.add(new LdcInsnNode("[Moonrise Weave] "+stage));
        code.add(new MethodInsnNode(Opcodes.INVOKEVIRTUAL,"java/io/PrintStream","println","(Ljava/lang/String;)V",false));
        return code;
    }
    private static byte[] observe(byte[] bytes, boolean constructor) {
        if(bytes==null) return null; // direct attachment tests have no class bytes
        var node=new ClassNode(); new ClassReader(bytes).accept(node,0);
        int matched=0;
        for(var method:node.methods) {
            if(constructor ? !method.name.equals("<init>") || !method.desc.equals("()V") :
                    !method.name.equals("init") || !method.desc.equals("(Ljava/lang/instrument/Instrumentation;)V")) continue;
            matched++;
            boolean selected=false;
            for(var insn:method.instructions.toArray()) {
                if(insn.getOpcode()==Opcodes.RETURN) method.instructions.insertBefore(insn,marker(constructor ? "service-created" : "init-complete"));
                if(!constructor && !selected && insn instanceof MethodInsnNode call && call.owner.equals("java/lang/instrument/Instrumentation") && call.name.equals("addTransformer")) {
                    method.instructions.insertBefore(insn,marker("service-selected=net.weavemc.loader.mixins.WeaveMixinService")); selected=true;
                }
            }
            if(!constructor) {
                var begin=new LabelNode(); var end=new LabelNode(); var handler=new LabelNode();
                method.instructions.insert(begin);
                method.instructions.insert(begin,marker("init-begin"));
                method.instructions.add(end); method.instructions.add(handler);
                method.instructions.add(new FrameNode(Opcodes.F_FULL,1,new Object[]{"java/lang/instrument/Instrumentation"},1,new Object[]{"java/lang/Throwable"}));
                method.instructions.add(marker("init-failed; first exception follows"));
                method.instructions.add(new InsnNode(Opcodes.DUP));
                method.instructions.add(new FieldInsnNode(Opcodes.GETSTATIC,"java/lang/System","err","Ljava/io/PrintStream;"));
                method.instructions.add(new MethodInsnNode(Opcodes.INVOKEVIRTUAL,"java/lang/Throwable","printStackTrace","(Ljava/io/PrintStream;)V",false));
                method.instructions.add(new InsnNode(Opcodes.ATHROW));
                method.tryCatchBlocks.add(new TryCatchBlockNode(begin,end,handler,"java/lang/Throwable"));
            }
        }
        if(matched!=1) throw new IllegalStateException("Pinned Weave observation boundary mismatch: "+matched);
        var writer=new ClassWriter(ClassWriter.COMPUTE_MAXS); node.accept(writer); return writer.toByteArray();
    }
}
