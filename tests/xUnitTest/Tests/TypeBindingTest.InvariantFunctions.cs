// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// PLAN G900 (SPEC 3.8, 10.7, 15.3.5): a Function Type at an invariant position (the target of `uniq/` or `raw/`, an invariant slot)
// relates both ways. A Callable comparison is oriented: it instantiates only the implementation's per-call inputs. So `(ref/i32) -> i32`
// (PC, a per-call input) is a subtype of `(ref/i32 during b) -> i32` (FX(b), a fixed one) and of the `static` one (ST), never the
// reverse, and the two are not equal. The rows are the unit's coverage table by id (one per holder, entry, nesting and Origin kind),
// each invalid row beside a valid twin, through the check entry: an invalid row has one UnprovenOriginContract_Kd at the fitted
// value, beside the records the declared Type itself gives and every independent plain position's (SPEC 23.1.2: the rn3 rows).
public partial class TypeBindingTest
{
    public static TheoryData<string, string, string> InvariantFunctionRows => new()
    {
        // a1i (invalid, SPEC 15.3.5, 10.7, 3.8): R<T> contravariant; argument; arg FX(b), param PC (rq2a, the D row verbatim).
        {
            "a1i",
            """
            struct R<T>
                public let call: (T) -> i32
                public init(call: (T) -> i32) => self.call = call@move

            func g(q: ref/i32, r: R<uniq/((ref/i32) -> i32) during q>) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> i32
                origin q outlives p
                return g(q, r0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move"
        },
        // a1v (valid, SPEC 15.3.5, 3.8): R<T> contravariant; argument; arg FX(b), param FX(c) with c bound to b.
        {
            "a1v",
            """
            struct R<T>
                public let call: (T) -> i32
                public init(call: (T) -> i32) => self.call = call@move

            func g(c: ref/i32, q: ref/i32, r: R<uniq/((ref/i32 during c) -> i32) during q>) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> i32
                origin q outlives p
                origin b outlives q
                return g(b, q, r0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // a2i (invalid, SPEC 15.3.5, 10.7, 3.8): R<T> contravariant; argument; arg PC, param FX(c) (the other orientation).
        {
            "a2i",
            """
            struct R<T>
                public let call: (T) -> i32
                public init(call: (T) -> i32) => self.call = call@move

            func g(c: ref/i32, q: ref/i32, r: R<uniq/((ref/i32 during c) -> i32) during q>) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> i32
                origin q outlives p
                origin b outlives q
                return g(b, q, r0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move"
        },
        // a3i (invalid, SPEC 15.3.5, 10.7, 3.8): R<T> contravariant; let; value PC, declared FX(b) (rq1w).
        {
            "a3i",
            """
            struct R<T>
                public let call: (T) -> i32
                public init(call: (T) -> i32) => self.call = call@move

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> i32
                origin q outlives p
                origin b outlives q
                let r: R<uniq/((ref/i32 during b) -> i32) during q> = r0@move
                return 0

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move"
        },
        // a4i (invalid, SPEC 15.3.5, 10.7, 3.8): R<T> contravariant; let; value FX(b), declared PC (rq2 with q outlives p).
        {
            "a4i",
            """
            struct R<T>
                public let call: (T) -> i32
                public init(call: (T) -> i32) => self.call = call@move

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> i32
                origin q outlives p
                let r: R<uniq/((ref/i32) -> i32) during q> = r0@move
                return 0

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move|Unsupported_Kd uniq/((ref/i32) -> i32) during q"
        },
        // a4v (valid, SPEC 15.3.5, 3.8): R<T> contravariant; let; value FX(b), declared FX(b), outer Origins differ.
        {
            "a4v",
            """
            struct R<T>
                public let call: (T) -> i32
                public init(call: (T) -> i32) => self.call = call@move

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> i32
                origin q outlives p
                origin b outlives q
                let r: R<uniq/((ref/i32 during b) -> i32) during q> = r0@move
                return 0

            public func main() => ()
            """,
            string.Empty
        },
        // a5i (invalid, SPEC 15.3.5, 10.7, 3.8): R<T> contravariant; return; value FX(b), result PC (rq2r).
        {
            "a5i",
            """
            struct R<T>
                public let call: (T) -> i32
                public init(call: (T) -> i32) => self.call = call@move

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> R<uniq/((ref/i32) -> i32) during q>
                origin q outlives p
                return r0@move

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move|UnprovenOriginRelation_Kd uniq/((ref/i32) -> i32) during q"
        },
        // a5j (invalid, SPEC 15.3.5, 10.7, 3.8): R<T> contravariant; return; value PC, result FX(b) (rq1r).
        {
            "a5j",
            """
            struct R<T>
                public let call: (T) -> i32
                public init(call: (T) -> i32) => self.call = call@move

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> R<uniq/((ref/i32 during b) -> i32) during q>
                origin q outlives p
                origin b outlives q
                return r0@move

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move"
        },
        // a5v (valid, SPEC 15.3.5, 3.8): R<T> contravariant; return; value FX(b), result FX(b).
        {
            "a5v",
            """
            struct R<T>
                public let call: (T) -> i32
                public init(call: (T) -> i32) => self.call = call@move

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> R<uniq/((ref/i32 during b) -> i32) during q>
                origin q outlives p
                origin b outlives q
                return r0@move

            public func main() => ()
            """,
            string.Empty
        },
        // a6i (invalid, SPEC 15.3.5, 10.7, 3.8): R<T> contravariant; assignment to a var; value PC, declared FX(b) (rq1v).
        {
            "a6i",
            """
            struct R<T>
                public let call: (T) -> i32
                public init(call: (T) -> i32) => self.call = call@move

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>, r1: R<uniq/((ref/i32 during b) -> i32) during q>) -> i32
                origin q outlives p
                origin b outlives q
                var r: R<uniq/((ref/i32 during b) -> i32) during q> = r1@move
                r = r0@move
                return 0

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move"
        },
        // b1i (invalid, SPEC 15.3.5, 10.7, 3.8): C<T> covariant; argument; arg PC, param FX(c).
        {
            "b1i",
            """
            struct C<T>
                public let item: T
                public init(item: T) => self.item = item@move

            func g(c: ref/i32, q: ref/i32, r: C<uniq/((ref/i32 during c) -> i32) during q>) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: C<uniq/((ref/i32) -> i32) during p>) -> i32
                origin p outlives q
                origin b outlives q
                return g(b, q, r0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move"
        },
        // b1v (valid, SPEC 15.3.5, 3.8): C<T> covariant; argument; arg FX(b), param FX(c).
        {
            "b1v",
            """
            struct C<T>
                public let item: T
                public init(item: T) => self.item = item@move

            func g(c: ref/i32, q: ref/i32, r: C<uniq/((ref/i32 during c) -> i32) during q>) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: C<uniq/((ref/i32 during b) -> i32) during p>) -> i32
                origin p outlives q
                return g(b, q, r0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // b2i (invalid, SPEC 15.3.5, 10.7, 3.8): C<T> covariant; argument; arg FX(b), param PC.
        {
            "b2i",
            """
            struct C<T>
                public let item: T
                public init(item: T) => self.item = item@move

            func g(q: ref/i32, r: C<uniq/((ref/i32) -> i32) during q>) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: C<uniq/((ref/i32 during b) -> i32) during p>) -> i32
                origin p outlives q
                return g(q, r0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move"
        },
        // c1i (invalid, SPEC 15.3.5, 10.7, 3.8): uniq layer; argument; arg PC, param FX(c).
        {
            "c1i",
            """
            func g(c: ref/i32, u: uniq/((ref/i32 during c) -> i32)) -> i32 => 0

            func f(b: ref/i32, u0: uniq/((ref/i32) -> i32)) -> i32
                return g(b, u0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd u0@move"
        },
        // c1v (valid, SPEC 15.3.5, 3.8): uniq layer; argument; arg FX(b), param FX(c).
        {
            "c1v",
            """
            func g(c: ref/i32, u: uniq/((ref/i32 during c) -> i32)) -> i32 => 0

            func f(b: ref/i32, u0: uniq/((ref/i32 during b) -> i32)) -> i32
                return g(b, u0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // c2i (invalid, SPEC 15.3.5, 10.7, 3.8): uniq layer; argument; arg FX(b), param PC.
        {
            "c2i",
            """
            func g(u: uniq/((ref/i32) -> i32)) -> i32 => 0

            func f(b: ref/i32, u0: uniq/((ref/i32 during b) -> i32)) -> i32
                return g(u0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd u0@move"
        },
        // c2v (valid, SPEC 15.3.5, 3.8): uniq layer; argument; arg PC, param PC.
        {
            "c2v",
            """
            func g(u: uniq/((ref/i32) -> i32)) -> i32 => 0

            func f(u0: uniq/((ref/i32) -> i32)) -> i32
                return g(u0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // c3i (invalid, SPEC 15.3.5, 10.7, 3.8): Tuple holder; argument; arg (uniq/PC, i32), param (uniq/FX(c), i32).
        {
            "c3i",
            """
            func g(c: ref/i32, q: ref/i32, t: (uniq/((ref/i32 during c) -> i32) during q, i32)) -> i32 => 0

            func f(b: ref/i32, q: ref/i32, t0: (uniq/((ref/i32) -> i32) during q, i32)) -> i32
                origin b outlives q
                return g(b, q, t0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd t0@move"
        },
        // c3v (valid, SPEC 15.3.5, 3.8): Tuple holder; argument; arg (uniq/FX(b), i32), param (uniq/FX(c), i32).
        {
            "c3v",
            """
            func g(c: ref/i32, q: ref/i32, t: (uniq/((ref/i32 during c) -> i32) during q, i32)) -> i32 => 0

            func f(b: ref/i32, q: ref/i32, t0: (uniq/((ref/i32 during b) -> i32) during q, i32)) -> i32
                return g(b, q, t0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // d1i (invalid, SPEC 15.3.5, 10.7, 3.8): uniq layer; arg PC, param ST.
        {
            "d1i",
            """
            func g(u: uniq/((ref/i32 during static) -> i32)) -> i32 => 0

            func f(u0: uniq/((ref/i32) -> i32)) -> i32
                return g(u0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd u0@move"
        },
        // d2i (invalid, SPEC 15.3.5, 10.7, 3.8): uniq layer; arg ST, param PC.
        {
            "d2i",
            """
            func g(u: uniq/((ref/i32) -> i32)) -> i32 => 0

            func f(u0: uniq/((ref/i32 during static) -> i32)) -> i32
                return g(u0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd u0@move"
        },
        // d3i (invalid, SPEC 15.3.5, 3.8): uniq layer; arg FX(b), param ST.
        {
            "d3i",
            """
            func g(u: uniq/((ref/i32 during static) -> i32)) -> i32 => 0

            func f(b: ref/i32, u0: uniq/((ref/i32 during b) -> i32)) -> i32
                return g(u0@move)

            public func main() => ()
            """,
            "Unsupported_Kd u0@move"
        },
        // d3v (valid, SPEC 15.3.5, 3.8): uniq layer; arg ST, param ST.
        {
            "d3v",
            """
            func g(u: uniq/((ref/i32 during static) -> i32)) -> i32 => 0

            func f(u0: uniq/((ref/i32 during static) -> i32)) -> i32
                return g(u0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // d4v (valid, SPEC 15.3.5, 3.8): uniq layer; let; FX(a) to FX(b), a == b by two clauses.
        {
            "d4v",
            """
            func f(a: ref/i32, b: ref/i32, u0: uniq/((ref/i32 during a) -> i32)) -> i32
                origin a outlives b
                origin b outlives a
                let u: uniq/((ref/i32 during b) -> i32) = u0@move
                return 0

            public func main() => ()
            """,
            string.Empty
        },
        // d4i (invalid, SPEC 15.3.5, 3.8): uniq layer; let; FX(a) to FX(b), only a outlives b.
        {
            "d4i",
            """
            func f(a: ref/i32, b: ref/i32, u0: uniq/((ref/i32 during a) -> i32)) -> i32
                origin a outlives b
                let u: uniq/((ref/i32 during b) -> i32) = u0@move
                return 0

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd u0@move|UnprovenOriginRelation_Kd uniq/((ref/i32 during b) -> i32)"
        },
        // d4j (invalid, SPEC 15.3.5, 3.8): uniq layer; let; FX(a) to FX(b), only b outlives a.
        {
            "d4j",
            """
            func f(a: ref/i32, b: ref/i32, u0: uniq/((ref/i32 during a) -> i32)) -> i32
                origin b outlives a
                let u: uniq/((ref/i32 during b) -> i32) = u0@move
                return 0

            public func main() => ()
            """,
            "Unsupported_Kd let u: uniq/((ref/i32 during b) -> i32) = u0@move"
        },
        // e1i (invalid, SPEC 15.3.5, 10.7, 3.8): uniq layer; nested Function input; arg (PC) -> i32, param (FX(c)) -> i32.
        {
            "e1i",
            """
            func g(c: ref/i32, u: uniq/((((ref/i32 during c) -> i32)) -> i32)) -> i32 => 0

            func f(b: ref/i32, u0: uniq/((((ref/i32) -> i32)) -> i32)) -> i32
                return g(b, u0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd u0@move"
        },
        // e1j (invalid, SPEC 15.3.5, 10.7, 3.8): uniq layer; nested Function input; arg (FX(b)) -> i32, param (PC) -> i32.
        {
            "e1j",
            """
            func g(u: uniq/((((ref/i32) -> i32)) -> i32)) -> i32 => 0

            func f(b: ref/i32, u0: uniq/((((ref/i32 during b) -> i32)) -> i32)) -> i32
                return g(u0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd u0@move"
        },
        // e1v (valid, SPEC 15.3.5, 3.8): uniq layer; nested Function input; arg (FX(b)) -> i32, param (FX(c)) -> i32.
        {
            "e1v",
            """
            func g(c: ref/i32, u: uniq/((((ref/i32 during c) -> i32)) -> i32)) -> i32 => 0

            func f(b: ref/i32, u0: uniq/((((ref/i32 during b) -> i32)) -> i32)) -> i32
                return g(b, u0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // e2i (invalid, SPEC 15.3.5, 10.7, 3.8): uniq layer; nested Function result; arg () -> PC, param () -> FX(c).
        {
            "e2i",
            """
            func g(c: ref/i32, u: uniq/(() -> ((ref/i32 during c) -> i32))) -> i32 => 0

            func f(b: ref/i32, u0: uniq/(() -> ((ref/i32) -> i32))) -> i32
                return g(b, u0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd u0@move"
        },
        // e2j (invalid, SPEC 15.3.5, 10.7, 3.8): uniq layer; nested Function result; arg () -> FX(b), param () -> PC.
        {
            "e2j",
            """
            func g(u: uniq/(() -> ((ref/i32) -> i32))) -> i32 => 0

            func f(b: ref/i32, u0: uniq/(() -> ((ref/i32 during b) -> i32))) -> i32
                return g(u0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd u0@move"
        },
        // e2v (valid, SPEC 15.3.5, 3.8): uniq layer; nested Function result; arg () -> FX(b), param () -> FX(c).
        {
            "e2v",
            """
            func g(c: ref/i32, u: uniq/(() -> ((ref/i32 during c) -> i32))) -> i32 => 0

            func f(b: ref/i32, u0: uniq/(() -> ((ref/i32 during b) -> i32))) -> i32
                return g(b, u0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // e3i (invalid, SPEC 15.3.5, 10.7, 3.8): Function holder (a contravariant input, no generic); arg (uniq/FX(b) during p) -> i32, param (uniq/PC during p) -> i32.
        {
            "e3i",
            """
            func g(p: ref/i32, h: (uniq/((ref/i32) -> i32) during p) -> i32) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, h0: (uniq/((ref/i32 during b) -> i32) during p) -> i32) -> i32
                return g(p, h0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd h0@move"
        },
        // e3j (invalid, SPEC 15.3.5, 10.7, 3.8): Function holder; arg (uniq/PC during p) -> i32, param (uniq/FX(c) during p) -> i32.
        {
            "e3j",
            """
            func g(c: ref/i32, p: ref/i32, h: (uniq/((ref/i32 during c) -> i32) during p) -> i32) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, h0: (uniq/((ref/i32) -> i32) during p) -> i32) -> i32
                origin b outlives p
                return g(b, p, h0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd h0@move"
        },
        // e3v (valid, SPEC 15.3.5, 3.8): Function holder; arg (uniq/FX(b) during p) -> i32, param (uniq/FX(c) during p) -> i32.
        {
            "e3v",
            """
            func g(c: ref/i32, p: ref/i32, h: (uniq/((ref/i32 during c) -> i32) during p) -> i32) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, h0: (uniq/((ref/i32 during b) -> i32) during p) -> i32) -> i32
                return g(b, p, h0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // f1i (invalid, SPEC 10.7, 15.3.5): named function value; impl input uniq/PC, required input uniq/FX(b).
        {
            "f1i",
            """
            func h(u: uniq/((ref/i32) -> i32)) -> i32 => 0

            func f(b: ref/i32) -> i32
                let k: (uniq/((ref/i32 during b) -> i32)) -> i32 = h
                return 0

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd h"
        },
        // f1v (valid, SPEC 10.7, 15.3.5): named function value; impl inputs (ref/i32 during b, uniq/FX(b)), required (ref/i32 during c, uniq/FX(c)).
        {
            "f1v",
            """
            func h(b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) -> i32 => 0

            func f(c: ref/i32) -> i32
                let k: (ref/i32 during c, uniq/((ref/i32 during c) -> i32)) -> i32 = h
                return 0

            public func main() => ()
            """,
            string.Empty
        },
        // f2j (invalid, SPEC 10.7, 15.3.5): Closure; header (b: ref/i32, u: uniq/FX(b)), required (ref/i32, uniq/PC).
        {
            "f2j",
            """
            func f() -> i32
                let k: (ref/i32, uniq/((ref/i32) -> i32)) -> i32 = func (b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) => 0
                return 0

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd func (b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) => 0"
        },
        // g1i (invalid, SPEC 15.3.5, 10.7, 3.8): raw layer; argument; arg raw/PC, param raw/FX(c).
        {
            "g1i",
            """
            func g(c: ref/i32, r: raw/((ref/i32 during c) -> i32)) -> i32 => 0

            func f(b: ref/i32, r0: raw/((ref/i32) -> i32)) -> i32
                return g(b, r0)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0"
        },
        // g1v (valid, SPEC 15.3.5, 3.8): raw layer; argument; arg raw/FX(b), param raw/FX(c).
        {
            "g1v",
            """
            func g(c: ref/i32, r: raw/((ref/i32 during c) -> i32)) -> i32 => 0

            func f(b: ref/i32, r0: raw/((ref/i32 during b) -> i32)) -> i32
                return g(b, r0)

            public func main() => ()
            """,
            string.Empty
        },
        // h1i (invalid, SPEC 15.3.5, 10.7, 3.8): V<T> invariant slot (T read and returned); argument; arg V<PC>, param V<FX(c)>.
        {
            "h1i",
            """
            struct V<T>
                public let call: (T) -> T
                public init(call: (T) -> T) => self.call = call@move

            func g(c: ref/i32, v: V<(ref/i32 during c) -> i32>) -> i32 => 0

            func f(b: ref/i32, v0: V<(ref/i32) -> i32>) -> i32
                return g(b, v0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd v0@move"
        },
        // h1j (invalid, SPEC 15.3.5, 10.7, 3.8): V<T> invariant slot; argument; arg V<FX(b)>, param V<PC>.
        {
            "h1j",
            """
            struct V<T>
                public let call: (T) -> T
                public init(call: (T) -> T) => self.call = call@move

            func g(v: V<(ref/i32) -> i32>) -> i32 => 0

            func f(b: ref/i32, v0: V<(ref/i32 during b) -> i32>) -> i32
                return g(v0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd v0@move"
        },
        // h1v (valid, SPEC 15.3.5, 3.8): V<T> invariant slot; argument; arg V<FX(b)>, param V<FX(c)>.
        {
            "h1v",
            """
            struct V<T>
                public let call: (T) -> T
                public init(call: (T) -> T) => self.call = call@move

            func g(c: ref/i32, v: V<(ref/i32 during c) -> i32>) -> i32 => 0

            func f(b: ref/i32, v0: V<(ref/i32 during b) -> i32>) -> i32
                return g(b, v0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // i1i (invalid, SPEC 15.3.5, 10.7, 3.8): uniq borrow of a local; arg uniq/PC during a local region, param uniq/FX(c), c a local's region.
        {
            "i1i",
            """
            func g(c: ref/i32, u: uniq/((ref/i32 during c) -> i32)) -> i32 => 0

            func keep(x: ref/i32) -> i32 => 0

            func f() -> i32
                var k: (ref/i32) -> i32 = keep
                let local = 1
                return g(local@ref, k@uniq)

            public func main() => ()
            """,
            "Unsupported_Kd k@uniq"
        },
        // i1v (valid, SPEC 15.3.5, 3.8): uniq borrow of a local; arg uniq/PC during a local region, param uniq/PC.
        {
            "i1v",
            """
            func g(u: uniq/((ref/i32) -> i32)) -> i32 => 0

            func keep(x: ref/i32) -> i32 => 0

            func f() -> i32
                var k: (ref/i32) -> i32 = keep
                return g(k@uniq)

            public func main() => ()
            """,
            string.Empty
        },
        // j1i (invalid, SPEC 10.7, 15.3.7, 15.3.5): contract member; required input uniq/PC, implementation's uniq/ST.
        {
            "j1i",
            """
            contract K
                func m(u: uniq/((ref/i32) -> i32)) -> i32

            struct S
                Self is K
                public func m(u: uniq/((ref/i32 during static) -> i32)) -> i32 => 0

            public func main() => ()
            """,
            "IncompatibleContractImplementation_Kd Self is K"
        },
        // j1j (invalid, SPEC 10.7, 15.3.7, 15.3.5): contract member; required input uniq/ST, implementation's uniq/PC.
        {
            "j1j",
            """
            contract K
                func m(u: uniq/((ref/i32 during static) -> i32)) -> i32

            struct S
                Self is K
                public func m(u: uniq/((ref/i32) -> i32)) -> i32 => 0

            public func main() => ()
            """,
            "IncompatibleContractImplementation_Kd Self is K"
        },
        // j1v (valid, SPEC 10.7, 15.3.7, 15.3.5): contract member; required input uniq/PC, implementation's uniq/PC.
        {
            "j1v",
            """
            contract K
                func m(u: uniq/((ref/i32) -> i32)) -> i32

            struct S
                Self is K
                public func m(u: uniq/((ref/i32) -> i32)) -> i32 => 0

            public func main() => ()
            """,
            string.Empty
        },
        // n1f (invalid, SPEC 15.3.5, 10.7, 3.8): R<uniq/R<X> during p>; let; value X = PC, declared X = FX(b) (review-i2-r2's n1f).
        {
            "n1f",
            """
            struct R<T>
                public let call: (T) -> i32
                public init(call: (T) -> i32) => self.call = call@move

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/R<(ref/i32) -> i32> during p>) -> i32
                origin q outlives p
                origin b outlives q
                let r: R<uniq/R<(ref/i32 during b) -> i32> during q> = r0@move
                return 0

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move"
        },
        // n1fv (valid, SPEC 15.3.5, 3.8): R<uniq/R<X> during p>; let; value and declared X = FX(b).
        {
            "n1fv",
            """
            struct R<T>
                public let call: (T) -> i32
                public init(call: (T) -> i32) => self.call = call@move

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/R<(ref/i32 during b) -> i32> during p>) -> i32
                origin q outlives p
                origin b outlives q
                let r: R<uniq/R<(ref/i32 during b) -> i32> during q> = r0@move
                return 0

            public func main() => ()
            """,
            string.Empty
        },
        // f1k (invalid, SPEC 10.7, 15.3.5): named function value; f1j with the per-call input at another position: impl (i32, ref/i32 during b, uniq/FX(b)), required (i32, ref/i32, uniq/PC).
        {
            "f1k",
            """
            func h(x: i32, b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) -> i32 => 0

            func f() -> i32
                let k: (i32, ref/i32, uniq/((ref/i32) -> i32)) -> i32 = h
                return 0

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd h"
        },
        // rn3k (invalid twice, SPEC 15.3.5, 10.7, 23.1.2): R2<T, U> contravariant; let; slot 1 PC against FX(b), slot 2 c == d (review B1).
        {
            "rn3k",
            """
            struct R2<T, U>
                public let call: (T, U) -> i32
                public init(call: (T, U) -> i32) => self.call = call@move

            func f(b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/((ref/i32) -> i32) during p, uniq/(ref/i32 during c) during p>) -> i32
                origin q outlives p
                origin b outlives q
                origin d outlives q
                let r: R2<uniq/((ref/i32 during b) -> i32) during q, uniq/(ref/i32 during d) during q> = r0@move
                return 0

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move|UnprovenOriginRelation_Kd let r: R2<uniq/((ref/i32 during b) -> i32) during q, uniq/(ref/i32 during d) during q> = r0@move"
        },
        // rn3s (invalid twice, SPEC 15.3.5, 10.7, 23.1.2): rn3k with the slots exchanged: the position first.
        {
            "rn3s",
            """
            struct R2<T, U>
                public let call: (T, U) -> i32
                public init(call: (T, U) -> i32) => self.call = call@move

            func f(b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/(ref/i32 during c) during p, uniq/((ref/i32) -> i32) during p>) -> i32
                origin q outlives p
                origin b outlives q
                origin d outlives q
                let r: R2<uniq/(ref/i32 during d) during q, uniq/((ref/i32 during b) -> i32) during q> = r0@move
                return 0

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move|UnprovenOriginRelation_Kd let r: R2<uniq/(ref/i32 during d) during q, uniq/((ref/i32 during b) -> i32) during q> = r0@move"
        },
        // rn3n (invalid twice, SPEC 15.3.5, 10.7, 23.1.2): C2<T, U> covariant; let; slot 1 PC against FX(b), slot 2 c == d (review B1).
        {
            "rn3n",
            """
            struct C2<T, U>
                public let a: T
                public let b: U
                public init(a: T, b: U)
                    self.a = a@move
                    self.b = b@move

            func f(b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: C2<uniq/((ref/i32) -> i32) during p, uniq/(ref/i32 during c) during p>) -> i32
                origin p outlives q
                origin b outlives q
                origin d outlives q
                let r: C2<uniq/((ref/i32 during b) -> i32) during q, uniq/(ref/i32 during d) during q> = r0@move
                return 0

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move|UnprovenOriginRelation_Kd let r: C2<uniq/((ref/i32 during b) -> i32) during q, uniq/(ref/i32 during d) during q> = r0@move"
        },
        // n3f (invalid twice, SPEC 15.3.5, 10.7, 23.1.2): rn3k without d outlives q (G492-a (i)'s n3f): also the declared Type's own record.
        {
            "n3f",
            """
            struct R2<T, U>
                public let call: (T, U) -> i32
                public init(call: (T, U) -> i32) => self.call = call@move

            // label: invalid (first slot: Function Types under uniq/ differ, per-call vs fixed; second slot: c == d not proven).
            func f(b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/((ref/i32) -> i32) during p, uniq/(ref/i32 during c) during p>) -> i32
                origin q outlives p
                origin b outlives q
                let r: R2<uniq/((ref/i32 during b) -> i32) during q, uniq/(ref/i32 during d) during q> = r0@move
                return 0
            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move|UnprovenOriginRelation_Kd let r: R2<uniq/((ref/i32 during b) -> i32) during q, uniq/(ref/i32 during d) during q> = r0@move|UnprovenOriginRelation_Kd uniq/(ref/i32 during d) during q"
        },
        // rn3m (invalid twice, SPEC 15.3.5, 10.7, 23.1.2): rn3k's other orientation (slot 1 FX(b) against PC); the declared Type's limit beside them.
        {
            "rn3m",
            """
            struct C2<T, U>
                public let a: T
                public let b: U
                public init(a: T, b: U)
                    self.a = a@move
                    self.b = b@move

            func f(b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: C2<uniq/((ref/i32 during b) -> i32) during p, uniq/(ref/i32 during c) during p>) -> i32
                origin p outlives q
                origin d outlives q
                let r: C2<uniq/((ref/i32) -> i32) during q, uniq/(ref/i32 during d) during q> = r0@move
                return 0

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move|UnprovenOriginRelation_Kd let r: C2<uniq/((ref/i32) -> i32) during q, uniq/(ref/i32 during d) during q> = r0@move|Unsupported_Kd uniq/((ref/i32) -> i32) during q"
        },
    };

    [Theory]
    [MemberData(nameof(InvariantFunctionRows))]
    public void AnInvariantFunctionPartRelatesBothWays(string row, string source, string records)
    {
        var found = DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {source.Substring(x.Span!.Value.Start, x.Span!.Value.Length)}").Order(StringComparer.Ordinal).ToArray();
        Assert.True(records.Split('|', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).SequenceEqual(found), $"{row}: {string.Join(" | ", found)}");
    }

    // The relation itself, without a use (Binding.FitsType): equal only when each is a subtype of the other.
    [Fact]
    public void AFunctionTargetOfAnExclusiveLayerIsEqualOnlyBothWays()
    {
        var c = Parse("func f(b: ref/i32, x: uniq/((ref/i32) -> i32) during b, y: uniq/((ref/i32 during b) -> i32) during b, z: uniq/((ref/i32 during static) -> i32) during b, s: ref/((ref/i32) -> i32) during b, t: ref/((ref/i32 during b) -> i32) during b) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var types = Nodes(c).OfType<FunctionKoto>().Single(x => x.Name == "f").Parameters.Select(x => x.Type.TypeOf()!).ToArray();
        Assert.True(Binding.FitsType(types[4], types[5]));  // A shared layer is covariant: PC <: FX(b).
        Assert.False(Binding.FitsType(types[5], types[4])); // The required per-call input is rigid.
        Assert.False(Binding.FitsType(types[1], types[2])); // An exclusive layer: PC and FX(b) are not equal, either way.
        Assert.False(Binding.FitsType(types[2], types[1]));
        Assert.False(Binding.FitsType(types[1], types[3]));
        Assert.False(Binding.FitsType(types[3], types[1]));
        Assert.True(Binding.FitsType(types[1], types[1]));
    }
}
