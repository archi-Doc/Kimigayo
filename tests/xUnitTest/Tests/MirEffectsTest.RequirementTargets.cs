// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// SPEC 8.11.3, 4.9.6, 7.2.3, 11.9.2, 9.6.1, 8.2.3, 23.1.2 (R7-P5a, DECISIONS R4-M28): A4's Require by what TyCtxt.Witness resolves, each
// form with its labelled records as a build checks the program (TestDiagnostics.ErrorSites; the unit's probes and the review's twins,
// labelled from SPEC before any run; E7's located Unsupported_Kd is gone): a published row's requirement and an atom composed into a
// Tuple with an abstract part (W, A), a default's copy the body writes (C), a standard Property witness (P), a Refused witness (R), a
// conformance only a premise states (N, also read by a confined obligation: P1prem, P2prem), a formation past IMPL A.4's height
// (H) and a requirement Item on a Self that is not abstract (X4, R7-P5b). Fail-before (dev 79934ae85): W1-W5, A1-A3, C3, C4 and P1-P4
// were E7's located Unsupported_Kd at the conformance, beside W2's and A2's P42 record and P3's and P4's DefaultArgumentAccess_Kd.
// W2 and A2 keep P42's independent Unsupported_Kd at the mutable static's declaration (R5-M82).
public sealed partial class MirEffectsTest
{
    private const string RowTupleI32 = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        group H
            public func put<K>(table: uniq/Dictionary<(K, i32), i32>, key: ref/(K, i32))
                K is Equatable
                _ = table.remove(key)
        struct S
            Self is Sink
            var table: Dictionary<(i32, i32), i32> = [:]
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                let k = (value, 1)
                H.put(self.table@uniq, k)
                return .Ok(())
        public func main() => ()
        """;

    private const string RowTupleEnvironment = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        group State
            public var value: i32 = 0
        struct Compared
            Self is Equatable
            public func equals(self: ref/Self, other: ref/Self) -> bool
                _ = State.value
                return true
        group H
            public func put<K>(table: uniq/Dictionary<(K, i32), i32>, key: ref/(K, i32))
                K is Equatable
                _ = table.remove(key)
        struct S
            Self is Sink
            var table: Dictionary<(Compared, i32), i32> = [:]
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                let k = (Compared.init(), 1)
                H.put(self.table@uniq, k)
                return .Ok(())
        public func main() => ()
        """;

    private const string RowTuplePureWitness = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        struct Key
            Self is Equatable
            public func equals(self: ref/Self, other: ref/Self) -> bool => true
        group H
            public func put<K>(table: uniq/Dictionary<(K, i32), i32>, key: ref/(K, i32))
                K is Equatable
                _ = table.remove(key)
        struct S
            Self is Sink
            var table: Dictionary<(Key, i32), i32> = [:]
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                let k = (Key.init(), 1)
                H.put(self.table@uniq, k)
                return .Ok(())
        public func main() => ()
        """;

    private const string RowTupleTopUnbounded = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        struct G<K>
            K is Equatable
            Self is Sink
            var table: Dictionary<(K, i32), i32> = [:]
            var key: (K, i32)
            public init(key: (K, i32)) => self.key = key@move
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                _ = self.table.tryGet(self.key)
                return .Ok(())
        public func main() => ()
        """;

    private const string RowTupleTopBounded = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        contract Fixed: Equatable
            effect Equatable.equals confined
        struct G<K>
            K is Fixed
            Self is Sink
            var table: Dictionary<(K, i32), i32> = [:]
            var key: (K, i32)
            public init(key: (K, i32)) => self.key = key@move
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                _ = self.table.tryGet(self.key)
                return .Ok(())
        public func main() => ()
        """;

    private const string AtomTupleI32 = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        group H
            public func put<T>(table: uniq/Dictionary<T, i32>, key: ref/T)
                T is Equatable
                _ = table.remove(key)
            public func wrap<K>(table: uniq/Dictionary<(K, i32), i32>, key: ref/(K, i32))
                K is Equatable
                H.put(table, key)
        struct S
            Self is Sink
            var table: Dictionary<(i32, i32), i32> = [:]
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                let k = (value, 1)
                H.wrap(self.table@uniq, k)
                return .Ok(())
        public func main() => ()
        """;

