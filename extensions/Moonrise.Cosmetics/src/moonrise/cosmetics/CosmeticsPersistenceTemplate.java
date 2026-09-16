package moonrise.cosmetics;

import java.io.File;
import java.io.FileOutputStream;
import java.lang.reflect.InvocationHandler;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;
import java.nio.file.AtomicMoveNotSupportedException;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import java.util.List;

/**
 * Method template transplanted into Lunar's CosmeticService.Stub.
 *
 * The transformed class must stay self-contained because Genesis cannot resolve
 * classes from the Java agent classloader.
 */
final class CosmeticsPersistenceTemplate implements InvocationHandler {
    private static final long MOONRISE_MAX_STATE_BYTES = 1024L * 1024L;

    private transient Object moonrise$loginCallback;
    private transient Object moonrise$loginProxy;

    private Object moonrise$wrapLoginCallback(Object callback) {
        if (callback == null) {
            return null;
        }
        Class<?> callbackInterface = moonrise$findCallbackInterface(callback.getClass());
        if (callbackInterface == null) {
            return callback;
        }
        synchronized (this) {
            moonrise$loginCallback = callback;
            moonrise$loginProxy = Proxy.newProxyInstance(
                    callbackInterface.getClassLoader(),
                    new Class<?>[] {callbackInterface},
                    this);
            return moonrise$loginProxy;
        }
    }

    private void moonrise$capture(String operation, Object request) {
        if (request == null) {
            return;
        }
        try {
            byte[] bytes = (byte[]) request.getClass().getMethod("toByteArray").invoke(request);
            if (bytes.length > MOONRISE_MAX_STATE_BYTES) {
                return;
            }
            File target = moonrise$stateFile(operation);
            File directory = target.getParentFile();
            if (!directory.isDirectory() && !directory.mkdirs() && !directory.isDirectory()) {
                return;
            }
            File temporary = new File(directory, target.getName() + ".tmp");
            try (FileOutputStream output = new FileOutputStream(temporary)) {
                output.write(bytes);
                output.getFD().sync();
            }
            try {
                Files.move(
                        temporary.toPath(),
                        target.toPath(),
                        StandardCopyOption.ATOMIC_MOVE,
                        StandardCopyOption.REPLACE_EXISTING);
            } catch (AtomicMoveNotSupportedException ignored) {
                Files.move(
                        temporary.toPath(),
                        target.toPath(),
                        StandardCopyOption.REPLACE_EXISTING);
            }
            System.out.println("[Moonrise Cosmetics] Saved local outfit state: " + operation);
        } catch (Exception exception) {
            moonrise$reportFailure("save", exception);
        }
    }

    private Object moonrise$mergeLoginResponse(Object response) {
        if (response == null) {
            return null;
        }
        try {
            Object builder = response.getClass().getMethod("toBuilder").invoke(response);
            Object savedOutfit = moonrise$readRequestPart(
                    "UpdateOutfitRequest", "hasOutfit", "getOutfit");
            if (savedOutfit != null) {
                Object savedId = savedOutfit.getClass().getMethod("getId").invoke(savedOutfit);
                List<?> serverOutfits = (List<?>) response.getClass()
                        .getMethod("getOutfitsList").invoke(response);
                builder.getClass().getMethod("clearOutfits").invoke(builder);
                Method addOutfit = builder.getClass()
                        .getMethod("addOutfits", savedOutfit.getClass());
                for (Object serverOutfit : serverOutfits) {
                    Object serverId = serverOutfit.getClass().getMethod("getId").invoke(serverOutfit);
                    if (!savedId.equals(serverId)) {
                        addOutfit.invoke(builder, serverOutfit);
                    }
                }
                addOutfit.invoke(builder, savedOutfit);
            }

            Object savedTree = moonrise$readRequestPart(
                    "SelectOutfitRequest", "hasOutfitTree", "getOutfitTree");
            if (savedTree != null) {
                builder.getClass().getMethod("setOutfitTree", savedTree.getClass())
                        .invoke(builder, savedTree);
            }
            Object merged = builder.getClass().getMethod("build").invoke(builder);
            if (savedOutfit != null || savedTree != null) {
                System.out.println("[Moonrise Cosmetics] Restored local outfit state at login");
            }
            return merged;
        } catch (Exception exception) {
            moonrise$reportFailure("restore", exception);
            return response;
        }
    }

