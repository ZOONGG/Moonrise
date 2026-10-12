import java.net.*;
import java.nio.file.*;
import java.util.*;
import java.util.jar.*;

// Run with Genesis on the application classpath, exactly as a premain host.
public final class WriterIsolationTest {
    public static void main(String[] args) throws Exception {
        ClassLoader tccl = Thread.currentThread().getContextClassLoader();
        URL genesis = Path.of(args[0]).toUri().toURL();
        try (var isolated = new URLClassLoader("MoonriseLegacyMapping",
                new URL[]{Path.of(args[1]).toUri().toURL(), genesis}, ClassLoader.getPlatformClassLoader())) {
            Class<?> api = isolated.loadClass("org.cadixdev.lorenz.io.MappingFormat");
            Class<?> provider = tccl.loadClass("org.cadixdev.lorenz.io.srg.SrgMappingFormat");
            for (Class<?> c : new Class<?>[]{api, provider, isolated.loadClass(provider.getName()),
                    isolated.loadClass("org.cadixdev.bombe.type.Type")})
                System.out.println(c.getName()+" source="+c.getProtectionDomain().getCodeSource().getLocation()
                    +" loader="+c.getClassLoader()+" version="+c.getPackage().getImplementationVersion());
            if (api.isAssignableFrom(provider)) throw new AssertionError("Expected distinct class identities");
            try {
                isolated.loadClass("org.cadixdev.lorenz.io.MappingFormats").getField("XSRG").get(null);
                throw new AssertionError("Original SPI failure not reproduced");
            } catch (ServiceConfigurationError expected) {
                if (!expected.getMessage().contains("SrgMappingFormat not a subtype")) throw expected;
                System.out.println("REPRODUCED: "+expected);
            }
            byte[] bytes = (byte[]) isolated.loadClass("dev.moonrise.mapping.LegacyMappingAdapter")
                .getMethod("convert", Path.class).invoke(null, Path.of(args[2]));
            String text = new String(bytes, java.nio.charset.StandardCharsets.UTF_8);
            long classes=text.lines().filter(s->s.startsWith("CL: ")).count();
            long methods=text.lines().filter(s->s.startsWith("MD: ")).count();
            long fields=text.lines().filter(s->s.startsWith("FD: ")).count();
            // Lorenz omits ten unchanged enclosing-class CL records; the reader
            // reconstructs them from inner classes. Preserve the original output.
            if(classes!=2507 || methods!=15072 || fields!=8737) throw new AssertionError(classes+"/"+methods+"/"+fields);
            Object restored = isolated.loadClass("org.cadixdev.lorenz.io.srg.xsrg.XSrgReader")
                .getConstructor(java.io.Reader.class).newInstance(new java.io.StringReader(text));
            Object set = restored.getClass().getMethod("read").invoke(restored);
            var semantics = isolated.loadClass("dev.moonrise.mapping.LegacyMappingAdapter")
                .getDeclaredMethod("semantics", isolated.loadClass("org.cadixdev.lorenz.MappingSet"));
            semantics.setAccessible(true);
            Set<?> records = (Set<?>) semantics.invoke(null, set);
            if(records.stream().filter(s->s.toString().startsWith("CL: ")).count()!=2517) throw new AssertionError("Semantic class count");
            if(!text.contains("CL: ave net/minecraft/client/Minecraft") || !text.contains("MD: ave/ak ()Ljava/util/Map; net/minecraft/client/Minecraft/getSessionInfo ()Ljava/util/Map;")) throw new AssertionError("Representative mappings");
            Files.write(Path.of(args[3]), bytes);
            if(Thread.currentThread().getContextClassLoader()!=tccl) throw new AssertionError("TCCL changed");
            System.out.println("PASS unchanged TCCL; poisoned MappingFormats bypassed; native KIN roundtrip; syntax; representatives; semantic counts=2517/"+methods+"/"+fields+"; explicit CL records="+classes);
        }
        try(var jar=new JarFile(args[0])) {
            for(var e: Collections.list(jar.entries())) if(e.getName().contains("lorenz") && !e.getName().endsWith(".class") && !e.isDirectory())
                System.out.println("metadata "+e.getName()+": "+new String(jar.getInputStream(e).readAllBytes(), java.nio.charset.StandardCharsets.UTF_8));
        }
    }
}