    private const string AtomTupleEnvironment = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        group State
            public var value: i32 = 0
        struct Compared
            Self is Equatable
            public func equals(self: ref/Self, other: ref/Self) -> bool
                _ = State.value
                return true
        group H
            public func put<T>(table: uniq/Dictionary<T, i32>, key: ref/T)
                T is Equatable
                _ = table.remove(key)
            public func wrap<K>(table: uniq/Dictionary<(K, i32), i32>, key: ref/(K, i32))
                K is Equatable
                H.put(table, key)
        struct S
            Self is Sink
            var table: Dictionary<(Compared, i32), i32> = [:]
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                let k = (Compared.init(), 1)
                H.wrap(self.table@uniq, k)
                return .Ok(())
        public func main() => ()
        """;

    private const string AtomTuplePureWitness = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        struct Key
            Self is Equatable
            public func equals(self: ref/Self, other: ref/Self) -> bool => true
        group H
            public func put<T>(table: uniq/Dictionary<T, i32>, key: ref/T)
                T is Equatable
                _ = table.remove(key)
            public func wrap<K>(table: uniq/Dictionary<(K, i32), i32>, key: ref/(K, i32))
                K is Equatable
                H.put(table, key)
        struct S
            Self is Sink
            var table: Dictionary<(Key, i32), i32> = [:]
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                let k = (Key.init(), 1)
                H.wrap(self.table@uniq, k)
                return .Ok(())
        public func main() => ()
        """;

    private const string CallCopyUnbounded = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        group H
            public func same<T>(a: ref/T, b: ref/T, marker: bool = a == b) -> bool
                T is Equatable
                return marker
            public func loose<A>(a: ref/A, b: ref/A) -> bool
                A is Equatable
                let x = (a, 1)
                let y = (b, 1)
                return H.same(x, y)
        struct S
            Self is Sink
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                _ = H.loose(value, value)
                return .Ok(())
        public func main() => ()
        """;

    private const string CallCopyBounded = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        contract Fixed: Equatable
            effect Equatable.equals confined
        struct Key
            Self is Fixed
            public func equals(self: ref/Self, other: ref/Self) -> bool => true
        group H
            public func same<T>(a: ref/T, b: ref/T, marker: bool = a == b) -> bool
                T is Equatable
                return marker
            public func pure<A>(a: ref/A, b: ref/A) -> bool
                A is Fixed
                let x = (a, 1)
                let y = (b, 1)
                return H.same(x, y)
        struct S
            Self is Sink
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                let key = Key.init()
                _ = H.pure(key, key)
                return .Ok(())
        public func main() => ()
        """;

