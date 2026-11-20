// tatara_text, tatara_flag and tatara_release: reading and releasing a call's result (tatara.h).
#include "internal.h"

TataraText TATARA_CALL tatara_text(const TataraResult* result, int32_t kind)
{
    const std::string* text = kind == 0 ? &result->message
        : kind == 1                     ? &result->undefinedSymbols
        : kind == 2                     ? &result->definedSymbols
                                        : nullptr;
    return text ? TataraText{text->data(), text->size()} : TataraText{nullptr, 0};
}

int32_t TATARA_CALL tatara_flag(const TataraResult* result, int32_t kind)
{
    return kind == 0 ? result->hasUnwind : kind == 1 ? result->hasDefaultLibrary : -1;
}

void TATARA_CALL tatara_release(TataraResult* result)
{
    delete result;
}
