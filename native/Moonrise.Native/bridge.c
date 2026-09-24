#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wchar.h>
#include <stdint.h>
#include <limits.h>
#include <stdio.h>
#include <string.h>
#include "MinHook.h"

typedef BOOL (WINAPI *CreateProcessWFn)(
    LPCWSTR,
    LPWSTR,
    LPSECURITY_ATTRIBUTES,
    LPSECURITY_ATTRIBUTES,
    BOOL,
    DWORD,
    LPVOID,
    LPCWSTR,
    LPSTARTUPINFOW,
    LPPROCESS_INFORMATION);

static WCHAR g_dll_path[32768];
static CreateProcessWFn g_original_create_process_w;
static SRWLOCK g_environment_lock = SRWLOCK_INIT;
static const WCHAR g_java_options_name[] = L"JAVA_TOOL_OPTIONS";
static const WCHAR g_bridge_config_name[] = L"MOONRISE_BRIDGE_CONFIG";
static const char g_bridge_config_name_ansi[] = "MOONRISE_BRIDGE_CONFIG";

static WCHAR *read_process_environment_value(const WCHAR *name);

static void safe_log(const char *event_name, DWORD process_id, DWORD error_code)
{
    WCHAR path[32768];
    DWORD capacity = (DWORD)(sizeof(path) / sizeof(path[0]));
    DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", path, capacity);
    if (length == 0 || length >= capacity ||
        length + wcslen(L"\\Moonrise\\logs\\native-bridge.log") + 1 >= capacity)
    {
        return;
    }
    wcscat(path, L"\\Moonrise");
    CreateDirectoryW(path, NULL);
    wcscat(path, L"\\logs");
    CreateDirectoryW(path, NULL);
    wcscat(path, L"\\native-bridge.log");

    HANDLE file = CreateFileW(
        path,
        FILE_APPEND_DATA,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL,
        OPEN_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        NULL);
    if (file == INVALID_HANDLE_VALUE)
    {
        return;
    }

    char line[512];
    int line_length = snprintf(
        line,
        sizeof(line),
        "%s pid=%lu error=%lu\r\n",
        event_name,
        (unsigned long)process_id,
        (unsigned long)error_code);
    if (line_length > 0)
    {
        DWORD written;
        WriteFile(file, line, (DWORD)line_length, &written, NULL);
    }
    CloseHandle(file);
}

static void ready_event_name(DWORD process_id, WCHAR *buffer, size_t capacity)
{
    swprintf(
        buffer,
        capacity,
        L"Local\\MoonriseBridgeReadyV1-%lu",
        (unsigned long)process_id);
}

static void signal_ready(void)
{
    WCHAR name[128];
    ready_event_name(GetCurrentProcessId(), name, sizeof(name) / sizeof(name[0]));
    HANDLE event_handle = OpenEventW(EVENT_MODIFY_STATE, FALSE, name);
    if (event_handle != NULL)
    {
        SetEvent(event_handle);
        CloseHandle(event_handle);
    }
}

static BOOL is_java_process(void)
{
    WCHAR path[32768];
    DWORD length = GetModuleFileNameW(NULL, path, (DWORD)(sizeof(path) / sizeof(path[0])));
    if (length == 0 || length >= (sizeof(path) / sizeof(path[0])))
    {
        return FALSE;
    }

    const WCHAR *name = path + length;
    while (name > path && name[-1] != L'\\' && name[-1] != L'/')
    {
        name--;
    }
    return _wcsicmp(name, L"java.exe") == 0 || _wcsicmp(name, L"javaw.exe") == 0;
}

static WCHAR *next_config_line(WCHAR **cursor)
{
    if (cursor == NULL || *cursor == NULL)
    {
        return NULL;
    }

    WCHAR *line = *cursor;
    WCHAR *newline = wcschr(line, L'\n');
    if (newline == NULL)
    {
        *cursor = NULL;
    }
    else
    {
        *newline = L'\0';
        *cursor = newline + 1;
    }

    size_t length = wcslen(line);
    if (length > 0 && line[length - 1] == L'\r')
    {
        line[length - 1] = L'\0';
    }
    return line;
}

static BOOL is_absolute_safe_path(const WCHAR *path)
{
    if (path == NULL || path[0] == L'\0' ||
        wcschr(path, L'\r') != NULL ||
        wcschr(path, L'\n') != NULL ||
        wcschr(path, L'"') != NULL)
    {
        return FALSE;
    }
    return ((path[0] >= L'A' && path[0] <= L'Z') ||
            (path[0] >= L'a' && path[0] <= L'z')) &&
           path[1] == L':' &&
           (path[2] == L'\\' || path[2] == L'/');
}


static WCHAR *next_config_field(WCHAR **cursor)
{
    if (cursor == NULL || *cursor == NULL)
    {
        return NULL;
    }

    WCHAR *field = *cursor;
    WCHAR *tab = wcschr(field, L'\t');
    if (tab == NULL)
    {
        *cursor = NULL;
    }
    else
    {
        *tab = L'\0';
        *cursor = tab + 1;
    }
    return field;
}

