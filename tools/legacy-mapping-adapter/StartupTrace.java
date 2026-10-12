package dev.moonrise.mapping;
import java.io.*;
import java.nio.file.*;
import java.time.Instant;
import java.util.Locale;
final class StartupTrace {
    static boolean enabled() { return System.getenv("MOONRISE_DEV_STARTUP_CAPTURE") != null; }
    static String sanitize(String line) {
        if(line.matches("\\s*at [A-Za-z0-9_.$/<>]+\\((?:[A-Za-z0-9_.$-]+\\.java:[0-9]+|Native Method|Unknown Source)\\)")) return line;
        String lower=line.toLowerCase(Locale.ROOT);
        for(String key:new String[]{"token","session","password","authorization","credential","bearer ","accounts.json","java_tool_options","commandline","command line","eyj"})
            if(lower.contains(key)) {
                String header=line.stripLeading();
                int message=header.indexOf(": ",header.startsWith("Caused by: ")?11:header.startsWith("Suppressed: ")?12:0);
                if(message>0 && header.substring(0,message).matches("(?:(?:Caused by|Suppressed): )?[A-Za-z0-9_.$]+(?:Exception|Error)"))
                    return header.substring(0,message)+": [sensitive diagnostic message redacted]";
                return "[sensitive diagnostic line redacted]";
            }
        return line;
    }
    static synchronized void stage(String stage) {
        if(!enabled()) return;
        write(Instant.now()+" pid="+ProcessHandle.current().pid()+" "+stage);
    }
    static void failure(Throwable failure) {
        if(!enabled()) return;
        StringWriter text=new StringWriter(); failure.printStackTrace(new PrintWriter(text));
        for(String line:text.toString().split("\\R")) write(sanitize(line));
    }
    private static void write(String line) {
        System.err.println("[Moonrise startup] "+line); System.err.flush();
        try(var writer=new PrintWriter(Files.newBufferedWriter(
                Path.of(System.getenv("MOONRISE_DEV_STARTUP_CAPTURE"),"jvm-"+ProcessHandle.current().pid()+"-mapping.log"),
                StandardOpenOption.CREATE,StandardOpenOption.APPEND))) {
            writer.println(line); writer.flush();
        } catch(Exception failure) {
            System.err.println("[Moonrise startup] trace-file-write-failed: "+failure.getClass().getName()); System.err.flush();
        }
    }
}
