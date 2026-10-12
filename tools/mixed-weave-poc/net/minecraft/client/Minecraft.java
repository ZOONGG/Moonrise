package net.minecraft.client;

/** Named offline target only; does not implement or launch Minecraft. */
public final class Minecraft {
    public int ticks;
    public void runTick() { ticks++; }
}
