package fixture;
import net.weavemc.loader.api.event.*;
import net.weavemc.loader.api.command.*;
/** Offline dispatch/cancellation fixture, not Raven functionality. */
public final class ProbeListener {
    public int calls;
    public String argument;
    @SubscribeEvent public void onChat(ChatSentEvent event) { calls++; }
    public void registerCommand() {
        CommandBus.register(new Command("moonriseproof") {
            @Override public void handle(String[] args) { argument = args[0]; }
        });
    }
}
