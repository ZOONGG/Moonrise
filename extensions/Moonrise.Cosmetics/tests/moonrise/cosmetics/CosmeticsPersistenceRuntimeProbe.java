package moonrise.cosmetics;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.lang.reflect.Constructor;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;
import java.net.URL;
import java.net.URLClassLoader;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

/** Executes capture and replay with Lunar classes in an isolated classloader. */
public final class CosmeticsPersistenceRuntimeProbe {
    private CosmeticsPersistenceRuntimeProbe() {
    }

    public static void main(String[] arguments) throws Exception {
        if (arguments.length != 3) {
            throw new IllegalArgumentException("Expected Lunar JAR, agent JAR, and state directory");
        }
        Path lunarJar = Paths.get(arguments[0]);
        Path agentJar = Paths.get(arguments[1]);
        Path stateDirectory = Paths.get(arguments[2]);
        Files.createDirectories(stateDirectory);
        System.setProperty("moonrise.cosmetics.stateDir", stateDirectory.toString());

        byte[] transformed = transform(lunarJar);
        try (IsolatedLoader first = new IsolatedLoader(lunarJar, agentJar, transformed)) {
            StubRuntime runtime = new StubRuntime(first);
            runtime.call("updateOutfit", "UpdateOutfitRequest");
            runtime.call("selectOutfit", "SelectOutfitRequest");
        }
        if (!Files.isRegularFile(stateDirectory.resolve("updateOutfit.pb"))
                || !Files.isRegularFile(stateDirectory.resolve("selectOutfit.pb"))) {
            throw new IllegalStateException("Outfit requests were not persisted");
        }

        try (IsolatedLoader second = new IsolatedLoader(lunarJar, agentJar, transformed)) {
            StubRuntime runtime = new StubRuntime(second);
            runtime.call("login", "LoginRequest");
            Object merged = runtime.loginResponse.get();
            int outfitCount = (Integer) merged.getClass().getMethod("getOutfitsCount")
                    .invoke(merged);
            boolean hasTree = (Boolean) merged.getClass().getMethod("hasOutfitTree")
                    .invoke(merged);
            if (outfitCount != 1 || !hasTree) {
                throw new IllegalStateException(
                        "Persisted outfit was not merged into login: outfits=" + outfitCount
                                + ", tree=" + hasTree);
            }
            if (runtime.updates.get() != 0 || runtime.selects.get() != 0) {
                throw new IllegalStateException("Login restore unexpectedly replayed network calls");
            }
        }
        System.out.println("cosmetics-persistence-runtime=verified");
    }

    private static byte[] transform(Path lunarJar) throws Exception {
        try (ZipFile zip = new ZipFile(lunarJar.toFile())) {
            ZipEntry entry = zip.getEntry(CosmeticsPersistenceTransformer.TARGET + ".class");
            if (entry == null) {
                throw new IllegalStateException("Current Lunar v2 Stub is missing");
            }
            byte[] original = read(zip, entry);
            byte[] transformed = new CosmeticsPersistenceTransformer().transform(
                    null,
                    CosmeticsPersistenceTransformer.TARGET,
                    null,
                    null,
                    original);
            if (transformed == null) {
                throw new IllegalStateException("Persistence transformer skipped current Lunar Stub");
            }
            return transformed;
        }
    }

