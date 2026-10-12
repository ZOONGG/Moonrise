import java.lang.reflect.*;
import java.nio.file.*;
import java.util.*;
import java.util.jar.JarFile;
import net.weavemc.loader.impl.bootstrap.Bootstrap;

/** Additional read-only assertions after the unchanged genuine definition probe. */
public final class ApiDefinitionOnlyBootstrap {
    static final String IMPL = "net.weavemc.loader.impl.";
    static Object field(Object owner, String name) throws Exception {
        var f = owner.getClass().getDeclaredField(name);
        f.setAccessible(true);
        return f.get(owner);
    }
    static void require(boolean value, String criterion) {
        if (!value) throw new AssertionError("BLOCKED " + criterion);
    }
    static Class<?> type(ClassLoader owner, String name) throws Exception {
        return Class.forName(name, false, owner);
    }
    static Object get(Object owner, String method) throws Exception {
        return invoke(owner.getClass().getMethod(method), owner);
    }
    static Object invoke(Method method, Object owner, Object... args) throws Exception {
        try { return method.invoke(owner, args); }
        catch (InvocationTargetException e) {
            // Preserve original getInstance/fatalError and loader failures.
            if (e.getCause() instanceof Exception failure) throw failure;
            if (e.getCause() instanceof Error failure) throw failure;
            throw e;
        }
    }
    static List<?> list(Object owner, String method) throws Exception {
        return (List<?>) get(owner, method);
    }
    static String identity(Object value) {
        return value == null ? "null" : value.getClass().getName() + "@" +
            Integer.toHexString(System.identityHashCode(value));
    }
    static void klogOperand(String operand, Object actual, Object expected, boolean result) {
        System.out.println("API_KLOG_OPERAND " + operand + "; actual=" + actual +
            "; expected=" + expected + "; result=" + result);
    }
    public static void main(String[] args) throws Exception {
        try { census(args); }
        catch (Exception | AssertionError failure) {
            System.err.println("BLOCKED API class identity or registration census");
            throw failure;
        }
    }
    static void census(String[] args) throws Exception {
        require("true".equals(System.getProperty("weave.api.minecraft.enabled")), "API enabled");
        require("INFO".equals(System.getProperty("klog.logger." + IMPL + "WeaveLoader.level")),
            "logger-specific INFO property present before bootstrap");
        DefinitionOnlyBootstrap.main(args);
        ClassLoader owner = Bootstrap.INSTANCE.getMinecraftBootstrapClassLoader();
        require(owner != null && owner == DefinitionOnlyBootstrap.expected &&
            owner == DefinitionOnlyBootstrap.callbackOwner && owner.getClass().getName().equals(
                IMPL + "bootstrap.transformer.ApplicationWrapper$WrappingLoader"), "genuine bootstrap WrappingLoader");
        // Snapshot before lookup: the original callback already constructed this class.
        Class<?> bootstrappedClass = null;
        for (Class<?> loaded : DefinitionOnlyBootstrap.real.getAllLoadedClasses()) {
            if (loaded.getName().equals(IMPL + "WeaveLoader") && loaded.getClassLoader() == owner) {
                require(bootstrappedClass == null, "unique bootstrap WeaveLoader class");
                bootstrappedClass = loaded;
            }
        }
        require(bootstrappedClass != null, "WeaveLoader already loaded by genuine bootstrap");
        Class<?> loaderClass = type(owner, IMPL + "WeaveLoader");
        require(loaderClass.getClassLoader() == owner && loaderClass == bootstrappedClass,
            "resolved Class equals actual bootstrap Class");
        Object loader = invoke(loaderClass.getMethod("getInstance"), null);
        require(loader != null && loader.getClass() == bootstrappedClass && field(loader, "classLoader") == owner,
            "original singleton belongs to genuine bootstrap owner");
        System.out.println("API_CLASS_IDENTITY_READY WrappingLoader; resolved=bootstrap Class; original getInstance; singleton owner=true");
        // Read the original getter on the verified singleton, never a system Klog copy.
        // This getter creates a fresh logger; this is a post-bootstrap check, not
        // a claim that an earlier logger instance was observed before addMinecraftApi.
        Method loggerGetter = loaderClass.getDeclaredMethod("getLogger");
        loggerGetter.setAccessible(true);
        Object logger = invoke(loggerGetter, loader);
        Class<?> loggerClass = type(owner, "me.xtrm.klog.Logger");
        // Original URLClassLoaderTransformer installs ignored package delegation.
        // Klog is shared on AppClassLoader, not redefined by WrappingLoader.
        ClassLoader klogOwner = ClassLoader.getSystemClassLoader();
        var ignored = (List<?>) field(owner, "weave$ignoredPackages");
        require(ignored.contains("me.xtrm.klog."),
            "original Klog parent delegation rule");
        require(loggerClass == type(klogOwner, "me.xtrm.klog.Logger"), "shared parent Klog Class identity");
        // Observe every operand before the original fail-closed gates, using only
        // the genuine getter result and types resolved through its WrappingLoader.
        boolean loggerIdentity, loggerOwner, loggerName, loggerInfo;
        List<?> appenders;
        try {
            Class<?> actualClass = logger.getClass();
            loggerIdentity = actualClass == loggerClass;
            loggerOwner = loggerClass.getClassLoader() == klogOwner;
            klogOperand("logger.class.identity", actualClass.getName() + "@" +
                Integer.toHexString(System.identityHashCode(actualClass)), loggerClass.getName() + "@" +
                Integer.toHexString(System.identityHashCode(loggerClass)), loggerIdentity);
            klogOperand("logger.actual.definingLoader", identity(actualClass.getClassLoader()), identity(klogOwner),
                actualClass.getClassLoader() == klogOwner);
            klogOperand("logger.expected.definingLoader", identity(loggerClass.getClassLoader()), identity(klogOwner), loggerOwner);
            Object name = get(logger, "getName");
            loggerName = name.equals(IMPL + "WeaveLoader");
            klogOperand("logger.name", name, IMPL + "WeaveLoader", loggerName);
            String level = ((Enum<?>) get(logger, "getLevel")).name();
            loggerInfo = level.equals("INFO");
            klogOperand("logger.effectiveLevel", level, "INFO", loggerInfo);
            String property = System.getProperty("klog.logger." + IMPL + "WeaveLoader.level");
            klogOperand("jvm.property.klog.logger." + IMPL + "WeaveLoader.level", property, "INFO", "INFO".equals(property));
            appenders = list(logger, "getAppenders");
            Class<?> consoleClass = type(owner, "me.xtrm.klog.ConsoleAppender");
            require(consoleClass.getClassLoader() == klogOwner &&
                consoleClass == type(klogOwner, "me.xtrm.klog.ConsoleAppender"),
                "original shared parent ConsoleAppender Class identity");
            Object console = consoleClass.getField("INSTANCE").get(null);
            klogOperand("appender.expected.definingLoader", identity(consoleClass.getClassLoader()), identity(klogOwner),
                consoleClass.getClassLoader() == klogOwner);
            klogOperand("appenders.count", appenders.size(), 1, appenders.size() == 1);
            boolean originalPresent = false;
            for (int i = 0; i < appenders.size(); i++) {
                Object appender = appenders.get(i);
                Class<?> actualAppenderClass = appender.getClass();
                klogOperand("appender[" + i + "].class.identity", actualAppenderClass.getName() + "@" +
                    Integer.toHexString(System.identityHashCode(actualAppenderClass)), consoleClass.getName() + "@" +
                    Integer.toHexString(System.identityHashCode(consoleClass)), actualAppenderClass == consoleClass);
                klogOperand("appender[" + i + "].definingLoader", identity(actualAppenderClass.getClassLoader()), identity(klogOwner),
                    actualAppenderClass.getClassLoader() == klogOwner);
                klogOperand("appender[" + i + "].singleton.identity", identity(appender), identity(console), appender == console);
                originalPresent |= appender == console;
            }
            klogOperand("appenders.originalConsolePresent", originalPresent, true, originalPresent);
            klogOperand("appenders.soleOriginalConsole", appenders.size() == 1 && appenders.get(0) == console,
                true, appenders.size() == 1 && appenders.get(0) == console);
        } catch (Exception | Error failure) {
            System.err.println("BLOCKED Klog operand observation");
            throw failure; // Preserve the exact exception/cause and stack trace; no unknown operand passes.
        }
        require(loggerIdentity && loggerOwner && loggerName && loggerInfo, "actual bootstrap Klog identity/name/INFO");
        require(appenders.size() == 1 && appenders.get(0) ==
            type(owner, "me.xtrm.klog.ConsoleAppender").getField("INSTANCE").get(null),
            "original bootstrap console appender for INFO/ERROR and throwable traces");
        System.out.println("API_KLOG_READY actual bootstrap getter; name=" + IMPL + "WeaveLoader; level=INFO; original ConsoleAppender; post-bootstrap check");
        require(((List<?>) field(loader, "mappedModJars")).isEmpty(), "empty user mod inputs");
        // This is a namespace -> infrastructure loader map, not user Mixin instances.
        // Read only the real singleton's field; never call mixinForNamespace/findTargets.
        var sandboxes = (Map<?, ?>) field(loader, "mixinInstances");
        Object mappingOwner = type(owner, IMPL + "util.MappingsHandler").getField("INSTANCE").get(null);
        Object merged = get(get(mappingOwner, "getMergedMappings"), "getMappings");
        var namespaces = list(merged, "getNamespaces");
        Set<String> pinnedNamespaces = Set.of("official", "mcp-srg", "mcp-named");
        require(namespaces.size() == 3 && new HashSet<>(namespaces).equals(pinnedNamespaces) &&
            sandboxes.size() == 3 && sandboxes.keySet().equals(pinnedNamespaces),
            "complete pinned mapping namespace sandbox census");
        Class<?> sandboxClass = type(owner, IMPL + "mixin.SandboxedMixinLoader");
        Class<?> stateClass = type(owner, IMPL + "mixin.SandboxedMixinState");
        require(sandboxClass.getClassLoader() == owner && stateClass.getClassLoader() == owner,
            "genuine sandbox infrastructure Class owners");
        Object backing = invoke(type(owner, IMPL + "bootstrap.transformer.URLClassLoaderAccessor")
            .getMethod("getWeaveBacking"), owner);
        Set<Object> sandboxIdentities = Collections.newSetFromMap(new IdentityHashMap<>());
        String sponge = IMPL + "shaded.spongepowered.asm.mixin.";
        for (Object sandbox : sandboxes.values()) {
            require(sandbox != null && sandbox.getClass() == sandboxClass && sandboxIdentities.add(sandbox),
                "unique original sandbox per namespace");
            require(field(sandbox, "parent") == backing && ((ClassLoader) sandbox).getParent() == backing,
                "sandbox parent is genuine weave backing loader");
            Object state = get(sandbox, "getState");
            require(state.getClass() == stateClass && field(state, "loader") == sandbox &&
                Boolean.TRUE.equals(get(state, "getInitialized")), "initialized original sandbox state owner");
            Object accessor = field(state, "$$delegate_0");
            Object transformer = get(state, "getTransformer$weave_loader");
            Object processor = field(transformer, "processor");
            ClassLoader isolated = (ClassLoader) sandbox;
            require(accessor.getClass() == type(isolated, IMPL + "mixin.MixinAccessImpl") &&
                accessor.getClass().getClassLoader() == isolated &&
                transformer.getClass() == type(isolated, sponge + "transformer.MixinTransformer") &&
                transformer.getClass().getClassLoader() == isolated &&
                processor.getClass() == type(isolated, sponge + "transformer.MixinProcessor") &&
                processor.getClass().getClassLoader() == isolated, "sandbox-local accessor/transformer/processor identities");
            Class<?> mixins = type(isolated, sponge + "Mixins");
            require(mixins.getClassLoader() == isolated, "sandbox-local Mixin config registry identity");
            var registered = mixins.getDeclaredField("registeredConfigs");
            registered.setAccessible(true);
            require(((Set<?>) registered.get(null)).isEmpty() &&
                ((Set<?>) invoke(mixins.getMethod("getConfigs"), null)).isEmpty() &&
                ((List<?>) field(processor, "configs")).isEmpty() &&
                ((List<?>) field(processor, "pendingConfigs")).isEmpty(),
                "no sandbox registered/queued/selected/pending Mixin configs or their targets");
            // findTargets derives all targets from processor.configs. Empty selected
            // configs proves empty targets without re-running checkForcedSelect/PREPARE.
        }
        Class<?> utility = type(owner, IMPL + "util.UtilityKt");
        Object source;
        Path api = Path.of(args[0], "inputs/api.jar");
        try (var jar = new JarFile(api.toFile())) {
            Method json = utility.getMethod("getJSON");
            source = invoke(utility.getMethod("fetchModConfig", JarFile.class, json.getReturnType()),
                null, jar, invoke(json, null));
        }
        var sourceHooks = list(source, "getHooks");
        var sourceWideners = list(source, "getAccessWideners");
        String sourceId = (String) get(source, "getModId");
        require(sourceId.equals("net.weavemc.api.v1_8"), "pinned API registry id");
        require(list(source, "getEntryPoints").isEmpty() && list(source, "getMixinConfigs").isEmpty(),
            "source API has no entrypoints or Mixin configs");
        var mods = (List<?>) field(loader, "mods");
        require(mods.size() == 3, "two builtins plus exactly one API registration");
        Class<?> modClass = type(owner, IMPL + "util.WeaveMod");
        Set<String> ids = new HashSet<>();
        for (Object mod : mods) {
            require(mod.getClass() == modClass, "registered mod Class identity");
            String id = (String) get(mod, "getModId");
            require(ids.add(id), "unique registered mod ids");
            Object config = get(mod, "getConfig");
            require(config.getClass() == source.getClass(), "registered config Class identity");
            require(list(config, "getEntryPoints").isEmpty() && list(config, "getMixinConfigs").isEmpty(),
                "no registered entrypoints or Mixin configs");
            if (id.equals(sourceId))
                require(list(config, "getHooks").equals(sourceHooks) &&
                    list(config, "getAccessWideners").equals(sourceWideners), "registered API config identity");
        }
        require(ids.equals(Set.of("minecraft", "java", sourceId)), "actual API mod registry");
        require(sourceHooks.size() == 18 && new HashSet<>(sourceHooks).size() == 18, "pinned source hook contract");
        Class<?> injection = type(owner, IMPL + "InjectionHandler");
        Class<?> hookClass = type(owner, IMPL + "ModHook");
        require(injection.getClassLoader() == owner && hookClass.getClassLoader() == owner,
            "actual InjectionHandler and ModHook owner");
        var f = injection.getDeclaredField("modifiers");
        f.setAccessible(true);
        var modifiers = (List<?>) f.get(null);
        List<String> actualHooks = new ArrayList<>();
        Set<Object> hookInstances = Collections.newSetFromMap(new IdentityHashMap<>());
        Object retainedTree = null;
        int wideners = 0;
        for (Object modifier : modifiers) {
            require(modifier.getClass() == hookClass || modifier.getClass() ==
                type(owner, IMPL + "WeaveLoader$setupAccessWideners$1"),
                "only API hooks and access widener; no Mixin or unknown modifiers");
            if (hookClass.isInstance(modifier)) {
                require(hookInstances.add(modifier), "distinct retained ModHook instance");
                Object hook = get(modifier, "getHook");
                require(hook.getClass() == type(owner, hook.getClass().getName()) &&
                    hook.getClass().getClassLoader() == owner, "original API hook Class identity");
                actualHooks.add(hook.getClass().getName());
            }
            if (modifier.getClass().getName().equals(IMPL + "WeaveLoader$setupAccessWideners$1")) {
                require(modifier.getClass() == type(owner, modifier.getClass().getName()), "access widener modifier identity");
                wideners++;
                retainedTree = field(modifier, "$tree");
            }
        }
        require(actualHooks.size() == 18 && new HashSet<>(actualHooks).equals(new HashSet<>(sourceHooks)),
            "all 18 original API hook instances retained by InjectionHandler");
        require(sourceWideners.size() == 1 && wideners == 1 && retainedTree != null,
            "one actual access widener modifier");
        Class<?> aw = type(owner, IMPL + "shaded.mappings.aw.AccessWidenerKt");
        Class<?> awType = type(owner, IMPL + "shaded.mappings.aw.AccessWidener");
        Object expected;
        try (var jar = new JarFile(api.toFile())) {
            var entry = jar.getJarEntry((String) sourceWideners.get(0));
            require(entry != null, "original access widener resource available");
            try (var stream = jar.getInputStream(entry)) {
                String text = new String(stream.readAllBytes(), java.nio.charset.StandardCharsets.UTF_8).trim();
                expected = invoke(aw.getMethod("loadAccessWidener", List.class), null,
                    Arrays.asList(text.split("\\R", -1)));
            }
        }
        Object mappingsHandler = type(owner, IMPL + "util.MappingsHandler").getField("INSTANCE").get(null);
        Object mappings = get(get(mappingsHandler, "getMergedMappings"), "getMappings");
        expected = invoke(aw.getMethod("remap", awType,
            type(owner, IMPL + "shaded.mappings.format.Mappings"), String.class), null,
            expected, mappings, get(mappingsHandler, "getEnvironmentRuntimeNamespace"));
        Object tree = invoke(aw.getMethod("toTree", awType), null, expected);
        require(tree.getClass() == retainedTree.getClass() &&
            !((Map<?, ?>) get(tree, "getClasses")).isEmpty() && tree.equals(retainedTree),
            "retained complete source access widener tree with matching Class identity");
        System.out.println("API_STATE_READY actual API mod; hooks=18; complete access widener tree=1; user mods/entrypoints/Mixin configs=0");
        // Only PowerShell certifies success after complete logs, swallowed API errors,
        // class-init trace, timeout, exit and final immutable hashes are checked.
    }
}
