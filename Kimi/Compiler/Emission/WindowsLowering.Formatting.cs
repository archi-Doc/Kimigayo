// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class WindowsLowering
{
    private static readonly FunctionAbi?[] FormattingFunctions = CreateFormattingFunctions();

    internal static FunctionAbi? GetFormattingFunction(CompilerFunctionKind kind)
        => (uint)kind < (uint)FormattingFunctions.Length ? FormattingFunctions[(int)kind] : null;

    private static FunctionAbi?[] CreateFormattingFunctions()
    {
        var functions = new FunctionAbi?[(int)CompilerFunctionKind.BuiltinFormat + 1];
        var ret = new AbiParameter("ptr", "ret", AbiParameterKind.ResultSlot);
        var reference = new AbiParameter("ptr", "self", LogicalIndex: 0);
        var owned = new AbiParameter("ptr", "self", AbiParameterKind.OwnedSlot, 0);
        var location = new AbiParameter("ptr", "location", AbiParameterKind.Location);
        var locationLength = new AbiParameter("i64", "location_length", AbiParameterKind.LocationLength);
        var amount = new AbiParameter("i64", "amount", LogicalIndex: 1);
        Add(CompilerFunctionKind.TextFixed, "__kimi_text_fixed", "void", [ret, reference, new("i64", "capacity", AbiParameterKind.Context)], true);
        Add(CompilerFunctionKind.TextHeap, "__kimi_text_heap", "void", [ret, new("i64", "capacity", LogicalIndex: 0), location, locationLength], true);
        Add(CompilerFunctionKind.TextWriter, "__kimi_text_writer", "void", [ret, reference, new("i64", "kind", AbiParameterKind.Context), new("ptr", "dispatch", AbiParameterKind.Context)], true);
        Add(CompilerFunctionKind.WriterStatus, "__kimi_writer_status", "void", [ret, reference], true);
        Add(CompilerFunctionKind.WriterWrite, "__kimi_writer_write_builtin", "void", [ret, reference, new("ptr", "value", LogicalIndex: 1), new("i32", "kind", AbiParameterKind.Context), location, locationLength], true);
        Add(CompilerFunctionKind.BuiltinFormat, "__kimi_format_builtin", "void", [ret, new("ptr", "value", LogicalIndex: 0), new("ptr", "self", LogicalIndex: 1), new("i32", "kind", AbiParameterKind.Context), location, locationLength], true);
        Add(CompilerFunctionKind.TextRelease, "__kimi_text_release", "void", [reference, location, locationLength]);
        Add(CompilerFunctionKind.TextUtf8, "__kimi_text_utf8", "void", [ret, new("ptr", "self", AbiParameterKind.SharedReference, 0)], true);
        Add(CompilerFunctionKind.TextValidateUtf8, "__kimi_text_validate_utf8", "void", [ret, owned], true);
        foreach (var kind in new[] { CompilerFunctionKind.FixedBufferBytes, CompilerFunctionKind.HeapBufferBytes })
        {
            Add(kind, "__kimi_buffer_bytes", "void", [ret, reference], true);
        }

        foreach (var kind in new[] { CompilerFunctionKind.FixedBufferText, CompilerFunctionKind.HeapBufferText })
        {
            Add(kind, "__kimi_buffer_text", "void", [ret, reference], true);
        }

        foreach (var kind in new[] { CompilerFunctionKind.FixedBufferValidate, CompilerFunctionKind.HeapBufferValidate })
        {
            Add(kind, "__kimi_buffer_validate", "void", [ret, reference], true);
        }

        foreach (var kind in new[] { CompilerFunctionKind.FixedBufferClear, CompilerFunctionKind.HeapBufferClear })
        {
            Add(kind, "__kimi_buffer_clear", "void", [reference]);
        }

        Add(CompilerFunctionKind.FixedBufferReserve, "__kimi_fixed_reserve", "void", [ret, reference, amount, location, locationLength], true);
        Add(CompilerFunctionKind.HeapBufferReserve, "__kimi_heap_reserve", "void", [ret, reference, amount, location, locationLength], true);
        Add(CompilerFunctionKind.FixedBufferIntoText, "__kimi_buffer_text", "void", [ret, owned], true);
        Add(CompilerFunctionKind.HeapBufferIntoString, "__kimi_buffer_into_string", "void", [ret, owned, location, locationLength], true);
        Add(CompilerFunctionKind.WindowPush, "__kimi_window_push", "void", [ret, reference, new("i8", "byte", LogicalIndex: 1)], true);
        Add(CompilerFunctionKind.WindowAppend, "__kimi_window_append", "void", [ret, reference, new("ptr", "bytes", AbiParameterKind.OwnedSlot, 1)], true);
        Add(CompilerFunctionKind.WindowLimit, "__kimi_window_limit", "void", [ret, owned, amount, location, locationLength], true);
        Add(CompilerFunctionKind.WindowCommit, "__kimi_window_commit", "i64", [owned]);
        Add(CompilerFunctionKind.WriteLineUtf8, "__kimi_write_line_utf8", "void", [owned, location, locationLength]);
        return functions;

        void Add(CompilerFunctionKind kind, string name, string result, AbiParameter[] parameters, bool resultSlot = false)
            => functions[(int)kind] = new(name, result, parameters, resultSlot: resultSlot);
    }
}
