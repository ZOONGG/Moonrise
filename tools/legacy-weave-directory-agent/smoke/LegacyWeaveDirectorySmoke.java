import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.nio.file.Path;

public final class LegacyWeaveDirectorySmoke {
    public static void main(String[] arguments) throws Exception {
        Path expected = Path.of(arguments[0]).toAbsolutePath().normalize();
        Class<?> loaderType = Class.forName("net.weavemc.loader.WeaveLoader");
        Field instanceField = loaderType.getDeclaredField("INSTANCE");
        Object instance = instanceField.get(null);
        Method directoryMethod = loaderType.getDeclaredMethod("getOrCreateModDirectory");
        directoryMethod.setAccessible(true);
        Path actual = ((Path) directoryMethod.invoke(instance)).toAbsolutePath().normalize();
        if (!expected.equals(actual)) {
            throw new AssertionError("Expected " + expected + " but got " + actual);
        }
        System.out.println("legacy-weave-directory-adapter-ok:" + actual);
    }
}