    private const string CallCopyClosed = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        group H
            public func same<T>(a: ref/T, b: ref/T, marker: bool = a == b) -> bool
                T is Equatable
                return marker
        struct S
            Self is Sink
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                let x = (value, 1)
                _ = H.same(x, x)
                return .Ok(())
        public func main() => ()
        """;

    private const string PropertyCopy = """
        contract Sized
            property size: i32 has get
        struct S
            Self is Sized
            public var size: i32
            public init(size: i32) => self.size = size
        contract Runner
            func run(self: ref/Self) -> i32
                effect confined
        group Helpers
            public func evaluate<T>(sample: ref/T, marker: i32 = sample.size) -> i32
                T is Sized
                return marker
        struct Worker
            Self is Runner
            public var held: S
            public init(held: S) => self.held = held@move
            public func run(self: ref/Self) -> i32 => Helpers.evaluate(self.held@ref)
        public func main() => ()
        """;

    private const string PropertyBorrow = """
        struct Loud
            public var n: i32 = 0
            drop => Console.writeLine("drop")
        contract Viewable
            property item: ref/Loud has get
        struct S
            Self is Viewable
            public var item: Loud
            public init(item: Loud) => self.item = item@move
        contract Runner
            func run(self: ref/Self) -> i32
                effect confined
        group Helpers
            public func evaluate<T>(sample: ref/T, marker: i32 = sample.item.n) -> i32
                T is Viewable
                return marker
        struct Worker
            Self is Runner
            public var held: S
            public init(held: S) => self.held = held@move
            public func run(self: ref/Self) -> i32 => Helpers.evaluate(self.held@ref)
        public func main() => ()
        """;

    private const string PropertySetLoud = """
        struct Loud
            public var n: i32 = 0
            drop => Console.writeLine("drop")
        contract Replaceable
            property item: ref/Loud
                get() -> ref/Loud
                set(value: Loud) -> ()
        struct S
            Self is Replaceable
            public var item: Loud
            public init(item: Loud) => self.item = item@move
        contract Runner
            func run(self: uniq/Self) -> i32
                effect confined
        group Helpers
            public func evaluate<T>(sample: uniq/T, marker: i32 = label result: do
                sample.item = Loud.init()
                exit to result 1
            ) -> i32
                T is Replaceable
                return marker
        struct Worker
            Self is Runner
            public var held: S
            public init(held: S) => self.held = held@move
            public func run(self: uniq/Self) -> i32 => Helpers.evaluate(self.held@uniq)
        public func main() => ()
        """;

    private const string PropertySetQuiet = """
        struct Quiet
            public var n: i32 = 0
        contract Replaceable
            property item: ref/Quiet
                get() -> ref/Quiet
                set(value: Quiet) -> ()
        struct S
            Self is Replaceable
            public var item: Quiet
            public init(item: Quiet) => self.item = item@move
        contract Runner
            func run(self: uniq/Self) -> i32
                effect confined
        group Helpers
            public func evaluate<T>(sample: uniq/T, marker: i32 = label result: do
                sample.item = Quiet.init()
                exit to result 1
            ) -> i32
                T is Replaceable
                return marker
        struct Worker
            Self is Runner
            public var held: S
            public init(held: S) => self.held = held@move
            public func run(self: uniq/Self) -> i32 => Helpers.evaluate(self.held@uniq)
        public func main() => ()
        """;

    private const string PropertySetInBody = """
        struct Quiet
            public var n: i32 = 0
        contract Replaceable
            property item: ref/Quiet
                get() -> ref/Quiet
                set(value: Quiet) -> ()
        struct S
            Self is Replaceable
            public var item: Quiet
            public init(item: Quiet) => self.item = item@move
        contract Runner
            func run(self: uniq/Self) -> i32
                effect confined
        group Helpers
            public func evaluate<T>(sample: uniq/T) -> i32
                T is Replaceable
                sample.item = Quiet.init()
                return 1
        struct Worker
            Self is Runner
            public var held: S
            public init(held: S) => self.held = held@move
            public func run(self: uniq/Self) -> i32 => Helpers.evaluate(self.held@uniq)
        public func main() => ()
        """;

    private const string RefusedOwner = """
        contract Consume
            func consume(self: owner/Self) -> i32
        open struct Base
            public init() => ()
            public func consume(self: owner/Self) -> i32 => 1
        struct Leaf : Base
            Self is Consume
            public init() : base() => ()
        public func main() => ()
        """;

    private const string RefusedOwnerUnderConfined = """
        contract Consume
            Self is Copy
            func consume(self: owner/Self) -> i32
        open struct Base
            public init() => ()
            public func consume(self: owner/Self) -> i32 => 1
        struct Leaf : Base
            Self is Consume
            public init() : base() => ()
        contract Runner
            func run(self: ref/Self) -> i32
                effect confined
        group Helpers
            public func evaluate<T>(sample: T, marker: i32 = sample.consume()) -> i32
                T is Consume
                return marker
        struct Worker
            Self is Runner
            public init() => ()
            public func run(self: ref/Self) -> i32 => Helpers.evaluate(Leaf.init())
        public func main() => ()
        """;

    private const string InheritedRef = """
        contract Consume
            func consume(self: ref/Self) -> i32
        open struct Base
            public init() => ()
            public func consume(self: ref/Self) -> i32 => 1
        struct Leaf : Base
            Self is Consume
            public init() : base() => ()
        contract Runner
            func run(self: ref/Self) -> i32
                effect confined
        group Helpers
            public func evaluate<T>(sample: ref/T, marker: i32 = sample.consume()) -> i32
                T is Consume
                return marker
        struct Worker
            Self is Runner
            public var held: Leaf
            public init(held: Leaf) => self.held = held@move
            public func run(self: ref/Self) -> i32 => Helpers.evaluate(self.held@ref)
        public func main() => ()
        """;

    private const string PremiseContradicted = """
        struct S<A>
            public init() => ()
        func f<I, A>(t: uniq/Dictionary<S<A>, i32>, k: ref/S<A>)
            I is Iterator
            I.Item is S<A>
            I.Item is Equatable
            _ = t.remove(k)
        public func main() => ()
        """;

    private const string PremiseUnderCondition = """
        struct S<A>
            Self is Equatable when A is Equatable
                public func equals(self: ref/Self, other: ref/Self) -> bool => true
            public init() => ()
        func f<I, A>(t: uniq/Dictionary<S<A>, i32>, k: ref/S<A>)
            I is Iterator
            I.Item is S<A>
            I.Item is Equatable
            _ = t.remove(k)
        public func main() => ()
        """;

    private const string PremiseUnboundedUnderConfined = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        struct S<A>
            Self is Equatable when A is Equatable
                public func equals(self: ref/Self, other: ref/Self) -> bool => true
            public init() => ()
        struct G<I, A>
            I is Iterator
            I.Item is S<A>
            I.Item is Equatable
            Self is Sink
            var table: Dictionary<S<A>, i32> = [:]
            var key: S<A>
            public init(key: S<A>) => self.key = key@move
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                _ = self.table.tryGet(self.key)
                return .Ok(())
        public func main() => ()
        """;

