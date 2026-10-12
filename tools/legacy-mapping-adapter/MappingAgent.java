package dev.moonrise.mapping;
import java.lang.instrument.*;
import java.net.*;
import java.nio.file.*;
public final class MappingAgent {
    public static void premain(String options, Instrumentation inst) throws Exception {
        if(System.getenv("MOONRISE_DEV_STARTUP_CAPTURE")!=null) { System.err.println("[Moonrise startup] pid="+ProcessHandle.current().pid()+" premain-enter"); System.err.flush(); }
        try {
        StartupTrace.stage("premain-enter");
        StartupTrace.stage("launch-config-read-begin");
        String[] p=options.split("\\|",-1);
        if(p.length!=3) throw new IllegalArgumentException("Genesis|mapping|session resource JAR required");
        StartupTrace.stage("launch-config-read-complete; option-fields=3; isolated-parent=platform");
        URL self=MappingAgent.class.getProtectionDomain().getCodeSource().getLocation();
        var isolated=new URLClassLoader("MoonriseLegacyMapping",new URL[]{self,Path.of(p[0]).toUri().toURL()},ClassLoader.getPlatformClassLoader());
        StartupTrace.stage("adapter-classload-begin");
        isolated.loadClass("dev.moonrise.mapping.LegacyMappingAdapter").getMethod("install",Instrumentation.class,String.class,String.class).invoke(null,inst,p[1],p[2]);
        StartupTrace.stage("premain-complete");
        } catch(Exception | Error failure) { StartupTrace.failure(failure); throw failure; }
    }
}
