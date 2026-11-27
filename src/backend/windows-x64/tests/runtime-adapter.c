// Copyright (c) All contributors. MIT license.
// Fault injection only: test builds replace the runtime's kernel32 calls with these fakes; production modules import the real ones.
typedef unsigned long long u64;
typedef unsigned int u32;
__declspec(dllimport) void ExitProcess(u32);
int test_mode;
static u32 stdout_count, stderr_count, writes, handles, allocs, frees, errors;
static char stdout_bytes[64], stderr_bytes[1024];
// The pages the fake VirtualAlloc gives: one 1 MiB region for the allocator's small blocks and mode 17's large run (1,048,576 bytes
// and the 16-byte header, rounded up to 257 pages, above the allocator's spare bound, so its release reaches VirtualFree).
static __declspec(align(4096)) char region_bytes[1 << 20], large_bytes[1052672];
static int contains(const char* bytes, u32 count, const char* text) {
    for (u32 i = 0; i < count; i++) {
        u32 j = 0;
        while (text[j] && i + j < count && text[j] == bytes[i + j]) j++;
        if (!text[j]) return 1;
    }
    return 0;
}
static void require(int value) { if (!value) ExitProcess(111); }
struct string_layout { void* data; u64 length; unsigned char release; };
_Static_assert(sizeof(struct string_layout) == 24 && _Alignof(struct string_layout) == 8, "string layout");
void test_layout(u64 size, u64 alignment, u64 length_offset, u64 release_offset) {
    require(size == sizeof(struct string_layout) && alignment == _Alignof(struct string_layout));
    require(length_offset == __builtin_offsetof(struct string_layout, length));
    require(release_offset == __builtin_offsetof(struct string_layout, release));
}
void* test_VirtualAlloc(void* address, u64 size, u32 type, u32 protect) {
    require(address == 0 && type == 12288 && protect == 4);
    require((size == sizeof(region_bytes) && allocs == 0) || (size == sizeof(large_bytes) && test_mode == 17));
    allocs++;
    return test_mode == 15 ? (void*)0 : size == sizeof(large_bytes) ? large_bytes : region_bytes;
}
int test_VirtualFree(void* address, u64 size, u32 type) {
    require(address == large_bytes && size == 0 && type == 32768);
    frees++;
    return test_mode != 17;
}
void* test_GetStdHandle(u32 which) {
    handles++;
    require(which == (u32)-11 || which == (u32)-12);
    if (which == (u32)-11 && test_mode == 5) return (void*)0;
    if ((which == (u32)-11 && test_mode == 6) || (which == (u32)-12 && test_mode == 25)) return (void*)(u64)-1;
    return (void*)(u64)which;
}
u32 test_GetLastError(void) {
    errors++;
    require(test_mode == 2 || test_mode == 7 || test_mode == 17 || test_mode == 23 || test_mode >= 25);
    return test_mode == 17 ? 87 : test_mode == 27 ? (u32)-1 : test_mode == 28 ? 0 : 6;
}
int test_WriteFile(void* handle, const char* bytes, u32 count, u32* written, void* overlapped) {
    require(overlapped == 0 && written != 0 && count > 0);
    if ((u64)handle == (u32)-11) {
        writes++;
        if (test_mode == 2 || test_mode == 7 || test_mode == 23 || test_mode == 25 || test_mode == 27 || test_mode == 28 || (test_mode == 26 && writes == 2)) return 0;
        if (test_mode == 3) { *written = 0; return 1; }
        if (test_mode == 4) { *written = count + 1; return 1; }
        if (test_mode == 12) {
            require((writes == 1 && count == (u32)-1 && (u64)bytes == 65536) || (writes == 2 && count == 3 && (u64)bytes == 65536ULL + 4294967295ULL));
            *written = count;
            return 1;
        }
        if (test_mode == 1) count = 1;
        require(stdout_count + count <= sizeof(stdout_bytes));
        for (u32 i = 0; i < count; i++) stdout_bytes[stdout_count++] = bytes[i];
    } else {
        require((u64)handle == (u32)-12);
        if (test_mode == 7) return 0;
        if (test_mode == 23) { *written = 0; return 1; }
        require(stderr_count + count <= sizeof(stderr_bytes));
        for (u32 i = 0; i < count; i++) stderr_bytes[stderr_count++] = bytes[i];
    }
    *written = count;
    return 1;
}
__declspec(noreturn) void test_ExitProcess(u32 code) {
    if (test_mode == 29 || test_mode == 30) {
        require(code == 1 && stdout_count == 0 && stderr_count == 0 && handles == 0 && allocs == 0);
        ExitProcess(0);
        __builtin_unreachable();
    }
    int aborting = (test_mode >= 2 && test_mode <= 7) || (test_mode >= 15 && test_mode <= 20) || test_mode >= 23;
    if (test_mode == 24) aborting = 0;
    require(code == (aborting ? 1U : 0U));
    if (aborting) {
        if (test_mode != 7 && test_mode != 23 && test_mode != 25) {
            require(contains(stderr_bytes, stderr_count, "Test.kimi:1:1: abort KIMI_E_"));
            require(stderr_bytes[stderr_count - 1] == '\n');
        } else require(stderr_count == 0);
        if (test_mode == 2) require(contains(stderr_bytes, stderr_count, "win32=6"));
        if (test_mode == 17) require(contains(stderr_bytes, stderr_count, "win32=87"));
        if (test_mode == 27) require(contains(stderr_bytes, stderr_count, "win32=4294967295"));
        if (test_mode == 28) require(contains(stderr_bytes, stderr_count, "win32=0"));
        if (test_mode == 15 || test_mode == 20) require(errors == 0);
    } else require(stderr_count == 0);
    if (test_mode == 0 || test_mode == 1 || test_mode == 22) {
        require(stdout_count == 14 && contains(stdout_bytes, stdout_count, "Hello, world!\n"));
    }
    if (test_mode == 14 || test_mode == 21 || test_mode == 24) require(stdout_count == 1 && stdout_bytes[0] == '\n');
    if (test_mode >= 8 && test_mode <= 11) require(handles == 0);
    if (test_mode == 12) require(writes == 2);
    if (test_mode == 0 || test_mode == 1 || test_mode == 13 || test_mode == 20 || test_mode == 21) require(allocs == 0 && frees == 0);
    // A small block's release keeps it on its class's list: one region, no page released.
    if (test_mode == 14 || test_mode == 22 || test_mode == 24) require(allocs == 1 && frees == 0);
    if (test_mode == 17) require(allocs == 1 && frees == 1);
    if (test_mode == 26) require(stdout_count == 13 && allocs == 1 && frees == 0);
    ExitProcess(0);
    __builtin_unreachable();
}