static BOOL is_safe_mnr4_field(const WCHAR *value, BOOL allow_empty)
{
    if (value == NULL || (!allow_empty && value[0] == L'\0'))
    {
        return FALSE;
    }
    return wcschr(value, L'\r') == NULL &&
           wcschr(value, L'\n') == NULL &&
           wcschr(value, L'\t') == NULL &&
           wcschr(value, L'"') == NULL;
}

static BOOL append_option_text(WCHAR *destination, size_t capacity, const WCHAR *value)
{
    size_t current = wcslen(destination);
    size_t addition = wcslen(value);
    if (current + addition + 1 > capacity)
    {
        SetLastError(ERROR_INSUFFICIENT_BUFFER);
        return FALSE;
    }
    CopyMemory(
        destination + current,
        value,
        (addition + 1) * sizeof(WCHAR));
    return TRUE;
}

static BOOL append_option_space(WCHAR *destination, size_t capacity)
{
    if (destination[0] == L'\0')
    {
        return TRUE;
    }
    return append_option_text(destination, capacity, L" ");
}

static BOOL append_mnr4_argument(WCHAR *destination, size_t capacity, const WCHAR *argument)
{
    if (!append_option_space(destination, capacity))
    {
        return FALSE;
    }

    BOOL quote = wcschr(argument, L' ') != NULL;
    if (quote && !append_option_text(destination, capacity, L"\""))
    {
        return FALSE;
    }
    if (!append_option_text(destination, capacity, argument))
    {
        return FALSE;
    }
    if (quote && !append_option_text(destination, capacity, L"\""))
    {
        return FALSE;
    }
    return TRUE;
}

static BOOL append_mnr4_agent(
    WCHAR *destination,
    size_t capacity,
    const WCHAR *path,
    const WCHAR *agent_options)
{
    if (!append_option_space(destination, capacity) ||
        !append_option_text(destination, capacity, L"-javaagent:\"") ||
        !append_option_text(destination, capacity, path) ||
        !append_option_text(destination, capacity, L"\""))
    {
        return FALSE;
    }

    if (agent_options != NULL && agent_options[0] != L'\0')
    {
        if (!append_option_text(destination, capacity, L"="))
        {
            return FALSE;
        }
        BOOL quote = wcschr(agent_options, L' ') != NULL;
        if (quote && !append_option_text(destination, capacity, L"\""))
        {
            return FALSE;
        }
        if (!append_option_text(destination, capacity, agent_options))
        {
            return FALSE;
        }
        if (quote && !append_option_text(destination, capacity, L"\""))
        {
            return FALSE;
        }
    }
    return TRUE;
}

static BOOL append_mnr4_property(
    WCHAR *destination,
    size_t capacity,
    const WCHAR *name,
    const WCHAR *value)
{
    if (!append_option_space(destination, capacity) ||
        !append_option_text(destination, capacity, L"-D") ||
        !append_option_text(destination, capacity, name) ||
        !append_option_text(destination, capacity, L"="))
    {
        return FALSE;
    }

    BOOL quote = wcschr(value, L' ') != NULL;
    if (quote && !append_option_text(destination, capacity, L"\""))
    {
        return FALSE;
    }
    if (!append_option_text(destination, capacity, value))
    {
        return FALSE;
    }
    if (quote && !append_option_text(destination, capacity, L"\""))
    {
        return FALSE;
    }
    return TRUE;
}

