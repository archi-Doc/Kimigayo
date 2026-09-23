// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Compiler-owned identity vocabulary.

public enum IntrinsicKind : byte
{
    None,
    Copy,
    Owned,
    Callable,
    Sealed,
    ObjectPayload,
}

/// <summary>Identifies a compiler-provided language function implementation.</summary>
public enum CompilerFunctionKind : byte
{
    None,
    WriteLine,
    Abort,
    Replace,
    Exchange,
    Swap,
    MakeObj,
    TestTempDirectory,
    ArrayReserve,
    ArrayAppend,
    ArrayInsert,
    ArrayPop,
    ArrayRemove,
    ArrayClear,
    ArrayShrinkToFit,
    ArrayInsertIndex,
    ArrayRemoveIndex,
    TextFixed,
    TextHeap,
    TextWriter,
    TextUtf8,
    TextValidateUtf8,
    TextToString,
    TextTryFormat,
    TextRelease,
    FixedBufferBytes,
    FixedBufferText,
    FixedBufferValidate,
    FixedBufferClear,
    FixedBufferReserve,
    FixedBufferIntoText,
    HeapBufferBytes,
    HeapBufferText,
    HeapBufferValidate,
    HeapBufferClear,
    HeapBufferReserve,
    HeapBufferIntoString,
    WindowPush,
    WindowAppend,
    WindowLimit,
    WindowCommit,
    WriterWrite,
    WriterStatus,
    WriteLineUtf8,
    BuiltinFormat,
}