    private const string PremiseBoundedUnderConfined = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        contract Fixed: Equatable
            effect Equatable.equals confined
        struct S<A>
            Self is Fixed when A is Fixed
                public func equals(self: ref/Self, other: ref/Self) -> bool => true
            public init() => ()
        struct G<I, A>
            I is Iterator
            I.Item is S<A>
            I.Item is Fixed
            Self is Sink
            var table: Dictionary<S<A>, i32> = [:]
            var key: S<A>
            public init(key: S<A>) => self.key = key@move
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                _ = self.table.tryGet(self.key)
                return .Ok(())
        public func main() => ()
        """;

    private const string ItemThroughDefaultCopy = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        struct S<A>
            Self is Equatable when A is Equatable
                public func equals(self: ref/Self, other: ref/Self) -> bool => true
            public init() => ()
        group H
            public func apply<F, K>(operation: ref/F, k: ref/K) -> bool
                F is Callable<(ref/K, ref/K) -> bool>
                return operation(k, k)
            public func check<T>(k: ref/T, same: bool = H.apply(T.equals, k)) -> bool
                T is Equatable
                return same
            public func outer<A>(k: ref/S<A>)
                A is Equatable
                _ = H.check(k)
        struct Q
            Self is Sink
            var key: S<i32>
            public init(key: S<i32>) => self.key = key@move
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                H.outer(self.key@ref)
                return .Ok(())
        public func main() => ()
        """;

    private const string ItemThroughDefaultCopyEnvironment = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        group State
            public var value: i32 = 0
        struct S<A>
            Self is Equatable when A is Equatable
                public func equals(self: ref/Self, other: ref/Self) -> bool
                    _ = State.value
                    return true
            public init() => ()
        group H
            public func apply<F, K>(operation: ref/F, k: ref/K) -> bool
                F is Callable<(ref/K, ref/K) -> bool>
                return operation(k, k)
            public func check<T>(k: ref/T, same: bool = H.apply(T.equals, k)) -> bool
                T is Equatable
                return same
            public func outer<A>(k: ref/S<A>)
                A is Equatable
                _ = H.check(k)
        struct Q
            Self is Sink
            var key: S<i32>
            public init(key: S<i32>) => self.key = key@move
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                H.outer(self.key@ref)
                return .Ok(())
        public func main() => ()
        """;

    private const string ItemThroughDefaultCopyConsole = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        struct S<A>
            Self is Equatable when A is Equatable
                public func equals(self: ref/Self, other: ref/Self) -> bool
                    Console.writeLine("eq")
                    return true
            public init() => ()
        group H
            public func apply<F, K>(operation: ref/F, k: ref/K) -> bool
                F is Callable<(ref/K, ref/K) -> bool>
                return operation(k, k)
            public func check<T>(k: ref/T, same: bool = H.apply(T.equals, k)) -> bool
                T is Equatable
                return same
            public func outer<A>(k: ref/S<A>)
                A is Equatable
                _ = H.check(k)
        struct Q
            Self is Sink
            var key: S<i32>
            public init(key: S<i32>) => self.key = key@move
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                H.outer(self.key@ref)
                return .Ok(())
        public func main() => ()
        """;