static WCHAR *read_mnr4_options(WCHAR *cursor, size_t wide_length)
{
    WCHAR *mode_line = next_config_line(&cursor);
    WCHAR *mode_cursor = mode_line;
    WCHAR *mode_tag = next_config_field(&mode_cursor);
    WCHAR *mode = next_config_field(&mode_cursor);
    if (mode_tag == NULL || wcscmp(mode_tag, L"mode") != 0 ||
        mode == NULL || mode_cursor != NULL ||
        !is_safe_mnr4_field(mode, FALSE))
    {
        SetLastError(ERROR_INVALID_DATA);
        return NULL;
    }

    BOOL weave_disabled = wcscmp(mode, L"Disabled") == 0;
    if (!weave_disabled &&
        wcscmp(mode, L"Current") != 0 &&
        wcscmp(mode, L"Legacy") != 0 &&
        wcscmp(mode, L"Custom") != 0)
    {
        SetLastError(ERROR_INVALID_DATA);
        return NULL;
    }

    WCHAR *mods_line = next_config_line(&cursor);
    WCHAR *mods_cursor = mods_line;
    WCHAR *mods_tag = next_config_field(&mods_cursor);
    WCHAR *mods_path = next_config_field(&mods_cursor);
    if (mods_tag == NULL || wcscmp(mods_tag, L"mods") != 0 ||
        mods_path == NULL || mods_cursor != NULL ||
        !is_absolute_safe_path(mods_path))
    {
        SetLastError(ERROR_INVALID_DATA);
        return NULL;
    }

    DWORD mods_attributes = GetFileAttributesW(mods_path);
    if (mods_attributes == INVALID_FILE_ATTRIBUTES ||
        (mods_attributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
    {
        SetLastError(ERROR_PATH_NOT_FOUND);
        return NULL;
    }

    size_t options_capacity = (wide_length + 1) * 8 + 1024;
    WCHAR *options = HeapAlloc(
        GetProcessHeap(),
        HEAP_ZERO_MEMORY,
        options_capacity * sizeof(WCHAR));
    if (options == NULL)
    {
        return NULL;
    }

    if (!weave_disabled)
    {
        if (!append_option_text(options, options_capacity, L"-Dweave.mods.directory=\"") ||
            !append_option_text(options, options_capacity, mods_path) ||
            !append_option_text(options, options_capacity, L"\"") ||
            !append_option_text(options, options_capacity, L" -Dweave.api.minecraft.enabled=true") ||
            !append_option_text(options, options_capacity, L" -Dweave.dump.bytecode.enabled=false"))
        {
            goto invalid_options;
        }
    }

    size_t weave_loader_count = 0;
    WCHAR *line;
    while ((line = next_config_line(&cursor)) != NULL)
    {
        if (line[0] == L'\0')
        {
            continue;
        }

        WCHAR *field_cursor = line;
        WCHAR *tag = next_config_field(&field_cursor);
        if (tag == NULL)
        {
            goto invalid_data;
        }

        if (wcscmp(tag, L"agent") == 0)
        {
            WCHAR *path = next_config_field(&field_cursor);
            WCHAR *agent_options = next_config_field(&field_cursor);
            WCHAR *runtime_id = next_config_field(&field_cursor);
            WCHAR *role = next_config_field(&field_cursor);
            if (path == NULL || agent_options == NULL || runtime_id == NULL || role == NULL ||
                field_cursor != NULL ||
                !is_absolute_safe_path(path) ||
                !is_safe_mnr4_field(agent_options, TRUE) ||
                !is_safe_mnr4_field(runtime_id, FALSE) ||
                !is_safe_mnr4_field(role, FALSE))
            {
                goto invalid_data;
            }

            DWORD attributes = GetFileAttributesW(path);
            if (attributes == INVALID_FILE_ATTRIBUTES ||
                (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
            {
                SetLastError(ERROR_FILE_NOT_FOUND);
                goto invalid_options;
            }

            BOOL is_weave_loader = wcscmp(role, L"WeaveLoader") == 0;
            if (!is_weave_loader &&
                wcscmp(role, L"NetworkAdapter") != 0 &&
                wcscmp(role, L"CompatibilityAdapter") != 0 &&
                wcscmp(role, L"PackageAgent") != 0)
            {
                goto invalid_data;
            }
            if (is_weave_loader)
            {
                weave_loader_count++;
                if (weave_disabled || weave_loader_count > 1)
                {
                    goto invalid_data;
                }
            }

            if (!append_mnr4_agent(options, options_capacity, path, agent_options))
            {
                goto invalid_options;
            }
        }
        else if (wcscmp(tag, L"arg") == 0)
        {
            WCHAR *argument = next_config_field(&field_cursor);
            if (argument == NULL || field_cursor != NULL ||
                !is_safe_mnr4_field(argument, FALSE) ||
                _wcsnicmp(argument, L"-javaagent:", 11) == 0 ||
                wcsncmp(argument, L"-D", 2) == 0)
            {
                goto invalid_data;
            }
            if (!append_mnr4_argument(options, options_capacity, argument))
            {
                goto invalid_options;
            }
        }
        else if (wcscmp(tag, L"prop") == 0)
        {
            WCHAR *name = next_config_field(&field_cursor);
            WCHAR *value = next_config_field(&field_cursor);
            if (name == NULL || value == NULL || field_cursor != NULL ||
                !is_safe_mnr4_field(name, FALSE) ||
                !is_safe_mnr4_field(value, TRUE) ||
                wcschr(name, L'=') != NULL)
            {
                goto invalid_data;
            }
            if (!append_mnr4_property(options, options_capacity, name, value))
            {
                goto invalid_options;
            }
        }
        else
        {
            goto invalid_data;
        }
    }

    if ((!weave_disabled && weave_loader_count != 1) ||
        (weave_disabled && weave_loader_count != 0))
    {
        goto invalid_data;
    }

    return options;

invalid_data:
    SetLastError(ERROR_INVALID_DATA);
invalid_options:
    SecureZeroMemory(options, options_capacity * sizeof(WCHAR));
    HeapFree(GetProcessHeap(), 0, options);
    return NULL;
}

static WCHAR *read_bridge_options(void)
{
    WCHAR path[32768];
    DWORD path_length = GetEnvironmentVariableW(
        g_bridge_config_name,
        path,
        (DWORD)(sizeof(path) / sizeof(path[0])));
    if (path_length == 0)
    {
        safe_log("bridge-config-env-missing-or-empty", GetCurrentProcessId(), ERROR_ENVVAR_NOT_FOUND);
        SetLastError(ERROR_ENVVAR_NOT_FOUND);
        return NULL;
    }
    if (path_length >= (sizeof(path) / sizeof(path[0])) || !is_absolute_safe_path(path))
    {
        safe_log("bridge-config-path-invalid", GetCurrentProcessId(), ERROR_INVALID_NAME);
        SetLastError(ERROR_INVALID_NAME);
        return NULL;
    }

    HANDLE file = CreateFileW(
        path,
        GENERIC_READ,
        FILE_SHARE_READ,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);
    if (file == INVALID_HANDLE_VALUE)
    {
        safe_log("bridge-config-file-missing", GetCurrentProcessId(), GetLastError());
        return NULL;
    }

    char buffer[65536];
    DWORD bytes_read = 0;
    BOOL read_ok = ReadFile(file, buffer, sizeof(buffer) - 1, &bytes_read, NULL);
    CloseHandle(file);
    if (!read_ok || bytes_read == 0 || bytes_read >= sizeof(buffer))
    {
        safe_log("bridge-config-read-invalid", GetCurrentProcessId(), ERROR_INVALID_DATA);
        SetLastError(ERROR_INVALID_DATA);
        return NULL;
    }
    buffer[bytes_read] = '\0';

    int wide_length = MultiByteToWideChar(
        CP_UTF8,
        MB_ERR_INVALID_CHARS,
        buffer,
        (int)bytes_read,
        NULL,
        0);
    if (wide_length <= 0)
    {
        safe_log("bridge-config-utf8-invalid", GetCurrentProcessId(), ERROR_NO_UNICODE_TRANSLATION);
        SetLastError(ERROR_NO_UNICODE_TRANSLATION);
        return NULL;
    }

    WCHAR *wide_buffer = HeapAlloc(
        GetProcessHeap(),
        HEAP_ZERO_MEMORY,
        ((size_t)wide_length + 1) * sizeof(WCHAR));
    if (wide_buffer == NULL)
    {
        return NULL;
    }
    if (MultiByteToWideChar(
            CP_UTF8,
            MB_ERR_INVALID_CHARS,
            buffer,
            (int)bytes_read,
            wide_buffer,
            wide_length) != wide_length)
    {
        HeapFree(GetProcessHeap(), 0, wide_buffer);
        return NULL;
    }

    WCHAR *cursor = wide_buffer;
    WCHAR *header = next_config_line(&cursor);
    if (header != NULL && wcscmp(header, L"MNR4") == 0)
    {
        WCHAR *options = read_mnr4_options(cursor, (size_t)wide_length);
        if (options == NULL)
        {
            safe_log("bridge-config-mnr4-invalid", GetCurrentProcessId(), GetLastError());
        }
        SecureZeroMemory(wide_buffer, ((size_t)wide_length + 1) * sizeof(WCHAR));
        HeapFree(GetProcessHeap(), 0, wide_buffer);
        return options;
    }

    WCHAR *agent_path = next_config_line(&cursor);
    WCHAR *mods_path = next_config_line(&cursor);
    if (header == NULL || wcscmp(header, L"MNR3") != 0 ||
        agent_path == NULL || agent_path[0] == L'\0' ||
        mods_path == NULL || mods_path[0] == L'\0' ||
        !is_absolute_safe_path(agent_path) ||
        !is_absolute_safe_path(mods_path) ||
        GetFileAttributesW(agent_path) == INVALID_FILE_ATTRIBUTES)
    {
        safe_log("bridge-config-content-invalid", GetCurrentProcessId(), ERROR_INVALID_DATA);
        SetLastError(ERROR_INVALID_DATA);
        HeapFree(GetProcessHeap(), 0, wide_buffer);
        return NULL;
    }
    DWORD mods_attributes = GetFileAttributesW(mods_path);
    if (mods_attributes == INVALID_FILE_ATTRIBUTES ||
        (mods_attributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
    {
        safe_log("bridge-config-mod-directory-invalid", GetCurrentProcessId(), ERROR_PATH_NOT_FOUND);
        SetLastError(ERROR_PATH_NOT_FOUND);
        HeapFree(GetProcessHeap(), 0, wide_buffer);
        return NULL;
    }

    size_t options_capacity = ((size_t)wide_length + 1) * 8 + 512;
    WCHAR *options = HeapAlloc(
        GetProcessHeap(),
        HEAP_ZERO_MEMORY,
        options_capacity * sizeof(WCHAR));
    if (options == NULL)
    {
        HeapFree(GetProcessHeap(), 0, wide_buffer);
        return NULL;
    }

    swprintf(
        options,
        options_capacity,
        L"-javaagent:\"%s\" -Dweave.mods.directory=\"%s\" "
        L"-Dweave.api.minecraft.enabled=true -Dweave.dump.bytecode.enabled=false",
        agent_path,
        mods_path);
    WCHAR *additional_agent;
    while ((additional_agent = next_config_line(&cursor)) != NULL)
    {
        if (additional_agent[0] == L'\0')
        {
            continue;
        }
        if (!is_absolute_safe_path(additional_agent) ||
            GetFileAttributesW(additional_agent) == INVALID_FILE_ATTRIBUTES)
        {
            safe_log("bridge-config-agent-invalid", GetCurrentProcessId(), ERROR_FILE_NOT_FOUND);
            SetLastError(ERROR_FILE_NOT_FOUND);
            SecureZeroMemory(options, options_capacity * sizeof(WCHAR));
            HeapFree(GetProcessHeap(), 0, options);
            HeapFree(GetProcessHeap(), 0, wide_buffer);
            return NULL;
        }
        wcscat(options, L" -javaagent:\"");
        wcscat(options, additional_agent);
        wcscat(options, L"\"");
    }

    SecureZeroMemory(wide_buffer, ((size_t)wide_length + 1) * sizeof(WCHAR));
    HeapFree(GetProcessHeap(), 0, wide_buffer);
    return options;
}

static WCHAR *build_java_options(const WCHAR *existing)
{
    WCHAR *additions = read_bridge_options();
    if (additions == NULL)
    {
        return NULL;
    }

    size_t additions_capacity = wcslen(additions) + 1;
    size_t existing_length = existing == NULL ? 0 : wcslen(existing);
    size_t combined_capacity = additions_capacity + existing_length + 2;
    WCHAR *combined = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, combined_capacity * sizeof(WCHAR));
    if (combined == NULL)
    {
        HeapFree(GetProcessHeap(), 0, additions);
        return NULL;
    }

    if (existing_length > 0 && wcsstr(existing, additions) != NULL)
    {
        wcscpy(combined, existing);
    }
    else
    {
        wcscpy(combined, additions);
        if (existing_length > 0)
        {
            wcscat(combined, L" ");
            wcscat(combined, existing);
        }
    }

    SecureZeroMemory(additions, additions_capacity * sizeof(WCHAR));
    HeapFree(GetProcessHeap(), 0, additions);
    return combined;
}

static BOOL validate_bridge_configuration(void)
{
    WCHAR *options = read_bridge_options();
    if (options == NULL)
    {
        safe_log("bridge-initialization-failed", GetCurrentProcessId(), GetLastError());
        return FALSE;
    }
    SecureZeroMemory(options, (wcslen(options) + 1) * sizeof(WCHAR));
    HeapFree(GetProcessHeap(), 0, options);
    return TRUE;
}

static WCHAR *read_process_environment_value(const WCHAR *name)
{
    DWORD length = GetEnvironmentVariableW(name, NULL, 0);
    if (length == 0)
    {
        return NULL;
    }

    WCHAR *value = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, length * sizeof(WCHAR));
    if (value == NULL || GetEnvironmentVariableW(name, value, length) == 0)
    {
        if (value != NULL)
        {
            HeapFree(GetProcessHeap(), 0, value);
        }
        return NULL;
    }
    return value;
}

static BOOL apply_java_options(void)
{
    WCHAR *existing = read_process_environment_value(g_java_options_name);
    WCHAR *combined = build_java_options(existing);
    BOOL result = combined != NULL &&
                  SetEnvironmentVariableW(g_java_options_name, combined) &&
                  SetEnvironmentVariableW(L"MOONRISE_BRIDGE_INJECTED_V1", L"1");
    safe_log(
        result ? "java-options-applied" : "java-options-failed",
        GetCurrentProcessId(),
        result ? 0 : GetLastError());

    if (existing != NULL)
    {
        SecureZeroMemory(existing, (wcslen(existing) + 1) * sizeof(WCHAR));
        HeapFree(GetProcessHeap(), 0, existing);
    }
    if (combined != NULL)
    {
        SecureZeroMemory(combined, (wcslen(combined) + 1) * sizeof(WCHAR));
        HeapFree(GetProcessHeap(), 0, combined);
    }
    return result;
}

static BOOL inject_bridge(HANDLE process, DWORD process_id)
{
    WCHAR event_name[128];
    ready_event_name(process_id, event_name, sizeof(event_name) / sizeof(event_name[0]));
    HANDLE ready = CreateEventW(NULL, TRUE, FALSE, event_name);
    if (ready == NULL)
    {
        return FALSE;
    }

    size_t path_bytes = (wcslen(g_dll_path) + 1) * sizeof(WCHAR);
    LPVOID remote_path = VirtualAllocEx(
        process,
        NULL,
        path_bytes,
        MEM_RESERVE | MEM_COMMIT,
        PAGE_READWRITE);
    if (remote_path == NULL)
    {
        CloseHandle(ready);
        return FALSE;
    }

    SIZE_T written = 0;
    BOOL result = WriteProcessMemory(
        process,
        remote_path,
        g_dll_path,
        path_bytes,
        &written);
    HMODULE kernel32 = GetModuleHandleW(L"kernel32.dll");
    FARPROC load_library = kernel32 == NULL ? NULL : GetProcAddress(kernel32, "LoadLibraryW");
    HANDLE remote_thread = NULL;
    if (result && written == path_bytes && load_library != NULL)
    {
        remote_thread = CreateRemoteThread(
            process,
            NULL,
            0,
            (LPTHREAD_START_ROUTINE)load_library,
            remote_path,
            0,
            NULL);
        result = remote_thread != NULL;
    }

    if (result)
    {
        result = WaitForSingleObject(remote_thread, 10000) == WAIT_OBJECT_0;
    }
    if (result)
    {
        DWORD load_result = 0;
        result = GetExitCodeThread(remote_thread, &load_result) && load_result != 0;
    }
    if (result)
    {
        result = WaitForSingleObject(ready, 10000) == WAIT_OBJECT_0;
    }

    DWORD error_code = result ? 0 : GetLastError();
    safe_log(result ? "child-bridge-ready" : "child-bridge-failed", process_id, error_code);

    if (remote_thread != NULL)
    {
        CloseHandle(remote_thread);
    }
    VirtualFreeEx(process, remote_path, 0, MEM_RELEASE);
    CloseHandle(ready);
    return result;
}

static BOOL wide_entry_has_name(const WCHAR *entry, const WCHAR *name)
{
    size_t name_length = wcslen(name);
    return _wcsnicmp(entry, name, name_length) == 0 && entry[name_length] == L'=';
}

static BOOL ansi_entry_has_name(const char *entry, const char *name)
{
    size_t name_length = strlen(name);
    return _strnicmp(entry, name, name_length) == 0 && entry[name_length] == '=';
}

static WCHAR *find_wide_environment_value(const WCHAR *environment, const WCHAR *name)
{
    const WCHAR *entry = environment;
    while (entry != NULL && *entry != L'\0')
    {
        if (wide_entry_has_name(entry, name))
        {
            return (WCHAR *)(entry + wcslen(name) + 1);
        }
        entry += wcslen(entry) + 1;
    }
    return NULL;
}

static char *find_ansi_environment_value(const char *environment, const char *name)
{
    const char *entry = environment;
    while (entry != NULL && *entry != '\0')
    {
        if (ansi_entry_has_name(entry, name))
        {
            return (char *)(entry + strlen(name) + 1);
        }
        entry += strlen(entry) + 1;
    }
    return NULL;
}

static WCHAR *augment_wide_environment(const WCHAR *environment)
{
    const WCHAR *existing = find_wide_environment_value(environment, g_java_options_name);
    WCHAR *config_path = read_process_environment_value(g_bridge_config_name);
    if (config_path == NULL)
        return NULL;
    WCHAR *combined = build_java_options(existing);
    if (combined == NULL)
    {
        HeapFree(GetProcessHeap(), 0, config_path);
        return NULL;
    }

    size_t original_chars = 1;
    const WCHAR *entry = environment;
    while (*entry != L'\0')
    {
        original_chars += wcslen(entry) + 1;
        entry += wcslen(entry) + 1;
    }

    size_t capacity = original_chars +
                      wcslen(g_java_options_name) + wcslen(combined) +
                      wcslen(g_bridge_config_name) + wcslen(config_path) + 5;
    WCHAR *result = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, capacity * sizeof(WCHAR));
    if (result == NULL)
    {
        SecureZeroMemory(combined, (wcslen(combined) + 1) * sizeof(WCHAR));
        HeapFree(GetProcessHeap(), 0, combined);
        HeapFree(GetProcessHeap(), 0, config_path);
        return NULL;
    }

    WCHAR *destination = result;
    entry = environment;
    while (*entry != L'\0')
    {
        size_t entry_chars = wcslen(entry) + 1;
        if (!wide_entry_has_name(entry, g_java_options_name) &&
            !wide_entry_has_name(entry, g_bridge_config_name))
        {
            CopyMemory(destination, entry, entry_chars * sizeof(WCHAR));
            destination += entry_chars;
        }
        entry += entry_chars;
    }

    wcscpy(destination, g_java_options_name);
    destination += wcslen(g_java_options_name);
    *destination++ = L'=';
    wcscpy(destination, combined);
    destination += wcslen(combined) + 1;
    wcscpy(destination, g_bridge_config_name);
    destination += wcslen(g_bridge_config_name);
    *destination++ = L'=';
    wcscpy(destination, config_path);
    destination += wcslen(config_path) + 1;
    *destination = L'\0';

    SecureZeroMemory(combined, (wcslen(combined) + 1) * sizeof(WCHAR));
    HeapFree(GetProcessHeap(), 0, combined);
    HeapFree(GetProcessHeap(), 0, config_path);
    return result;
}

static char *augment_ansi_environment(const char *environment)
{
    const char *existing_ansi = find_ansi_environment_value(environment, "JAVA_TOOL_OPTIONS");
    WCHAR *config_path_wide = read_process_environment_value(g_bridge_config_name);
    if (config_path_wide == NULL)
        return NULL;
    WCHAR *existing_wide = NULL;
    if (existing_ansi != NULL && *existing_ansi != '\0')
    {
        int wide_length = MultiByteToWideChar(CP_ACP, 0, existing_ansi, -1, NULL, 0);
        if (wide_length > 0)
        {
            existing_wide = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, wide_length * sizeof(WCHAR));
            if (existing_wide != NULL)
            {
                MultiByteToWideChar(CP_ACP, 0, existing_ansi, -1, existing_wide, wide_length);
            }
        }
    }

    WCHAR *combined_wide = build_java_options(existing_wide);
    if (existing_wide != NULL)
    {
        SecureZeroMemory(existing_wide, (wcslen(existing_wide) + 1) * sizeof(WCHAR));
        HeapFree(GetProcessHeap(), 0, existing_wide);
    }
    if (combined_wide == NULL)
    {
        HeapFree(GetProcessHeap(), 0, config_path_wide);
        return NULL;
    }

    int combined_length = WideCharToMultiByte(CP_ACP, 0, combined_wide, -1, NULL, 0, NULL, NULL);
    if (combined_length <= 0)
    {
        SecureZeroMemory(combined_wide, (wcslen(combined_wide) + 1) * sizeof(WCHAR));
        HeapFree(GetProcessHeap(), 0, combined_wide);
        HeapFree(GetProcessHeap(), 0, config_path_wide);
        return NULL;
    }
    char *combined = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, combined_length);
    if (combined == NULL ||
        WideCharToMultiByte(CP_ACP, 0, combined_wide, -1, combined, combined_length, NULL, NULL) == 0)
    {
        if (combined != NULL)
        {
            HeapFree(GetProcessHeap(), 0, combined);
        }
        SecureZeroMemory(combined_wide, (wcslen(combined_wide) + 1) * sizeof(WCHAR));
        HeapFree(GetProcessHeap(), 0, combined_wide);
        HeapFree(GetProcessHeap(), 0, config_path_wide);
        return NULL;
    }
    SecureZeroMemory(combined_wide, (wcslen(combined_wide) + 1) * sizeof(WCHAR));
    HeapFree(GetProcessHeap(), 0, combined_wide);

    int config_length = WideCharToMultiByte(CP_ACP, 0, config_path_wide, -1, NULL, 0, NULL, NULL);
    if (config_length <= 0)
    {
        SecureZeroMemory(combined, strlen(combined) + 1);
        HeapFree(GetProcessHeap(), 0, combined);
        HeapFree(GetProcessHeap(), 0, config_path_wide);
        return NULL;
    }
    char *config_path = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, config_length);
    if (config_path == NULL ||
        WideCharToMultiByte(CP_ACP, 0, config_path_wide, -1, config_path, config_length, NULL, NULL) == 0)
    {
        if (config_path != NULL)
            HeapFree(GetProcessHeap(), 0, config_path);
        SecureZeroMemory(combined, strlen(combined) + 1);
        HeapFree(GetProcessHeap(), 0, combined);
        HeapFree(GetProcessHeap(), 0, config_path_wide);
        return NULL;
    }
    HeapFree(GetProcessHeap(), 0, config_path_wide);

    size_t original_bytes = 1;
    const char *entry = environment;
    while (*entry != '\0')
    {
        original_bytes += strlen(entry) + 1;
        entry += strlen(entry) + 1;
    }

    size_t capacity = original_bytes +
                      strlen("JAVA_TOOL_OPTIONS") + strlen(combined) +
                      strlen(g_bridge_config_name_ansi) + strlen(config_path) + 5;
    char *result = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, capacity);
    if (result == NULL)
    {
        SecureZeroMemory(combined, strlen(combined) + 1);
        HeapFree(GetProcessHeap(), 0, combined);
        HeapFree(GetProcessHeap(), 0, config_path);
        return NULL;
    }

    char *destination = result;
    entry = environment;
    while (*entry != '\0')
    {
        size_t entry_bytes = strlen(entry) + 1;
        if (!ansi_entry_has_name(entry, "JAVA_TOOL_OPTIONS") &&
            !ansi_entry_has_name(entry, g_bridge_config_name_ansi))
        {
            CopyMemory(destination, entry, entry_bytes);
            destination += entry_bytes;
        }
        entry += entry_bytes;
    }

    strcpy(destination, "JAVA_TOOL_OPTIONS");
    destination += strlen("JAVA_TOOL_OPTIONS");
    *destination++ = '=';
    strcpy(destination, combined);
    destination += strlen(combined) + 1;
    strcpy(destination, g_bridge_config_name_ansi);
    destination += strlen(g_bridge_config_name_ansi);
    *destination++ = '=';
    strcpy(destination, config_path);
    destination += strlen(config_path) + 1;
    *destination = '\0';

    SecureZeroMemory(combined, strlen(combined) + 1);
    HeapFree(GetProcessHeap(), 0, combined);
    HeapFree(GetProcessHeap(), 0, config_path);
    return result;
}

