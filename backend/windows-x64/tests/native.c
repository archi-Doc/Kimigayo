// Copyright (c) All contributors. MIT license.
// No CRT headers/startup. Volatile reference storage prevents libcalls in the oracle.
typedef unsigned long long size_t;
__declspec(dllimport) __declspec(noreturn) void ExitProcess(unsigned int);
__declspec(dllimport) void *VirtualAlloc(void *, size_t, unsigned int, unsigned int);
__declspec(dllimport) int VirtualProtect(void *, size_t, unsigned int, unsigned int *);
__declspec(dllimport) int VirtualFree(void *, size_t, unsigned int);
void *memcpy(void *, const void *, size_t);
void *memmove(void *, const void *, size_t);
void *memset(void *, int, size_t);
int test_probe(size_t);
int _fltused = 0; // Test executable owns the marker; never the backend archive.
static unsigned char actual[4096];
static volatile unsigned char expected[4096];
static unsigned char source[4096];
static volatile unsigned char saved[4096];

static void reset(void) {
    for (size_t i = 0; i < 4096; ++i) {
        actual[i] = expected[i] = (unsigned char)(i * 17 + 3);
        source[i] = (unsigned char)(i * 29 + 7);
    }
}
static int equal(void) {
    for (size_t i = 0; i < 4096; ++i)
        if (actual[i] != expected[i]) return 0;
    return 1;
}
static unsigned char *guarded(void) {
    unsigned char *memory = VirtualAlloc(0, 12288, 0x3000, 4);
    unsigned int old;
    if (!memory) return 0;
    if (!VirtualProtect(memory, 4096, 1, &old) || !VirtualProtect(memory + 8192, 4096, 1, &old)) {
        VirtualFree(memory, 0, 0x8000);
        return 0;
    }
    return memory;
}
static int guard_tests(void) {
    unsigned char *a = guarded(), *b = guarded();
    if (!a || !b) return 9;
    for (size_t n = 0; n <= 4096; ++n) {
        // End-adjacent and start-adjacent buffers: both overreads and overwrites fault.
        for (int end = 0; end <= 1; ++end) {
            unsigned char *dst = a + (end ? 8192 - n : 4096);
            unsigned char *src = b + (end ? 8192 - n : 4096);
            memset(src, 0x57, n);
            if (memcpy(dst, src, n) != dst) return 10;
            for (size_t i = 0; i < n; ++i) if (dst[i] != 0x57) return 11;
            if (memmove(dst, dst, n) != dst) return 12;
            if (memset(dst, -1, n) != dst) return 13;
            for (size_t i = 0; i < n; ++i) if (dst[i] != 0xff) return 14;
        }
    }
    return (!VirtualFree(a, 0, 0x8000) || !VirtualFree(b, 0, 0x8000)) ? 15 : 0;
}
__declspec(noinline) static int large_frame(void) {
    volatile unsigned char frame[32768];
    for (size_t i = 0; i < sizeof(frame); ++i) frame[i] = (unsigned char)i;
    for (size_t i = 0; i < sizeof(frame); ++i)
        if (frame[i] != (unsigned char)i) return 1;
    return 0;
}
static unsigned int run(void) {
    for (size_t n = 0; n <= 257; ++n) {
        for (size_t offset = 0; offset < 16; ++offset) {
            reset();
            if (memcpy(actual + 32 + offset, source + offset, n) != actual + 32 + offset) return 1;
            for (size_t i = 0; i < n; ++i) expected[32 + offset + i] = source[offset + i];
            if (!equal()) return 2;
            reset();
            if (memset(actual + 32 + offset, 0x1234, n) != actual + 32 + offset) return 3;
            for (size_t i = 0; i < n; ++i) expected[32 + offset + i] = 0x34;
            if (!equal()) return 4;
            for (int direction = -1; direction <= 1; ++direction) {
                reset();
                size_t start = 64, dest = (size_t)(64 + direction * (int)offset);
                for (size_t i = 0; i < n; ++i) saved[i] = expected[start + i];
                for (size_t i = 0; i < n; ++i) expected[dest + i] = saved[i];
                if (memmove(actual + dest, actual + start, n) != actual + dest) return 5;
                if (!equal()) return 6;
            }
        }
    }
    const size_t sizes[] = {0, 1, 4095, 4096, 4097, 16384, 65537};
    for (size_t i = 0; i < sizeof(sizes) / sizeof(sizes[0]); ++i)
        if (test_probe(sizes[i])) return 7;
    if (large_frame()) return 8;
    return guard_tests();
}
__declspec(noreturn) void test_entry(void) { ExitProcess(run()); }
