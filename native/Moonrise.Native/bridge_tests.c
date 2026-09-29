#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>

#define MOONRISE_NATIVE_TEST
#include "bridge.c"

static int g_failures;

static void record_failure(const char *name, const char *message)
{
    fprintf(stderr, "[native-test] %s: %s\n", name, message);
    g_failures++;
}

static BOOL write_bytes(const WCHAR *path, const char *content, DWORD length)
{
    HANDLE file = CreateFileW(
        path,
        GENERIC_WRITE,
        0,
        NULL,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        NULL);
    if (file == INVALID_HANDLE_VALUE)
    {
        return FALSE;
    }

    DWORD written = 0;
    BOOL result = WriteFile(file, content, length, &written, NULL) && written == length;
    CloseHandle(file);
    return result;
}

static BOOL write_utf8(const WCHAR *path, const WCHAR *content)
{
    int required = WideCharToMultiByte(CP_UTF8, 0, content, -1, NULL, 0, NULL, NULL);
    if (required <= 1)
    {
        return FALSE;
    }

    char *utf8 = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, (size_t)required);
    if (utf8 == NULL ||
        WideCharToMultiByte(CP_UTF8, 0, content, -1, utf8, required, NULL, NULL) == 0)
    {
        if (utf8 != NULL)
        {
            HeapFree(GetProcessHeap(), 0, utf8);
        }
        return FALSE;
    }

    BOOL result = write_bytes(path, utf8, (DWORD)(required - 1));
    HeapFree(GetProcessHeap(), 0, utf8);
    return result;
}

static BOOL touch_file(const WCHAR *path)
{
    return write_bytes(path, "", 0);
}

static void free_options(WCHAR *options)
{
    if (options != NULL)
    {
        SecureZeroMemory(options, (wcslen(options) + 1) * sizeof(WCHAR));
        HeapFree(GetProcessHeap(), 0, options);
    }
}

static void expect_options(
    const char *name,
    const WCHAR *config_path,
    const WCHAR *config,
    const WCHAR *expected)
{
    if (!write_utf8(config_path, config))
    {
        record_failure(name, "unable to write configuration");
        return;
    }

    WCHAR *actual = read_bridge_options();
    if (actual == NULL)
    {
        record_failure(name, "valid configuration was rejected");
        return;
    }
    if (wcscmp(actual, expected) != 0)
    {
        record_failure(name, "generated JAVA_TOOL_OPTIONS did not match");
    }
    free_options(actual);
}

static void expect_invalid(
    const char *name,
    const WCHAR *config_path,
    const WCHAR *config)
{
    if (!write_utf8(config_path, config))
    {
        record_failure(name, "unable to write malformed configuration");
        return;
    }

    WCHAR *actual = read_bridge_options();
    if (actual != NULL)
    {
        record_failure(name, "malformed configuration was accepted");
        free_options(actual);
    }
}

static void test_embedded_nul(
    const WCHAR *config_path,
    const WCHAR *valid_prefix)
{
    int required = WideCharToMultiByte(CP_UTF8, 0, valid_prefix, -1, NULL, 0, NULL, NULL);
    if (required <= 1)
    {
        record_failure("embedded-nul", "unable to size configuration");
        return;
    }

    static const char suffix[] = "unknown\tvalue\n";
    size_t total = (size_t)(required - 1) + 1 + sizeof(suffix) - 1;
    char *bytes = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, total);
    if (bytes == NULL ||
        WideCharToMultiByte(CP_UTF8, 0, valid_prefix, -1, bytes, required, NULL, NULL) == 0)
    {
        record_failure("embedded-nul", "unable to encode configuration");
        if (bytes != NULL)
        {
            HeapFree(GetProcessHeap(), 0, bytes);
        }
        return;
    }

    bytes[required - 1] = '\0';
    CopyMemory(bytes + required, suffix, sizeof(suffix) - 1);
    if (!write_bytes(config_path, bytes, (DWORD)total))
    {
        record_failure("embedded-nul", "unable to write configuration");
    }
    else
    {
        WCHAR *actual = read_bridge_options();
        if (actual != NULL)
        {
            record_failure("embedded-nul", "configuration containing NUL was accepted");
            free_options(actual);
        }
    }
    HeapFree(GetProcessHeap(), 0, bytes);
}