static BOOL WINAPI hooked_create_process_w(
    LPCWSTR application_name,
    LPWSTR command_line,
    LPSECURITY_ATTRIBUTES process_attributes,
    LPSECURITY_ATTRIBUTES thread_attributes,
    BOOL inherit_handles,
    DWORD creation_flags,
    LPVOID environment,
    LPCWSTR current_directory,
    LPSTARTUPINFOW startup_info,
    LPPROCESS_INFORMATION process_information)
{
    BOOL caller_requested_suspended = (creation_flags & CREATE_SUSPENDED) != 0;
    LPVOID augmented_environment = NULL;
    WCHAR *saved_parent_options = NULL;
    BOOL parent_had_options = FALSE;
    BOOL parent_environment_locked = FALSE;

    if (environment != NULL)
    {
        if ((creation_flags & CREATE_UNICODE_ENVIRONMENT) != 0)
        {
            augmented_environment = augment_wide_environment((const WCHAR *)environment);
        }
        else
        {
            augmented_environment = augment_ansi_environment((const char *)environment);
        }
        safe_log(
            augmented_environment != NULL
                ? "child-environment-patched"
                : "child-environment-patch-failed",
            GetCurrentProcessId(),
            augmented_environment != NULL ? 0 : GetLastError());
    }
    else
    {
        AcquireSRWLockExclusive(&g_environment_lock);
        parent_environment_locked = TRUE;
        saved_parent_options = read_process_environment_value(g_java_options_name);
        parent_had_options = saved_parent_options != NULL;
        WCHAR *combined = build_java_options(saved_parent_options);
        if (combined != NULL)
        {
            SetEnvironmentVariableW(g_java_options_name, combined);
            SecureZeroMemory(combined, (wcslen(combined) + 1) * sizeof(WCHAR));
            HeapFree(GetProcessHeap(), 0, combined);
        }
    }

    LPVOID environment_to_use = augmented_environment != NULL ? augmented_environment : environment;
    BOOL created = g_original_create_process_w(
        application_name,
        command_line,
        process_attributes,
        thread_attributes,
        inherit_handles,
        creation_flags | CREATE_SUSPENDED,
        environment_to_use,
        current_directory,
        startup_info,
        process_information);

    if (augmented_environment != NULL)
    {
        HeapFree(GetProcessHeap(), 0, augmented_environment);
    }
    if (parent_environment_locked)
    {
        SetEnvironmentVariableW(
            g_java_options_name,
            parent_had_options ? saved_parent_options : NULL);
        if (saved_parent_options != NULL)
        {
            SecureZeroMemory(saved_parent_options, (wcslen(saved_parent_options) + 1) * sizeof(WCHAR));
            HeapFree(GetProcessHeap(), 0, saved_parent_options);
        }
        ReleaseSRWLockExclusive(&g_environment_lock);
    }

    if (!created || process_information == NULL)
    {
        return created;
    }

    inject_bridge(process_information->hProcess, process_information->dwProcessId);
    if (!caller_requested_suspended)
    {
        ResumeThread(process_information->hThread);
    }
    return created;
}

