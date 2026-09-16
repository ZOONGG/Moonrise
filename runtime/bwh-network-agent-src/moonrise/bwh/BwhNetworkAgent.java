package moonrise.bwh;

import java.lang.instrument.ClassFileTransformer;
import java.lang.instrument.Instrumentation;
import java.security.ProtectionDomain;
import java.util.Arrays;

/**
 * Redirects the exact supported BWH player endpoint to Moonrise's loopback relay.
 * The target and replacement have the same modified-UTF8 byte length, so the
 * third-party class structure is left untouched.
 */
public final class BwhNetworkAgent {
    private static final String TARGET_CLASS =
        "xyz/mangal/bwhelper/utils/APIUtilsKt$player$1";
    private static final byte[] OFFICIAL_ENDPOINT =
        ascii("https://api.hypixel.net/v2/player?uuid=");
    private static final byte[] LOOPBACK_ENDPOINT =
        ascii("http://127.0.0.1:45678/v2/player/?uuid=");

    private BwhNetworkAgent() {
    }

    public static void premain(String arguments, Instrumentation instrumentation) {
        install(instrumentation);
    }

    public static void agentmain(String arguments, Instrumentation instrumentation) {
        install(instrumentation);
    }

    private static void install(Instrumentation instrumentation) {
        if (instrumentation == null) {
            return;
        }
        if (OFFICIAL_ENDPOINT.length != LOOPBACK_ENDPOINT.length) {
            throw new IllegalStateException("BWH endpoint replacement length changed");
        }
        instrumentation.addTransformer(new EndpointTransformer(), false);
        System.out.println("[Moonrise] BWH ExitLag network adapter installed.");
    }

    private static byte[] ascii(String value) {
        byte[] bytes = new byte[value.length()];
        for (int index = 0; index < value.length(); index++) {
            char character = value.charAt(index);
            if (character > 0x7f) {
                throw new IllegalArgumentException("Endpoint must be ASCII");
            }
            bytes[index] = (byte) character;
        }
        return bytes;
    }

    private static final class EndpointTransformer implements ClassFileTransformer {
        @Override
        public byte[] transform(
            ClassLoader loader,
            String className,
            Class<?> classBeingRedefined,
            ProtectionDomain protectionDomain,
            byte[] classfileBuffer) {
            if (!TARGET_CLASS.equals(className) || classfileBuffer == null) {
                return null;
            }

            int match = findUnique(classfileBuffer, OFFICIAL_ENDPOINT);
            if (match < 0) {
                System.err.println("[Moonrise] Supported BWH endpoint marker was not found; network adapter skipped.");
                return null;
            }

            byte[] rewritten = Arrays.copyOf(classfileBuffer, classfileBuffer.length);
            System.arraycopy(LOOPBACK_ENDPOINT, 0, rewritten, match, LOOPBACK_ENDPOINT.length);
            System.out.println("[Moonrise] BWH player API routed outside ExitLag.");
            return rewritten;
        }

        private static int findUnique(byte[] haystack, byte[] needle) {
            int match = -1;
            for (int start = 0; start <= haystack.length - needle.length; start++) {
                int offset = 0;
                while (offset < needle.length && haystack[start + offset] == needle[offset]) {
                    offset++;
                }
                if (offset != needle.length) {
                    continue;
                }
                if (match >= 0) {
                    return -1;
                }
                match = start;
                start += needle.length - 1;
            }
            return match;
        }
    }
}
