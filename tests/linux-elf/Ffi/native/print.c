int printf(const char *format, ...);
int atexit(void (*handler)(void));
static void done(void) { printf(" atexit\n"); }
int codec_add(int a, short b) { printf("c says"); atexit(done); return a + b; }
