// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The recognition entries (SPEC 22.1): each cataloged library declaration with its stable ID, container, name, overload position and
// what the compiler knows of it, and the indices built from them once: by ID, and from each linked implementation to its public
// declaration (KimiLibraryCatalog.cs reads them).
internal static partial class KimiLibraryCatalog
{
    // The published rows (SPEC 4.9.6) the entries name: no user code beyond the input accesses; `K` equality; the destruction of the
    // target in place (`replace`) or of its elements (the `clear`s, `truncate`); the calls of the Callable input (parameter 1), then the
    // destruction of the target's removed elements (`removeAll`, SPEC 4.9.7: after the last call) and of the Callable input it consumes
    // (DECISIONS BU-70); a formatting of the value (first parameter, or
    // second after a writer). Every initializer the entries and indices read is in this file, in order: the order of static initializers
    // across partial files is not defined.
    private static readonly PublishedRow None = new(Runs: null); // Not `default`, which skips the parameter defaults (On 0, Destroys 0).
    private static readonly PublishedRow Equality = new(KimiDeclarationId.Equatable, On: 1);
    private static readonly PublishedRow Replace = new(Destroys: 0, InPlace: true);
    private static readonly PublishedRow Target = new(Destroys: 0);
    private static readonly PublishedRow CallsThenTarget = new(Destroys: 0, Calls: 1);
    private static readonly PublishedRow Formats = new(KimiDeclarationId.Utf8Format, On: 0);
    private static readonly PublishedRow FormatsValue = new(KimiDeclarationId.Utf8Format, On: 1);

