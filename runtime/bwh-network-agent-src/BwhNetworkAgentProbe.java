/** Build-time smoke probe; this class is intentionally not packaged in the agent JAR. */
public final class BwhNetworkAgentProbe {
    public static void main(String[] arguments) throws Exception {
        Class.forName("xyz.mangal.bwhelper.utils.APIUtilsKt$player$1");
    }
}
