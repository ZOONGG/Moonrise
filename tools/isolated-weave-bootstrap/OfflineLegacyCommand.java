import java.nio.file.*;
import java.util.*;

/** Selective packaging of the existing proof contract; never includes old runtime. */
public final class OfflineLegacyCommand {
    public static void main(String[] args) throws Exception {
        Path old = Path.of(args[0]);
        if (!MixedWeaveProof.hash(old).equals("c4200d145c0d3d78f57eab4ddee3a64ff1a3176e65f2e2b15a31777a0142b524"))
            throw new AssertionError("Legacy contract input hash changed");
        try (var contract = new MixedWeaveProof.ApiLoader(old, Path.of(args[1]), Path.of(args[2]), Path.of(args[3]))) {
            // CommandBus <clinit> directly requires ChatSentEvent and its base types.
            // Use exactly the same selective contract/remapping as the ABI proof.
            for (String name : List.of("command.Command", "command.CommandBus",
                    "event.ChatSentEvent", "event.CancellableEvent", "event.Event",
                    "event.StartGameEvent", "event.StartGameEvent$Post", "event.KeyboardEvent",
                    "event.ShutdownEvent", "event.SubscribeEvent")) {
                name = "net.weavemc.loader.api." + name;
                Path output = Path.of(args[4]).resolve(name.replace('.', '/') + ".class");
                Files.createDirectories(output.getParent());
                Files.write(output, contract.classBytes(name));
            }
        }
    }
    static void verify(ClassLoader backing) throws Exception {
        ClassLoader actual = net.weavemc.loader.impl.WeaveLoader.class.getClassLoader();
        String name = "net.weavemc.loader.api.command.Command";
        Class<?> command = Class.forName(name, false, actual);
        if (command.getClassLoader() != actual || Class.forName(name, false, backing) != command ||
            Class.forName(name, false, actual) != command)
            throw new AssertionError("Legacy Command split/duplicate identity");
        System.out.println("PASS legacy Command single/repeated identity; owner=" + command.getClassLoader());
        Class<?> bus = Class.forName("net.weavemc.loader.api.command.CommandBus", false, actual);
        if (bus.getClassLoader() != actual || Class.forName(bus.getName(), false, backing) != bus ||
                Class.forName(bus.getName(), false, actual) != bus ||
                Collections.list(actual.getResources(bus.getName().replace('.', '/') + ".class")).size() != 1)
            throw new AssertionError("Legacy CommandBus split/duplicate identity");
        if (bus.getDeclaredMethod("register", command).getReturnType() != void.class)
            throw new AssertionError("Legacy register descriptor changed");
        System.out.println("PASS legacy CommandBus single/repeated identity; owner=" + actual);
        for (String suffix : List.of("command.Command", "event.ChatSentEvent", "event.CancellableEvent", "event.Event",
                "event.StartGameEvent", "event.StartGameEvent$Post", "event.KeyboardEvent",
                "event.ShutdownEvent", "event.SubscribeEvent")) {
            String shared = "net.weavemc.loader.api." + suffix;
            Class<?> type = Class.forName(shared, false, actual);
            if (type.getClassLoader() != actual || Class.forName(shared, false, backing) != type ||
                    Collections.list(actual.getResources(shared.replace('.', '/') + ".class")).size() != 1)
                throw new AssertionError("Shared legacy ABI split/duplicate: " + shared);
            System.out.println("PASS shared legacy ABI identity AppClassLoader/backing Owner " + shared);
        }
    }
    static void verifyEntrypoint(ClassLoader actual, byte[] bytes) throws Exception {
        Class<?> command = Class.forName("net.weavemc.loader.api.command.Command", false, actual);
        Class<?> current = Class.forName("net.weavemc.api.command.Command", false, actual);
        if (current.getClassLoader() != actual || command.getClassLoader() != actual)
            throw new AssertionError("Unexpected command definition owner");
        if (current == command || current.isAssignableFrom(command) || command.isAssignableFrom(current))
            throw new AssertionError("Current Command ABI mixed with legacy");
        System.out.println("PASS current Command independent; owner=" + current.getClassLoader());
        if (Class.forName("net.weavemc.loader.api.command.CommandBus", false, actual) !=
                Class.forName("net.weavemc.loader.api.command.CommandBus", false, OfflineBootstrap.class.getClassLoader()))
            throw new AssertionError("Raven CommandBus identity differs from backing owner");
        System.out.println("PASS Raven CommandBus identity equals backing Owner");
        try (var jar = new java.util.jar.JarFile(System.getProperty("moonrise.proof.raven"))) {
            if (!Arrays.equals(bytes, jar.getInputStream(jar.getJarEntry("me/pianopenguin471/RavenWeave.class")).readAllBytes()))
                throw new AssertionError("Original Raven entrypoint bytes changed");
        }
        System.out.println("PASS original Raven entrypoint bytes unchanged; initializeMods owner=" + actual);
    }
    static void verifyRegistration() throws Exception {
        ClassLoader actual = OfflineBootstrap.class.getClassLoader();
        Class<?> bus = Class.forName("net.weavemc.loader.api.command.CommandBus", false, actual);
        var field = bus.getDeclaredField("commands");
        field.setAccessible(true);
        List<?> commands = (List<?>) field.get(null);
        if (commands.size() != 1 || !commands.get(0).getClass().getName().equals("me.pianopenguin471.command.GUICommand"))
            throw new AssertionError("Original GUICommand registration count/content: " + commands);
        Object gui = commands.get(0);
        Class<?> raven = Class.forName("me.pianopenguin471.RavenWeave", false, actual);
        Class<?> eventBus = Class.forName("net.weavemc.loader.api.event.EventBus", false, actual);
        var ownersField = eventBus.getDeclaredField("owners");
        ownersField.setAccessible(true);
        long subscribed = ((Set<?>) ownersField.get(null)).stream().filter(o -> o.getClass() == raven).count();
        if (subscribed != 1) throw new AssertionError("Actual Raven subscriber count: " + subscribed);
        int handlers = 0;
        for (var method : raven.getDeclaredMethods()) {
            if (method.getName().equals("onGameStart") || method.getName().equals("onKeyPress") || method.getName().equals("onShutdown")) {
                Class<?> parameter = method.getParameterTypes()[0];
                if (parameter != Class.forName(parameter.getName(), false, actual) || parameter.getClassLoader() != actual)
                    throw new AssertionError("Raven handler event identity split");
                handlers++;
            }
        }
        if (handlers != 3) throw new AssertionError("Incomplete Raven handler reflection");
        System.out.println("PASS actual Raven getDeclaredMethods; three handler identities; EventBus.subscribe(this) completed once");
        Class<?> command = Class.forName("net.weavemc.loader.api.command.Command", false, actual);
        if (gui.getClass().getClassLoader() != actual || gui.getClass().getSuperclass() != command ||
                !"clickgui".equals(command.getField("name").get(gui)) ||
                !Arrays.equals((String[]) command.getField("aliases").get(gui), new String[]{"gui", "raven"}))
            throw new AssertionError("Original GUICommand identity/arguments changed");
        System.out.println("PASS actual bootstrap original GUICommand registered exactly once; name=clickgui; aliases=[gui, raven]");
    }
}
