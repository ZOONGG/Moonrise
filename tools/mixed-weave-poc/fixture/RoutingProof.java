package fixture;
import java.util.*;
import java.util.function.Consumer;
import net.weavemc.loader.api.event.*;
public final class RoutingProof {
    static void check(boolean ok, String message) { if (!ok) throw new AssertionError(message); }
    @SuppressWarnings({"rawtypes", "unchecked"})
    public static void run() throws Exception {
        var trace = new ArrayList<String>();
        Consumer<Event> first = e -> { trace.add("first"); EventBus.callEvent(e); };
        Consumer<Event> second = e -> trace.add("second");
        Consumer<Event> base = e -> trace.add("base");
        EventBus.subscribe(Event.class, base);
        int[] wildcard = {0};
        Consumer<net.weavemc.api.event.Event> observer = e -> wildcard[0]++;
        net.weavemc.api.event.EventBus.subscribe(net.weavemc.api.event.Event.class, observer);
        String[] current = {"StartGameEvent$Post", "ShutdownEvent", "KeyboardEvent", "ChatEvent$Sent", "ChatEvent$Received"};
        String[] legacy = {"StartGameEvent$Post", "ShutdownEvent", "KeyboardEvent", "ChatSentEvent", "ChatReceivedEvent"};
        Object payload = new net.minecraft.util.IChatComponent() {};
        for (int i = 0; i < current.length; i++) {
            Class ct = Class.forName("net.weavemc.api.event." + current[i]);
            Class lt = Class.forName("net.weavemc.loader.api.event." + legacy[i]);
            Object event = i < 2 ? ct.getField("INSTANCE").get(null) : i == 2 ? ct.getConstructor().newInstance() :
                i == 3 ? ct.getConstructor(String.class).newInstance("payload") : ct.getConstructor(net.minecraft.util.IChatComponent.class).newInstance(payload);
            EventBus.subscribe(lt, first); EventBus.subscribe(lt, first); EventBus.subscribe(lt, second);
            Consumer<Event> verify = e -> {
                try {
                    if (e instanceof KeyboardEvent keyboard) check(keyboard.getKeyCode() == 42 && keyboard.getKeyState(), "Keyboard snapshot");
                    if (e instanceof ChatReceivedEvent received) check(received.getMessage() == payload, "Received identity");
                    if (e instanceof ChatSentEvent sent) check(sent.getMessage().equals("payload"), "Sent payload");
                    if (e instanceof CancellableEvent cancel) { check(cancel.isCancelled(), "Incoming cancellation"); cancel.setCancelled(false); }
                } catch (Exception ex) { throw new IllegalStateException(ex); }
            };
            EventBus.subscribe(lt, verify);
            if (i >= 3) ct.getMethod("setCancelled", boolean.class).invoke(event, true);
            org.lwjgl.input.Keyboard.key = 99; org.lwjgl.input.Keyboard.state = false;
            trace.clear(); int before = wildcard[0];
            net.weavemc.api.event.EventBus.postEvent((net.weavemc.api.event.Event) event);
            check(trace.equals(List.of("first", "second", "base")), "Order/duplicate/recursion: " + trace);
            check(wildcard[0] == before + 1, "Current wildcard double delivery");
            if (i >= 3) check(!(Boolean) ct.getMethod("isCancelled").invoke(event), "Cancellation reset returned");
            EventBus.unsubscribe(first); EventBus.unsubscribe(second); EventBus.unsubscribe(verify);
            trace.clear(); net.weavemc.api.event.EventBus.postEvent((net.weavemc.api.event.Event) event);
            check(trace.equals(List.of("base")), "Consumer unsubscribe");
            org.lwjgl.input.Keyboard.key = 42; org.lwjgl.input.Keyboard.state = true;
        }
        EventBus.unsubscribe(base); net.weavemc.api.event.EventBus.unsubscribe(observer);
        Object owner = new Object() { @SubscribeEvent public void chat(ChatSentEvent e) { trace.add("owner"); e.setCancelled(true); } };
        EventBus.subscribe(owner); EventBus.subscribe(owner);
        trace.clear(); EventBus.callEvent(new ChatSentEvent("custom"));
        check(trace.equals(List.of("owner")), "Object duplicate");
        Class<?> sentType = Class.forName("net.weavemc.api.event.ChatEvent$Sent");
        Object sent = sentType.getConstructor(String.class).newInstance("ordinary");
        trace.clear(); net.weavemc.api.event.EventBus.postEvent((net.weavemc.api.event.Event) sent);
        check(trace.equals(List.of("owner")) && (Boolean) sentType.getMethod("isCancelled").invoke(sent), "Current cancellation set returned");
        Object command = sentType.getConstructor(String.class).newInstance("/moonriseproof beta");
        net.weavemc.api.event.EventBus.postEvent((net.weavemc.api.event.Event) command);
        check((Boolean) sentType.getMethod("isCancelled").invoke(command), "Current legacy command cancellation");
        EventBus.unsubscribe(owner); trace.clear(); EventBus.callEvent(new ChatSentEvent("custom"));
        check(trace.isEmpty(), "Object unsubscribe");
        System.out.println("PASS router: five real current event types; hierarchy/FIFO; cancellation set/reset; payload; identity unsubscribe; duplicate/recursion guards; wildcard once");
    }
}

