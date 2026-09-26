// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal enum KimiLibraryContainer : byte
{
    Root,
    Console,
    Intrinsics,
    Test,
    Array,
    Dictionary,
    Text,
    FixedBuffer,
    HeapBuffer,
    WriteWindow,
    Utf8Writer,
}

/// <summary>Immutable recognition rules, shared across compilations. Stable IDs are independent of catalog and syntax order.</summary>
internal static class KimiLibraryCatalog
{
    private static readonly Entry[] Definitions =
    [
        new(KimiDeclarationId.Copy, "Copy", Intrinsic: IntrinsicKind.Copy),
        new(KimiDeclarationId.Owned, "Owned", Intrinsic: IntrinsicKind.Owned),
        new(KimiDeclarationId.Callable, "Callable", Intrinsic: IntrinsicKind.Callable),
        new(KimiDeclarationId.WriteLine, "writeLine", KimiLibraryContainer.Console, Function: CompilerFunctionKind.WriteLine, Overload: 0),
        new(KimiDeclarationId.TestTempDirectory, "tempDirectory", KimiLibraryContainer.Test, Function: CompilerFunctionKind.TestTempDirectory),
        new(KimiDeclarationId.Option, "Option"),
        new(KimiDeclarationId.Result, "Result"),
        new(KimiDeclarationId.Array, "Array"),
        new(KimiDeclarationId.Index, "Index"),
        new(KimiDeclarationId.Range, "Range", SourceExpected: false),
        new(KimiDeclarationId.ResolvedRange, "ResolvedRange", SourceExpected: false),
        new(KimiDeclarationId.Slice, "Slice"),
        new(KimiDeclarationId.Dictionary, "Dictionary"),
        new(KimiDeclarationId.Equatable, "Equatable"),
        new(KimiDeclarationId.Comparable, "Comparable"),
        new(KimiDeclarationId.Iterator, "Iterator"),
        new(KimiDeclarationId.IntoIterable, "IntoIterable"),
        new(KimiDeclarationId.Sealed, "Sealed", Intrinsic: IntrinsicKind.Sealed),
        new(KimiDeclarationId.ObjectPayload, "ObjectPayload", Intrinsic: IntrinsicKind.ObjectPayload),
        new(KimiDeclarationId.Replace, "replace", KimiLibraryContainer.Intrinsics, Function: CompilerFunctionKind.Replace),
        new(KimiDeclarationId.Exchange, "exchange", KimiLibraryContainer.Intrinsics, Function: CompilerFunctionKind.Exchange),
        new(KimiDeclarationId.Swap, "swap", KimiLibraryContainer.Intrinsics, Function: CompilerFunctionKind.Swap),
        new(KimiDeclarationId.MakeObj, "makeObj", KimiLibraryContainer.Intrinsics, Function: CompilerFunctionKind.MakeObj),
        new(KimiDeclarationId.MakeRc, "makeRc", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.MakeArc, "makeArc", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.Clone, "clone", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.Downgrade, "downgrade", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.Upgrade, "upgrade", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.MakeRcCyclic, "makeRcCyclic", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.MakeArcCyclic, "makeArcCyclic", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.Weak, "Weak", SourceExpected: false),
        // SPEC 4.7.2, 4.7.4: compiler-implemented Array mutation operations declared inside the Array struct.
        new(KimiDeclarationId.ArrayReserve, "reserve", KimiLibraryContainer.Array, Function: CompilerFunctionKind.ArrayReserve),
        new(KimiDeclarationId.ArrayAppend, "append", KimiLibraryContainer.Array, Function: CompilerFunctionKind.ArrayAppend),
        new(KimiDeclarationId.ArrayInsert, "insert", KimiLibraryContainer.Array, Function: CompilerFunctionKind.ArrayInsert, Overload: 0),
        new(KimiDeclarationId.ArrayPop, "pop", KimiLibraryContainer.Array, Function: CompilerFunctionKind.ArrayPop),
        new(KimiDeclarationId.ArrayRemove, "remove", KimiLibraryContainer.Array, Function: CompilerFunctionKind.ArrayRemove, Overload: 0),
        new(KimiDeclarationId.ArrayClear, "clear", KimiLibraryContainer.Array, Function: CompilerFunctionKind.ArrayClear),
        new(KimiDeclarationId.ArrayShrinkToFit, "shrinkToFit", KimiLibraryContainer.Array, Function: CompilerFunctionKind.ArrayShrinkToFit),
        new(KimiDeclarationId.ArrayInsertIndex, "insert", KimiLibraryContainer.Array, Function: CompilerFunctionKind.ArrayInsertIndex, Overload: 1),
        new(KimiDeclarationId.ArrayRemoveIndex, "remove", KimiLibraryContainer.Array, Function: CompilerFunctionKind.ArrayRemoveIndex, Overload: 1),
        new(KimiDeclarationId.Utf8Format, "Utf8Format"),
        new(KimiDeclarationId.BufferWriter, "BufferWriter"),
        new(KimiDeclarationId.BufferFull, "BufferFull"),
        new(KimiDeclarationId.WriteWindow, "WriteWindow"),
        new(KimiDeclarationId.Utf8Writer, "Utf8Writer"),
        new(KimiDeclarationId.FixedBuffer, "FixedBuffer", KimiLibraryContainer.Text),
        new(KimiDeclarationId.HeapBuffer, "HeapBuffer", KimiLibraryContainer.Text),
        new(KimiDeclarationId.Utf8Slice, "Utf8Slice", KimiLibraryContainer.Text),
        new(KimiDeclarationId.InvalidUtf8, "InvalidUtf8", KimiLibraryContainer.Text),
        new(KimiDeclarationId.TextFixed, "fixed", KimiLibraryContainer.Text, Function: CompilerFunctionKind.TextFixed),
        new(KimiDeclarationId.TextHeap, "heap", KimiLibraryContainer.Text, Function: CompilerFunctionKind.TextHeap),
        new(KimiDeclarationId.TextWriter, "writer", KimiLibraryContainer.Text, Function: CompilerFunctionKind.TextWriter),
        new(KimiDeclarationId.TextUtf8, "utf8", KimiLibraryContainer.Text, Function: CompilerFunctionKind.TextUtf8),
        new(KimiDeclarationId.TextValidateUtf8, "validateUtf8", KimiLibraryContainer.Text, Function: CompilerFunctionKind.TextValidateUtf8),
        new(KimiDeclarationId.TextToString, "toString", KimiLibraryContainer.Text, Function: CompilerFunctionKind.TextToString),
        new(KimiDeclarationId.TextTryFormat, "tryFormat", KimiLibraryContainer.Text, Function: CompilerFunctionKind.TextTryFormat),
        new(KimiDeclarationId.TextRelease, "release", KimiLibraryContainer.Text, Function: CompilerFunctionKind.TextRelease),
        new(KimiDeclarationId.FixedBufferBytes, "bytes", KimiLibraryContainer.FixedBuffer, Function: CompilerFunctionKind.FixedBufferBytes),
        new(KimiDeclarationId.FixedBufferText, "text", KimiLibraryContainer.FixedBuffer, Function: CompilerFunctionKind.FixedBufferText),
        new(KimiDeclarationId.FixedBufferValidate, "validate", KimiLibraryContainer.FixedBuffer, Function: CompilerFunctionKind.FixedBufferValidate),
        new(KimiDeclarationId.FixedBufferClear, "clear", KimiLibraryContainer.FixedBuffer, Function: CompilerFunctionKind.FixedBufferClear),
        new(KimiDeclarationId.FixedBufferReserve, "reserve", KimiLibraryContainer.FixedBuffer, Function: CompilerFunctionKind.FixedBufferReserve),
        new(KimiDeclarationId.FixedBufferIntoText, "intoText", KimiLibraryContainer.FixedBuffer, Function: CompilerFunctionKind.FixedBufferIntoText),
        new(KimiDeclarationId.HeapBufferBytes, "bytes", KimiLibraryContainer.HeapBuffer, Function: CompilerFunctionKind.HeapBufferBytes),
        new(KimiDeclarationId.HeapBufferText, "text", KimiLibraryContainer.HeapBuffer, Function: CompilerFunctionKind.HeapBufferText),
        new(KimiDeclarationId.HeapBufferValidate, "validate", KimiLibraryContainer.HeapBuffer, Function: CompilerFunctionKind.HeapBufferValidate),
        new(KimiDeclarationId.HeapBufferClear, "clear", KimiLibraryContainer.HeapBuffer, Function: CompilerFunctionKind.HeapBufferClear),
        new(KimiDeclarationId.HeapBufferReserve, "reserve", KimiLibraryContainer.HeapBuffer, Function: CompilerFunctionKind.HeapBufferReserve),
        new(KimiDeclarationId.HeapBufferIntoString, "intoString", KimiLibraryContainer.HeapBuffer, Function: CompilerFunctionKind.HeapBufferIntoString),
        new(KimiDeclarationId.WindowPush, "push", KimiLibraryContainer.WriteWindow, Function: CompilerFunctionKind.WindowPush),
        new(KimiDeclarationId.WindowAppend, "append", KimiLibraryContainer.WriteWindow, Function: CompilerFunctionKind.WindowAppend),
        new(KimiDeclarationId.WindowLimit, "limit", KimiLibraryContainer.WriteWindow, Function: CompilerFunctionKind.WindowLimit),
        new(KimiDeclarationId.WindowCommit, "commit", KimiLibraryContainer.WriteWindow, Function: CompilerFunctionKind.WindowCommit),
        new(KimiDeclarationId.WriterWrite, "write", KimiLibraryContainer.Utf8Writer, Function: CompilerFunctionKind.WriterWrite),
        new(KimiDeclarationId.WriterStatus, "status", KimiLibraryContainer.Utf8Writer, Function: CompilerFunctionKind.WriterStatus),
        new(KimiDeclarationId.WriteLineUtf8, "writeLine", KimiLibraryContainer.Console, Function: CompilerFunctionKind.WriteLineUtf8, Overload: 1),
        new(KimiDeclarationId.DictionaryReserve, "reserve", KimiLibraryContainer.Dictionary, Function: CompilerFunctionKind.DictionaryReserve),
        new(KimiDeclarationId.DictionaryTryInsert, "tryInsert", KimiLibraryContainer.Dictionary, Function: CompilerFunctionKind.DictionaryTryInsert),
        new(KimiDeclarationId.DictionaryInsertOrReplace, "insertOrReplace", KimiLibraryContainer.Dictionary, Function: CompilerFunctionKind.DictionaryInsertOrReplace),
        new(KimiDeclarationId.DictionaryRemove, "remove", KimiLibraryContainer.Dictionary, Function: CompilerFunctionKind.DictionaryRemove),
        new(KimiDeclarationId.DictionaryTryGet, "tryGet", KimiLibraryContainer.Dictionary, Function: CompilerFunctionKind.DictionaryTryGet),
        new(KimiDeclarationId.DictionaryClear, "clear", KimiLibraryContainer.Dictionary, Function: CompilerFunctionKind.DictionaryClear),
        new(KimiDeclarationId.DictionaryShrinkToFit, "shrinkToFit", KimiLibraryContainer.Dictionary, Function: CompilerFunctionKind.DictionaryShrinkToFit),
        new(KimiDeclarationId.Indexable, "Indexable"),
        new(KimiDeclarationId.UniqIndexable, "UniqIndexable"),
    ];

