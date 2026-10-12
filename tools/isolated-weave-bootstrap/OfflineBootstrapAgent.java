import java.lang.instrument.*;
import java.security.ProtectionDomain;

/** Test-only premain; packaged exclusively under ignored runtime artifacts. */
public final class OfflineBootstrapAgent {
    static Instrumentation instrumentation;
    static boolean intercepted;
    public static void premain(String args, Instrumentation inst) {
        instrumentation = inst;
        inst.addTransformer(new ClassFileTransformer() {
            public byte[] transform(ClassLoader owner, String name, Class<?> type,
                    ProtectionDomain domain, byte[] bytes) {
                if (name.equals("me/pianopenguin471/RavenWeave")) {
                    try { OfflineLegacyCommand.verifyEntrypoint(owner, bytes); }
                    catch (Exception error) { throw new AssertionError(error); }
                }
                if (name.equals("net/minecraft/entity/Entity") || name.equals("net/minecraft/client/gui/GuiScreen")) {
                    if (owner != OfflineBootstrapAgent.class.getClassLoader())
                        throw new AssertionError("Unexpected game definition owner: " + owner);
                    System.out.println("GAME definition requested " + name + "; owner=" + owner);
                }
                if (name.equals("net/weavemc/loader/impl/mixin/SandboxedMixinLoader"))
                    return OfflineMixinBytes.intercept(bytes);
                if (!name.equals("net/weavemc/loader/impl/util/UtilityKt")) return null;
                try {
                    byte[] result = MetadataReadInterception.adapt(bytes);
                    intercepted = true;
                    System.out.println("INTERCEPTION installed in actual UtilityKt owner=" + owner);
                    return result;
                } catch (Throwable error) {
                    error.printStackTrace();
                    return null; // Main checks intercepted before constructor; never runs unadapted.
                }
            }
        });
    }
}
