// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The recognition entries (SPEC 22.1): each cataloged library declaration's stable ID, which its `#Lang` names (IMPL 21.3.4), with
// what the compiler knows of it beyond its source, and the indices built from them once: by ID, and from each linked implementation
// to its public declaration (KimiLibraryCatalog.cs reads them).
internal static partial class KimiLibraryCatalog
{
    // The published rows (SPEC 4.9.6, 22.10.1) the entries name: no user code beyond the input accesses; `K` equality; the destruction
    // of the target in place (`replace`) or of its elements (the `clear`s, `truncate`); the calls of the Callable input (parameter 1),
    // then the destruction of the target's removed elements (`removeAll`, SPEC 4.9.7: after the last call) and of the Callable input it
    // consumes (DECISIONS BU-70); a formatting of the value (first parameter, or second after a writer). Every initializer the entries
    // and indices read is in this file, in order: the order of static initializers across partial files is not defined.
    private static readonly PublishedRow None = new(Runs: null); // Not `default`, which skips the parameter defaults (On 0, Destroys 0).
    private static readonly PublishedRow Equality = new(KimiDeclarationId.Equatable, On: 1);
    private static readonly PublishedRow Replace = new(Destroys: 0, InPlace: true);
    private static readonly PublishedRow Target = new(Destroys: 0);
    private static readonly PublishedRow CallsThenTarget = new(Destroys: 0, Calls: 1);
    private static readonly PublishedRow Formats = new(KimiDeclarationId.Utf8Format, On: 0);
    private static readonly PublishedRow FormatsValue = new(KimiDeclarationId.Utf8Format, On: 1);

