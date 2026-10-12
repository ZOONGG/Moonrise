package net.weavemc.loader.api.event;

import java.lang.reflect.*;
import java.util.*;
import java.util.function.Consumer;

/** Offline router. Translations are delivered locally, never reposted to current bus. */
public final class EventBus {
    private record Listener(Class<?> type, Object owner, Consumer<Event> callback) {}
    private static final List<Listener> listeners = new ArrayList<>();
    private static final Set<Object> owners = Collections.newSetFromMap(new IdentityHashMap<>());
    private static final ThreadLocal<Boolean> routing = ThreadLocal.withInitial(() -> false);
    private static final Map<String, String> mappings = Map.of(
        "StartGameEvent$Post", "StartGameEvent$Post", "ShutdownEvent", "ShutdownEvent",
        "KeyboardEvent", "KeyboardEvent", "ChatEvent$Sent", "ChatSentEvent",
        "ChatEvent$Received", "ChatReceivedEvent");
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
