/* Optional Windows x64 C ABI counterpart. The caller provides a live,
 * aligned, initialized record. No pointer is retained; no allocation or I/O.
 */
#include <stddef.h>
#include <stdint.h>

typedef struct NativeRecord {
    uint8_t kind;
    uint64_t value;
    uint8_t flags;
} NativeRecord;

_Static_assert(offsetof(NativeRecord, value) == 8, "Windows x64 value offset");
_Static_assert(offsetof(NativeRecord, flags) == 16, "Windows x64 flags offset");
_Static_assert(sizeof(NativeRecord) == 24, "Windows x64 record size");

uint64_t observe_record(const NativeRecord *record)
{
    return record->value;
}
