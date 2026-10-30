// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 23.1.2 (DECISIONS BU-69): a fit record with no Unbound end rests on a failed contract only when that contract could prove it: its
// bound clauses, or one repair of a failed clause whose free end ranges over the Origins visible at that clause, read wherever the premise
// extraction reads the contract (R1, a Type's premises substituted). The forms are bu85's design and review probes, each with a repair or
// valid twin where it has one: the first rows report the twin's record (independent), the rest stay derived (dependent).
public partial class OriginRelationDiagnosticTest
{
    // A function returning a pair binder's value whose Origin slot exists only in the binder's borrow cases.
    private const string PairReturn = "func g<s/T>(a: ref/i32, b: ref/i32, c: ref/i32, x: s/T during b) -> s/T during a\n    s is owner or ref\n" +
        "    T is Copy\n";

    // The same with an input whose pair layer makes `d` outlive `e` only in the borrow cases (the re-review's form).
    private const string PairCondition = "func g<s/T>(a: ref/i32, d: ref/i32, e: ref/i32, y: ref/(s/T during d) during e, x: s/T during d) -> s/T during a\n" +
        "    s is owner or ref\n    T is Copy\n";

    /// <summary>Gets each form with a failed contract and its Origin records, with its repair or valid twin (or null) and the twin's.</summary>
    public static TheoryData<string, string[], string?, string[]> FailedContracts => new()
    {
        // a-base-method (twin a-base-method-valid)
        {
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\nopen struct B<T>\n    public init() => ()\n" +
            "struct D {a} : B<(ref/C{x} during a, i32)>\n    origin x.s == zz\n    public init() : base() => ()\n" +
            "    public func m(self: ref/Self, l: ref/i32) -> ref/i32 during a => l\n",
            ["UnprovenOriginRelation_Kd l"],
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\nopen struct B<T>\n    public init() => ()\n" +
            "struct D {a} : B<(ref/C{x} during a, i32)>\n    origin x.s == a\n    public init() : base() => ()\n" +
            "    public func m(self: ref/Self, l: ref/i32) -> ref/i32 during a => l\n",
            ["UnprovenOriginRelation_Kd l"]
        },
        // a-base-method-dep (twin a-base-method-dep-r1)
        {
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\nopen struct B<T>\n    public init() => ()\n" +
            "struct D {a, b} : B<(ref/C{x} during a, i32)>\n    origin x.s == zz\n" +
            "    public init() : base() => ()\n" +
            "    public func m(self: ref/Self, l: ref/i32 during b) -> ref/i32 during a => l\n",
            ["UnprovenOriginRelation_Kd l"],
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\nopen struct B<T>\n    public init() => ()\n" +
            "struct D {a, b} : B<(ref/C{x} during a, i32)>\n    origin x.s == b\n" +
            "    public init() : base() => ()\n" +
            "    public func m(self: ref/Self, l: ref/i32 during b) -> ref/i32 during a => l\n",
            ["UnprovenOriginRelation_Kd ref/C{x} during a", "UnprovenOriginRelation_Kd l"]
        },
        // a-fn-clause (twin a-fn-clause-valid)
        {
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\n" +
            "func g(a: ref/i32, b: ref/i32, l: ref/i32) -> ref/i32 during a\n    origin b == zz\n    return l\n",
            ["UnprovenOriginRelation_Kd l"],
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\n" +
            "func g(a: ref/i32, b: ref/i32, l: ref/i32) -> ref/i32 during a\n    origin b == a\n    return l\n",
            ["UnprovenOriginRelation_Kd l"]
        },
        // a-fn-out-indep (twin a-fn-out-indep-valid)
        {
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\n" +
            "func g(a: ref/i32, b: ref/i32, c: ref/i32) -> ref/i32 during a\n    origin zz outlives c\n" +
            "    return b\n",
            ["UnprovenOriginRelation_Kd b"],
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\n" +
            "func g(a: ref/i32, b: ref/i32, c: ref/i32) -> ref/i32 during a\n    origin a outlives c\n" +
            "    return b\n",
            ["UnprovenOriginRelation_Kd b"]
        },
        // r-dir-left (twin r-dir-left-valid)
        {
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\n" +
            "func g(a: ref/i32, l: ref/i32) -> ref/i32 during a\n    origin a outlives zz\n    return l\n",
            ["UnprovenOriginRelation_Kd l"],
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\n" +
            "func g(a: ref/i32, l: ref/i32) -> ref/i32 during a\n    origin a outlives static\n    return l\n",
            ["UnprovenOriginRelation_Kd l"]
        },
        // r-m-unnameable (twin r-m-unnameable-valid)
        {
            "struct D {a}\n    origin zz outlives a\n    public let r: ref/i32 during a\n" +
            "    public init(r: ref/i32 during a) => self.r = r\n" +
            "    public func m(self: ref/Self, l: ref/i32) -> ref/i32 during a => l\n",
            ["UnprovenOriginRelation_Kd l"],
            "struct D {a}\n    origin static outlives a\n    public let r: ref/i32 during a\n" +
            "    public init(r: ref/i32 during a) => self.r = r\n" +
            "    public func m(self: ref/Self, l: ref/i32) -> ref/i32 during a => l\n",
            ["UnprovenOriginRelation_Kd l"]
        },
        // r-m-whole (twin r-m-unnameable-valid)
        {
            "struct D {a}\n    origin zz outlives yy\n    public let r: ref/i32 during a\n" +
            "    public init(r: ref/i32 during a) => self.r = r\n" +
            "    public func m(self: ref/Self, l: ref/i32) -> ref/i32 during a => l\n",
            ["UnprovenOriginRelation_Kd l"],
            "struct D {a}\n    origin static outlives a\n    public let r: ref/i32 during a\n" +
            "    public init(r: ref/i32 during a) => self.r = r\n" +
            "    public func m(self: ref/Self, l: ref/i32) -> ref/i32 during a => l\n",
            ["UnprovenOriginRelation_Kd l"]
        },
        // r-nest-unnameable (twin r-nest-unnameable-valid)
        {
            "func g(a: ref/i32) -> ref/i32 during a\n    origin zz outlives a\n" +
            "    func inner(l: ref/i32) -> ref/i32 during a => l\n    return a\n",
            ["UnprovenOriginRelation_Kd l"],
            "func g(a: ref/i32) -> ref/i32 during a\n    origin static outlives a\n" +
            "    func inner(l: ref/i32) -> ref/i32 during a => l\n    return a\n",
            ["UnprovenOriginRelation_Kd l"]
        },
        // n2-indep (twin n2-indep-noclause)
        {
            "func g(a: ref/i32) -> ref/i32 during a\n    origin zz outlives a\n" +
            "    func inner(l: ref/i32, k: ref/i32) -> ref/i32 during a\n        origin yy outlives k\n" +
            "        return l\n    return a\n",
            ["UnprovenOriginRelation_Kd l"],
            "func g(a: ref/i32) -> ref/i32 during a\n    func inner(l: ref/i32, k: ref/i32) -> ref/i32 during a\n" +
            "        return l\n    return a\n",
            ["UnprovenOriginRelation_Kd l"]
        },
        // m2-indep
        {
            "struct D {a}\n    origin zz outlives a\n    public let r: ref/i32 during a\n" +
            "    public init(r: ref/i32 during a) => self.r = r\n" +
            "    public func m(self: ref/Self, l: ref/i32, k: ref/i32) -> ref/i32 during a\n" +
            "        origin yy outlives k\n        return l\n",
            ["UnprovenOriginRelation_Kd l"],
            null,
            []
        },
        // g-guessed-method
        {
            "struct D {a}\n    origin a = static\n    public let r: ref/i32 during a\n" +
            "    public init(r: ref/i32 during a) => self.r = r\n" +
            "    public func m(self: ref/Self, l: ref/i32) -> ref/i32 during a => l\n",
            ["UnprovenOriginRelation_Kd l"],
            null,
            []
        },
        // c-store-failed (twin c-store-static)
        {
            "func use(p: uniq/i32, q: uniq/(uniq/i32 during p)) -> i32\n    origin p outlives zz\n" +
            "    var c = func [p@move, q@move] () -> i32\n        q@follow = p\n        return 0\n    return 0\n",
            ["UnprovenOriginRelation_Kd p"],
            "func use(p: uniq/i32, q: uniq/(uniq/i32 during p)) -> i32\n    origin p outlives static\n" +
            "    var c = func [p@move, q@move] () -> i32\n        q@follow = p\n        return 0\n    return 0\n",
            ["UnprovenOriginRelation_Kd p"]
        },
        // r-fn-other (twin r-fn-other-valid)
        {
            "struct D {a, b}\n    origin b outlives a\n    origin a == zz\n    public let ra: ref/i32 during a\n" +
            "    public let rb: ref/i32 during b\n    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n" +
            "        self.ra = ra\n        self.rb = rb\n" +
            "func h(other: ref/D{y}) -> ref/i32 during y.a => other.rb\n",
            [],
            "struct D {a, b}\n    origin b outlives a\n    public let ra: ref/i32 during a\n" +
            "    public let rb: ref/i32 during b\n    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n" +
            "        self.ra = ra\n        self.rb = rb\n" +
            "func h(other: ref/D{y}) -> ref/i32 during y.a => other.rb\n",
            []
        },
        // t2-same (twin t2-same-noclause)
        {
            "struct D {a, b}\n    origin b outlives a\n    origin a == zz\n    public let ra: ref/i32 during a\n" +
            "    public let rb: ref/i32 during b\n    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n" +
            "        self.ra = ra\n        self.rb = rb\n" +
            "func h(o: ref/D{y}, p: ref/D{w}) -> ref/i32 during y.a => o.rb\n",
            [],
            "struct D {a, b}\n    origin b outlives a\n    public let ra: ref/i32 during a\n" +
            "    public let rb: ref/i32 during b\n    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n" +
            "        self.ra = ra\n        self.rb = rb\n" +
            "func h(o: ref/D{y}, p: ref/D{w}) -> ref/i32 during y.a => o.rb\n",
            []
        },
        // t2-static (twin t2-static-r)
        {
            "struct D {a, b}\n    origin a outlives zz\n    public let ra: ref/i32 during a\n" +
            "    public let rb: ref/i32 during b\n    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n" +
            "        self.ra = ra\n        self.rb = rb\n" +
            "func h(o: ref/D{y}, p: ref/D{w}) -> ref/i32 during w.b => o.ra\n",
            [],
            "struct D {a, b}\n    origin a outlives static\n    public let ra: ref/i32 during a\n" +
            "    public let rb: ref/i32 during b\n    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n" +
            "        self.ra = ra\n        self.rb = rb\n" +
            "func h(o: ref/D{y}, p: ref/D{w}) -> ref/i32 during w.b => o.ra\n",
            []
        },
        // as-fail (twin as-valid)
        {
            "contract K\n    associate Item(a, b)\n        origin b outlives a\n        origin a == zz\n" +
            "func use<T>(v: T.Item(p, q), w: ref/i32 during q) -> ref/i32 during p\n    T is K\n    return w\n",
            [],
            "contract K\n    associate Item(a, b)\n        origin b outlives a\n" +
            "func use<T>(v: T.Item(p, q), w: ref/i32 during q) -> ref/i32 during p\n    T is K\n    return w\n",
            []
        },
        // a-fn-dep (twin a-fn-dep-valid)
        {
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\n" +
            "func g(a: ref/i32, b: ref/i32) -> ref/i32 during a\n    origin b == zz\n    return b\n",
            [],
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\n" +
            "func g(a: ref/i32, b: ref/i32) -> ref/i32 during a\n    origin b == a\n    return b\n",
            []
        },
        // a-fn-rejected (twin a-fn-rejected-valid)
        {
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\n" +
            "func g(a: ref/i32, b: ref/i32, c: ref/i32) -> ref/i32 during a\n    origin b outlives a\n" +
            "    origin c == zz\n    return b\n",
            [],
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\n" +
            "func g(a: ref/i32, b: ref/i32, c: ref/i32) -> ref/i32 during a\n    origin b outlives a\n" +
            "    origin c == a\n    return b\n",
            []
        },
        // a-fn-chain
        {
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\n" +
            "func g(a: ref/i32, b: ref/i32, c: ref/i32) -> ref/i32 during a\n    origin b outlives c\n" +
            "    origin c outlives zz\n    return b\n",
            [],
            null,
            []
        },
        // r-m-other (twin r-m-other-valid)
        {
            "struct D {a, b}\n    origin b outlives a\n    origin a == zz\n    public let ra: ref/i32 during a\n" +
            "    public let rb: ref/i32 during b\n    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n" +
            "        self.ra = ra\n        self.rb = rb\n" +
            "    public func m(self: ref/Self, other: ref/D{y}) -> ref/i32 during y.a => other.rb\n",
            [],
            "struct D {a, b}\n    origin b outlives a\n    origin a == a\n    public let ra: ref/i32 during a\n" +
            "    public let rb: ref/i32 during b\n    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n" +
            "        self.ra = ra\n        self.rb = rb\n" +
            "    public func m(self: ref/Self, other: ref/D{y}) -> ref/i32 during y.a => other.rb\n",
            []
        },
        // r-m-other-repair (twin r-m-other-repair-valid)
        {
            "struct D {a, b}\n    origin b outlives zz\n    public let ra: ref/i32 during a\n" +
            "    public let rb: ref/i32 during b\n    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n" +
            "        self.ra = ra\n        self.rb = rb\n" +
            "    public func m(self: ref/Self, other: ref/D{y}) -> ref/i32 during y.a => other.rb\n",
            [],
            "struct D {a, b}\n    origin b outlives a\n    public let ra: ref/i32 during a\n" +
            "    public let rb: ref/i32 during b\n    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n" +
            "        self.ra = ra\n        self.rb = rb\n" +
            "    public func m(self: ref/Self, other: ref/D{y}) -> ref/i32 during y.a => other.rb\n",
            []
        },
        // r-m-self
        {
            "struct D {a, b}\n    origin b outlives a\n    origin a == zz\n    public let ra: ref/i32 during a\n" +
            "    public let rb: ref/i32 during b\n    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n" +
            "        self.ra = ra\n        self.rb = rb\n" +
            "    public func m(self: ref/Self) -> ref/i32 during a => self.rb\n",
            [],
            null,
            []
        },
        // g-guessed-fn
        {
            "struct C {s}\n    public let r: ref/i32 during s\n" +
            "    public init(r: ref/i32 during s) => self.r = r\n" +
            "func g(a: ref/i32, b: ref/i32, l: ref/i32) -> ref/i32 during a\n    origin b = a\n    return l\n",
            [],
            null,
            []
        },
        // g-guessed-method-dep
        {
            "struct D {a, b}\n    origin b = a\n    public let r: ref/i32 during a\n" +
            "    public init(r: ref/i32 during a) => self.r = r\n" +
            "    public func m(self: ref/Self, l: ref/i32 during b) -> ref/i32 during a => l\n",
            [],
            null,
            []
        },
        // n2-dep-inner (twin n2-dep-inner-r)
        {
            "func g(a: ref/i32) -> ref/i32 during a\n    origin zz outlives a\n" +
            "    func inner(l: ref/i32, k: ref/i32) -> ref/i32 during a\n        origin l outlives yy\n" +
            "        return l\n    return a\n",
            [],
            "func g(a: ref/i32) -> ref/i32 during a\n    origin static outlives a\n" +
            "    func inner(l: ref/i32, k: ref/i32) -> ref/i32 during a\n        origin l outlives a\n" +
            "        return l\n    return a\n",
            []
        },
        // m2-dep (twin m2-dep-r)
        {
            "struct D {a}\n    origin zz outlives a\n    public let r: ref/i32 during a\n" +
            "    public init(r: ref/i32 during a) => self.r = r\n" +
            "    public func m(self: ref/Self, l: ref/i32, k: ref/i32) -> ref/i32 during a\n" +
            "        origin yy outlives a\n        return l\n",
            [],
            "struct D {a}\n    public let r: ref/i32 during a\n" +
            "    public init(r: ref/i32 during a) => self.r = r\n" +
            "    public func m(self: ref/Self, l: ref/i32, k: ref/i32) -> ref/i32 during a\n" +
            "        origin l outlives a\n        return l\n",
            []
        },
        // r-nest-dep (twin r-nest-dep-valid)
        {
            "func g(a: ref/i32, b: ref/i32) -> ref/i32 during a\n    origin b == zz\n" +
            "    func inner(l: ref/i32 during b) -> ref/i32 during a => l\n    return a\n",
            [],
            "func g(a: ref/i32, b: ref/i32) -> ref/i32 during a\n    origin b == a\n" +
            "    func inner(l: ref/i32 during b) -> ref/i32 during a => l\n    return a\n",
            []
        },
        // t2-cross (twin t2-cross-static)
        {
            "struct D {a, b}\n    origin b outlives a\n    origin a == zz\n    public let ra: ref/i32 during a\n" +
            "    public let rb: ref/i32 during b\n    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n" +
            "        self.ra = ra\n        self.rb = rb\n" +
            "func h(o: ref/D{y}, p: ref/D{w}) -> ref/i32 during w.a => o.rb\n",
            [],
            "struct D {a, b}\n    origin b outlives a\n    origin a == static\n" +
            "    public let ra: ref/i32 during a\n    public let rb: ref/i32 during b\n" +
            "    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n        self.ra = ra\n" +
            "        self.rb = rb\nfunc h(o: ref/D{y}, p: ref/D{w}) -> ref/i32 during w.a => o.rb\n",
            []
        },
        // t2-cross-repair (twin t2-cross-repair-r)
        {
            "struct D {a, b}\n    origin zz outlives a\n    public let ra: ref/i32 during a\n" +
            "    public let rb: ref/i32 during b\n    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n" +
            "        self.ra = ra\n        self.rb = rb\n" +
            "func h(o: ref/D{y}, p: ref/D{w}) -> ref/i32 during w.a => o.rb\n",
            ["UnprovenOriginRelation_Kd o.rb"],
            "struct D {a, b}\n    origin b outlives a\n    public let ra: ref/i32 during a\n" +
            "    public let rb: ref/i32 during b\n    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n" +
            "        self.ra = ra\n        self.rb = rb\n" +
            "func h(o: ref/D{y}, p: ref/D{w}) -> ref/i32 during w.a => o.rb\n",
            ["UnprovenOriginRelation_Kd o.rb"]
        },

        // A position that exists only in the pair binder's borrow cases (the record's Condition): `b == zz`'s repair `b == a` proves it,
        // `c == zz`'s does not.
        { PairReturn + "    origin b == zz\n    return x\n", [], PairReturn + "    origin b == a\n    return x\n", [] },
        {
            PairReturn + "    origin c == zz\n    return x\n", [Unproven + "x"], PairReturn + "    origin c == a\n    return x\n",
            [Unproven + "x"]
        },

        // The record's Condition decides: `d >= e` holds only in the borrow cases (y's pair layer), where the repair `e == a` proves the
        // record, so it is derived; in every case it would not be.
        { PairCondition + "    origin e == zz\n    return x\n", [], PairCondition + "    origin e == a\n    return x\n", [] },
    };