    private const string ClosedItemThroughDefaultCopy = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        struct Key
            Self is Equatable
            public func equals(self: ref/Self, other: ref/Self) -> bool => true
            public init() => ()
        group H
            public func apply<F, K>(operation: ref/F, k: ref/K) -> bool
                F is Callable<(ref/K, ref/K) -> bool>
                return operation(k, k)
            public func check<T>(k: ref/T, same: bool = H.apply(T.equals, k)) -> bool
                T is Equatable
                return same
        struct Q
            Self is Sink
            var key: Key
            public init(key: Key) => self.key = key@move
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                _ = H.check(self.key@ref)
                return .Ok(())
        public func main() => ()
        """;

    private const string ClosedItemThroughDefaultCopyConsole = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        struct Key
            Self is Equatable
            public func equals(self: ref/Self, other: ref/Self) -> bool
                Console.writeLine("eq")
                return true
            public init() => ()
        group H
            public func apply<F, K>(operation: ref/F, k: ref/K) -> bool
                F is Callable<(ref/K, ref/K) -> bool>
                return operation(k, k)
            public func check<T>(k: ref/T, same: bool = H.apply(T.equals, k)) -> bool
                T is Equatable
                return same
        struct Q
            Self is Sink
            var key: Key
            public init(key: Key) => self.key = key@move
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                _ = H.check(self.key@ref)
                return .Ok(())
        public func main() => ()
        """;

    private const string HeightFromStartup = """
        contract HasItem
            associate Item
        struct D0<T>
            Self is HasItem
            associate HasItem.Item is (T, i32)
        struct D1<T>
            Self is HasItem
            associate HasItem.Item is D0<D0<T>.Item>.Item
        struct D2<T>
            Self is HasItem
            associate HasItem.Item is D1<D1<T>.Item>.Item
        struct D3<T>
            Self is HasItem
            associate HasItem.Item is D2<D2<T>.Item>.Item
        struct D4<T>
            Self is HasItem
            associate HasItem.Item is D3<D3<T>.Item>.Item
        struct D5<T>
            Self is HasItem
            associate HasItem.Item is D4<D4<T>.Item>.Item
        struct D6<T>
            Self is HasItem
            associate HasItem.Item is D5<D5<T>.Item>.Item
        struct D7<T>
            Self is HasItem
            associate HasItem.Item is D6<D6<T>.Item>.Item
        struct D8<T>
            Self is HasItem
            associate HasItem.Item is D7<D7<T>.Item>.Item
        struct D9<T>
            Self is HasItem
            associate HasItem.Item is D8<D8<T>.Item>.Item
        struct D10<T>
            Self is HasItem
            associate HasItem.Item is D9<D9<T>.Item>.Item
        struct D11<T>
            Self is HasItem
            associate HasItem.Item is D10<D10<T>.Item>.Item
        struct D12<T>
            Self is HasItem
            associate HasItem.Item is D11<D11<T>.Item>.Item
        struct Box<T>
            public let value: T
            Self is Eqv<Box<Box<Box<Box<Box<Box<Box<Box<Self>>>>>>>>>
        contract Eqv<X>
        struct W<T>
            Self is Equatable when T is Eqv<Box<Box<Box<Box<Box<Box<Box<Box<T>>>>>>>>>
                public func equals(self: ref/Self, other: ref/Self) -> bool => false
            public init() => ()
        group G
            public func h<T>() -> bool
                T is Eqv<Box<Box<Box<Box<Box<Box<Box<Box<T>>>>>>>>>
                let w = W<T>.init()
                return not (w == w)
            public func g2<T>() -> bool => G.h<Box<D11<D10<D9<D8<D7<D6<D5<D4<D3<D0<T>.Item>.Item>.Item>.Item>.Item>.Item>.Item>.Item>.Item>.Item>>()
            public func g1<T>() -> bool => G.g2<D12<T>.Item>()
        contract Sink
            func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                effect confined
        struct S
            Self is Sink
            public init() => ()
            public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
                _ = G.g1<i32>()
                return .Ok(())
        public func main()
            var s = S.init()
            _ = s.put(1)
        """;

