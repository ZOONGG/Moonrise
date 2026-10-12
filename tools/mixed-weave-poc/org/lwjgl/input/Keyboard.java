package org.lwjgl.input;
/** Offline input state only; no native library or Minecraft. */
public final class Keyboard {
    public static int key = 42;
    public static boolean state = true;
    public static int getEventKey() { return key; }
    public static char getEventCharacter() { return 'x'; }
    public static boolean getEventKeyState() { return state; }
}
