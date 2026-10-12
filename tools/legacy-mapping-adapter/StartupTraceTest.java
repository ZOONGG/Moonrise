package dev.moonrise.mapping;
import java.nio.file.*;
public final class StartupTraceTest {
    public static void main(String[] args) throws Exception {
        try { MappingAgent.premain("bad",null); throw new AssertionError("invalid config accepted"); }
        catch(IllegalArgumentException expected) { }
        String frame="\tat example.TokenSession.premain(TokenSession.java:7)";
        if(!StartupTrace.sanitize(frame).equals(frame)) throw new AssertionError("stack frame redacted");
        Exception cause=new Exception("root failure");
        Exception outer=new Exception("outer failure",cause);
        outer.addSuppressed(new Exception("suppressed failure"));
        StartupTrace.failure(outer);
        StartupTrace.failure(new Exception("accessToken=SECRET"));
        Path file=Path.of(System.getenv("MOONRISE_DEV_STARTUP_CAPTURE"),"jvm-"+ProcessHandle.current().pid()+"-mapping.log");
        String text=Files.readString(file);
        for(String marker:new String[]{"premain-enter","launch-config-read-begin","IllegalArgumentException","Caused by:","Suppressed:","root failure","StartupTraceTest.main"})
            if(!text.contains(marker)) throw new AssertionError("missing "+marker);
        if(text.contains("SECRET")) throw new AssertionError("secret persisted");
        if(text.contains("xsrg-generation-begin")) throw new AssertionError("unreached stage recorded");
        System.out.println("Startup trace tests passed");
    }
}