    [Theory]
    [MemberData(nameof(FailedContracts))]
    public void AFailedContractExplainsOnlyWhatItsRepairCouldProve(string source, string[] records, string? twin, string[] twinRecords)
    {
        Assert.Equal(records, OriginRecords(source + Main));
        if (twin is not null)
        {
            Assert.Equal(twinRecords, OriginRecords(twin + Main));
        }
    }

    // A warm rebind decides a failed contract's dependents from reused storage: a dependent record (r-m-other) and an independent one
    // (a-fn-clause).
    [Theory]
    [Trait("Purpose", "Allocation")]
    [InlineData("struct D {a, b}\n    origin b outlives a\n    origin a == zz\n    public let ra: ref/i32 during a\n" +
        "    public let rb: ref/i32 during b\n    public init(ra: ref/i32 during a, rb: ref/i32 during b)\n        self.ra = ra\n        self.rb = rb\n" +
        "    public func m(self: ref/Self, other: ref/D{y}) -> ref/i32 during y.a => other.rb\n")]
    [InlineData("func g(a: ref/i32, b: ref/i32, l: ref/i32) -> ref/i32 during a\n    origin b == zz\n    return l\n")]
    public void AWarmRebindDecidesAFailedContractsDependentsWithoutAllocating(string source)
    {
        var c = MirBuildTest.Compile(source + Main);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }
}
