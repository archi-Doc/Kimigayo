// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// SPEC 15.6.4, 15.6.7, 16.3.3, 8.11.3 (R7-P5a, DECISIONS R4-M92): A5's effect rule over what a requirement on a Tuple with an abstract
// part runs, Dictionary's row composed at the caller: a call under a live Loan of a mutable static (S) and a static's destruction (M6),
// each with its labelled records as a build checks the program (TestDiagnostics.ErrorSites). P42's independent Unsupported_Kd stays at each mutable static's
// declaration (R5-M82); a write to the loaned static is ComparisonLoanConflict_Kd with its EffectWrite declaration (after "<-").
// Fail-before (dev 79934ae85): each was E7's located Unsupported_Kd at the call (S) or the static's assignment (M6) beside P42's.
// SPEC 8.6.2, 9.6.1, 8.15, 15.6.3, 15.6.4 (R7-P5b): a Requirement callee on a closed Self makes its witness's call: a selected
// operator's receiver borrows the inherited witness's base subobject (T1), an outer call retains an argument through a relation its
// result outlives (T5), and a default copy's receiver, borrowed past its parameter's shared dereference, carries that parameter's
// Loans (T6). Labelled from SPEC before any run.
public sealed partial class MirBorrowsTest
{
    private const string BaseReceiverDisjoint = """
        open struct Base
            Self is Addable<i32>
            associate Addable<i32>.Output is i32
            public var tally: i32 = 0
            public init() => ()
            public func added(self: ref/Self, right: ref/i32) -> i32 => 1
        struct Leaf : Base
            public var own: i32 = 0
            public init() : base() => ()
        func bump(slot: uniq/i32) -> i32 => 0
        public func main()
            var leaf = Leaf.init()
            _ = leaf + bump(leaf.own@uniq)
        """;

    private const string BaseReceiverOverlapping = """
        open struct Base
            Self is Addable<i32>
            associate Addable<i32>.Output is i32
            public var tally: i32 = 0
            public init() => ()
            public func added(self: ref/Self, right: ref/i32) -> i32 => 1
        struct Leaf : Base
            public var own: i32 = 0
            public init() : base() => ()
        func bump(slot: uniq/i32) -> i32 => 0
        public func main()
            var leaf = Leaf.init()
            _ = leaf + bump(leaf.tally@uniq)
        """;

    private const string UnaryBaseReceiverDisjoint = """
        open struct Base
            Self is Negatable
            associate Negatable.Output is i32
            public var tally: i32 = 0
            public init() => ()
            public func negated(self: ref/Self) -> i32 => 0
        struct Leaf : Base
            public var own: i32 = 0
            public init() : base() => ()
        public func main()
            var leaf = Leaf.init()
            let u = leaf.own@uniq
            _ = -leaf
            u@follow = 1
        """;

    private const string UnaryBaseReceiverOverlapping = """
        open struct Base
            Self is Negatable
            associate Negatable.Output is i32
            public var tally: i32 = 0
            public init() => ()
            public func negated(self: ref/Self) -> i32 => 0
        struct Leaf : Base
            public var own: i32 = 0
            public init() : base() => ()
        public func main()
            var leaf = Leaf.init()
            let u = leaf.tally@uniq
            _ = -leaf
            u@follow = 1
        """;

    private const string CopyOfStaticWitness = """
        contract View
            func view(self: ref/Self) -> ref/i32
        struct Leaf
            Self is View
            public var own: i32 = 0
            public init() => ()
            public func view(self: ref/Self) -> ref/i32 during static => $abort("no")
        group Helpers
            public func evaluate<T>(sample: ref/T during s, marker: ref/i32 during m = sample.view()) -> ref/i32 during m
                T is View
                origin s outlives m
                return marker
        public func main()
            var leaf = Leaf.init()
            let r = Helpers.evaluate(leaf@ref)
            leaf.own = 1
            _ = r@follow
        """;

    private const string CopyWitnessRetainsReceiver = """
        contract View
            func view(self: ref/Self) -> ref/i32
        group Config
            public let limit: i32 = 100
        struct Leaf
            Self is View
            public var own: i32 = 0
            public init() => ()
            public func view(self: ref/Self) -> ref/i32 => self.own@ref
        group Helpers
            public func evaluate<T>(sample: ref/T during s, marker: ref/i32 during m = sample.view()) -> ref/i32 during m
                T is View
                origin s outlives m
                return marker
        public func main()
            var leaf = Leaf.init()
            let r = Helpers.evaluate(leaf@ref)
            leaf.own = 1
            _ = r@follow
        """;