static DWORD WINAPI initialize_bridge(LPVOID ignored)
{
    (void)ignored;
    if (!validate_bridge_configuration())
    {
        return 0;
    }
    if (is_java_process())
    {
        if (apply_java_options())
        {
            signal_ready();
        }
        return 0;
    }

    MH_STATUS status = MH_Initialize();
    if (status == MH_OK || status == MH_ERROR_ALREADY_INITIALIZED)
    {
        status = MH_CreateHookApi(
            L"kernel32.dll",
            "CreateProcessW",
            (LPVOID)hooked_create_process_w,
            (LPVOID *)&g_original_create_process_w);
    }
    if (status == MH_OK)
    {
        status = MH_EnableHook(MH_ALL_HOOKS);
    }

    if (status == MH_OK)
    {
        safe_log("process-hook-ready", GetCurrentProcessId(), 0);
        signal_ready();
    }
    else
    {
        safe_log("process-hook-failed", GetCurrentProcessId(), (DWORD)status);
    }
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(instance);
        if (GetModuleFileNameW(
                instance,
                g_dll_path,
                (DWORD)(sizeof(g_dll_path) / sizeof(g_dll_path[0]))) == 0)
        {
            return FALSE;
        }

        HANDLE thread = CreateThread(NULL, 0, initialize_bridge, NULL, 0, NULL);
        if (thread == NULL)
        {
            return FALSE;
        }
        CloseHandle(thread);
    }
    return TRUE;
}
