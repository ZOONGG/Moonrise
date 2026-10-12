package dev.moonrise.current;
import java.net.*;
// Compiled method templates; only these two methods are inserted into the game loader.
public abstract class SyntheticLoaderMethods extends URLClassLoader {
    protected SyntheticLoaderMethods() { super(new URL[0]); }
    private synchronized Class<?> moonrise$resolveSynthetic(String name) throws ClassNotFoundException {
        Class<?> loaded = findLoadedClass(name);
        if (loaded != null) return loaded;
        byte[] bytes;
        try {
            // This method lives in Genesis, whose defining loader cannot see game URLs.
            // Resolve the helper explicitly through the requesting game loader.
            var resolver = Class.forName("dev.moonrise.current.SyntheticResolver", true, this);
            System.err.println("[Moonrise current] synthetic-resolver loader=" + resolver.getClassLoader().getName()
                + " requesting-loader=" + getName());
            bytes = (byte[])resolver.getMethod("generate", ClassLoader.class, String.class).invoke(null, this, name);
        } catch (java.lang.reflect.InvocationTargetException failure) {
            var cause = failure.getCause();
            if (cause instanceof ClassNotFoundException missing) throw missing;
            if (cause instanceof RuntimeException runtime) throw runtime;
            if (cause instanceof Error error) throw error;
            throw new ClassNotFoundException("Synthetic resolver failed: " + name, cause);
        } catch (ReflectiveOperationException failure) {
            throw new ClassNotFoundException("Synthetic resolver unavailable: " + name, failure);
        }
        if (bytes == null) throw new ClassNotFoundException("Unregistered current Weave synthetic class: " + name);
        Class<?> defined = defineClass(name, bytes, 0, bytes.length);
        System.err.println("[Moonrise current] synthetic-defined name=" + name + " loader=" + getName());
        return defined;
    }
    private Class<?> moonrise$loadSynthetic(String name, boolean resolve) throws ClassNotFoundException {
        Class<?> result = moonrise$resolveSynthetic(name);
        if (resolve) resolveClass(result);
        return result;
    }
}
