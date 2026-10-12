package net.weavemc.loader.api;

/** Offline ABI facade: no loader bootstrap. */
public interface ModInitializer extends net.weavemc.api.ModInitializer {
    void preInit();
    @Override default void preInit(java.lang.instrument.Instrumentation instrumentation) {
        preInit();
        if (Boolean.getBoolean("moonrise.proof.isolatedBootstrap") &&
                getClass().getName().equals("me.pianopenguin471.RavenWeave"))
            System.out.println("PASS actual original Raven preInit returned; init deferred");
    }
}