    // SPEC 4.9.6, 8.11.3 (R4-M28): Dictionary's row composes its requirement on (K, i32) at the caller, judged by the definition's premises
    // only at the top: i32's and Key's equality are pure, Compared's reads a mutable static, and an unbounded K at the top is unknown.
    [Theory]
    [InlineData(RowTupleI32, "")]
    [InlineData(RowTupleEnvironment, "Unsupported_Kd@5:12 | IncompatibleContractImplementation_Kd@16:5")]
    [InlineData(RowTuplePureWitness, "")]
    [InlineData(RowTupleTopUnbounded, "IncompatibleContractImplementation_Kd@6:5")]
    [InlineData(RowTupleTopBounded, "")]
    [InlineData(AtomTupleI32, "")]
    [InlineData(AtomTupleEnvironment, "Unsupported_Kd@5:12 | IncompatibleContractImplementation_Kd@19:5")]
    [InlineData(AtomTuplePureWitness, "")]
    public void ARowsRequirementOnATupleWithAnAbstractPartComposesAtTheCaller(string source, string expected)
        => Assert.Equal(expected, TestDiagnostics.ErrorSites(MinimalEmissionTest.Check(source + "\n"), source));

    // SPEC 8.11.3, 7.2.3: a default's copy the body writes takes the premises' bound for its abstract leaf: unknown under `A is Equatable`,
    // confined under `A is Fixed`; a closed Tuple's copy runs i32's equality (C5's control). A tuple operator's abstract leaf is
    // ACompositeComparisonRunsItsPartsWitnesses' `H.same` and `H.loose` rows.
    [Theory]
    [InlineData(CallCopyUnbounded, "IncompatibleContractImplementation_Kd@14:5")]
    [InlineData(CallCopyBounded, "")]
    [InlineData(CallCopyClosed, "")]
    public void ACallsAbstractLeafTakesThePremisesBound(string source, string expected)
        => Assert.Equal(expected, TestDiagnostics.ErrorSites(MinimalEmissionTest.Check(source + "\n"), source));

    // SPEC 11.9.2, 16.3, 8.11.2, 7.2.3: a standard Property witness is its Field operation: a Copy or a shared borrow adds nothing, a Set
    // in a default (instantiated per call at S) destroys the replaced value, Loud's drop writing the Console and Quiet's running nothing,
    // beside A5's DefaultArgumentAccess_Kd. A Set written in a generic body calls the Property requirement, whose effect bound SPEC
    // defers (Appendix D; 8.11.3, 8.6), so under `confined` it is unknown whatever T is (X2, R4-M22).
    [Theory]
    [InlineData(PropertyCopy, "")]
    [InlineData(PropertyBorrow, "")]
    [InlineData(PropertySetLoud, "DefaultArgumentAccess_Kd@17:9 | IncompatibleContractImplementation_Kd@23:5")]
    [InlineData(PropertySetQuiet, "DefaultArgumentAccess_Kd@16:9")]
    [InlineData(PropertySetInBody, "IncompatibleContractImplementation_Kd@20:5")]
    public void AStandardPropertyWitnessIsItsFieldOperation(string source, string expected)
        => Assert.Equal(expected, TestDiagnostics.ErrorSites(MinimalEmissionTest.Check(source + "\n"), source));

    // SPEC 9.6.1, 23.1.2: a witness inherited with an owning receiver is Binding's conformance record alone, also where a confined
    // obligation's default calls it (Refused is Derived); a shared receiver through the base is valid.
    [Theory]
    [InlineData(RefusedOwner, "MissingContractImplementation_Kd@7:5")]
    [InlineData(RefusedOwnerUnderConfined, "MissingContractImplementation_Kd@8:5")]
    [InlineData(InheritedRef, "")]
    public void ARefusedWitnessIsBindingsRecordAlone(string source, string expected)
        => Assert.Equal(expected, TestDiagnostics.ErrorSites(MinimalEmissionTest.Check(source + "\n"), source));

