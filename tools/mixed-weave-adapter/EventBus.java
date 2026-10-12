package net.weavemc.loader.api.event;

import java.lang.reflect.*;
import java.util.*;
import java.util.function.Consumer;

/** Experimental runtime router. Translations are delivered locally, never reposted. */
public final class EventBus {
    private record Listener(Class<?> type, Object owner, Consumer<Event> callback) {}
    private static final List<Listener> listeners = new ArrayList<>();
    private static final Set<Object> owners = Collections.newSetFromMap(new IdentityHashMap<>());
    private static final ThreadLocal<Boolean> routing = ThreadLocal.withInitial(() -> false);
    private static final Map<String, String> mappings = Map.ofEntries(
        Map.entry("StartGameEvent$Post", "StartGameEvent$Post"), Map.entry("ShutdownEvent", "ShutdownEvent"),
        Map.entry("KeyboardEvent", "KeyboardEvent"), Map.entry("ChatEvent$Sent", "ChatSentEvent"),
        Map.entry("ChatEvent$Received", "ChatReceivedEvent"), Map.entry("MouseEvent", "MouseEvent"),
        Map.entry("TickEvent$Pre", "TickEvent$Pre"), Map.entry("TickEvent$Post", "TickEvent$Post"),
        Map.entry("WorldEvent$Load", "WorldEvent$Load"), Map.entry("WorldEvent$Unload", "WorldEvent$Unload"),
        Map.entry("RenderGameOverlayEvent$Pre", "RenderGameOverlayEvent$Pre"), Map.entry("RenderGameOverlayEvent$Post", "RenderGameOverlayEvent$Post"),
        Map.entry("RenderWorldEvent", "RenderWorldEvent"), Map.entry("RenderHandEvent", "RenderHandEvent"),
        Map.entry("RenderLivingEvent$Pre", "RenderLivingEvent$Pre"), Map.entry("RenderLivingEvent$Post", "RenderLivingEvent$Post"));
    static { net.weavemc.api.event.EventBus.subscribe(net.weavemc.api.event.Event.class, EventBus::route); }

    @SuppressWarnings("unchecked")
    public static synchronized <T extends Event> void subscribe(Class<T> type, Consumer<T> callback) {
        Objects.requireNonNull(type); Objects.requireNonNull(callback);
        if (listeners.stream().anyMatch(l -> l.type == type && l.owner == callback)) return;
        listeners.add(new Listener(type, callback, e -> callback.accept((T) e)));
    }
    public static synchronized void subscribe(Object owner) {
        if (owners.contains(owner)) return;
        var pending = new ArrayList<Listener>();
        // Legacy contract: declared methods only; no unspecified inherited-handler extension.
        for (Method method : owner.getClass().getDeclaredMethods()) {
            if (!method.isAnnotationPresent(SubscribeEvent.class)) continue;
            if (method.getParameterCount() != 1 || !Event.class.isAssignableFrom(method.getParameterTypes()[0]))
                throw new IllegalArgumentException("Unsupported subscriber: " + method);
            method.setAccessible(true);
            pending.add(new Listener(method.getParameterTypes()[0], owner, e -> {
                try { method.invoke(owner, e); }
                catch (ReflectiveOperationException error) { throw new IllegalStateException(error); }
            }));
        }
        listeners.addAll(pending); owners.add(owner);
    }
    public static synchronized void unsubscribe(Object owner) {
        listeners.removeIf(l -> l.owner == owner); owners.remove(owner);
    }
    public static void unsubscribe(Consumer<? extends Event> callback) { unsubscribe((Object) callback); }
    public static <T extends Event> void callEvent(T event) {
        if (routing.get()) return; // A listener cannot recursively repost a translated event.
        net.weavemc.api.event.EventBus.postEvent((net.weavemc.api.event.Event) (Object) event);
    }
    private static void dispatch(Event event) {
        List<Listener> snapshot;
        synchronized (EventBus.class) { snapshot = List.copyOf(listeners); }
        for (Class<?> type = event.getClass(); Event.class.isAssignableFrom(type); type = type.getSuperclass())
            for (Listener listener : snapshot) if (listener.type == type) listener.callback.accept(event);
    }
    private static void route(net.weavemc.api.event.Event source) {
        if (routing.get()) return;
        routing.set(true);
        try {
            if ((Object) source instanceof Event legacy) { dispatch(legacy); return; }
            String name = source.getClass().getName();
            String prefix = "net.weavemc.api.event.";
            String target = name.startsWith(prefix) ? mappings.get(name.substring(prefix.length())) : null;
            if (target == null) return;
            Class<?> type = Class.forName("net.weavemc.loader.api.event." + target, true, EventBus.class.getClassLoader());
            Event translated;
            if (target.startsWith("Chat")) {
                Method getter = source.getClass().getMethod("getMessage");
                translated = (Event) type.getConstructor(getter.getReturnType()).newInstance(getter.invoke(source));
            } else if (target.equals("KeyboardEvent")) {
                translated = (Event) type.getConstructor().newInstance();
                // Copy the captured payload, not the potentially advanced LWJGL input state.
                for (String field : List.of("keyCode", "keyState")) {
                    Field payload = type.getDeclaredField(field); payload.setAccessible(true);
                    String getter = field.equals("keyCode") ? "getKeyCode" : "getKeyState";
                    payload.set(translated, source.getClass().getMethod(getter).invoke(source));
                }
            } else if (target.equals("MouseEvent")) {
                translated = (Event) type.getConstructor().newInstance();
                for (String field : List.of("x", "y", "dx", "dy", "dwheel", "button", "buttonState", "nanoseconds")) {
                    String getter = switch (field) { case "dx" -> "getDX"; case "dy" -> "getDY"; case "dwheel" -> "getDWheel";
                        default -> "get" + Character.toUpperCase(field.charAt(0)) + field.substring(1); };
                    Field payload = type.getDeclaredField(field); payload.setAccessible(true);
                    payload.set(translated, source.getClass().getMethod(getter).invoke(source));
                }
            } else if (target.startsWith("RenderLivingEvent")) {
                String[] getters = {"getRenderer", "getEntity", "getX", "getY", "getZ", "getPartialTicks"};
                Class<?>[] types = new Class<?>[getters.length]; Object[] values = new Object[getters.length];
                for (int i = 0; i < getters.length; i++) {
                    Method getter = source.getClass().getMethod(getters[i]); types[i] = getter.getReturnType(); values[i] = getter.invoke(source);
                }
                translated = (Event) type.getConstructor(types).newInstance(values);
            } else if (target.startsWith("Render")) {
                translated = (Event) type.getConstructor(float.class).newInstance(source.getClass().getMethod("getPartialTicks").invoke(source));
            } else if (target.startsWith("WorldEvent")) {
                Method getter = source.getClass().getMethod("getWorld");
                translated = (Event) type.getConstructor(getter.getReturnType()).newInstance(getter.invoke(source));
            } else translated = (Event) type.getField("INSTANCE").get(null);
            if (translated instanceof CancellableEvent cancellable) {
                Method read = source.getClass().getMethod("isCancelled");
                Method write = source.getClass().getMethod("setCancelled", boolean.class);
                cancellable.setCancelled((Boolean) read.invoke(source));
                try { dispatch(translated); }
                finally { write.invoke(source, cancellable.isCancelled()); }
            } else dispatch(translated);
        } catch (ReflectiveOperationException error) { throw new IllegalStateException("Event contract mismatch", error); }
        finally { routing.remove(); }
    }
}
