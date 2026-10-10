// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The shapes of the embedded Kimi library that the compiler and the static <c>.ll.in</c> runtime rely on (SPEC 22.1).
/// A shape is the declaration's source header with its direct member lines; Binding gives the same text the same meaning.</summary>
public class KimiLibraryShapeTest
{
    // Read-only: no test changes the library or its Binding.
    private static readonly Lazy<Compilation> Library = new(() =>
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
        return c;
    });

    // Lowering and the runtime call these bodiless functions by position (BodyLowering, WindowsLowering): parameter order,
    // Types, overload position and constraints.
    private static readonly (KimiDeclarationId Id, string Shape)[] CompilerFunctionShapes =
    [
        (KimiDeclarationId.WriteLine, "public func writeLine(text: ref/string)"),
        (KimiDeclarationId.TestTempDirectory, "public func tempDirectory() -> string"),
        (KimiDeclarationId.StorageBorrowUniqSlice, "internal func borrowStorageUniq<E>(value: uniq/(UniqSlice<E> during source) during a) -> UniqRemainder<E> during a"),
        (KimiDeclarationId.Replace, "public func replace<T>(target: uniq/T ! with => value: T) -> ()"),
        (KimiDeclarationId.Exchange, "public func exchange<T>(target: uniq/T ! with => value: T) -> T"),
        (KimiDeclarationId.Swap, "public func swap<T>(first: uniq/T, second: uniq/T) -> ()"),
        (KimiDeclarationId.MakeObj, "public func makeObj<T>(value: T) -> obj/T\nT is ObjectPayload"),
        (KimiDeclarationId.MakeRc, "public func makeRc<T>(value: T) -> rc/T\nT is ObjectPayload"),
        (KimiDeclarationId.MakeArc, "public func makeArc<T>(value: T) -> arc/T\nT is ObjectPayload"),
        (KimiDeclarationId.Clone, "public func clone<s/T>(value: ref/(s/T)) -> s/T\ns is rc or arc"),
        (KimiDeclarationId.ArrayReserve, "public func reserve(self: uniq/Self, additional: isize)"),
        (KimiDeclarationId.ArrayAppend, "public func append(self: uniq/Self, value: T)"),
        (KimiDeclarationId.ArrayInsert, "internal func insertAt(self: uniq/Self, index: isize, value: T)"),
        (KimiDeclarationId.ArrayPop, "public func pop(self: uniq/Self) -> Option<T>"),
        (KimiDeclarationId.ArrayRemove, "internal func removeAt(self: uniq/Self, index: isize) -> T"),
        (KimiDeclarationId.ArrayClear, "public func clear(self: uniq/Self)"),
        (KimiDeclarationId.ArrayShrinkToFit, "public func shrinkToFit(self: uniq/Self)"),
        (KimiDeclarationId.ArraySwap, "internal func swapAt(self: uniq/Self, first: isize, second: isize)"),
        (KimiDeclarationId.ArrayWithCapacity, "public init(! capacity: isize)"),
        (KimiDeclarationId.ArrayRepeating, "public init(! repeating: T, count: isize)\nT is Copy"),
        (KimiDeclarationId.TextFixed, "public func fixed<length N>(destination: uniq/[N of u8]) -> FixedBuffer"),
        (KimiDeclarationId.TextHeap, "public func heap(capacity: isize) -> HeapBuffer"),
        (KimiDeclarationId.TextWriter, "public func writer<W>(destination: uniq/W) -> Utf8Writer\nW is BufferWriter"),
        (KimiDeclarationId.TextUtf8, "public func utf8(text: ref/string) -> Utf8Slice"),
        (KimiDeclarationId.TextValidateUtf8, "public func validateUtf8(bytes: Slice<u8>) -> Result<Utf8Slice during bytes.source, InvalidUtf8>"),
        (KimiDeclarationId.TextToString, "public func toString<T>(value: ref/T) -> string\nT is Utf8Format"),
        (KimiDeclarationId.TextTryFormat, "public func tryFormat<T, length N>(value: ref/T, destination: uniq/[N of u8]) -> Result<Utf8Slice during destination, BufferFull>\nT is Utf8Format"),
        (KimiDeclarationId.TextRelease, "private func release(data: raw/u8)"),
        (KimiDeclarationId.FixedBufferBytes, "public func bytes(self: ref/Self) -> Slice<u8>"),
        (KimiDeclarationId.FixedBufferText, "public func text(self: ref/Self) -> Result<Text.Utf8Slice, Text.InvalidUtf8>"),
        (KimiDeclarationId.FixedBufferValidate, "public func validate(self: uniq/Self) -> Result<(), Text.InvalidUtf8>"),
        (KimiDeclarationId.FixedBufferClear, "public func clear(self: uniq/Self)"),
        (KimiDeclarationId.FixedBufferReserve, "public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>"),
        (KimiDeclarationId.FixedBufferIntoText, "public func intoText(self: owner/Self) -> Result<Text.Utf8Slice during self.source, Text.InvalidUtf8>"),
        (KimiDeclarationId.HeapBufferBytes, "public func bytes(self: ref/Self) -> Slice<u8>"),
        (KimiDeclarationId.HeapBufferText, "public func text(self: ref/Self) -> Result<Text.Utf8Slice, Text.InvalidUtf8>"),
        (KimiDeclarationId.HeapBufferValidate, "public func validate(self: uniq/Self) -> Result<(), Text.InvalidUtf8>"),
        (KimiDeclarationId.HeapBufferClear, "public func clear(self: uniq/Self)"),
        (KimiDeclarationId.HeapBufferReserve, "public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>"),
        (KimiDeclarationId.HeapBufferIntoString, "public func intoString(self: owner/Self) -> Result<string, Text.InvalidUtf8>"),
        (KimiDeclarationId.WindowPush, "public func push(self: uniq/Self, byte: u8) -> Result<(), BufferFull>"),
        (KimiDeclarationId.WindowAppend, "public func append(self: uniq/Self, bytes: Slice<u8>) -> Result<(), BufferFull>"),
        (KimiDeclarationId.WindowLimit, "public func limit(self: owner/Self, maximum: isize) -> Self{r}\norigin r.source == self.source"),
        (KimiDeclarationId.WindowCommit, "public func commit(self: owner/Self) -> isize"),
        (KimiDeclarationId.WriterWrite, "public func write<T>(self: uniq/Self, value: ref/T) -> Result<(), BufferFull>\nT is Utf8Format"),
        (KimiDeclarationId.WriterStatus, "public func status(self: ref/Self) -> Result<(), BufferFull>"),
        (KimiDeclarationId.WriteLineUtf8, "public func writeLine(text: Text.Utf8Slice)"),
        (KimiDeclarationId.StorageBorrowShared, "internal func borrowStorage<E>(value: ref/Array<E> during a) -> RefRemainder<E> during a"),
        (KimiDeclarationId.StorageBorrowExclusive, "internal func borrowStorageUniq<E>(value: uniq/Array<E> during a) -> UniqRemainder<E> during a"),
        (KimiDeclarationId.StorageOwn, "internal func ownStorage<E>(value: Array<E>) -> OwnedRemainder<E>"),
        (KimiDeclarationId.StorageBorrowDictionary, "internal func borrowStorage<K, V>(value: ref/Dictionary<K, V> during a) -> DictionaryRefRemainder<K, V> during a\nK is Equatable"),
        (KimiDeclarationId.StorageBorrowDictionaryExclusive, "internal func borrowStorageUniq<K, V>(value: uniq/Dictionary<K, V> during a) -> DictionaryUniqRemainder<K, V> during a\nK is Equatable"),
        (KimiDeclarationId.StorageOwnDictionary, "internal func ownStorage<K, V>(value: Dictionary<K, V>) -> DictionaryOwnedRemainder<K, V>\nK is Equatable"),
        (KimiDeclarationId.StorageKeyAt, "internal unsafe func keyAt<K, V>(slot: raw/u8) -> raw/K"),
        (KimiDeclarationId.StorageValueAt, "internal unsafe func valueAt<K, V>(slot: raw/u8) -> raw/V"),
        (KimiDeclarationId.StorageBorrowFixedShared, "internal func borrowStorage<E, length N>(value: ref/[N of E] during a) -> RefRemainder<E> during a"),
        (KimiDeclarationId.StorageBorrowFixedExclusive, "internal func borrowStorageUniq<E, length N>(value: uniq/[N of E] during a) -> UniqRemainder<E> during a"),
        (KimiDeclarationId.StorageOwnFixed, "internal func ownStorage<E, length N>(value: [N of E]) -> FixedOwnedRemainder<E, [N of E]>"),
        (KimiDeclarationId.StorageDictionaryLayout, "internal unsafe func dictionaryStorage<K, V>(value: uniq/Dictionary<K, V>) -> (raw/u8, isize)\nK is Equatable"),
        (KimiDeclarationId.StorageMissingDictionaryKey, "internal func missingDictionaryKey() -> Never"),
        (KimiDeclarationId.StoragePlaceDictionaryEntry, "internal unsafe func placeEntry<K, V>(handle: raw/u8, key: K, value: V)"),
        (KimiDeclarationId.StorageCountOverflow, "internal func countOverflow() -> Never"),
        (KimiDeclarationId.StorageAllocationSizeExceeded, "internal func allocationSizeExceeded() -> Never"),
        (KimiDeclarationId.StorageTryAllocateBytes, "internal func tryAllocateBytes(bytes: isize) -> raw/u8"),
        (KimiDeclarationId.StorageTransferBytes, "internal unsafe func transferBytes(destination: raw/u8, source: raw/u8, count: isize)"),
        (KimiDeclarationId.RawAllocate, "public func allocate<T>(count: isize) -> raw/T"),
        (KimiDeclarationId.RawRelease, "public unsafe func release<T>(storage: raw/T)"),
        (KimiDeclarationId.RawInitialize, "public unsafe func initialize<T>(storage: raw/T, value: T)"),
        (KimiDeclarationId.RawSlice, "public unsafe func slice<T>(storage: raw/T, length: isize) -> Slice<T> during s"),
        (KimiDeclarationId.StorageArgumentOutOfRange, "internal func argumentOutOfRange() -> Never"),
        (KimiDeclarationId.StorageIndexBounds, "internal func indexBounds() -> Never"),
        (KimiDeclarationId.StorageSetArrayLength, "internal unsafe func setArrayLength<E>(value: uniq/Array<E> during a, length: isize)"),
    ];

    public static TheoryData<KimiDeclarationId> CatalogEntries
    {
        get
        {
            var data = new TheoryData<KimiDeclarationId>();
            foreach (var entry in KimiLibraryCatalog.Entries)
            {
                data.Add(entry.Id);
            }

            return data;
        }
    }

    public static TheoryData<KimiDeclarationId, string> CompilerFunctions
    {
        get
        {
            var data = new TheoryData<KimiDeclarationId, string>();
            foreach (var (id, shape) in CompilerFunctionShapes)
            {
                data.Add(id, shape);
            }

            return data;
        }
    }

    [Fact]
    public void LibraryBindsCompletely()
    {
        var c = Library.Value;
        Assert.False(c.Diagnostics.HasSyntaxErrors(c.Library.Kotonoha));
        Assert.Null(c.Library.Kotonoha.GeneratedFunction);
        Assert.Equal(183, KimiLibraryCatalog.Entries.Length);
        var unsourced = new List<KimiDeclarationId>();
        foreach (var entry in KimiLibraryCatalog.Entries)
        {
            if (c.Library.GetSymbol(entry.Id) is not { } symbol)
            {
                unsourced.Add(entry.Id);
                continue;
            }

            Assert.Same(symbol, symbol.Declaration.BoundSymbol);
            Assert.Equal(BindingState.Resolved, symbol.Declaration.BindingState);
        }

        // SPEC 22.1: the Weak family is cataloged but not yet declared in source.
        Assert.Equal([KimiDeclarationId.Downgrade, KimiDeclarationId.Upgrade, KimiDeclarationId.MakeRcCyclic, KimiDeclarationId.MakeArcCyclic, KimiDeclarationId.Weak], unsourced);
    }

    // Recognition finds each cataloged declaration once in its container, at its overload position, with the body kind
    // its role requires.
    [Theory]
    [MemberData(nameof(CatalogEntries))]
    public void CatalogEntriesResolveExactlyOnce(KimiDeclarationId id)
    {
        var c = Library.Value;
        var entry = KimiLibraryCatalog.Entries[KimiLibraryCatalog.Index(id)];
        var symbol = c.Library.GetSymbol(id);
        Assert.Equal(entry.SourceExpected, symbol is not null);
        if (symbol is null)
        {
            return;
        }

        var declaration = symbol.Declaration;
        var container = Container(declaration);
        var path = entry.Owner ?? entry.Container switch
        {
            KimiLibraryContainer.Root => string.Empty,
            KimiLibraryContainer.FixedBuffer or KimiLibraryContainer.HeapBuffer => "Text." + entry.Container,
            KimiLibraryContainer.FixedArray => "Storage.FixedArray",
            _ => entry.Container.ToString(),
        };
        Assert.Equal(path, Path(container));
        var same = Declarations(container, entry.Name, entry.IsFunction);
        Assert.Same(declaration, entry.Overload < 0 ? Assert.Single(same) : same[entry.Overload]);
        if (entry.Intrinsic != IntrinsicKind.None)
        {
            Assert.Equal(entry.Intrinsic, symbol.Intrinsic);
            Assert.Equal("public contract " + entry.Name, Shape(declaration));
        }

        if (entry.Function != CompilerFunctionKind.None || entry.Implementation is not null)
        {
            Assert.Equal(entry.Function, symbol.CompilerFunction);
            Assert.False(HasBody(declaration));
        }

        if (entry.SourceFunction)
        {
            Assert.Equal(CompilerFunctionKind.None, symbol.CompilerFunction);
            Assert.True(HasBody(declaration));
        }

        // SPEC 22.1: a linked public declaration is defined by an internal source function of the same container.
        if (entry.Implementation is { } implementation)
        {
            var linked = Assert.IsType<FunctionKoto>(c.Library.GetSymbol(implementation)!.Declaration);
            Assert.Equal(ModifierKind.Internal, linked.Modifier);
            Assert.True(HasBody(linked));
            Assert.Same(declaration.Parent, linked.Parent);
        }
    }

    // A bodiless function outside a Contract is a cataloged compiler function or a foreign import, and the signature-only
    // groups hold only registered compiler functions.
    [Fact]
    public void BodilessFunctionsAreCompilerFunctions()
    {
        var c = Library.Value;
        foreach (var group in new GroupKoto[] { c.Library.Intrinsics, c.Library.Console, c.Library.Test })
        {
            Assert.All(group.Members, member => Assert.Contains(Assert.IsType<FunctionKoto>(member).BoundSymbol!, c.Library.RegisteredSymbols.ToArray()));
        }

        Visit(c.Library.Kotonoha.RootKoto);

        void Visit(DeclarationContainerKoto container)
        {
            for (var i = 0; container is not ContractKoto && i < container.Members.Count; i++)
            {
                if (container.Members[i] is FunctionKoto { IsRequirement: false, AttributeChain: null, BoundSymbol: var symbol } function && !HasBody(function))
                {
                    Assert.True(
                        symbol?.LibraryDeclaration is { } id && ReferenceEquals(c.Library.GetSymbol(id), symbol) &&
                        (symbol.CompilerFunction != CompilerFunctionKind.None || KimiLibraryCatalog.ImplementationOf(id) is not null),
                        Line(function));
                }
            }

            foreach (var nested in container.NestedContainers)
            {
                Visit(nested);
            }
        }
    }

    [Fact]
    public void EveryCompilerFunctionHasAShape()
    {
        var expected = new List<KimiDeclarationId>();
        foreach (var entry in KimiLibraryCatalog.Entries)
        {
            if ((entry.Function != CompilerFunctionKind.None || entry.Implementation is not null) && entry.SourceExpected)
            {
                expected.Add(entry.Id);
            }
        }

        Assert.Equal(expected, CompilerFunctionShapes.Select(x => x.Id));
    }

    [Theory]
    [MemberData(nameof(CompilerFunctions))]
    public void CompilerFunctionShapesAreFixed(KimiDeclarationId id, string shape)
        => Assert.Equal(shape, Shape(Library.Value.Library.GetSymbol(id)!.Declaration));

    // SPEC 22.1 and its formatting and arithmetic profiles: the compiler reaches these requirements by name and position.
    [Theory]
    [InlineData(KimiDeclarationId.Copy, "public contract Copy")]
    [InlineData(KimiDeclarationId.Owned, "public contract Owned")]
    [InlineData(KimiDeclarationId.Callable, "public contract Callable")]
    [InlineData(KimiDeclarationId.Sealed, "public contract Sealed")]
    [InlineData(KimiDeclarationId.ObjectPayload, "public contract ObjectPayload")]
    [InlineData(KimiDeclarationId.PrimitiveInteger, "public contract PrimitiveInteger")]
    [InlineData(KimiDeclarationId.Equatable, "public contract Equatable\nfunc equals(self: ref/Self, other: ref/Self) -> bool")]
    [InlineData(KimiDeclarationId.Comparable, "public contract Comparable: Equatable\nfunc compare(self: ref/Self, other: ref/Self) -> i32")]
    [InlineData(KimiDeclarationId.Addable, "public contract Addable<Rhs>\nassociate Output\nfunc added(self: ref/Self, right: ref/Rhs) -> Self.Output")]
    [InlineData(KimiDeclarationId.Subtractable, "public contract Subtractable<Rhs>\nassociate Output\nfunc subtracted(self: ref/Self, right: ref/Rhs) -> Self.Output")]
    [InlineData(KimiDeclarationId.Multipliable, "public contract Multipliable<Rhs>\nassociate Output\nfunc multiplied(self: ref/Self, right: ref/Rhs) -> Self.Output")]
    [InlineData(KimiDeclarationId.Dividable, "public contract Dividable<Rhs>\nassociate Output\nfunc divided(self: ref/Self, right: ref/Rhs) -> Self.Output")]
    [InlineData(KimiDeclarationId.RemainderProvider, "public contract RemainderProvider<Rhs>\nassociate Output\nfunc remainder(self: ref/Self, right: ref/Rhs) -> Self.Output")]
    [InlineData(KimiDeclarationId.LeftAddable, "public contract LeftAddable<Lhs>\nassociate Output\nfunc addedFrom(left: ref/Lhs, self: ref/Self) -> Self.Output")]
    [InlineData(KimiDeclarationId.LeftSubtractable, "public contract LeftSubtractable<Lhs>\nassociate Output\nfunc subtractedFrom(left: ref/Lhs, self: ref/Self) -> Self.Output")]
    [InlineData(KimiDeclarationId.LeftMultipliable, "public contract LeftMultipliable<Lhs>\nassociate Output\nfunc multipliedFrom(left: ref/Lhs, self: ref/Self) -> Self.Output")]
    [InlineData(KimiDeclarationId.LeftDividable, "public contract LeftDividable<Lhs>\nassociate Output\nfunc dividedFrom(left: ref/Lhs, self: ref/Self) -> Self.Output")]
    [InlineData(KimiDeclarationId.LeftRemainderProvider, "public contract LeftRemainderProvider<Lhs>\nassociate Output\nfunc remainderFrom(left: ref/Lhs, self: ref/Self) -> Self.Output")]
    [InlineData(KimiDeclarationId.Negatable, "public contract Negatable\nassociate Output\nfunc negated(self: ref/Self) -> Self.Output")]
    [InlineData(KimiDeclarationId.Indexable, "public contract Indexable<Key>\nassociate Element\nfunc index(self: ref/Self, key: ref/Key) -> place ref/Element during self")]
    [InlineData(KimiDeclarationId.UniqIndexable, "public contract UniqIndexable<Key>: Indexable<Key>\nfunc indexUniq(self: uniq/Self, key: ref/Key) -> place uniq/Element during self")]
    [InlineData(KimiDeclarationId.LendingIterator, "public contract LendingIterator\nassociate LentItem(step) for uniq/Self during step\nfunc next(self: uniq/Self during step) -> Option<Self.LentItem(step)>")]
    [InlineData(KimiDeclarationId.Iterator, "public contract Iterator: LendingIterator\nassociate Item\nassociate LendingIterator.LentItem(step) is Item\neffect LendingIterator.next preserves results")]
    [InlineData(KimiDeclarationId.Iterable, "public contract Iterable\nassociate IteratorType(source) is ::Kimi.LendingIterator for ref/Self during source\nfunc iterate(self: ref/Self during source) -> Self.IteratorType(source)")]
    [InlineData(KimiDeclarationId.UniqIterable, "public contract UniqIterable\nassociate IteratorType(source) is ::Kimi.LendingIterator for uniq/Self during source\nfunc iterateUniq(self: uniq/Self during source) -> Self.IteratorType(source)")]
    [InlineData(KimiDeclarationId.IntoIterable, "public contract IntoIterable\nassociate IteratorType is ::Kimi.LendingIterator\nfunc intoIterator(self: Self) -> Self.IteratorType")]
    [InlineData(KimiDeclarationId.Position, "public contract Position: Equatable, Utf8Format\nSelf is Copy\nSelf is Owned\nfunc tryResolve(self: Self, length: isize) -> Option<isize>")]
    [InlineData(KimiDeclarationId.PositionRange, "public contract PositionRange: Equatable, Utf8Format\nSelf is Copy\nSelf is Owned\nfunc tryResolve(self: Self, length: isize) -> Option<ResolvedRange>")]
    [InlineData(KimiDeclarationId.Utf8Format, "public contract Utf8Format\nfunc format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>")]
    [InlineData(KimiDeclarationId.BufferWriter, "public contract BufferWriter\nfunc reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>")]
    public void ContractShapesAreFixed(KimiDeclarationId id, string shape)
        => Assert.Equal(shape, Shape(Assert.IsType<ContractKoto>(Library.Value.Library.GetSymbol(id)!.Declaration)));

    // Lowering and the runtime address these records' stored Fields by name, in this order, and run their drop
    // (BodyLowering, LlvmModuleWriter, Utf8BufferRuntime.ll.in); a record without Fields stays zero-sized.
    [Theory]
    [InlineData("Storage.RefRemainder", "internal struct RefRemainder<E> {source}\ninternal let storage: raw/E\ninternal var position: isize\ninternal var count: isize\ninternal let loan: Loan<ref/E during source>")]
    [InlineData("Storage.UniqRemainder", "internal struct UniqRemainder<E> {source}\ninternal let storage: raw/E\ninternal var position: isize\ninternal var count: isize\ninternal let loan: Loan<uniq/E during source>")]
    [InlineData("Storage.OwnedRemainder", "internal struct OwnedRemainder<E>\ninternal let storage: raw/E\ninternal var position: isize\ninternal var count: isize\ninternal var capacity: isize\ndrop")]
    [InlineData("Storage.DictionaryRefRemainder", "internal struct DictionaryRefRemainder<K, V> {source}\ninternal let storage: raw/u8\ninternal let stride: isize\ninternal var link: isize\ninternal var count: isize\ninternal let loan: Loan<ref/(K, V) during source>")]
    [InlineData("Storage.DictionaryUniqRemainder", "internal struct DictionaryUniqRemainder<K, V> {source}\ninternal let storage: raw/u8\ninternal let stride: isize\ninternal var link: isize\ninternal var count: isize\ninternal let loan: Loan<uniq/(K, V) during source>")]
    [InlineData("Storage.DictionaryOwnedRemainder", "internal struct DictionaryOwnedRemainder<K, V>\ninternal let storage: raw/u8\ninternal let stride: isize\ninternal var link: isize\ninternal var tail: isize\ninternal var count: isize\ndrop")]
    [InlineData("Storage.InlineStorage", "internal struct InlineStorage<A>\ninternal var value: A")]
    [InlineData("Storage.FixedOwnedRemainder", "internal struct FixedOwnedRemainder<E, A>\ninternal var storage: InlineStorage<A>\ninternal var position: isize\ninternal var count: isize\ndrop")]
    [InlineData("UniqSlice", "public struct UniqSlice<T> {source}\ninternal let storage: raw/T\npublic let length: isize\ninternal let loan: Loan<uniq/T during source>")]
    [InlineData("ResolvedRange", "public struct ResolvedRange\npublic let start: isize\npublic let end: isize")]
    [InlineData("FromEnd", "public struct FromEnd<T>\npublic let offset: T")]
    [InlineData("Range", "public struct Range<S, E>\npublic let start: S\npublic let end: E")]
    [InlineData("ClosedRange", "public struct ClosedRange<S, E>\npublic let start: S\npublic let end: E")]
    [InlineData("Start", "public struct Start")]
    [InlineData("End", "public struct End")]
    [InlineData("Loan", "public struct Loan<T>")]
    [InlineData("Wrapping", "public struct Wrapping<T>")]
    [InlineData("BufferFull", "public struct BufferFull")]
    [InlineData("WriteWindow", "public struct WriteWindow {source}\nlet state: raw/u8\nlet start: isize\npublic let written: isize\npublic let remaining: isize\nlet loan: Loan<uniq/u8 during source>")]
    [InlineData("Utf8Writer", "public struct Utf8Writer {target}\nlet destination: raw/u8\nlet dispatch: raw/u8\nlet kind: isize\nlet failed: bool\nlet hint: isize\nlet pending: raw/u8\nlet pendingLength: isize\nlet logicalLength: isize\nlet loan: Loan<uniq/u8 during target>")]
    [InlineData("Text.FixedBuffer", "public struct FixedBuffer {source}\nlet data: raw/u8\npublic let capacity: isize\npublic let length: isize\nlet validated: isize\nlet loan: Loan<uniq/u8 during source>")]
    [InlineData("Text.HeapBuffer", "public struct HeapBuffer\nlet data: raw/u8\npublic let capacity: isize\npublic let length: isize\nlet validated: isize\ndrop")]
    [InlineData("Text.Utf8Slice", "public struct Utf8Slice {source}\nlet value: Slice<u8> during source")]
    [InlineData("Text.InvalidUtf8", "public struct InvalidUtf8")]
    public void RecordFieldsAreFixed(string path, string shape)
    {
        var c = Library.Value;
        var type = c.Binding.SelfType(Record(path).BoundSymbol!);
        var lines = new List<string> { Line(Record(path)) };
        for (var i = 0; i < AdtDef.Count(type); i++)
        {
            Assert.NotNull(AdtDef.FieldType(type, i));
            lines.Add(Line(AdtDef.Field(type, i)));
        }

        if (AdtDef.Destructor(type) is not null)
        {
            lines.Add("drop");
        }

        Assert.Equal(shape, string.Join('\n', lines));
    }

    // SPEC 22.1: the compiler manages these representations, so their declarations store nothing.
    [Theory]
    [InlineData("Array")]
    [InlineData("Dictionary")]
    [InlineData("Slice")]
    [InlineData("Loan")]
    [InlineData("Wrapping")]
    public void ManagedRepresentationsStoreNothing(string path)
        => Assert.DoesNotContain(Record(path).Members, member => member is VariableKoto and not PropertyKoto { DeclarationKind: PropertyDeclarationKind.Computed or PropertyDeclarationKind.Requirement });

    // SPEC 22.1: Case order is the tag (Some/Ok = 0, None/Err = 1) that lowering and the runtime construct and test.
    [Theory]
    [InlineData(KimiDeclarationId.Option, "public enum Option<T>\nSelf is Copy when T is Copy\nSome(T)\nNone")]
    [InlineData(KimiDeclarationId.Result, "public enum Result<T, E>\nOk(T)\nErr(E)")]
    public void OptionAndResultCasesAreFixed(KimiDeclarationId id, string shape)
    {
        var symbol = Library.Value.Library.GetSymbol(id)!;
        var declaration = Assert.IsType<EnumKoto>(symbol.Declaration);
        Assert.Equal(shape, Shape(declaration));
        var ordinal = 0;
        foreach (var member in declaration.Members)
        {
            if (member is not SyntaxFormKoto { BoundSymbol.EnumCase: { } bound })
            {
                continue;
            }

            Assert.Same(symbol, bound.Owner);
            Assert.Equal(ordinal, bound.Ordinal);
            var payload = bound.Payload;
            var parameter = id == KimiDeclarationId.Option ? (ordinal == 0 ? 0 : -1) : ordinal;
            Assert.Equal(parameter < 0 ? 0 : 1, payload.Length);
            if (parameter >= 0)
            {
                Assert.Same(declaration.GenericParameterNodes[parameter].BoundType, payload[0].BoundType);
            }

            ordinal++;
        }

        Assert.Equal(2, ordinal);
    }

    // The static runtime (Utf8BufferRuntime.ll.in, Utf8FormatRuntime.ll.in) reads these records at fixed offsets until R8.
    [Theory]
    [InlineData("Utf8Writer", 64, "destination", 0, "dispatch", 8, "kind", 16, "hint", 24, "pending", 32, "pendingLength", 40, "logicalLength", 48, "failed", 56)]
    [InlineData("Text.FixedBuffer", 32, "data", 0, "capacity", 8, "length", 16, "validated", 24)]
    [InlineData("Text.HeapBuffer", 32, "data", 0, "capacity", 8, "length", 16, "validated", 24)]
    [InlineData("WriteWindow", 32, "state", 0, "start", 8, "written", 16, "remaining", 24)]
    [InlineData("Text.Utf8Slice", 16, "value", 0)]
    public void RuntimeLayoutsAreFixed(string path, int size, params object[] offsets)
    {
        var (layout, type) = Layout(path);
        Assert.Equal(size, layout.Value.Layout.Size);
        for (var i = 0; i < offsets.Length; i += 2)
        {
            Assert.Equal((int)offsets[i + 1], layout.Offset(type, (string)offsets[i]));
        }
    }

    // The writer wrapper and the conversions are runtime glue: they test `failed` and allocate the buffer and writer
    // records the runtime initializes, at the offset and sizes of the library's named layouts.
    [Fact]
    public void RuntimeGlueMatchesTheNamedLayouts()
    {
        const string Source = """
            struct Value
                Self is Utf8Format
                public init() => ()
                public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => writer.write("word")
            var bytes = [4 of 0@u8]
            match Text.tryFormat(Value.init(), bytes@uniq)@move
                .Ok(let view) => Console.writeLine(view)
                .Err(_) => $abort("full")
            Console.writeLine(Text.toString(Value.init()))
            """;
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(Source));
        var (writer, writerType) = Layout("Utf8Writer");
        Assert.Contains($"%failed = getelementptr i8, ptr %self, i64 {writer.Offset(writerType, "failed")}\n", ir, StringComparison.Ordinal);
        foreach (var (path, initializer) in new[] { ("Text.FixedBuffer", "__kimi_text_fixed"), ("Text.HeapBuffer", "__kimi_text_heap") })
        {
            Assert.Contains($"%buffer = alloca [{Layout(path).Layout.Value.Layout.Size} x i8], align 8\n  %writer = alloca [{writer.Value.Layout.Size} x i8], align 8\n  %result = alloca i32, align 4\n  call void @{initializer}(", ir, StringComparison.Ordinal);
        }
    }

    // PositionSyntax functions construct `^x` and range syntax values (Binding.Ranges): one internal function per name.
    [Theory]
    [InlineData("fromEnd")]
    [InlineData("between")]
    [InlineData("from")]
    [InlineData("to")]
    [InlineData("all")]
    [InlineData("through")]
    [InlineData("upTo")]
    public void PositionSyntaxFunctionsAreUnique(string name)
    {
        var function = Assert.IsType<FunctionKoto>(Assert.Single(Declarations(Library.Value.Library.PositionSyntax!, name, true)));
        Assert.Equal(ModifierKind.Internal, function.Modifier);
        Assert.False(function.IsSpecialization);
        Assert.True(HasBody(function));
    }

    private static bool HasBody(Koto declaration)
        => declaration is FunctionKoto function && (function.Body is not null || function.ExpressionBody is not null);

    private static DeclarationContainerKoto Container(Koto declaration)
    {
        var parent = declaration.Parent;
        while (parent is not DeclarationContainerKoto)
        {
            parent = parent!.Parent;
        }

        return (DeclarationContainerKoto)parent;
    }

    private static string Path(DeclarationContainerKoto container)
        => container.IsRoot ? string.Empty : container.Parent is DeclarationContainerKoto { IsRoot: false } parent ? Path(parent) + "." + container.Name : container.Name;

    private static DeclarationContainerKoto Record(string path)
    {
        DeclarationContainerKoto container = Library.Value.Library.Kotonoha.RootKoto;
        foreach (var name in path.Split('.'))
        {
            container = Assert.Single(container.NestedContainers, x => x.Name == name);
        }

        return container;
    }

    // The same-named functions of a container, including those of its conditional conformance blocks, or its same-named
    // nested declarations, in source order (the order of catalog overload positions).
    private static List<Koto> Declarations(DeclarationContainerKoto container, string name, bool function)
    {
        var found = new List<Koto>();
        if (!function)
        {
            found.AddRange(container.NestedContainers.Where(x => x.Name == name));
            return found;
        }

        foreach (var member in container.Members)
        {
            Visit(member);
        }

        return found;

        void Visit(Koto node)
        {
            if (node is FunctionKoto member && member.Name == name)
            {
                found.Add(member);
            }
            else if (node is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } conformance && conformance.Operands is [_, _, CodeBlockKoto block])
            {
                foreach (var item in block.Items)
                {
                    Visit(item);
                }
            }
        }
    }

    private static (AggregateLayout Layout, BoundType Type) Layout(string path)
    {
        var record = Record(path);
        var type = Library.Value.Binding.SelfType(record.BoundSymbol!);
        var pool = new AggregateLayoutPool();
        if (AdtDef.Destructor(type) is { } destructor)
        {
            pool.RegisterDestructor(destructor, 0);
        }

        return (Assert.IsType<AggregateLayout>(pool.Get(type)), type);
    }

    // The trimmed source line holding the node's start: a declaration header with its modifiers, or a Field.
    private static string Line(Koto node) => Lines(node, false)[0];

    // The header and its direct member lines, one level deeper, without comments or bodies.
    private static string Shape(Koto node) => string.Join('\n', Lines(node, true));

    private static List<string> Lines(Koto node, bool members)
    {
        var text = node.CodeContext.SourceDocument!.SourceText;
        var lines = text.Split('\n');
        var index = text.AsSpan(0, node.Span.Start).Count('\n');
        var result = new List<string> { lines[index].Trim() };
        var indent = Indent(lines[index]);
        var child = -1;
        for (var i = index + 1; members && i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var depth = Indent(lines[i]);
            if (depth <= indent)
            {
                break;
            }

            child = child < 0 ? depth : child;
            if (depth == child)
            {
                result.Add(line);
            }
        }

        return result;
    }

    private static int Indent(string line) => line.Length - line.TrimStart(' ').Length;
}