    private static readonly int[] Indices = CreateIndices();

    internal static ReadOnlySpan<Entry> Entries => Definitions;

    internal static bool IsArrayOperation(CompilerFunctionKind kind) => kind is >= CompilerFunctionKind.ArrayReserve and <= CompilerFunctionKind.ArrayRemoveIndex;

    internal static bool IsDictionaryOperation(CompilerFunctionKind kind) => kind is >= CompilerFunctionKind.DictionaryReserve and <= CompilerFunctionKind.DictionaryShrinkToFit;

    internal static int Index(KimiDeclarationId id) => (uint)id < (uint)Indices.Length ? Indices[(int)id] : -1;

    private static int[] CreateIndices()
    {
        var result = new int[Enum.GetValues<KimiDeclarationId>().Length];
        Array.Fill(result, -1);
        for (var i = 0; i < Definitions.Length; i++)
        {
            var id = (int)Definitions[i].Id;
            if (result[id] != -1)
            {
                throw new InvalidOperationException("Duplicate Kimi catalog ID.");
            }

            result[id] = i;
        }

        return result;
    }

    internal readonly record struct Entry(KimiDeclarationId Id, string Name, KimiLibraryContainer Container = KimiLibraryContainer.Root, IntrinsicKind Intrinsic = IntrinsicKind.None, CompilerFunctionKind Function = CompilerFunctionKind.None, bool SourceExpected = true, int Overload = -1)
    {
        internal bool IsFunction => this.Function != CompilerFunctionKind.None || this.Container == KimiLibraryContainer.Intrinsics;
    }
}