    private static readonly Entry[] Definitions =
    [
        new(KimiDeclarationId.Copy, Intrinsic: IntrinsicKind.Copy),
        new(KimiDeclarationId.Owned, Intrinsic: IntrinsicKind.Owned),
        new(KimiDeclarationId.Callable, Intrinsic: IntrinsicKind.Callable),
        new(KimiDeclarationId.WriteLine),
        new(KimiDeclarationId.ConsoleWriteStdout, Function: CompilerFunctionKind.ConsoleWriteStdout),
        new(KimiDeclarationId.TestTempDirectory, Function: CompilerFunctionKind.TestTempDirectory),
        new(KimiDeclarationId.Option),
        new(KimiDeclarationId.Result),
        new(KimiDeclarationId.Never, ManagedRepresentation: true),
        new(KimiDeclarationId.Array, ManagedRepresentation: true),
        new(KimiDeclarationId.FromEnd),
        new(KimiDeclarationId.Wrapping, ManagedRepresentation: true),
        new(KimiDeclarationId.ResolvedRange),
        new(KimiDeclarationId.Range),
        new(KimiDeclarationId.ClosedRange),
        new(KimiDeclarationId.Slice, ManagedRepresentation: true),
        new(KimiDeclarationId.UniqSlice, ManagedRepresentation: true),
        new(KimiDeclarationId.Dictionary, ManagedRepresentation: true),
        new(KimiDeclarationId.Equatable),
        new(KimiDeclarationId.Comparable),
        new(KimiDeclarationId.Addable),
        new(KimiDeclarationId.Subtractable),
        new(KimiDeclarationId.Multipliable),
        new(KimiDeclarationId.Dividable),
        new(KimiDeclarationId.RemainderProvider),
        new(KimiDeclarationId.LeftAddable),
        new(KimiDeclarationId.LeftSubtractable),
        new(KimiDeclarationId.LeftMultipliable),
        new(KimiDeclarationId.LeftDividable),
        new(KimiDeclarationId.LeftRemainderProvider),
        new(KimiDeclarationId.Negatable),
        new(KimiDeclarationId.Iterator),
        new(KimiDeclarationId.LendingIterator),
        new(KimiDeclarationId.Iterable),
        new(KimiDeclarationId.UniqIterable),
        new(KimiDeclarationId.IntoIterable),
        new(KimiDeclarationId.Sealed, Intrinsic: IntrinsicKind.Sealed),
        new(KimiDeclarationId.ObjectPayload, Intrinsic: IntrinsicKind.ObjectPayload),
        new(KimiDeclarationId.PrimitiveInteger, Intrinsic: IntrinsicKind.PrimitiveInteger),
        new(KimiDeclarationId.Replace, Row: Replace),
        new(KimiDeclarationId.Exchange, Row: None),
        new(KimiDeclarationId.Swap, Row: None),
        new(KimiDeclarationId.MakeObj, Function: CompilerFunctionKind.MakeObj),
        new(KimiDeclarationId.MakeRc, Function: CompilerFunctionKind.MakeRc),
        new(KimiDeclarationId.MakeArc, Function: CompilerFunctionKind.MakeArc),
        new(KimiDeclarationId.Clone, Function: CompilerFunctionKind.Clone),
        new(KimiDeclarationId.Downgrade, SourceExpected: false, Name: "downgrade", Container: KimiLibraryContainer.Intrinsics),
        new(KimiDeclarationId.Upgrade, SourceExpected: false, Name: "upgrade", Container: KimiLibraryContainer.Intrinsics),
        new(KimiDeclarationId.MakeRcCyclic, SourceExpected: false, Name: "makeRcCyclic", Container: KimiLibraryContainer.Intrinsics),
        new(KimiDeclarationId.MakeArcCyclic, SourceExpected: false, Name: "makeArcCyclic", Container: KimiLibraryContainer.Intrinsics),
        new(KimiDeclarationId.Weak, SourceExpected: false, ManagedRepresentation: true, Name: "Weak"),
        // SPEC 4.9.3, 4.9.5: the Array mutation operations, Kimigayo bodies declared inside the Array struct; SPEC 4.9.6 gives `reserve`,
        // `append`, `insert`, `pop`, `remove`, `swap` and `shrinkToFit` no user-code effect and `clear` the destruction of its elements.
        new(KimiDeclarationId.ArrayReserve, Row: None),
        new(KimiDeclarationId.ArrayAppend, Row: None),
        new(KimiDeclarationId.ArrayInsert, Row: None),
        new(KimiDeclarationId.ArrayPop, Row: None),
        new(KimiDeclarationId.ArrayRemove, Row: None),
        new(KimiDeclarationId.ArrayClear, Row: Target),
        new(KimiDeclarationId.ArrayShrinkToFit, Row: None),
        new(KimiDeclarationId.ArraySwap, Row: None),
        // SPEC 4.9.3, 4.9.5, 22.1: the public constructors are bodiless signatures whose definitions the compiler supplies from the
        // linked internal source functions, with the same parameters and premises and a `Self` result.
        new(KimiDeclarationId.ArrayWithCapacity, Implementation: KimiDeclarationId.ArrayCapacityImplementation),
        new(KimiDeclarationId.ArrayCapacityImplementation, Row: None),
        new(KimiDeclarationId.ArrayRepeating, Implementation: KimiDeclarationId.ArrayRepeatingImplementation),
        new(KimiDeclarationId.ArrayRepeatingImplementation, Row: None),
        new(KimiDeclarationId.Utf8Format),
        new(KimiDeclarationId.BufferWriter),
        new(KimiDeclarationId.BufferFull),
        new(KimiDeclarationId.WriteWindow),
        new(KimiDeclarationId.Utf8Writer),
        new(KimiDeclarationId.FixedBuffer),
        new(KimiDeclarationId.HeapBuffer),
        new(KimiDeclarationId.Utf8Slice),
        new(KimiDeclarationId.InvalidUtf8),
        new(KimiDeclarationId.TextFixed), // A direct line's region (R8-W2).
        new(KimiDeclarationId.TextHeap),
        new(KimiDeclarationId.TextWriter, Function: CompilerFunctionKind.TextWriter),
        new(KimiDeclarationId.TextToString, Row: Formats),
        new(KimiDeclarationId.TextTryFormat, Row: Formats),
        new(KimiDeclarationId.FixedBufferReserve),
        new(KimiDeclarationId.FixedBufferText), // A direct line's view (R8-W2).
        new(KimiDeclarationId.HeapBufferReserve),
        new(KimiDeclarationId.HeapBufferIntoString),
        new(KimiDeclarationId.WindowLimit),
        new(KimiDeclarationId.WriterWrite, Row: FormatsValue),
        new(KimiDeclarationId.WriterStatus),
        new(KimiDeclarationId.WriterReserve),
        new(KimiDeclarationId.WriterWriteUtf8),
        new(KimiDeclarationId.WriterDeferLiteral),
        new(KimiDeclarationId.WriterFinish),
        new(KimiDeclarationId.WriteLineUtf8),
        new(KimiDeclarationId.BoolFormatFormat),
        new(KimiDeclarationId.CharFormatFormat),
        new(KimiDeclarationId.UnitFormatFormat),
        new(KimiDeclarationId.F32FormatFormat),
        new(KimiDeclarationId.F64FormatFormat),
        new(KimiDeclarationId.StringWitnessFormat),
        new(KimiDeclarationId.Utf8SliceFormatFormat),
        new(KimiDeclarationId.WrappingFormatFormat),
        new(KimiDeclarationId.IntegerFormatFormat),
        new(KimiDeclarationId.DictionaryReserve, Row: None),
        new(KimiDeclarationId.DictionaryTryInsert, Row: Equality),
        new(KimiDeclarationId.DictionaryInsertOrReplace, Row: new(KimiDeclarationId.Equatable, On: 1, Destroys: 1)), // `K` equality and the unused input `K`'s destruction.
        new(KimiDeclarationId.DictionaryRemove, Row: Equality),
        new(KimiDeclarationId.DictionaryTryGet, Row: Equality),
        new(KimiDeclarationId.DictionaryClear, Row: Target),
        new(KimiDeclarationId.DictionaryShrinkToFit, Row: None),
        new(KimiDeclarationId.Indexable),
        new(KimiDeclarationId.UniqIndexable),
        // IMPL 21.1.3: the structs that state the handles' inline components, by identity for Layout and the tests.
        new(KimiDeclarationId.ArrayRepr),
        new(KimiDeclarationId.SliceRepr),
        new(KimiDeclarationId.StringRepr),
        new(KimiDeclarationId.DictionaryStorageHandle),
        // SPEC 22.2: the standard storage boundary; the exclusive borrow of each family is borrowStorageUniq (SPEC 7.4).
        new(KimiDeclarationId.RefRemainder),
        new(KimiDeclarationId.UniqRemainder),
        // SPEC 22.2: the distinct Dictionary family traverses live entries in insertion order.
        new(KimiDeclarationId.DictionaryRefRemainder),
        new(KimiDeclarationId.DictionaryUniqRemainder),
        new(KimiDeclarationId.DictionaryOwnedRemainder),
        // SPEC 22.2: the fixed-array overloads of the contiguous borrowing remainders.
        // PLAN G33: the fixed-array owning remainder keeps its elements in compiler-known inline storage.
        new(KimiDeclarationId.InlineStorage),
        new(KimiDeclarationId.Position),
        new(KimiDeclarationId.PositionRange),
        new(KimiDeclarationId.Start),
        new(KimiDeclarationId.End),
        new(KimiDeclarationId.DictionaryIndex),
        new(KimiDeclarationId.DictionaryIndexUniq, Row: Equality),
        new(KimiDeclarationId.StorageMissingDictionaryKey, Function: CompilerFunctionKind.StorageMissingDictionaryKey),
        // SPEC 22.2: private primitives that hold no collection logic, used by the DictionaryStorage capacity bodies.
        new(KimiDeclarationId.StorageCountOverflow, Function: CompilerFunctionKind.StorageCountOverflow),
        new(KimiDeclarationId.StorageAllocationSizeExceeded, Function: CompilerFunctionKind.StorageAllocationSizeExceeded),
        new(KimiDeclarationId.StorageTryAllocateBytes, Function: CompilerFunctionKind.StorageTryAllocateBytes),
        new(KimiDeclarationId.StorageTransferBytes, Function: CompilerFunctionKind.StorageTransferBytes),
        new(KimiDeclarationId.StorageStride, Function: CompilerFunctionKind.StorageStride),
        new(KimiDeclarationId.StorageGrowArray),
        new(KimiDeclarationId.StorageDestroyValues, Function: CompilerFunctionKind.StorageDestroyValues),
        new(KimiDeclarationId.StorageErasedReserve, Function: CompilerFunctionKind.StorageErasedReserve),
        new(KimiDeclarationId.StorageFormatFloat, Function: CompilerFunctionKind.StorageFormatFloat),
        new(KimiDeclarationId.StorageFormatInteger128, Function: CompilerFunctionKind.StorageFormatInteger128),
        new(KimiDeclarationId.StorageShrinkArray),
        new(KimiDeclarationId.StorageDestroyArray),
        new(KimiDeclarationId.StorageDestroyDictionary),
        new(KimiDeclarationId.StorageReleaseDictionary),
        new(KimiDeclarationId.StorageArrayCapacity, Row: None),
        new(KimiDeclarationId.DictionaryStorageCapacity, Row: None),
        new(KimiDeclarationId.DictionaryStorageInitialize), // SPEC 4.3: `[:]`'s target.
        new(KimiDeclarationId.DictionaryStorageReserveEntries),
        new(KimiDeclarationId.DictionaryStorageReserve),
        new(KimiDeclarationId.DictionaryStorageGrow),
        new(KimiDeclarationId.DictionaryStorageAppend),
        new(KimiDeclarationId.DictionaryStoragePlaceEntry),
        new(KimiDeclarationId.DictionaryStorageShrinkEntries),
        new(KimiDeclarationId.DictionaryStorageShrinkToFit),
        new(KimiDeclarationId.DictionaryStorageFindEntry, Row: Equality), // SPEC 12.4.3.
        // SPEC 5.6: the public raw storage operations.
        new(KimiDeclarationId.RawAllocate, Function: CompilerFunctionKind.RawAllocate),
        new(KimiDeclarationId.RawRelease, Function: CompilerFunctionKind.RawRelease),
        new(KimiDeclarationId.RawInitialize, Function: CompilerFunctionKind.RawInitialize),
        new(KimiDeclarationId.RawSlice, Function: CompilerFunctionKind.RawSlice),
        // SPEC 15.3.5: the zero-sized dependency Field Core.
        new(KimiDeclarationId.Loan, ManagedRepresentation: true),
        new(KimiDeclarationId.StorageArgumentOutOfRange, Function: CompilerFunctionKind.StorageArgumentOutOfRange),
        new(KimiDeclarationId.FromEndResolve),
        new(KimiDeclarationId.StartResolve),
        new(KimiDeclarationId.EndResolve),
        new(KimiDeclarationId.RangeResolve),
        new(KimiDeclarationId.ClosedRangeResolve),
        new(KimiDeclarationId.ResolvedRangeResolve),
        new(KimiDeclarationId.RangeIterate),
        new(KimiDeclarationId.RangeIterateUniq),
        new(KimiDeclarationId.RangeIntoIterator),
        new(KimiDeclarationId.ClosedRangeIterate),
        new(KimiDeclarationId.ClosedRangeIterateUniq),
        new(KimiDeclarationId.ClosedRangeIntoIterator),
        new(KimiDeclarationId.RangeIteratorStarting),
        new(KimiDeclarationId.ClosedRangeIteratorStarting),
        new(KimiDeclarationId.DictionaryStorageClearEntries),
        new(KimiDeclarationId.StorageDestroyDictionaryEntries),
        new(KimiDeclarationId.StorageIndexBounds, Function: CompilerFunctionKind.StorageIndexBounds),
        new(KimiDeclarationId.UniqSliceIndex),
        new(KimiDeclarationId.UniqSliceIndexUniq),
        new(KimiDeclarationId.UniqSliceIndexPosition),
        new(KimiDeclarationId.UniqSliceIndexPositionUniq),
        new(KimiDeclarationId.UniqSliceSliceRange),
        new(KimiDeclarationId.UniqSliceSliceUniqRange),
        new(KimiDeclarationId.ArraySliceRange),
        new(KimiDeclarationId.ArraySliceUniqRange),
        new(KimiDeclarationId.FixedArraySliceRange),
        new(KimiDeclarationId.FixedArraySliceUniqRange),
        new(KimiDeclarationId.SliceSliceRange),
        new(KimiDeclarationId.StorageSliceUniq),
        new(KimiDeclarationId.SliceToArray),
        new(KimiDeclarationId.ArrayAppendCopies, Row: None),
        // SPEC 4.9.6: `truncate` destroys its receiver's elements from `length` on; `removeAll` calls `matching`, destroys the removed
        // elements and destroys `matching`.
        new(KimiDeclarationId.ArrayTruncate, Row: Target),
        new(KimiDeclarationId.ArrayRemoveAll, Row: CallsThenTarget),
        // SPEC 4.9.6, 15.6.4: the public position insert and appendAll, which add inputs to the element storage (AddedRows), and the
        // position remove, swap, swapRemove and reverse; none runs user code.
        new(KimiDeclarationId.ArrayInsertPosition, Row: None),
        new(KimiDeclarationId.ArrayAppendAll, Row: None),
        new(KimiDeclarationId.ArrayRemovePosition, Row: None),
        new(KimiDeclarationId.ArraySwapPositions, Row: None),
        new(KimiDeclarationId.ArraySwapRemove, Row: None),
        new(KimiDeclarationId.ArrayReverse, Row: None),
    ];

    private static readonly int[] Indices = CreateIndices();

    // The public declaration of each linked implementation, by catalog index; -1 elsewhere.
    private static readonly int[] Presentations = CreatePresentations();

    // A link joins a public declaration served by no compiler function to one implementation; no implementation is linked twice or
    // linked onward. That the implementation is an internal source function of the same container carrying `#CallerLocation` is the
    // library's source, which KimiLibraryShapeTest checks (DECISIONS BU-112).
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
            if (target < 0 || result[target] >= 0 || Definitions[i].Function != CompilerFunctionKind.None ||
                Definitions[target] is not { Function: CompilerFunctionKind.None, Implementation: null })
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
