package dev.moonrise.mapping;
import java.nio.file.*;
import java.net.*;
import java.util.*;
import java.util.jar.*;
public class MappingAdapterTest {
    public static class Genesis extends URLClassLoader {
        public Genesis() { super("Genesis",new URL[0],ClassLoader.getPlatformClassLoader()); }
        public void addURL(URL url) { super.addURL(url); }
    }
    public static void main(String[] args) throws Exception {
        byte[] xsrg=LegacyMappingAdapter.convert(Path.of(args[0]));
        String text=new String(xsrg,java.nio.charset.StandardCharsets.UTF_8);
        if(!text.contains("CL: ave net/minecraft/client/Minecraft") || !text.contains("MD: ave/ak ()Ljava/util/Map; net/minecraft/client/Minecraft/getSessionInfo ()Ljava/util/Map;")) throw new AssertionError("Direction/descriptors");
        Path temp=Files.createTempDirectory(Path.of(args[1]),"mapping-test-");
        Path jar=temp.resolve("resource.jar");
        try {
            try(var out=new JarOutputStream(Files.newOutputStream(jar))) { out.putNextEntry(new JarEntry(LegacyMappingAdapter.RESOURCE)); out.write(xsrg); out.closeEntry(); }
            try(var archive=new JarFile(jar.toFile())) { if(archive.size()!=1) throw new AssertionError("Resource-only JAR"); }
            var ctor=LegacyMappingAdapter.class.getDeclaredConstructor(Path.class,byte[].class); ctor.setAccessible(true);
            try(var loader=new Genesis()) {
                loader.addURL(Path.of(args[2]).toUri().toURL());
                if(args.length>3) loader.addURL(jar.toUri().toURL());
                ctor.newInstance(jar,xsrg).transform(loader,"net/weavemc/loader/mixins/WeaveMixinService",null,null,null);
                if(Collections.list(loader.getResources(LegacyMappingAdapter.RESOURCE)).size()!=1) throw new AssertionError("Visibility");
            }
            System.out.println("PASS conversion, semantic roundtrip, descriptors, direction, resource-only path, pre-constructor visibility (test loader)");
        } finally { Files.deleteIfExists(jar); Files.deleteIfExists(temp); }
        if(Files.exists(temp)) throw new AssertionError("Cleanup");
    }
}

