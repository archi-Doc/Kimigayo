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
            "UnprovenOriginContract_Kd r0@move"
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
            "UnprovenOriginContract_Kd r0@move"
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
        // f2j (invalid, SPEC 10.7, 15.3.5): Closure; header (b: ref/i32, u: uniq/FX(b)), required (ref/i32, uniq/PC). u's `b` names the
        // literal's per-call Origin in its own input (SPEC 15.3.4, 8.10; PLAN G972, the owner's O-403 (b)): its record beside the fit's.
        {
            "f2j",
            """
            func f() -> i32
                let k: (ref/i32, uniq/((ref/i32) -> i32)) -> i32 = func (b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) => 0
                return 0

            public func main() => ()
            """,
            "InvalidOriginBinding_Kd b|UnprovenOriginContract_Kd func (b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) => 0"
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
            "UnprovenOriginContract_Kd r0@move|UnprovenOriginRelation_Kd r0@move"
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
            "UnprovenOriginContract_Kd r0@move|UnprovenOriginRelation_Kd r0@move"
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
            "UnprovenOriginContract_Kd r0@move|UnprovenOriginRelation_Kd r0@move"
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
            "UnprovenOriginContract_Kd r0@move|UnprovenOriginRelation_Kd r0@move|UnprovenOriginRelation_Kd uniq/(ref/i32 during d) during q"
        },
        // rn3m (invalid twice, SPEC 15.3.5, 10.7, 23.1.2): rn3k's other orientation (slot 1 FX(b) against PC); the declared Type relates
        // no per-call Origin of slot 1 (SPEC 15.2.3).
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
            "UnprovenOriginContract_Kd r0@move|UnprovenOriginRelation_Kd r0@move"
        },
        // rn3g (invalid twice, review): rn3k at an argument.
        {
            "rn3g",
            """
            struct R2<T, U>
                public let call: (T, U) -> i32
                public init(call: (T, U) -> i32) => self.call = call@move

            func g(e: ref/i32, d: ref/i32, q: ref/i32, r: R2<uniq/((ref/i32 during e) -> i32) during q, uniq/(ref/i32 during d) during q>) -> i32 => 0

            func f(b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/((ref/i32) -> i32) during p, uniq/(ref/i32 during c) during p>) -> i32
                origin q outlives p
                origin b outlives q
                origin d outlives q
                return g(b, d, q, r0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move|UnprovenOriginRelation_Kd d"
        },
        // rn3h (invalid twice, review): rn3k at an assignment.
        {
            "rn3h",
            """
            struct R2<T, U>
                public let call: (T, U) -> i32
                public init(call: (T, U) -> i32) => self.call = call@move

            func f(b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: R2<uniq/((ref/i32) -> i32) during p, uniq/(ref/i32 during c) during p>, r1: R2<uniq/((ref/i32 during b) -> i32) during q, uniq/(ref/i32 during d) during q>) -> i32
                origin q outlives p
                origin b outlives q
                origin d outlives q
                var r: R2<uniq/((ref/i32 during b) -> i32) during q, uniq/(ref/i32 during d) during q> = r1@move
                r = r0@move
                return 0

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move|UnprovenOriginRelation_Kd r0@move"
        },
        // ro1i (invalid, review): Option<uniq/F during p>, an enum Case payload; PC against FX(c).
        {
            "ro1i",
            """
            func g(c: ref/i32, q: ref/i32, r: Option<uniq/((ref/i32 during c) -> i32) during q>) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: Option<uniq/((ref/i32) -> i32) during p>) -> i32
                origin p outlives q
                origin b outlives q
                return g(b, q, r0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move"
        },
        // ro1v (valid, review): ro1i's twin, FX(b) against FX(c).
        {
            "ro1v",
            """
            func g(c: ref/i32, q: ref/i32, r: Option<uniq/((ref/i32 during c) -> i32) during q>) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: Option<uniq/((ref/i32 during b) -> i32) during p>) -> i32
                origin p outlives q
                return g(b, q, r0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // rk1i (invalid, review): a user enum E<T> Case payload.
        {
            "rk1i",
            """
            enum E<T>
                Some(T)
                Empty

            func g(c: ref/i32, q: ref/i32, r: E<uniq/((ref/i32 during c) -> i32) during q>) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: E<uniq/((ref/i32) -> i32) during p>) -> i32
                origin p outlives q
                origin b outlives q
                return g(b, q, r0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move"
        },
        // rk1v (valid, review): rk1i's twin.
        {
            "rk1v",
            """
            enum E<T>
                Some(T)
                Empty

            func g(c: ref/i32, q: ref/i32, r: E<uniq/((ref/i32 during c) -> i32) during q>) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: E<uniq/((ref/i32 during b) -> i32) during p>) -> i32
                origin p outlives q
                return g(b, q, r0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // ry1i (invalid, review): Array<uniq/F during p> element.
        {
            "ry1i",
            """
            func g(c: ref/i32, q: ref/i32, r: Array<uniq/((ref/i32 during c) -> i32) during q>) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: Array<uniq/((ref/i32) -> i32) during p>) -> i32
                origin p outlives q
                origin b outlives q
                return g(b, q, r0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move"
        },
        // ry1v (valid, review): ry1i's twin.
        {
            "ry1v",
            """
            func g(c: ref/i32, q: ref/i32, r: Array<uniq/((ref/i32 during c) -> i32) during q>) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: Array<uniq/((ref/i32 during b) -> i32) during p>) -> i32
                origin p outlives q
                return g(b, q, r0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // rw1i (invalid, review): struct W<T> with a var Field, under uniq/.
        {
            "rw1i",
            """
            struct W<T>
                public var item: T
                public init(item: T) => self.item = item@move

            func g(c: ref/i32, q: ref/i32, r: W<uniq/((ref/i32 during c) -> i32) during q>) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: W<uniq/((ref/i32) -> i32) during p>) -> i32
                origin p outlives q
                origin b outlives q
                return g(b, q, r0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r0@move"
        },
        // rw1v (valid, review): rw1i's twin.
        {
            "rw1v",
            """
            struct W<T>
                public var item: T
                public init(item: T) => self.item = item@move

            func g(c: ref/i32, q: ref/i32, r: W<uniq/((ref/i32 during c) -> i32) during q>) -> i32 => 0

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: W<uniq/((ref/i32 during b) -> i32) during p>) -> i32
                origin p outlives q
                return g(b, q, r0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // rw2v (valid, review): W<PC> to W<FX(c)>: a var Field is storage, covariant, no invariant layer.
        {
            "rw2v",
            """
            struct W<T>
                public var item: T
                public init(item: T) => self.item = item@move

            func g(c: ref/i32, r: W<(ref/i32 during c) -> i32>) -> i32 => 0

            func f(b: ref/i32, r0: W<(ref/i32) -> i32>) -> i32
                return g(b, r0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // ru1i (invalid, review): UniqSlice<T>, a library invariant slot.
        {
            "ru1i",
            """
            func g(c: ref/i32, s: UniqSlice<(ref/i32 during c) -> i32>) -> i32 => 0

            func f(b: ref/i32, s0: UniqSlice<(ref/i32) -> i32>) -> i32
                return g(b, s0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd s0@move"
        },
        // ru1v (valid, review): ru1i's twin.
        {
            "ru1v",
            """
            func g(c: ref/i32, s: UniqSlice<(ref/i32 during c) -> i32>) -> i32 => 0

            func f(b: ref/i32, s0: UniqSlice<(ref/i32 during b) -> i32>) -> i32
                return g(b, s0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // rr1i (invalid, review): a result over the per-call input, (ref/i32) -> ref/i32 against a fixed one.
        {
            "rr1i",
            """
            func g(c: ref/i32, u: uniq/((ref/i32 during c) -> ref/i32 during c)) -> i32 => 0

            func f(b: ref/i32, u0: uniq/((ref/i32) -> ref/i32)) -> i32
                return g(b, u0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd u0@move"
        },
        // rr1j (invalid, review): rr1i's other orientation.
        {
            "rr1j",
            """
            func g(u: uniq/((ref/i32) -> ref/i32)) -> i32 => 0

            func f(b: ref/i32, u0: uniq/((ref/i32 during b) -> ref/i32 during b)) -> i32
                return g(u0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd u0@move"
        },
        // rr1v (valid, review): two spellings of (ref/i32) -> ref/i32.
        {
            "rr1v",
            """
            func g(u: uniq/((ref/i32) -> ref/i32)) -> i32 => 0

            func f(u0: uniq/((ref/i32) -> ref/i32)) -> i32
                return g(u0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // rt2i (invalid, review): a generic caller g<T>(x: T, y: T), uniq/PC then uniq/FX(b). SPEC 10.8 as revised (G1128): the Function
        // Type's input Origin in T is a slot variable that only FX's `b` bounds (PC's per-call input is bound by its own Type, 15.3.4), so
        // T is uniq/FX(b) and PC's argument is the one that does not fit it, in either order (rt2j).
        {
            "rt2i",
            """
            func g<T>(x: T, y: T) -> i32 => 0

            func f(b: ref/i32, q: ref/i32, x0: uniq/((ref/i32) -> i32) during q, y0: uniq/((ref/i32 during b) -> i32) during q) -> i32
                origin b outlives q
                return g(x0@move, y0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd x0@move"
        },
        // rt2j (invalid, review): rt2i with FX(b) first.
        {
            "rt2j",
            """
            func g<T>(x: T, y: T) -> i32 => 0

            func f(b: ref/i32, q: ref/i32, x0: uniq/((ref/i32 during b) -> i32) during q, y0: uniq/((ref/i32) -> i32) during q) -> i32
                origin b outlives q
                return g(x0@move, y0@move)

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd y0@move"
        },
        // rt2v (valid, review): two spellings of uniq/PC.
        {
            "rt2v",
            """
            func g<T>(x: T, y: T) -> i32 => 0

            func f(q: ref/i32, x0: uniq/((ref/i32) -> i32) during q, y0: uniq/((ref/i32) -> i32) during q) -> i32
                return g(x0@move, y0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // rt2c, rt2d (valid, G1128): rt2i's bindings at covariant positions, in either order: T is FX(b), whose input Origin only FX's
        // `b` bounds, and PC's value instantiates its per-call input to `b` (SPEC 10.7, 10.8 as revised).
        {
            "rt2c",
            """
            func g<T>(x: T, y: T) -> i32 => 0

            func f(b: ref/i32, x0: (ref/i32) -> i32, y0: (ref/i32 during b) -> i32) -> i32
                return g(x0@move, y0@move)

            public func main() => ()
            """,
            string.Empty
        },
        {
            "rt2d",
            """
            func g<T>(x: T, y: T) -> i32 => 0

            func f(b: ref/i32, x0: (ref/i32 during b) -> i32, y0: (ref/i32) -> i32) -> i32
                return g(x0@move, y0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // ra6v (valid, review): a6i's assignment with equal targets.
        {
            "ra6v",
            """
            struct R<T>
                public let call: (T) -> i32
                public init(call: (T) -> i32) => self.call = call@move

            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>, r1: R<uniq/((ref/i32 during b) -> i32) during q>) -> i32
                origin q outlives p
                origin b outlives q
                var r: R<uniq/((ref/i32 during b) -> i32) during q> = r1@move
                r = r0@move
                return 0

            public func main() => ()
            """,
            string.Empty
        },
        // ra7v (valid, review): PC and PC at the contravariant slot.
        {
            "ra7v",
            """
            struct R<T>
                public let call: (T) -> i32
                public init(call: (T) -> i32) => self.call = call@move

            func g(q: ref/i32, r: R<uniq/((ref/i32) -> i32) during q>) -> i32 => 0

            func f(p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32) -> i32) during p>) -> i32
                origin q outlives p
                return g(q, r0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // rf1w (valid, review): f1i's named function with uniq/PC required.
        {
            "rf1w",
            """
            func h(u: uniq/((ref/i32) -> i32)) -> i32 => 0

            func f() -> i32
                let k: (uniq/((ref/i32) -> i32)) -> i32 = h
                return 0

            public func main() => ()
            """,
            string.Empty
        },
        // rf2w (valid, review): a Closure with uniq/PC required.
        {
            "rf2w",
            """
            func f() -> i32
                let k: (uniq/((ref/i32) -> i32)) -> i32 = func (u: uniq/((ref/i32) -> i32)) => 0
                return 0

            public func main() => ()
            """,
            string.Empty
        },
        // rp1v (valid, review): a per-call input with a fixed inner Origin at an argument (the declared side's well-formedness under the exchange).
        {
            "rp1v",
            """
            func g(c: ref/i32, u: uniq/((uniq/(ref/i32 during c)) -> i32)) -> i32 => 0

            func f(b: ref/i32, u0: uniq/((uniq/(ref/i32 during b)) -> i32)) -> i32
                return g(b, u0@move)

            public func main() => ()
            """,
            string.Empty
        },
        // rp1i (invalid, review): rp1v's invalid twin at a declared local.
        {
            "rp1i",
            """
            func f(a: ref/i32, b: ref/i32, u0: uniq/((uniq/(ref/i32 during a)) -> i32)) -> i32
                origin a outlives b
                let u: uniq/((uniq/(ref/i32 during b)) -> i32) = u0@move
                return 0

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd u0@move|UnprovenOriginRelation_Kd uniq/((uniq/(ref/i32 during b)) -> i32)"
        },
        // rp1w (valid, SPEC 15.3.4, 15.2.3, 15.6.1, 15.3.7): rp1v at a declared local. The declared Type's well-formedness relates the
        // fixed `b` to the local's region (u0's premise and the fit give it), never the held Function Type's per-call input.
        {
            "rp1w",
            """
            func f(b: ref/i32, u0: uniq/((uniq/(ref/i32 during b)) -> i32)) -> i32
                let u: uniq/((uniq/(ref/i32 during b)) -> i32) = u0@move
                return 0

            public func main() => ()
            """,
            string.Empty
        },
        // rp1w_ref (valid, as rp1w): shared layers.
        {
            "rp1w_ref",
            """
            func f(b: ref/i32, u0: ref/((ref/(ref/i32 during b)) -> i32)) -> i32
                let u: ref/((ref/(ref/i32 during b)) -> i32) = u0
                return 0

            public func main() => ()
            """,
            string.Empty
        },
        // rp2v (valid, as rp1w): `a == b` makes the two Function Types equal.
        {
            "rp2v",
            """
            func f(a: ref/i32, b: ref/i32, u0: uniq/((uniq/(ref/i32 during a)) -> i32)) -> i32
                origin a == b
                let u: uniq/((uniq/(ref/i32 during b)) -> i32) = u0@move
                return 0

            public func main() => ()
            """,
            string.Empty
        },
        // ri2v (valid, review): i1i's twin: a local region on both sides.
        {
            "ri2v",
            """
            func g(c: ref/i32, u: uniq/((ref/i32 during c) -> i32)) -> i32 => 0

            func keep(x: ref/i32) -> i32 => 0

            func mk(c: ref/i32) -> (ref/i32 during c) -> i32
                return keep

            func f() -> i32
                let local = 1
                var k = mk(local@ref)
                return g(local@ref, k@uniq)

            public func main() => ()
            """,
            string.Empty
        },
        // ri2w (valid, review): as ri2v.
        {
            "ri2w",
            """
            func g(c: ref/i32, u: uniq/((ref/i32 during c) -> i32)) -> i32 => 0

            func keep(x: ref/i32) -> i32 => 0

            func mk(c: ref/i32) -> (ref/i32 during c) -> i32
                return keep

            func f() -> i32
                let local = 1
                let r = local@ref
                var k = mk(r)
                return g(r, k@uniq)

            public func main() => ()
            """,
            string.Empty
        },
        // s1 (invalid twice, re-review): N6: a Type mismatch (slot 1) before c == d: no Origin relation forms after the mismatch (SPEC 15.6.1); as dev.
        {
            "s1",
            """
            func f(c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: (uniq/i32 during p, uniq/(ref/i32 during c) during p)) -> i32
                origin p outlives q
                origin c outlives q
                origin d outlives q
                let r: (uniq/i64 during q, uniq/(ref/i32 during d) during q) = r0@move
                return 0

            public func main() => ()
            """,
            "TypeMismatch_Kd r0@move"
        },
        // s2 (invalid twice, re-review): N6: c == d before the Type mismatch: its obligation formed before the mismatch; as dev.
        {
            "s2",
            """
            func f(c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: (uniq/(ref/i32 during c) during p, uniq/i32 during p)) -> i32
                origin p outlives q
                origin c outlives q
                origin d outlives q
                let r: (uniq/(ref/i32 during d) during q, uniq/i64 during q) = r0@move
                return 0

            public func main() => ()
            """,
            "TypeMismatch_Kd r0@move|UnprovenOriginRelation_Kd r0@move"
        },
        // s3 (invalid twice, re-review): N6: a Function part, then a Type mismatch, then c == d: the mismatch ends the decomposition; as dev.
        {
            "s3",
            """
            func f(b: ref/i32, c: ref/i32, d: ref/i32, p: ref/i32, q: ref/i32, r0: (uniq/((ref/i32) -> i32) during p, uniq/i32 during p, uniq/(ref/i32 during c) during p)) -> i32
                origin p outlives q
                origin b outlives q
                origin c outlives q
                origin d outlives q
                let r: (uniq/((ref/i32 during b) -> i32) during q, uniq/i64 during q, uniq/(ref/i32 during d) during q) = r0@move
                return 0

            public func main() => ()
            """,
            "TypeMismatch_Kd r0@move"
        },
        // c3bn (invalid, re-review): N6 (G492-a (i)'s c3bn): one Type mismatch, no Origin relation after it; as dev.
        {
            "c3bn",
            """
            func f(p: ref/i64, q: ref/i64, b: bool) -> (i32, ref/i64 during p)
                var t = (b, q)
                return t
            public func main() => ()
            """,
            "TypeMismatch_Kd t"
        },
        // k1 (invalid twice, re-review): the H11 move (BU's fncomp): (g, local@ref) at return t has the row and A5's twin that k2 has on dev.
        {
            "k1",
            """
            func f(x: ref/i32, y: ref/i32, g: ref/((ref/i32 during y) -> i32)) -> (ref/((ref/i32 during x) -> i32), ref/i32 during x)
                let local = 1
                let t = (g, local@ref)
                return t

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd t|UnsatisfiedOriginRelation_Kd t"
        },
        // k2 (invalid twice, re-review): (local@ref, g) at return t: unchanged.
        {
            "k2",
            """
            func f(x: ref/i32, y: ref/i32, g: ref/((ref/i32 during y) -> i32)) -> (ref/i32 during x, ref/((ref/i32 during x) -> i32))
                let local = 1
                let t = (local@ref, g)
                return t

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd t|UnsatisfiedOriginRelation_Kd t"
        },
        // k3 (invalid twice, re-review): the H11 move: two body chains after g.
        {
            "k3",
            """
            func f(x: ref/i32, y: ref/i32, g: ref/((ref/i32 during y) -> i32)) -> (ref/((ref/i32 during x) -> i32), ref/i32 during x, ref/i32 during x)
                let local = 1
                let t = (g, local@ref, local@ref)
                return t

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd t|UnsatisfiedOriginRelation_Kd t"
        },
        // k6 (invalid twice, re-review): the H11 move: a declared local after t.
        {
            "k6",
            """
            func f(x: ref/i32, y: ref/i32, g: ref/((ref/i32 during y) -> i32)) -> i32
                let local = 1
                let t = (g, local@ref)
                let u: (ref/((ref/i32 during x) -> i32), ref/i32 during x) = t@move
                return 0

            public func main() => ()
            """,
            "UnprovenOriginContract_Kd t@move|UnprovenOriginRelation_Kd ref/((ref/i32 during x) -> i32)|UnsatisfiedOriginRelation_Kd t@move"
        },
        // rq2av (invalid, re-review): rq2a through a var local (G492-a (i)'s implementation review).
        {
            "rq2av",
            """
            struct R<T>
                public let call: (T) -> i32
                public init(call: (T) -> i32) => self.call = call@move

            func g(q: ref/i32, r: R<uniq/((ref/i32) -> i32) during q>) -> i32 => 0
            func f(b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/((ref/i32 during b) -> i32) during p>) -> i32
                origin q outlives p
                var r1 = r0@move
                return g(q, r1@move)
            public func main() => ()
            """,
            "UnprovenOriginContract_Kd r1@move"
        },
    };

    // R7-O1 H: a local annotation's fit is A5's record at its initializer (D5), and the twin at `return t` went (rn3*, n3f, s2, k*).
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
        Assert.True(c.Binding.FitsType(types[4], types[5]));  // A shared layer is covariant: PC <: FX(b).
        Assert.False(c.Binding.FitsType(types[5], types[4])); // The required per-call input is rigid.
        Assert.False(c.Binding.FitsType(types[1], types[2])); // An exclusive layer: PC and FX(b) are not equal, either way.
        Assert.False(c.Binding.FitsType(types[2], types[1]));
        Assert.False(c.Binding.FitsType(types[1], types[3]));
        Assert.False(c.Binding.FitsType(types[3], types[1]));
        Assert.True(c.Binding.FitsType(types[1], types[1]));
    }
}