    private const string CopyLoanEndsWithTheCall = """
        contract View
            func view(self: ref/Self) -> ref/i32
        struct Leaf
            Self is View
            public var own: i32 = 0
            public init() => ()
            public func view(self: ref/Self) -> ref/i32 => self.own@ref
        group Helpers
            public func evaluate<T>(sample: ref/T during s, marker: ref/i32 during m = sample.view()) -> i32
                T is View
                origin s outlives m
                return marker@follow
        public func main()
            var leaf = Leaf.init()
            let r = Helpers.evaluate(leaf@ref)
            leaf.own = 1
            _ = r
        """;

    private const string CopyBaseReceiverDisjoint = """
        contract Peek
            func peek(self: ref/Self) -> ref/i32
        open struct Base
            public var tally: i32 = 0
            public init() => ()
            public func peek(self: ref/Self) -> ref/i32 => self.tally@ref
        struct Leaf : Base
            Self is Peek
            public var own: i32 = 0
            public init() : base() => ()
        group Helpers
            public func evaluate<T>(sample: ref/T during s, marker: ref/i32 during m = sample.peek()) -> ref/i32 during m
                T is Peek
                origin s outlives m
                return marker
        public func main()
            var leaf = Leaf.init()
            let r = Helpers.evaluate(leaf@ref)
            leaf.own = 1
            _ = r@follow
        """;

    private const string CopyBaseReceiverOverlapping = """
        contract Peek
            func peek(self: ref/Self) -> ref/i32
        open struct Base
            public var tally: i32 = 0
            public init() => ()
            public func peek(self: ref/Self) -> ref/i32 => self.tally@ref
        struct Leaf : Base
            Self is Peek
            public var own: i32 = 0
            public init() : base() => ()
        group Helpers
            public func evaluate<T>(sample: ref/T during s, marker: ref/i32 during m = sample.peek()) -> ref/i32 during m
                T is Peek
                origin s outlives m
                return marker
        public func main()
            var leaf = Leaf.init()
            let r = Helpers.evaluate(leaf@ref)
            leaf.tally = 1
            _ = r@follow
        """;

    private const string StaticLoanI32 = """
        group State
            public var olog: Option<i32> = .Some(0)
        group H
            public func put<K>(table: uniq/Dictionary<(K, i32), i32>, key: ref/(K, i32)) -> i32
                K is Equatable
                _ = table.remove(key)
                return 0
            public func use(table: uniq/Dictionary<(i32, i32), i32>, key: ref/(i32, i32)) -> i32
                match State.olog
                    .Some(let w) => return H.put(table, key) + w@follow
                    .None => return 0
        public func main() => ()
        """;

    private const string StaticLoanWritesLoaned = """
        group State
            public var olog: Option<i32> = .Some(0)
        struct Bump
            Self is Equatable
            public func equals(self: ref/Self, other: ref/Self) -> bool
                State.olog = .None
                return true
        group H
            public func put<K>(table: uniq/Dictionary<(K, i32), i32>, key: ref/(K, i32)) -> i32
                K is Equatable
                _ = table.remove(key)
                return 0
            public func use(table: uniq/Dictionary<(Bump, i32), i32>, key: ref/(Bump, i32)) -> i32
                match State.olog
                    .Some(let w) => return H.put(table, key) + w@follow
                    .None => return 0
        public func main() => ()
        """;

    private const string StaticLoanWritesAnother = """
        group State
            public var olog: Option<i32> = .Some(0)
            public var hits: i32 = 0
        struct Tick
            Self is Equatable
            public func equals(self: ref/Self, other: ref/Self) -> bool
                State.hits += 1
                return true
        group H
            public func put<K>(table: uniq/Dictionary<(K, i32), i32>, key: ref/(K, i32)) -> i32
                K is Equatable
                _ = table.remove(key)
                return 0
            public func use(table: uniq/Dictionary<(Tick, i32), i32>, key: ref/(Tick, i32)) -> i32
                match State.olog
                    .Some(let w) => return H.put(table, key) + w@follow
                    .None => return 0
        public func main() => ()
        """;

    private const string DestroyStaticPure = """
        group H
            public func put<K>(table: uniq/Dictionary<(K, i32), i32>, key: ref/(K, i32))
                K is Equatable
                _ = table.remove(key)
        struct Holder
            public var table: Dictionary<(i32, i32), i32> = [:]
            drop
                let k = (1, 1)
                H.put(self.table@uniq, k)
        group State
            public var held: Holder = Holder.init()
            public func reset()
                State.held = Holder.init()
        public func main() => ()
        """;

    private const string DestroyStaticWritesLoaned = """
        group H
            public func put<K>(table: uniq/Dictionary<(K, i32), i32>, key: ref/(K, i32))
                K is Equatable
                _ = table.remove(key)
        struct Bump
            Self is Equatable
            public func equals(self: ref/Self, other: ref/Self) -> bool
                State.held.n = 1
                return true
        struct Holder
            public var n: i32 = 0
            public var table: Dictionary<(Bump, i32), i32> = [:]
            drop
                let k = (Bump.init(), 1)
                H.put(self.table@uniq, k)
        group State
            public var held: Holder = Holder.init()
            public func reset()
                State.held = Holder.init()
        public func main() => ()
        """;