int main(void)
{
    WCHAR temporary_root[MAX_PATH];
    WCHAR temporary_seed[MAX_PATH];
    WCHAR mods[MAX_PATH];
    WCHAR config_path[MAX_PATH];
    WCHAR agent_a[MAX_PATH];
    WCHAR agent_b[MAX_PATH];
    WCHAR agent_c[MAX_PATH];
    WCHAR agent_d[MAX_PATH];
    WCHAR missing_agent[MAX_PATH];

    if (GetTempPathW(MAX_PATH, temporary_root) == 0 ||
        GetTempFileNameW(temporary_root, L"mnr", 0, temporary_seed) == 0 ||
        !DeleteFileW(temporary_seed) ||
        !CreateDirectoryW(temporary_seed, NULL))
    {
        record_failure("setup", "unable to create temporary directory");
        return 1;
    }

    swprintf(mods, MAX_PATH, L"%s\\mods", temporary_seed);
    swprintf(config_path, MAX_PATH, L"%s\\bridge.cfg", temporary_seed);
    swprintf(agent_a, MAX_PATH, L"%s\\agent-a.jar", temporary_seed);
    swprintf(agent_b, MAX_PATH, L"%s\\agent-b.jar", temporary_seed);
    swprintf(agent_c, MAX_PATH, L"%s\\agent-c.jar", temporary_seed);
    swprintf(agent_d, MAX_PATH, L"%s\\agent-d.jar", temporary_seed);
    swprintf(missing_agent, MAX_PATH, L"%s\\missing.jar", temporary_seed);

    if (!CreateDirectoryW(mods, NULL) ||
        !touch_file(agent_a) ||
        !touch_file(agent_b) ||
        !touch_file(agent_c) ||
        !touch_file(agent_d) ||
        !SetEnvironmentVariableW(g_bridge_config_name, config_path))
    {
        record_failure("setup", "unable to create test fixtures");
        goto cleanup;
    }

    WCHAR config[32768];
    WCHAR expected[32768];

    swprintf(config, 32768, L"MNR3\n%s\n%s\n%s\n", agent_a, mods, agent_b);
    swprintf(
        expected,
        32768,
        L"-javaagent:\"%s\" -Dweave.mods.directory=\"%s\" "
        L"-Dweave.api.minecraft.enabled=true -Dweave.dump.bytecode.enabled=false "
        L"-javaagent:\"%s\"",
        agent_a,
        mods,
        agent_b);
    expect_options("mnr3-backward-compatible", config_path, config, expected);

    swprintf(
        config,
        32768,
        L"MNR4\nmode\tCurrent\nmods\t%s\n"
        L"agent\t%s\t\tweave-loader-current\tWeaveLoader\n"
        L"agent\t%s\tmode=strict\tagent-a\tPackageAgent\n"
        L"agent\t%s\t\tagent-b\tPackageAgent\n"
        L"arg\t-Xmx4G\narg\t-XX:+UseG1GC\n"
        L"prop\tmoonrise.test\tone\nprop\texample.mode\ttwo\n",
        mods,
        agent_a,
        agent_b,
        agent_c);
    swprintf(
        expected,
        32768,
        L"-Dweave.mods.directory=\"%s\" -Dweave.api.minecraft.enabled=true "
        L"-Dweave.dump.bytecode.enabled=false -javaagent:\"%s\" "
        L"-javaagent:\"%s\"=mode=strict -javaagent:\"%s\" "
        L"-Xmx4G -XX:+UseG1GC -Dmoonrise.test=one -Dexample.mode=two",
        mods,
        agent_a,
        agent_b,
        agent_c);
    expect_options("mnr4-current-order-options-args-properties", config_path, config, expected);

    swprintf(
        config,
        32768,
        L"MNR4\nmode\tLegacy\nmods\t%s\n"
        L"agent\t%s\t\tnetwork\tNetworkAdapter\n"
        L"agent\t%s\t\tcompatibility\tCompatibilityAdapter\n"
        L"agent\t%s\t\tweave-loader-legacy\tWeaveLoader\n"
        L"agent\t%s\t\tpackage\tPackageAgent\n",
        mods,
        agent_a,
        agent_b,
        agent_c,
        agent_d);
    swprintf(
        expected,
        32768,
        L"-Dweave.mods.directory=\"%s\" -Dweave.api.minecraft.enabled=true "
        L"-Dweave.dump.bytecode.enabled=false -javaagent:\"%s\" "
        L"-javaagent:\"%s\" -javaagent:\"%s\" -javaagent:\"%s\"",
        mods,
        agent_a,
        agent_b,
        agent_c,
        agent_d);
    expect_options("mnr4-legacy-agent-order", config_path, config, expected);

    swprintf(
        config,
        32768,
        L"MNR4\nmode\tDisabled\nmods\t%s\n"
        L"agent\t%s\tmode=strict\tagent-a\tPackageAgent\n"
        L"agent\t%s\t\tagent-b\tPackageAgent\n"
        L"arg\t-Xmx4G\nprop\tmoonrise.test\tone\n",
        mods,
        agent_a,
        agent_b);
    swprintf(
        expected,
        32768,
        L"-javaagent:\"%s\"=mode=strict -javaagent:\"%s\" -Xmx4G -Dmoonrise.test=one",
        agent_a,
        agent_b);
    expect_options("mnr4-java-agent-only", config_path, config, expected);
    if (wcsstr(expected, L"weave.") != NULL)
    {
        record_failure("mnr4-java-agent-only", "disabled mode unexpectedly contains Weave properties");
    }

    WCHAR *combined = build_java_options(L"-Xms512m -Duser.flag=yes");
    if (combined == NULL)
    {
        record_failure("existing-java-tool-options", "unable to combine options");
    }
    else
    {
        WCHAR expected_combined[32768];
        swprintf(expected_combined, 32768, L"%s -Xms512m -Duser.flag=yes", expected);
        if (wcscmp(combined, expected_combined) != 0)
        {
            record_failure("existing-java-tool-options", "existing options were not preserved");
        }

        WCHAR *reinjected = build_java_options(combined);
        if (reinjected == NULL || wcscmp(reinjected, combined) != 0)
        {
            record_failure("duplicate-injection", "Moonrise options were injected twice");
        }
        free_options(reinjected);
        free_options(combined);
    }

    swprintf(
        config,
        32768,
        L"MNR4\nmode\tDisabled\nmods\t%s\nunknown\tvalue\n",
        mods);
    expect_invalid("unknown-tag", config_path, config);

    swprintf(
        config,
        32768,
        L"MNR4\nmode\tDisabled\nmods\t%s\nagent\t%s\t\tagent-a\n",
        mods,
        agent_a);
    expect_invalid("malformed-agent", config_path, config);

    swprintf(
        config,
        32768,
        L"MNR4\nmode\tDisabled\nmods\t%s\nagent\t%s\t\tagent-a\tUnknownRole\n",
        mods,
        agent_a);
    expect_invalid("bad-role", config_path, config);

    swprintf(
        config,
        32768,
        L"MNR4\nmode\tDisabled\nmods\t%s\nagent\trelative.jar\t\tagent-a\tPackageAgent\n",
        mods);
    expect_invalid("relative-agent-path", config_path, config);

    swprintf(
        config,
        32768,
        L"MNR4\nmode\tDisabled\nmods\t%s\nagent\t%s\t\tagent-a\tPackageAgent\n",
        mods,
        missing_agent);
    expect_invalid("missing-agent", config_path, config);

    swprintf(
        config,
        32768,
        L"MNR4\nmode\tDisabled\nmods\t%s\n"
        L"agent\t%s\t\tagent-a\tPackageAgent\n"
        L"agent\t%s\t\tagent-b\tPackageAgent\n",
        mods,
        agent_a,
        agent_a);
    expect_invalid("duplicate-agent-path", config_path, config);

    swprintf(
        config,
        32768,
        L"MNR4\nmode\tDisabled\nmods\t%s\n"
        L"agent\t%s\t\tagent-a\tPackageAgent\n"
        L"agent\t%s\t\tAGENT-A\tPackageAgent\n",
        mods,
        agent_a,
        agent_b);
    expect_invalid("duplicate-runtime-id", config_path, config);

    swprintf(
        config,
        32768,
        L"MNR4\nmode\tDisabled\nmods\t%s\n"
        L"prop\tmoonrise.test\tone\nprop\tMOONRISE.TEST\ttwo\n",
        mods);
    expect_invalid("duplicate-property", config_path, config);

    swprintf(
        config,
        32768,
        L"MNR4\nmode\tCurrent\nmods\t%s\n"
        L"agent\t%s\t\tweave-loader-current\tWeaveLoader\n"
        L"prop\tweave.mods.directory\toverride\n",
        mods,
        agent_a);
    expect_invalid("duplicate-weave-managed-property", config_path, config);

    swprintf(
        config,
        32768,
        L"MNR5\nmode\tDisabled\nmods\t%s\n",
        mods);
    expect_invalid("invalid-header", config_path, config);

    swprintf(
        config,
        32768,
        L"MNR4\nmode\tDisabled\nmods\t%s\nagent\t%s\t\tagent-a\tPackageAgent\n",
        mods,
        agent_a);
    test_embedded_nul(config_path, config);

cleanup:
    SetEnvironmentVariableW(g_bridge_config_name, NULL);
    DeleteFileW(config_path);
    DeleteFileW(agent_a);
    DeleteFileW(agent_b);
    DeleteFileW(agent_c);
    DeleteFileW(agent_d);
    RemoveDirectoryW(mods);
    RemoveDirectoryW(temporary_seed);

    if (g_failures == 0)
    {
        printf("Native bridge parser tests passed.\n");
        return 0;
    }
    fprintf(stderr, "Native bridge parser tests failed: %d\n", g_failures);
    return 1;
}
