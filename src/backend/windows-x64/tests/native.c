// Copyright (c) All contributors. MIT license.
// The backend archive's helper suite (__chkstk). No CRT headers/startup.
typedef unsigned long long size_t;
__declspec(dllimport) __declspec(noreturn) void ExitProcess(unsigned int);
int test_probe(size_t);
int _fltused = 0; // Test executable owns the marker; never the backend archive.

__declspec(noinline) static int large_frame(void) {
    volatile unsigned char frame[32768];
    for (size_t i = 0; i < sizeof(frame); ++i) frame[i] = (unsigned char)i;
    for (size_t i = 0; i < sizeof(frame); ++i)
        if (frame[i] != (unsigned char)i) return 1;
    return 0;
}
static unsigned int run(void) {
    // Static storage: at O0 a local constant array is copied by a memcpy call, and this suite references no memory function.
    static const size_t sizes[] = {0, 1, 4095, 4096, 4097, 16384, 65537};
    for (size_t i = 0; i < sizeof(sizes) / sizeof(sizes[0]); ++i)
        if (test_probe(sizes[i])) return 7;
    return large_frame() ? 8 : 0;
}
__declspec(noreturn) void test_entry(void) { ExitProcess(run()); }