    private static readonly Entry[] Definitions =
    [
        new(KimiDeclarationId.Copy, "Copy", Intrinsic: IntrinsicKind.Copy),
        new(KimiDeclarationId.Owned, "Owned", Intrinsic: IntrinsicKind.Owned),
        new(KimiDeclarationId.Callable, "Callable", Intrinsic: IntrinsicKind.Callable),
        new(KimiDeclarationId.WriteLine, "writeLine", KimiLibraryContainer.Console, Function: CompilerFunctionKind.WriteLine, Overload: 1),
        new(KimiDeclarationId.ConsoleWriteStdout, "writeStdout", KimiLibraryContainer.Console, Function: CompilerFunctionKind.ConsoleWriteStdout),
        new(KimiDeclarationId.TestTempDirectory, "tempDirectory", KimiLibraryContainer.Test, Function: CompilerFunctionKind.TestTempDirectory),
        new(KimiDeclarationId.Option, "Option"),
        new(KimiDeclarationId.Result, "Result"),
        new(KimiDeclarationId.Array, "Array", ManagedRepresentation: true),
        new(KimiDeclarationId.FromEnd, "FromEnd"),
        new(KimiDeclarationId.Wrapping, "Wrapping", ManagedRepresentation: true),
        new(KimiDeclarationId.ResolvedRange, "ResolvedRange"),
        new(KimiDeclarationId.Range, "Range"),
        new(KimiDeclarationId.ClosedRange, "ClosedRange"),
        new(KimiDeclarationId.Slice, "Slice", ManagedRepresentation: true),
        new(KimiDeclarationId.UniqSlice, "UniqSlice", ManagedRepresentation: true),
        new(KimiDeclarationId.Dictionary, "Dictionary", ManagedRepresentation: true),
        new(KimiDeclarationId.Equatable, "Equatable"),
        new(KimiDeclarationId.Comparable, "Comparable"),
        new(KimiDeclarationId.Addable, "Addable"),
        new(KimiDeclarationId.Subtractable, "Subtractable"),
        new(KimiDeclarationId.Multipliable, "Multipliable"),
        new(KimiDeclarationId.Dividable, "Dividable"),
        new(KimiDeclarationId.RemainderProvider, "RemainderProvider"),
        new(KimiDeclarationId.LeftAddable, "LeftAddable"),
        new(KimiDeclarationId.LeftSubtractable, "LeftSubtractable"),
        new(KimiDeclarationId.LeftMultipliable, "LeftMultipliable"),
        new(KimiDeclarationId.LeftDividable, "LeftDividable"),
        new(KimiDeclarationId.LeftRemainderProvider, "LeftRemainderProvider"),
        new(KimiDeclarationId.Negatable, "Negatable"),
        new(KimiDeclarationId.Iterator, "Iterator"),
        new(KimiDeclarationId.LendingIterator, "LendingIterator"),
        new(KimiDeclarationId.Iterable, "Iterable"),
        new(KimiDeclarationId.UniqIterable, "UniqIterable"),
        new(KimiDeclarationId.IntoIterable, "IntoIterable"),
        new(KimiDeclarationId.Sealed, "Sealed", Intrinsic: IntrinsicKind.Sealed),
        new(KimiDeclarationId.ObjectPayload, "ObjectPayload", Intrinsic: IntrinsicKind.ObjectPayload),
        new(KimiDeclarationId.PrimitiveInteger, "PrimitiveInteger", Intrinsic: IntrinsicKind.PrimitiveInteger),
        new(KimiDeclarationId.Replace, "replace", KimiLibraryContainer.Intrinsics, SourceFunction: true, CallerLocation: true, Row: Replace),
        new(KimiDeclarationId.Exchange, "exchange", KimiLibraryContainer.Intrinsics, SourceFunction: true, Row: None),
        new(KimiDeclarationId.Swap, "swap", KimiLibraryContainer.Intrinsics, SourceFunction: true, Row: None),
        new(KimiDeclarationId.MakeObj, "makeObj", KimiLibraryContainer.Intrinsics, Function: CompilerFunctionKind.MakeObj),
        new(KimiDeclarationId.MakeRc, "makeRc", KimiLibraryContainer.Intrinsics, Function: CompilerFunctionKind.MakeRc),
        new(KimiDeclarationId.MakeArc, "makeArc", KimiLibraryContainer.Intrinsics, Function: CompilerFunctionKind.MakeArc),
        new(KimiDeclarationId.Clone, "clone", KimiLibraryContainer.Intrinsics, Function: CompilerFunctionKind.Clone),
        new(KimiDeclarationId.Downgrade, "downgrade", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.Upgrade, "upgrade", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.MakeRcCyclic, "makeRcCyclic", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.MakeArcCyclic, "makeArcCyclic", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.Weak, "Weak", SourceExpected: false, ManagedRepresentation: true),
        // SPEC 4.9.3, 4.9.5: the Array mutation operations, Kimigayo bodies declared inside the Array struct; SPEC 4.9.6 gives `reserve`,
        // `append`, `insert`, `pop`, `remove`, `swap` and `shrinkToFit` no user-code effect and `clear` the destruction of its elements.
        new(KimiDeclarationId.ArrayReserve, "reserve", KimiLibraryContainer.Array, SourceFunction: true, CallerLocation: true, Row: None),
        new(KimiDeclarationId.ArrayAppend, "append", KimiLibraryContainer.Array, SourceFunction: true, CallerLocation: true, Row: None),
        new(KimiDeclarationId.ArrayInsert, "insertAt", KimiLibraryContainer.Array, SourceFunction: true, CallerLocation: true, Row: None),
        new(KimiDeclarationId.ArrayPop, "pop", KimiLibraryContainer.Array, SourceFunction: true, Row: None),
        new(KimiDeclarationId.ArrayRemove, "removeAt", KimiLibraryContainer.Array, SourceFunction: true, CallerLocation: true, Row: None),
        new(KimiDeclarationId.ArrayClear, "clear", KimiLibraryContainer.Array, SourceFunction: true, CallerLocation: true, Row: Target),
        new(KimiDeclarationId.ArrayShrinkToFit, "shrinkToFit", KimiLibraryContainer.Array, SourceFunction: true, CallerLocation: true, Row: None),
        new(KimiDeclarationId.ArraySwap, "swapAt", KimiLibraryContainer.Array, SourceFunction: true, CallerLocation: true, Row: None),
        // SPEC 4.9.3, 4.9.5, 22.1: the public constructors are bodiless signatures whose definitions the compiler supplies from the
        // linked internal source functions, with the same parameters and premises and a `Self` result.
        new(
            KimiDeclarationId.ArrayWithCapacity,
            "init",
            KimiLibraryContainer.Array,
            Overload: 0,
            Implementation: KimiDeclarationId.ArrayCapacityImplementation),
        new(KimiDeclarationId.ArrayCapacityImplementation, "initCapacity", KimiLibraryContainer.Array, SourceFunction: true, CallerLocation: true, Row: None),
        new(KimiDeclarationId.ArrayRepeating, "init", KimiLibraryContainer.Array, Overload: 1, Implementation: KimiDeclarationId.ArrayRepeatingImplementation),
        new(KimiDeclarationId.ArrayRepeatingImplementation, "initRepeating", KimiLibraryContainer.Array, SourceFunction: true, CallerLocation: true, Row: None),
        new(KimiDeclarationId.Utf8Format, "Utf8Format"),
        new(KimiDeclarationId.BufferWriter, "BufferWriter"),
        new(KimiDeclarationId.BufferFull, "BufferFull"),
        new(KimiDeclarationId.WriteWindow, "WriteWindow"),
        new(KimiDeclarationId.Utf8Writer, "Utf8Writer"),
        new(KimiDeclarationId.FixedBuffer, "FixedBuffer", KimiLibraryContainer.Text),
        new(KimiDeclarationId.HeapBuffer, "HeapBuffer", KimiLibraryContainer.Text),
        new(KimiDeclarationId.Utf8Slice, "Utf8Slice", KimiLibraryContainer.Text),
        new(KimiDeclarationId.InvalidUtf8, "InvalidUtf8", KimiLibraryContainer.Text),
        new(KimiDeclarationId.TextFixed, "fixed", KimiLibraryContainer.Text, SourceFunction: true), // A direct line's region (R8-W2).
        new(KimiDeclarationId.TextHeap, "heap", KimiLibraryContainer.Text, SourceFunction: true, CallerLocation: true),
        new(KimiDeclarationId.TextWriter, "writer", KimiLibraryContainer.Text, Function: CompilerFunctionKind.TextWriter),
        new(KimiDeclarationId.TextToString, "toString", KimiLibraryContainer.Text, SourceFunction: true, CallerLocation: true, Row: Formats),
        new(KimiDeclarationId.TextTryFormat, "tryFormat", KimiLibraryContainer.Text, SourceFunction: true, CallerLocation: true, Row: Formats),
        new(KimiDeclarationId.FixedBufferReserve, "reserve", KimiLibraryContainer.FixedBuffer, SourceFunction: true, CallerLocation: true),
        new(KimiDeclarationId.FixedBufferText, "text", KimiLibraryContainer.FixedBuffer, SourceFunction: true), // A direct line's view (R8-W2).
        new(KimiDeclarationId.HeapBufferReserve, "reserve", KimiLibraryContainer.HeapBuffer, SourceFunction: true, CallerLocation: true),
        new(KimiDeclarationId.HeapBufferIntoString, "intoString", KimiLibraryContainer.HeapBuffer, SourceFunction: true, CallerLocation: true),
        new(KimiDeclarationId.WindowLimit, "limit", SourceFunction: true, CallerLocation: true, Owner: "WriteWindow"),
        new(KimiDeclarationId.WriterWrite, "write", SourceFunction: true, CallerLocation: true, Owner: "Utf8Writer", Row: FormatsValue),
        new(KimiDeclarationId.WriterStatus, "status", SourceFunction: true, Owner: "Utf8Writer"),
        new(KimiDeclarationId.WriterReserve, "reserve", SourceFunction: true, CallerLocation: true, Owner: "Utf8Writer"),
        new(KimiDeclarationId.WriterWriteUtf8, "writeUtf8", SourceFunction: true, CallerLocation: true, Owner: "Utf8Writer"),
        new(KimiDeclarationId.WriteLineUtf8, "writeLine", KimiLibraryContainer.Console, SourceFunction: true, CallerLocation: true, Overload: 0),
        new(KimiDeclarationId.BoolFormatFormat, "format", SourceFunction: true, CallerLocation: true, Owner: "BoolFormat"), // SPEC 22.5.1 until G1.
        new(KimiDeclarationId.CharFormatFormat, "format", SourceFunction: true, CallerLocation: true, Owner: "CharFormat"), // SPEC 22.5.1 until G1.
        new(KimiDeclarationId.UnitFormatFormat, "format", SourceFunction: true, CallerLocation: true, Owner: "UnitFormat"), // SPEC 22.5.1 until G1.
        new(KimiDeclarationId.F32FormatFormat, "format", SourceFunction: true, CallerLocation: true, Owner: "F32Format"), // SPEC 22.5.1 until G1.
        new(KimiDeclarationId.F64FormatFormat, "format", SourceFunction: true, CallerLocation: true, Owner: "F64Format"), // SPEC 22.5.1 until G1.
        new(KimiDeclarationId.DictionaryReserve, "reserve", KimiLibraryContainer.Dictionary, SourceFunction: true, CallerLocation: true, Row: None),
        new(KimiDeclarationId.DictionaryTryInsert, "tryInsert", KimiLibraryContainer.Dictionary, SourceFunction: true, CallerLocation: true, Row: Equality),
        new(
            KimiDeclarationId.DictionaryInsertOrReplace,
            "insertOrReplace",
            KimiLibraryContainer.Dictionary,
            SourceFunction: true,
            CallerLocation: true,
            Row: new(KimiDeclarationId.Equatable, On: 1, Destroys: 1)), // `K` equality and the unused input `K`'s destruction.
        new(KimiDeclarationId.DictionaryRemove, "remove", KimiLibraryContainer.Dictionary, SourceFunction: true, Row: Equality),
        new(KimiDeclarationId.DictionaryTryGet, "tryGet", KimiLibraryContainer.Dictionary, SourceFunction: true, Row: Equality),
        new(KimiDeclarationId.DictionaryClear, "clear", KimiLibraryContainer.Dictionary, SourceFunction: true, Row: Target),
        new(KimiDeclarationId.DictionaryShrinkToFit, "shrinkToFit", KimiLibraryContainer.Dictionary, SourceFunction: true, CallerLocation: true, Row: None),
        new(KimiDeclarationId.Indexable, "Indexable"),
        new(KimiDeclarationId.UniqIndexable, "UniqIndexable"),
        // IMPL 21.1.3: the structs that state the handles' inline components, by identity for Layout and the tests.
        new(KimiDeclarationId.ArrayRepr, "ArrayRepr", KimiLibraryContainer.Storage),
        new(KimiDeclarationId.SliceRepr, "SliceRepr", KimiLibraryContainer.Storage),
        new(KimiDeclarationId.StringRepr, "StringRepr", KimiLibraryContainer.Storage),
        new(KimiDeclarationId.DictionaryStorageHandle, "Handle", Owner: "DictionaryStorage"),
        // SPEC 22.2: the standard storage boundary; the exclusive borrow of each family is borrowStorageUniq (SPEC 7.4), and
        // the overload index counts the declarations of one Name in source order (Array, Dictionary, fixed array).
        new(KimiDeclarationId.RefRemainder, "RefRemainder", KimiLibraryContainer.Storage),
        new(KimiDeclarationId.UniqRemainder, "UniqRemainder", KimiLibraryContainer.Storage),
        // SPEC 22.2: the distinct Dictionary family traverses live entries in insertion order.
        new(KimiDeclarationId.DictionaryRefRemainder, "DictionaryRefRemainder", KimiLibraryContainer.Storage),
        new(KimiDeclarationId.DictionaryUniqRemainder, "DictionaryUniqRemainder", KimiLibraryContainer.Storage),
        new(KimiDeclarationId.DictionaryOwnedRemainder, "DictionaryOwnedRemainder", KimiLibraryContainer.Storage),
        // SPEC 22.2: the fixed-array overloads of the contiguous borrowing remainders.
        // PLAN G33: the fixed-array owning remainder keeps its elements in compiler-known inline storage.
        new(KimiDeclarationId.InlineStorage, "InlineStorage", KimiLibraryContainer.Storage),
        new(KimiDeclarationId.Position, "Position"),
        new(KimiDeclarationId.PositionRange, "PositionRange"),
        new(KimiDeclarationId.Start, "Start"),
        new(KimiDeclarationId.End, "End"),
        new(KimiDeclarationId.DictionaryIndex, "index", KimiLibraryContainer.Dictionary, SourceFunction: true, CallerLocation: true),
        new(KimiDeclarationId.DictionaryIndexUniq, "indexUniq", KimiLibraryContainer.Dictionary, SourceFunction: true, CallerLocation: true, Row: Equality),
        new(KimiDeclarationId.StorageMissingDictionaryKey, "missingDictionaryKey", KimiLibraryContainer.Storage, Function: CompilerFunctionKind.StorageMissingDictionaryKey),
        // SPEC 22.2: private primitives that hold no collection logic, used by the DictionaryStorage capacity bodies.
        new(KimiDeclarationId.StorageCountOverflow, "countOverflow", KimiLibraryContainer.Storage, Function: CompilerFunctionKind.StorageCountOverflow),
        new(KimiDeclarationId.StorageAllocationSizeExceeded, "allocationSizeExceeded", KimiLibraryContainer.Storage, Function: CompilerFunctionKind.StorageAllocationSizeExceeded),
        new(KimiDeclarationId.StorageTryAllocateBytes, "tryAllocateBytes", KimiLibraryContainer.Storage, Function: CompilerFunctionKind.StorageTryAllocateBytes),
        new(KimiDeclarationId.StorageTransferBytes, "transferBytes", KimiLibraryContainer.Storage, Function: CompilerFunctionKind.StorageTransferBytes),
        new(KimiDeclarationId.StorageStride, "stride", KimiLibraryContainer.Storage, Function: CompilerFunctionKind.StorageStride),
        new(KimiDeclarationId.StorageGrowArray, "growArray", KimiLibraryContainer.Storage, SourceFunction: true, CallerLocation: true),
        new(KimiDeclarationId.StorageDestroyValues, "destroyValues", KimiLibraryContainer.Storage, Function: CompilerFunctionKind.StorageDestroyValues),
        new(KimiDeclarationId.StorageErasedReserve, "erasedReserve", KimiLibraryContainer.Storage, Function: CompilerFunctionKind.StorageErasedReserve),
        new(KimiDeclarationId.StorageFormatFloat, "formatFloat", KimiLibraryContainer.Storage, Function: CompilerFunctionKind.StorageFormatFloat),
        new(KimiDeclarationId.StorageShrinkArray, "shrinkArray", KimiLibraryContainer.Storage, SourceFunction: true, CallerLocation: true),
        new(KimiDeclarationId.StorageDestroyArray, "destroyArray", KimiLibraryContainer.Storage, SourceFunction: true, CallerLocation: true),
        new(KimiDeclarationId.StorageDestroyDictionary, "destroyDictionary", KimiLibraryContainer.Storage, SourceFunction: true, CallerLocation: true),
        new(KimiDeclarationId.StorageReleaseDictionary, "releaseDictionary", KimiLibraryContainer.Storage, SourceFunction: true, CallerLocation: true),
        new(KimiDeclarationId.StorageArrayCapacity, "arrayCapacity", KimiLibraryContainer.Storage, SourceFunction: true, Row: None),
        new(KimiDeclarationId.DictionaryStorageCapacity, "capacity", SourceFunction: true, Owner: "DictionaryStorage", Row: None),
        new(KimiDeclarationId.DictionaryStorageInitialize, "initialize", SourceFunction: true, Owner: "DictionaryStorage"), // SPEC 4.3: `[:]`'s target.
        // SPEC 22.5.1: the Kimigayo capacity decisions carry the standard operation's caller context to their failures.
        new(KimiDeclarationId.DictionaryStorageReserveEntries, "reserveEntries", SourceFunction: true, CallerLocation: true, Owner: "DictionaryStorage"),
        new(KimiDeclarationId.DictionaryStorageReserve, "reserve", SourceFunction: true, CallerLocation: true, Owner: "DictionaryStorage"),
        new(KimiDeclarationId.DictionaryStorageGrow, "grow", SourceFunction: true, CallerLocation: true, Owner: "DictionaryStorage"),
        new(KimiDeclarationId.DictionaryStorageAppend, "append", SourceFunction: true, CallerLocation: true, Owner: "DictionaryStorage"),
        new(KimiDeclarationId.DictionaryStoragePlaceEntry, "placeEntry", SourceFunction: true, CallerLocation: true, Owner: "DictionaryStorage"),
        new(KimiDeclarationId.DictionaryStorageShrinkEntries, "shrinkEntries", SourceFunction: true, CallerLocation: true, Owner: "DictionaryStorage"),
        new(KimiDeclarationId.DictionaryStorageShrinkToFit, "shrinkToFit", SourceFunction: true, CallerLocation: true, Owner: "DictionaryStorage"),
        new(KimiDeclarationId.DictionaryStorageFindEntry, "findEntry", SourceFunction: true, Owner: "DictionaryStorage", Row: Equality), // SPEC 12.4.3.
        // SPEC 5.6: the public raw storage operations.
        new(KimiDeclarationId.RawAllocate, "allocate", KimiLibraryContainer.Raw, Function: CompilerFunctionKind.RawAllocate),
        new(KimiDeclarationId.RawRelease, "release", KimiLibraryContainer.Raw, Function: CompilerFunctionKind.RawRelease),
        new(KimiDeclarationId.RawInitialize, "initialize", KimiLibraryContainer.Raw, Function: CompilerFunctionKind.RawInitialize),
        new(KimiDeclarationId.RawSlice, "slice", KimiLibraryContainer.Raw, Function: CompilerFunctionKind.RawSlice),
        // SPEC 15.3.5: the zero-sized dependency Field Core.
        new(KimiDeclarationId.Loan, "Loan", ManagedRepresentation: true),
        // Ordinary source bodies carry private diagnostic context; their public signatures remain ordinary source.
        new(KimiDeclarationId.StorageArgumentOutOfRange, "argumentOutOfRange", KimiLibraryContainer.Storage, Function: CompilerFunctionKind.StorageArgumentOutOfRange),
        new(KimiDeclarationId.FromEndResolve, "resolve", SourceFunction: true, CallerLocation: true, Owner: "FromEnd"),
        new(KimiDeclarationId.StartResolve, "resolve", SourceFunction: true, CallerLocation: true, Owner: "Start"),
        new(KimiDeclarationId.EndResolve, "resolve", SourceFunction: true, CallerLocation: true, Owner: "End"),
        new(KimiDeclarationId.RangeResolve, "resolve", SourceFunction: true, CallerLocation: true, Owner: "Range"),
        new(KimiDeclarationId.ClosedRangeResolve, "resolve", SourceFunction: true, CallerLocation: true, Owner: "ClosedRange"),
        new(KimiDeclarationId.ResolvedRangeResolve, "resolve", SourceFunction: true, CallerLocation: true, Owner: "ResolvedRange"),
        new(KimiDeclarationId.RangeIterate, "iterate", SourceFunction: true, CallerLocation: true, Owner: "Range"),
        new(KimiDeclarationId.RangeIterateUniq, "iterateUniq", SourceFunction: true, CallerLocation: true, Owner: "Range"),
        new(KimiDeclarationId.RangeIntoIterator, "intoIterator", SourceFunction: true, CallerLocation: true, Owner: "Range"),
        new(KimiDeclarationId.ClosedRangeIterate, "iterate", SourceFunction: true, CallerLocation: true, Owner: "ClosedRange"),
        new(KimiDeclarationId.ClosedRangeIterateUniq, "iterateUniq", SourceFunction: true, CallerLocation: true, Owner: "ClosedRange"),
        new(KimiDeclarationId.ClosedRangeIntoIterator, "intoIterator", SourceFunction: true, CallerLocation: true, Owner: "ClosedRange"),
        new(KimiDeclarationId.RangeIteratorStarting, "starting", SourceFunction: true, CallerLocation: true, Owner: "RangeIterator"),
        new(KimiDeclarationId.ClosedRangeIteratorStarting, "starting", SourceFunction: true, CallerLocation: true, Owner: "ClosedRangeIterator"),
        new(KimiDeclarationId.DictionaryStorageClearEntries, "clearEntries", SourceFunction: true, CallerLocation: true, Owner: "DictionaryStorage"),
        new(KimiDeclarationId.StorageDestroyDictionaryEntries, "destroyDictionaryEntries", KimiLibraryContainer.Storage, SourceFunction: true, CallerLocation: true),
        new(KimiDeclarationId.StorageIndexBounds, "indexBounds", KimiLibraryContainer.Storage, Function: CompilerFunctionKind.StorageIndexBounds),
        new(KimiDeclarationId.UniqSliceIndex, "index", KimiLibraryContainer.Root, SourceFunction: true, CallerLocation: true, Owner: "UniqSlice"),
        new(KimiDeclarationId.UniqSliceIndexUniq, "indexUniq", KimiLibraryContainer.Root, SourceFunction: true, CallerLocation: true, Owner: "UniqSlice"),
        new(KimiDeclarationId.UniqSliceIndexPosition, "indexPosition", KimiLibraryContainer.Root, SourceFunction: true, CallerLocation: true, Owner: "UniqSlice"),
        new(KimiDeclarationId.UniqSliceIndexPositionUniq, "indexPositionUniq", KimiLibraryContainer.Root, SourceFunction: true, CallerLocation: true, Owner: "UniqSlice"),
        new(KimiDeclarationId.UniqSliceSliceRange, "slice", KimiLibraryContainer.Root, SourceFunction: true, CallerLocation: true, Owner: "UniqSlice", Overload: 1),
        new(KimiDeclarationId.UniqSliceSliceUniqRange, "sliceUniq", KimiLibraryContainer.Root, SourceFunction: true, CallerLocation: true, Owner: "UniqSlice", Overload: 1),
        new(KimiDeclarationId.ArraySliceRange, "slice", KimiLibraryContainer.Array, SourceFunction: true, CallerLocation: true, Overload: 1),
        new(KimiDeclarationId.ArraySliceUniqRange, "sliceUniq", KimiLibraryContainer.Array, SourceFunction: true, CallerLocation: true, Overload: 1),
        new(KimiDeclarationId.FixedArraySliceRange, "slice", KimiLibraryContainer.FixedArray, SourceFunction: true, CallerLocation: true, Overload: 1),
        new(KimiDeclarationId.FixedArraySliceUniqRange, "sliceUniq", KimiLibraryContainer.FixedArray, SourceFunction: true, CallerLocation: true, Overload: 1),
        new(KimiDeclarationId.SliceSliceRange, "slice", KimiLibraryContainer.Root, SourceFunction: true, CallerLocation: true, Owner: "Slice", Overload: 1),
        new(KimiDeclarationId.StorageSliceUniq, "sliceUniq", KimiLibraryContainer.Storage, SourceFunction: true, CallerLocation: true),
        new(KimiDeclarationId.SliceToArray, "toArray", SourceFunction: true, CallerLocation: true, Owner: "Slice"),
        new(KimiDeclarationId.ArrayAppendCopies, "appendCopies", KimiLibraryContainer.Array, SourceFunction: true, CallerLocation: true, Row: None),
        // SPEC 4.9.6: `truncate` destroys its receiver's elements from `length` on; `removeAll` calls `matching`, destroys the removed
        // elements and destroys `matching`.
        new(KimiDeclarationId.ArrayTruncate, "truncate", KimiLibraryContainer.Array, SourceFunction: true, CallerLocation: true, Row: Target),
        new(KimiDeclarationId.ArrayRemoveAll, "removeAll", KimiLibraryContainer.Array, SourceFunction: true, Row: CallsThenTarget),
        // SPEC 4.9.6, 15.6.4: the public position insert and appendAll, which add inputs to the element storage (AddedRows), and the
        // position remove, swap, swapRemove and reverse; none runs user code.
        new(KimiDeclarationId.ArrayInsertPosition, "insert", KimiLibraryContainer.Array, SourceFunction: true, Row: None),
        new(KimiDeclarationId.ArrayAppendAll, "appendAll", KimiLibraryContainer.Array, SourceFunction: true, Row: None),
        new(KimiDeclarationId.ArrayRemovePosition, "remove", KimiLibraryContainer.Array, SourceFunction: true, Row: None),
        new(KimiDeclarationId.ArraySwapPositions, "swap", KimiLibraryContainer.Array, SourceFunction: true, Row: None),
        new(KimiDeclarationId.ArraySwapRemove, "swapRemove", KimiLibraryContainer.Array, SourceFunction: true, Row: None),
        new(KimiDeclarationId.ArrayReverse, "reverse", KimiLibraryContainer.Array, SourceFunction: true, Row: None),
    ];

    private static readonly int[] Indices = CreateIndices();

    // The public declaration of each linked implementation, by catalog index; -1 elsewhere.
    private static readonly int[] Presentations = CreatePresentations();

    // A link joins a bodiless public declaration to one internal source function of the same container that carries the caller
    // location; no implementation is linked twice or linked onward.
    private static int[] CreatePresentations()
    {
        var result = new int[Definitions.Length];
        Array.Fill(result, -1);
        for (var i = 0; i < Definitions.Length; i++)
        {
            if (Definitions[i].Implementation is not { } implementation)
            {
                continue;
            }

            var target = Indices[(int)implementation];
            if (target < 0 || result[target] >= 0 || Definitions[i].SourceFunction || Definitions[i].Function != CompilerFunctionKind.None ||
                Definitions[target] is not { SourceFunction: true, CallerLocation: true, Implementation: null } linked || linked.Container != Definitions[i].Container)
            {
                throw new InvalidOperationException("Invalid Kimi implementation link.");
            }

            result[target] = i;
        }

        return result;
    }

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
}