    private Object moonrise$readRequestPart(
            String requestName,
            String presenceMethod,
            String valueMethod) throws Exception {
        File state = moonrise$stateFile(
                requestName.startsWith("Update") ? "updateOutfit" : "selectOutfit");
        if (!state.isFile() || state.length() > MOONRISE_MAX_STATE_BYTES) {
            return null;
        }
        byte[] bytes = Files.readAllBytes(state.toPath());
        Class<?> requestType = Class.forName(
                "com.lunarclient.websocket.cosmetic.v2." + requestName,
                true,
                getClass().getClassLoader());
        Object request = requestType.getMethod("parseFrom", byte[].class)
                .invoke(null, new Object[] {bytes});
        if (!Boolean.TRUE.equals(requestType.getMethod(presenceMethod).invoke(request))) {
            return null;
        }
        return requestType.getMethod(valueMethod).invoke(request);
    }

    @Override
    public Object invoke(Object proxy, Method method, Object[] arguments) throws Throwable {
        Object callback;
        synchronized (this) {
            callback = proxy == moonrise$loginProxy ? moonrise$loginCallback : null;
        }
        if (callback == null) {
            return moonrise$defaultValue(method.getReturnType());
        }
        Object[] forwarded = arguments;
        if ("run".equals(method.getName()) && arguments != null && arguments.length == 1) {
            forwarded = arguments.clone();
            forwarded[0] = moonrise$mergeLoginResponse(arguments[0]);
        }
        try {
            return method.invoke(callback, forwarded);
        } catch (InvocationTargetException exception) {
            throw exception.getCause();
        }
    }

    private static Class<?> moonrise$findCallbackInterface(Class<?> type) {
        if (type == null) {
            return null;
        }
        for (Class<?> candidate : type.getInterfaces()) {
            if ("com.google.protobuf.RpcCallback".equals(candidate.getName())) {
                return candidate;
            }
            Class<?> nested = moonrise$findCallbackInterface(candidate);
            if (nested != null) {
                return nested;
            }
        }
        return moonrise$findCallbackInterface(type.getSuperclass());
    }

    private static File moonrise$stateFile(String operation) {
        String override = System.getProperty("moonrise.cosmetics.stateDir");
        File directory;
        if (override != null && !override.isEmpty()) {
            directory = new File(override);
        } else {
            String localAppData = System.getenv("LOCALAPPDATA");
            directory = localAppData == null || localAppData.isEmpty()
                    ? new File(new File(System.getProperty("user.home"), ".moonrise"), "cosmetics")
                    : new File(new File(localAppData, "Moonrise"), "cosmetics");
        }
        return new File(directory, operation + ".pb");
    }

    private static Object moonrise$defaultValue(Class<?> type) {
        if (!type.isPrimitive() || type == Void.TYPE) return null;
        if (type == Boolean.TYPE) return Boolean.FALSE;
        if (type == Character.TYPE) return Character.valueOf('\0');
        if (type == Byte.TYPE) return Byte.valueOf((byte) 0);
        if (type == Short.TYPE) return Short.valueOf((short) 0);
        if (type == Integer.TYPE) return Integer.valueOf(0);
        if (type == Long.TYPE) return Long.valueOf(0L);
        if (type == Float.TYPE) return Float.valueOf(0F);
        if (type == Double.TYPE) return Double.valueOf(0D);
        return null;
    }

    private static void moonrise$reportFailure(String stage, Exception exception) {
        Throwable cause = exception instanceof InvocationTargetException
                && ((InvocationTargetException) exception).getCause() != null
                ? ((InvocationTargetException) exception).getCause()
                : exception;
        System.err.println(
                "[Moonrise Cosmetics] Local outfit " + stage + " failed: "
                        + cause.getClass().getName());
    }
}