    // SPEC 8.2.3, 8.3.2, 8.11.3: a conformance only an identity premise states has no known concrete callee, so the requirement takes the
    // premises' bound (N1'', S<A>'s conditional path not Refuted for an unbound A); where the closed world refutes it, the premise is
    // contradictory evidence, Binding's records at f and at t's Type (Dictionary's `K is Equatable`), none of A4's (N1'). Under a confined
    // obligation (P1prem, P2prem), tryGet's row on S<A> takes that bound: Equatable bounds no effect, a conflict at `Self is Sink`, and
    // Fixed's confined equals keeps put confined.
    [Theory]
    [InlineData(PremiseUnderCondition, "")]
    [InlineData(PremiseContradicted, "InvalidConstraint_Kd@3:6 | InvalidConstraint_Kd@3:22")]
    [InlineData(PremiseUnboundedUnderConfined, "IncompatibleContractImplementation_Kd@12:5")]
    [InlineData(PremiseBoundedUnderConfined, "")]
    public void APremiseOnlyConformanceTakesThePremisesBound(string source, string expected)
        => Assert.Equal(expected, TestDiagnostics.ErrorSites(MinimalEmissionTest.Check(source + "\n"), source));

    // SPEC 8.15, 8.11.3, 8.11.4, 8.11.2, 4.9.6, 7.2.3 (R7-P5b X4): the Item formed as `T.equals` in a default and copied at T = S<A>, a
    // Self with an abstract part, or at T = Key, a closed Self, is a requirement Item on a Self that is not abstract, and runs what its
    // call runs (A4's Require) under the reading definition's premises (outer's `A is Equatable` proves S<A>'s conditional conformance),
    // so its effects reach put's confined obligation: a pure equals keeps put confined (X4-V, X4-Cv, V); one that reads a mutable static
    // conflicts at `Self is Sink` beside P42's record at the static (X4-I, I), and one that writes the Console conflicts there alone
    // (X4-Ee, X4-Ce, I). Labelled from SPEC before any run; fail-before: X4-V against dev, whose Item entered the requirement's
    // declaration and was rejected at `Self is Sink`; X4-I, X4-Ee and X4-Ce against the arm that wrote the Item into Local's store
    // (`Item`), which no solve reads. Mutant: A4's Item arm removed rejects X4-V again.
    [Theory]
    [InlineData(ItemThroughDefaultCopy, "")]
    [InlineData(ItemThroughDefaultCopyEnvironment, "Unsupported_Kd@5:12 | IncompatibleContractImplementation_Kd@23:5")]
    [InlineData(ItemThroughDefaultCopyConsole, "IncompatibleContractImplementation_Kd@21:5")]
    [InlineData(ClosedItemThroughDefaultCopy, "")]
    [InlineData(ClosedItemThroughDefaultCopyConsole, "IncompatibleContractImplementation_Kd@18:5")]
    public void ARequirementItemOnASelfThatIsNotAbstractRunsItsWitness(string source, string expected)
        => Assert.Equal(expected, TestDiagnostics.ErrorSites(MinimalEmissionTest.Check(source + "\n"), source));

    // IMPL 21.3.2, A.4, SPEC 23.1.2: a premise the collector forms past the height in h, reached from startup through g1 and g2, is the
    // collector's one TypeHeightLimit_Kd at `w == w`; A4 adds nothing at `Self is Sink`. The row also pins TyCtxt.Witness's premise
    // scope: read under the library root's premises, A4 adds an IncompatibleContractImplementation_Kd (R7-P5a; the mutant
    // `EffectPremises(null)` fails it).
    [Fact]
    public void AFormationPastTheHeightIsTheCollectorsRecordAlone()
        => Assert.Equal("TypeHeightLimit_Kd@54:21", TestDiagnostics.ErrorSites(MinimalEmissionTest.Check(HeightFromStartup + "\n"), HeightFromStartup));
}