    private const string DestroyStaticWritesAnother = """
        group H
            public func put<K>(table: uniq/Dictionary<(K, i32), i32>, key: ref/(K, i32))
                K is Equatable
                _ = table.remove(key)
        struct Tick
            Self is Equatable
            public func equals(self: ref/Self, other: ref/Self) -> bool
                State.hits += 1
                return true
        struct Holder
            public var table: Dictionary<(Tick, i32), i32> = [:]
            drop
                let k = (Tick.init(), 1)
                H.put(self.table@uniq, k)
        group State
            public var held: Holder = Holder.init()
            public var hits: i32 = 0
            public func reset()
                State.held = Holder.init()
        public func main() => ()
        """;

    // SPEC 15.6.4, 15.6.7, 16.3.3: the call's effects are i32's, Bump's or Tick's equality composed into (K, i32): pure meets nothing, a
    // write to the loaned olog or to Holder.n meets the Loan, and a write to another static (hits) meets none.
    [Theory]
    [InlineData(StaticLoanI32, "Unsupported_Kd@2:12")]
    [InlineData(StaticLoanWritesLoaned, "Unsupported_Kd@2:12 | ComparisonLoanConflict_Kd@15:36 <- 2:12")]
    [InlineData(StaticLoanWritesAnother, "Unsupported_Kd@2:12 | Unsupported_Kd@3:12")]
    [InlineData(DestroyStaticPure, "Unsupported_Kd@11:12")]
    [InlineData(DestroyStaticWritesLoaned, "Unsupported_Kd@17:12 | ComparisonLoanConflict_Kd@19:9 <- 17:12")]
    [InlineData(DestroyStaticWritesAnother, "Unsupported_Kd@16:12 | Unsupported_Kd@17:12")]
    public void AStaticLoanMeetsWhatARequirementOnATupleRuns(string source, string expected)
        => Assert.Equal(expected, TestDiagnostics.ErrorSites(MinimalEmissionTest.Check(source + "\n"), source));

    // SPEC 8.6.2, 9.6.1, 15.6.2: a requirement on a closed Self whose witness is inherited borrows the base subobject as its receiver,
    // before the arguments, at a selected operator on a direct Place: a borrow of Leaf's own field is disjoint (T1-V: V), one of Base's
    // tally overlaps it (T1-I at `leaf.tally@uniq`: I); so does a unary operator of an unbound Contract against a live exclusive Loan
    // (T1u-V on `own`: V; T1u-I on `tally`: I at `-leaf`). SPEC 15.6.3: a default copy's receiver is borrowed past `sample`'s shared
    // dereference, so it forms no Loan of its own and carries sample's whole-Leaf Loan, which peek's result and r carry: a write of own
    // (T6-V) and one of tally (T6-I) both conflict (I). The copy's projection itself is pinned by the validator's acceptance of
    // MirEffectsTest's InheritedRef.
    [Theory]
    [InlineData(BaseReceiverDisjoint, "")]
    [InlineData(BaseReceiverOverlapping, "CallActivationConflict_Kd@13:16")]
    [InlineData(UnaryBaseReceiverDisjoint, "")]
    [InlineData(UnaryBaseReceiverOverlapping, "ComparisonLoanConflict_Kd@13:10")]
    [InlineData(CopyBaseReceiverDisjoint, "ComparisonLoanConflict_Kd@19:5")]
    [InlineData(CopyBaseReceiverOverlapping, "ComparisonLoanConflict_Kd@19:5")]
    public void AnInheritedWitnessReceiverBorrowsItsBase(string source, string expected)
        => Assert.Equal(expected, TestDiagnostics.ErrorSites(MinimalEmissionTest.Check(source + "\n"), source));

    // SPEC 15.6.4, 3.8 (T5): origin s outlives m, which the default needs to fit marker, lets evaluate's result during m hold a borrow
    // formed through sample, so the result retains sample and r carries leaf@ref's Loan whatever the default copy runs: a write of own
    // conflicts with a during-static witness (T5-V2: I) and with a receiver-tied one (T5-I: I, the review's C5); an i32 result retains
    // neither (T5-V3: V). No fail-before: dev answers the same, since F3 has no public observation through a default copy (t5-label.md).
    // Labelled from SPEC before any run.
    [Theory]
    [InlineData(CopyOfStaticWitness, "ComparisonLoanConflict_Kd@16:5")]
    [InlineData(CopyWitnessRetainsReceiver, "ComparisonLoanConflict_Kd@18:5")]
    [InlineData(CopyLoanEndsWithTheCall, "")]
    public void ACallRetainsAnArgumentItsResultOutlives(string source, string expected)
        => Assert.Equal(expected, TestDiagnostics.ErrorSites(MinimalEmissionTest.Check(source + "\n"), source));
}