    private static byte[] read(ZipFile zip, ZipEntry entry) throws Exception {
        try (InputStream input = zip.getInputStream(entry)) {
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = input.read(buffer)) >= 0) {
                output.write(buffer, 0, count);
            }
            return output.toByteArray();
        }
    }

    private static final class IsolatedLoader extends URLClassLoader {
        private final byte[] transformed;

        IsolatedLoader(Path lunarJar, Path agentJar, byte[] transformed) throws Exception {
            super(new URL[] {lunarJar.toUri().toURL(), agentJar.toUri().toURL()},
                    CosmeticsPersistenceRuntimeProbe.class.getClassLoader());
            this.transformed = transformed;
        }

        @Override
        protected synchronized Class<?> loadClass(String name, boolean resolve)
                throws ClassNotFoundException {
            if (!CosmeticsPersistenceTransformer.TARGET.replace('/', '.').equals(name)) {
                return super.loadClass(name, resolve);
            }
            Class<?> loaded = findLoadedClass(name);
            if (loaded == null) {
                loaded = defineClass(name, transformed, 0, transformed.length);
            }
            if (resolve) {
                resolveClass(loaded);
            }
            return loaded;
        }
    }

    private static final class StubRuntime {
        private final ClassLoader loader;
        private final Object stub;
        private final AtomicInteger updates = new AtomicInteger();
        private final AtomicInteger selects = new AtomicInteger();
        private final AtomicReference<Object> loginResponse = new AtomicReference<>();

        StubRuntime(ClassLoader loader) throws Exception {
            this.loader = loader;
            Class<?> channelType = loader.loadClass("com.google.protobuf.RpcChannel");
            Class<?> callbackType = loader.loadClass("com.google.protobuf.RpcCallback");
            Method callbackRun = callbackType.getMethod("run", Object.class);
            Object channel = Proxy.newProxyInstance(
                    loader,
                    new Class<?>[] {channelType},
                    (proxy, method, arguments) -> {
                        if ("callMethod".equals(method.getName())) {
                            String operation = (String) arguments[0].getClass()
                                    .getMethod("getName").invoke(arguments[0]);
                            if ("UpdateOutfit".equals(operation)) updates.incrementAndGet();
                            if ("SelectOutfit".equals(operation)) selects.incrementAndGet();
                            Object callback = arguments[4];
                            callbackRun.invoke(callback, arguments[3]);
                        }
                        return null;
                    });
            Class<?> stubType = loader.loadClass(
                    CosmeticsPersistenceTransformer.TARGET.replace('/', '.'));
            Constructor<?> constructor = stubType.getDeclaredConstructor(channelType);
            constructor.setAccessible(true);
            stub = constructor.newInstance(channel);
        }

        void call(String operation, String requestName) throws Exception {
            Class<?> controllerType = loader.loadClass("com.google.protobuf.RpcController");
            Class<?> requestType = loader.loadClass(
                    "com.lunarclient.websocket.cosmetic.v2." + requestName);
            Class<?> callbackType = loader.loadClass("com.google.protobuf.RpcCallback");
            Object request = requestType.getMethod("getDefaultInstance").invoke(null);
            if ("updateOutfit".equals(operation)) {
                Class<?> outfitType = loader.loadClass(
                        "com.lunarclient.websocket.cosmetic.v2.Outfit");
                Object outfitBuilder = outfitType.getMethod("newBuilder").invoke(null);
                outfitBuilder.getClass().getMethod("setName", String.class)
                        .invoke(outfitBuilder, "Moonrise persisted outfit");
                Object outfit = outfitBuilder.getClass().getMethod("build").invoke(outfitBuilder);
                Object requestBuilder = requestType.getMethod("newBuilder").invoke(null);
                requestBuilder.getClass().getMethod("setOutfit", outfitType)
                        .invoke(requestBuilder, outfit);
                request = requestBuilder.getClass().getMethod("build").invoke(requestBuilder);
            } else if ("selectOutfit".equals(operation)) {
                Class<?> treeType = loader.loadClass(
                        "com.lunarclient.websocket.cosmetic.v2.OutfitTree");
                Object tree = treeType.getMethod("getDefaultInstance").invoke(null);
                Object requestBuilder = requestType.getMethod("newBuilder").invoke(null);
                requestBuilder.getClass().getMethod("setOutfitTree", treeType)
                        .invoke(requestBuilder, tree);
                request = requestBuilder.getClass().getMethod("build").invoke(requestBuilder);
            }
            Object callback = Proxy.newProxyInstance(
                    loader,
                    new Class<?>[] {callbackType},
                    (proxy, method, arguments) -> {
                        if ("login".equals(operation) && "run".equals(method.getName())) {
                            loginResponse.set(arguments[0]);
                        }
                        return null;
                    });
            Method target = stub.getClass().getMethod(
                    operation, controllerType, requestType, callbackType);
            target.invoke(stub, null, request, callback);
        }
    }
}
