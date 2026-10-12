package dev.moonrise.contract;

import java.io.*;
import java.lang.instrument.*;
import java.net.*;
import java.nio.file.*;
import java.security.*;
import java.util.*;
import org.objectweb.asm.*;
import org.objectweb.asm.commons.*;
import org.objectweb.asm.tree.*;

/** Targeted observations only. Never alters game or host bytecode. */
public final class GenesisContractTrace implements ClassFileTransformer {
    private final PrintStream log;
    private final Path evidence;
    private final URL marker;
    private final Remapper mapper;
    private final Object actualMapper;
    private final java.lang.reflect.Method reverse;
    private final List<String[]> records = new ArrayList<>();
    private boolean checked;
    private GenesisContractTrace(String directory, String markerJar) throws Exception {
        evidence = Path.of(directory);
        log = new PrintStream(Files.newOutputStream(evidence.resolve("contract.log")), true, "UTF-8");
        marker = Path.of(markerJar).toUri().toURL();
        Class<?> type = Class.forName("net.weavemc.loader.mappings.XSrgMapper");
        actualMapper = type.getConstructor(String.class).newInstance("/dev/moonrise/contract/candidate.xsrg");
        mapper = (Remapper) actualMapper;
        reverse = type.getMethod("mapReverse", String.class);
        for (String line : Files.readAllLines(evidence.resolve("candidate.xsrg")))
            if (!line.isEmpty()) records.add(line.split(" "));
        log.println("INSTALLED read-only transformer; actual isolated XSrgMapper; no service resource supplied");
    }
    public static void install(Instrumentation inst, String directory, String markerJar) throws Exception {
        inst.addTransformer(new GenesisContractTrace(directory, markerJar));
    }
    private String id(ClassLoader loader) {
        return loader == null ? "bootstrap" : loader.getClass().getName()+"@"+
            Integer.toHexString(System.identityHashCode(loader))+" name="+loader.getName();
    }
    private void state(ClassLoader loader) throws Exception {
        log.println("PIPELINE " + loader.getClass().getMethod("ROOCIHHOCOIHCCORHRIRHRRICOHCRO").invoke(loader));
    }
    private ClassNode resource(ClassLoader loader, String name) {
        try {
            log.println("URL " + name + " " + loader.getResource(name));
            try (InputStream stream = loader.getResourceAsStream(name)) {
                if (stream == null) { log.println("STREAM " + name + " NULL"); return null; }
                byte[] bytes = stream.readAllBytes();
                log.println("STREAM " + name + " bytes="+bytes.length+" sha256="+
                    HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(bytes)));
                ClassNode node = new ClassNode();
                new ClassReader(bytes).accept(node, 0);
                log.println("INTERNAL " + name + " " + node.name);
                Files.write(evidence.resolve(name.replace('/', '_')), bytes);
                return node;
            }
        } catch (Throwable e) { log.println("RESOURCE ERROR " + name); e.printStackTrace(log); return null; }
    }
    private boolean member(ClassNode node, boolean method, String name, String desc) {
        return method ? node.methods.stream().anyMatch(m -> m.name.equals(name) && m.desc.equals(desc)) :
            node.fields.stream().anyMatch(f -> f.name.equals(name) && f.desc.equals(desc));
    }
    private void sample(ClassLoader loader, String source) throws Exception {
        String target = mapper.map(source);
        String back = (String) reverse.invoke(actualMapper, target);
        log.println("MAP " + source+" -> "+target+" REVERSE "+back);
        ClassNode raw = resource(loader, back+".class");
        ClassNode named = resource(loader, target+".class");
        if (raw != null) {
            ClassNode mapped = new ClassNode();
            raw.accept(new ClassRemapper(mapped, mapper));
            log.println("REMAPPED INTERNAL " + mapped.name + " expected="+target);
        }
        // Bounded sample: first two methods with MC object/array types, two fields,
        // and known Minecraft method/field. No complete mapping audit.
        int methods = 0, fields = 0;
        for (String[] r : records) {
            boolean m = r[0].equals("MD:");
            boolean f = r[0].equals("FD:");
            if ((!m && !f) || !r[1].startsWith(source+"/")) continue;
            String sn = r[1].substring(r[1].lastIndexOf('/')+1);
            String tn = r[3].substring(r[3].lastIndexOf('/')+1);
            boolean known = source.equals("ave") && (tn.equals("loadWorld") || tn.equals("getMinecraft") || tn.equals("thePlayer") || tn.equals("getSessionInfo"));
            if (!known && (m ? methods >= 2 || !(r[2].matches(".*L[a-z]{1,3}[;$].*") || r[2].contains("[")) : fields >= 2)) continue;
            if (m) methods++; else fields++;
            String mappedName = m ? mapper.mapMethodName(source,sn,r[2]) : mapper.mapFieldName(source,sn,r[2]);
            String mappedDesc = m ? mapper.mapMethodDesc(r[2]) : mapper.mapDesc(r[2]);
            log.println("MEMBER "+String.join(" ",r)+" actualMapper="+mappedName+" "+mappedDesc+
                " descriptorOK="+mappedDesc.equals(r[4])+
                " sourceInReverseBytes="+(raw != null && member(raw,m,sn,r[2]))+
                " targetInNamedBytes="+(named != null && member(named,m,tn,r[4])));
        }
    }
    private void marker(ClassLoader loader) throws Exception {
        String path = "lunar/moonrise_genesis_contract_probe.xsrg";
        log.println("MARKER BEFORE " + loader.getResource(path));
        loader.getClass().getMethod("addURL", URL.class).invoke(loader, marker);
        log.println("MARKER AFTER " + loader.getResource(path));
        try (InputStream stream = loader.getResourceAsStream(path)) {
            log.println("MARKER CONTENT " + (stream == null ? "NULL" : new String(stream.readAllBytes(), java.nio.charset.StandardCharsets.UTF_8).trim()));
        }
        log.println("REQUIRED XSRG STILL " + loader.getResource("lunar/lunar_named_b5_1.8.9.xsrg"));
    }
    @Override public synchronized byte[] transform(ClassLoader loader, String name, Class<?> old, ProtectionDomain pd, byte[] bytes) {
        if (name == null) return null;
        if (name.startsWith("net/minecraft/client/") || name.equals("net/weavemc/loader/WeaveLoader") || name.equals("net/weavemc/loader/mappings/XSrgMapper"))
            log.println("DEFINE "+name+" loader="+id(loader));
        if (!checked && name.equals("net/weavemc/loader/mixins/WeaveMixinService")) {
            checked = true;
            log.println("BOUNDARY before WeaveMixinService definition/constructor loader="+id(loader));
            for (StackTraceElement frame : Thread.currentThread().getStackTrace()) log.println("AT "+frame);
            try {
                state(loader);
                for (String source : List.of("ave", "bew", "adm", "a")) sample(loader,source);
                marker(loader);
                state(loader);
                log.println("CONTRACT COMPLETE");
            } catch (Throwable e) { log.println("PROBE FAILURE"); e.printStackTrace(log); }
        }
        return null;
    }
}
