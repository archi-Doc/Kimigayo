// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 15.6.4, 15.6.7, 16.3.3, 8.11.3 (R7-P5a, DECISIONS R4-M92): A5's effect rule over what a requirement on a Tuple with an abstract
// part runs, Dictionary's row composed at the caller: a call under a live Loan of a mutable static (S) and a static's destruction (M6),
// each with its labelled records as a build checks the program. P42's independent Unsupported_Kd stays at each mutable static's
// declaration (R5-M82); a write to the loaned static is ComparisonLoanConflict_Kd with its EffectWrite declaration (after "<-").
// Fail-before (dev 79934ae85): each was E7's located Unsupported_Kd at the call (S) or the static's assignment (M6) beside P42's.
public sealed partial class MirBorrowsTest
{
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
    [InlineData(StaticLoanI32, "Unsupported_Kd 2:12")]
    [InlineData(StaticLoanWritesLoaned, "Unsupported_Kd 2:12|ComparisonLoanConflict_Kd 15:36 <- 2:12")]
    [InlineData(StaticLoanWritesAnother, "Unsupported_Kd 2:12|Unsupported_Kd 3:12")]
    [InlineData(DestroyStaticPure, "Unsupported_Kd 11:12")]
    [InlineData(DestroyStaticWritesLoaned, "Unsupported_Kd 17:12|ComparisonLoanConflict_Kd 19:9 <- 17:12")]
    [InlineData(DestroyStaticWritesAnother, "Unsupported_Kd 16:12|Unsupported_Kd 17:12")]
    public void AStaticLoanMeetsWhatARequirementOnATupleRuns(string source, string expected)
        => Assert.Equal(expected.Split('|'), StaticRecords(source));

    // SPEC 23.1 (R7-P5a's probes): MirEffectsTest.Records with each record's related declarations (an EffectWrite's static) after it.
    private static string[] StaticRecords(string source)
        => [.. TestDiagnostics.Of(MinimalEmissionTest.Check(source + "\n"), "Hello.kimi").Where(static x => x.Severity == DiagnosticSeverity.Error).Select(x =>
            $"{x.Code} {MirEffectsTest.At(source, x.Span.Start)}" +
            string.Concat((x.Related ?? []).Where(static r => r.Role == "declaration").Select(r => $" <- {MirEffectsTest.At(source, r.Span!.Value.Start)}")))];
}
