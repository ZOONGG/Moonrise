/* Explicit one-launch developer opt-in; never writes raw command lines. */
#include <ctype.h>
typedef struct startup_capture {
    HANDLE read, write, original, file;
} startup_capture;

static const char *safe_startup_line(char *line)
{
    char lower[8192];
    size_t n = strlen(line), i;
    if (n >= sizeof(lower)) return "[Moonrise: oversized output line omitted]\n";
    for (i = 0; i <= n; i++) lower[i] = (char)tolower((unsigned char)line[i]);
    const char *blocked[] = { "token", "session", "password", "authorization", "credential", "bearer ", "accounts.json", "java_tool_options", "jdk_java_options", "_java_options", "command line", "commandline", "eyj" };
    for (i = 0; i < sizeof(blocked)/sizeof(blocked[0]); i++)
        if (strstr(lower, blocked[i])) return "[Moonrise: sensitive/options output line redacted]\n";
    return line;
}

static void save_startup_line(startup_capture *capture, char *line, DWORD *total)
{
    const char *safe = safe_startup_line(line);
    DWORD written, count = (DWORD)strlen(safe);
    if (*total + count > 2 * 1024 * 1024) return;
    WriteFile(capture->file, safe, count, &written, NULL);
    FlushFileBuffers(capture->file);
    *total += count;
}

static DWORD WINAPI pump_startup_output(LPVOID context)
{
    startup_capture *capture = context;
    char chunk[2048], line[8192];
    DWORD count, written, total = 0;
    size_t used = 0;
    BOOL oversized = FALSE;
    while (ReadFile(capture->read, chunk, sizeof(chunk), &count, NULL) && count) {

        for (DWORD i = 0; i < count; i++) {
            if (used < sizeof(line)-2) line[used++] = chunk[i]; else oversized = TRUE;
            if (chunk[i] == '\n') {
                line[used] = 0;
                save_startup_line(capture, oversized ? "[Moonrise: oversized output line omitted]\n" : line, &total);
                used = 0; oversized = FALSE;
            }
        }
        /* Persist first, then forward to the official launcher's pipe. */
        if (capture->original && capture->original != INVALID_HANDLE_VALUE)
            WriteFile(capture->original, chunk, count, &written, NULL);
    }
    if (used) { line[used] = 0; save_startup_line(capture, oversized ? "[Moonrise: oversized output line omitted]\n" : line, &total); }
    CloseHandle(capture->read);
    CloseHandle(capture->file);
    if (capture->original && capture->original != INVALID_HANDLE_VALUE) CloseHandle(capture->original);
    HeapFree(GetProcessHeap(), 0, capture);
    return 0;
}

static startup_capture *prepare_startup_capture(HANDLE original)
{
    startup_capture *capture = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(startup_capture));
    SECURITY_ATTRIBUTES attributes = { sizeof(attributes), NULL, TRUE };
    if (!capture) return NULL;
    if (!CreatePipe(&capture->read, &capture->write, &attributes, 0)) { HeapFree(GetProcessHeap(), 0, capture); return NULL; }
    SetHandleInformation(capture->read, HANDLE_FLAG_INHERIT, 0);
    if (original && original != INVALID_HANDLE_VALUE)
        DuplicateHandle(GetCurrentProcess(), original, GetCurrentProcess(), &capture->original, 0, FALSE, DUPLICATE_SAME_ACCESS);
    return capture;
}

static void finish_startup_capture(startup_capture *capture, const WCHAR *directory, DWORD pid, const WCHAR *stream)
{
    WCHAR path[32768];
    CloseHandle(capture->write);
    swprintf(path, 32768, L"%s\\jvm-%lu-%s.log", directory, (unsigned long)pid, stream);
    capture->file = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    if (capture->file == INVALID_HANDLE_VALUE) safe_log("startup-capture-file-failed", pid, GetLastError());
    HANDLE worker = CreateThread(NULL, 0, pump_startup_output, capture, 0, NULL);
    if (worker) { CloseHandle(worker); return; }
    safe_log("startup-capture-failed", pid, GetLastError());
    /* Caller only uses this after child creation; drain even if file creation failed. */
    if (capture->file != INVALID_HANDLE_VALUE) CloseHandle(capture->file);
    CloseHandle(capture->read);
    if (capture->original) CloseHandle(capture->original);
    HeapFree(GetProcessHeap(), 0, capture);
}

/* Read only the executable token, never the authentication-bearing arguments. */
static BOOL capture_is_java(LPCWSTR application, LPCWSTR command)
{
    WCHAR executable[32768];
    if (!application) {
        if (!command) return FALSE;
        size_t n = 0;
        BOOL quoted = *command == L'"';
        if (quoted) command++;
        while (*command && n < 32767 && (quoted ? *command != L'"' : *command != L' ' && *command != L'\t')) executable[n++] = *command++;
        executable[n] = 0;
        application = executable;
    }
    const WCHAR *name = wcsrchr(application, L'\\');
    name = name ? name + 1 : application;
    return !_wcsicmp(name, L"java.exe") || !_wcsicmp(name, L"javaw.exe");
}