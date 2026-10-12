import java.io.*;
import java.nio.file.*;
import java.util.*;
import java.util.jar.*;
import org.cadixdev.lorenz.*;
import org.cadixdev.lorenz.io.*;
import org.cadixdev.lorenz.model.*;

/** Offline evidence only. Uses the exact snapshot's own KIN reader, never installs resources. */
public class LegacyMappingProbe {
    static final String READER = "com.moonsworth.lunar.ICCRRCCIOOIRICIHHCIOROHHROOHHR.RCHICCCIHRCRCHCIRRIIRRHICORHHO.IHIRRHORRRRHICCOHOHORCHIHHHIOO";
    static void classes(ClassMapping<?,?> c, Map<String,ClassMapping<?,?>> out) {
        if(out.put(c.getFullObfuscatedName(),c)!=null) throw new IllegalStateException("Duplicate class");
        for(var inner:c.getInnerClassMappings()) classes(inner,out);
    }
    static Map<String,ClassMapping<?,?>> flatten(MappingSet set) {
        var out=new TreeMap<String,ClassMapping<?,?>>();
        for(var c:set.getTopLevelClassMappings()) classes(c,out);
        return out;
    }
    static SortedSet<String> semantics(MappingSet set) {
        var out=new TreeSet<String>();
        for(var c:flatten(set).values()) {
            out.add("CL: "+c.getFullObfuscatedName()+" "+c.getFullDeobfuscatedName());
            for(var f:c.getFieldMappings()) out.add("FD: "+f.getFullObfuscatedName()+" "+f.getType().orElseThrow()+" "+f.getFullDeobfuscatedName()+" "+f.getDeobfuscatedSignature().getType().orElseThrow());
            for(var m:c.getMethodMappings()) out.add("MD: "+m.getFullObfuscatedName()+" "+m.getObfuscatedDescriptor()+" "+m.getFullDeobfuscatedName()+" "+m.getDeobfuscatedDescriptor());
        }
        return out;
    }
    public static void main(String[] args) throws Exception {
        if(args.length!=2) throw new IllegalArgumentException("Usage: LegacyMappingProbe <mapping-jar> <evidence-xsrg-path>");
        var all=new TreeMap<String,Map<String,ClassMapping<?,?>>>();
        try(var jar=new JarFile(args[0])) {
            for(var entry:Collections.list(jar.entries())) if(entry.getName().endsWith(".kin")) {
                try(var input=jar.getInputStream(entry); var reader=(MappingsReader)Class.forName(READER).getConstructor(InputStream.class).newInstance(input)) {
                    var set=reader.read(); var cs=flatten(set); all.put(entry.getName(),cs); cs.values().stream().filter(c->c.getFullObfuscatedName().equals("net/minecraft/client/Minecraft") || c.getFullDeobfuscatedName().equals("net/minecraft/client/Minecraft") || c.getFullDeobfuscatedName().equals("net/minecraft/client/renderer/EntityRenderer")).forEach(c->System.out.println("RUNTIME-ANCHOR "+entry.getName()+" "+c.getFullObfuscatedName()+" -> "+c.getFullDeobfuscatedName()+" first-method="+c.getMethodMappings().stream().findFirst().map(m->m.getFullObfuscatedName()+m.getObfuscatedDescriptor()+" -> "+m.getFullDeobfuscatedName()).orElse("none")));
                    if(!entry.getName().contains("named_b5")) continue; var text=new StringWriter();
                    try(var writer=MappingFormats.XSRG.createWriter(text)){writer.write(set);}
                    var legacyClasses=new HashMap<String,String>();
                    var legacyFields=new HashMap<String,String>();
                    var legacyMethods=new HashMap<String,String>();
                    for(String line:text.toString().split("\\R")) {
                        if(line.isEmpty()) continue;
                        String[] parts=line.substring(4).split(" ");
                        switch(line.substring(0,4)) {
                            case "CL: ":
                                if(parts.length!=2) throw new IllegalStateException("CL syntax");
                                legacyClasses.put(parts[0],parts[1]); break;
                            case "FD: ":
                                if(parts.length!=4) throw new IllegalStateException("FD syntax");
                                legacyFields.put(parts[0],parts[2]); break;
                            case "MD: ":
                                if(parts.length!=4) throw new IllegalStateException("MD syntax");
                                legacyMethods.put(parts[0]+parts[1],parts[2]); break;
                            default: throw new IllegalStateException("Unexpected XSRG record");
                        }
                    }
                    for(var c:cs.values()) {
                        if(!c.getFullDeobfuscatedName().equals(legacyClasses.getOrDefault(c.getFullObfuscatedName(),c.getFullObfuscatedName()))) throw new IllegalStateException("Class direction: "+c.getFullObfuscatedName()+" -> "+c.getFullDeobfuscatedName());
                        for(var f:c.getFieldMappings()) if(!f.getFullDeobfuscatedName().equals(legacyFields.get(f.getFullObfuscatedName()))) throw new IllegalStateException("Field erasure conflict");
                        for(var m:c.getMethodMappings()) if(!m.getFullDeobfuscatedName().equals(legacyMethods.get(m.getFullObfuscatedName()+m.getObfuscatedDescriptor()))) throw new IllegalStateException("Method descriptor/direction");
                    }
                    System.out.println("Legacy XSRG record syntax/source-key semantics=PASS");
                    MappingSet restored;
                    try(var r=MappingFormats.XSRG.createReader(new StringReader(text.toString()))){restored=r.read();}
                    boolean roundtrip=semantics(set).equals(semantics(restored)); if(!roundtrip && entry.getName().contains("named_b5")) throw new IllegalStateException("Lossy candidate");
                    long fields=cs.values().stream().mapToLong(c->c.getFieldMappings().size()).sum();
                    long methods=cs.values().stream().mapToLong(c->c.getMethodMappings().size()).sum();
                    System.out.println(entry.getName()+" classes="+cs.size()+" fields="+fields+" methods="+methods+" XSRG-roundtrip="+roundtrip);
                    if(entry.getName().contains("named_b5")) {
                        var fieldKeys=new HashMap<String,String>();
                        for(var c:cs.values()) for(var f:c.getFieldMappings()) {
                            var old=fieldKeys.putIfAbsent(f.getFullObfuscatedName(),f.getFullDeobfuscatedName());
                            if(old!=null && !old.equals(f.getFullDeobfuscatedName())) throw new IllegalStateException("Legacy field descriptor erasure conflict: "+f.getFullObfuscatedName());
                        }
                        System.out.println("Legacy field descriptor-erasure=PASS");
                        Files.writeString(Path.of(args[1]),text.toString());
                        cs.values().stream().filter(c->c.getFullDeobfuscatedName().equals("net/minecraft/client/Minecraft") || c.getFullDeobfuscatedName().equals("net/minecraft/util/EnumChatFormatting")).forEach(c->System.out.println("ANCHOR "+c.getFullObfuscatedName()+" -> "+c.getFullDeobfuscatedName()));
                    }
                }
            }
        }
        var named=all.get("lunar/lunar_named_b5_1.8.9.kin"); if(named==null) throw new IllegalStateException("MISSING_MAPPING: lunar/lunar_named_b5_1.8.9.kin");
        var current=all.get("lunar/lunar2searge_1.8.9.kin");
        long matchingFields=0,matchingMethods=0;
        for(var c:named.values()) {
            var runtime=current.get(c.getFullObfuscatedName()); if(runtime==null) continue;
            for(var f:c.getFieldMappings()) if(runtime.getFieldMapping(f.getSignature()).isPresent()) matchingFields++;
            for(var m:c.getMethodMappings()) if(runtime.getMethodMapping(m.getSignature()).isPresent()) matchingMethods++;
        }
        System.out.println("DIRECT current-Lunar source-key coverage: classes="+named.keySet().stream().filter(current::containsKey).count()+"/"+named.size()+" fields="+matchingFields+"/"+named.values().stream().mapToLong(c->c.getFieldMappings().size()).sum()+" methods="+matchingMethods+"/"+named.values().stream().mapToLong(c->c.getMethodMappings().size()).sum());
        System.out.println("VERDICT: KIN conversion proven; actual Genesis vanilla-obfuscated resource alias/bytecode contract UNVERIFIED. Do not install this candidate.");
        for(var e:all.entrySet()) {
            var src=e.getValue().keySet(); var dst=new HashSet<String>();
            e.getValue().values().forEach(c->dst.add(c.getFullDeobfuscatedName()));
            System.out.println("OVERLAP named source vs "+e.getKey()+" source="+named.keySet().stream().filter(src::contains).count()+" target="+named.keySet().stream().filter(dst::contains).count());
        }
    }
}
