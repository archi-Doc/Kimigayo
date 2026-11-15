// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// PLAN G904 (SPEC 8.10, 10.7, 15.3.4): a Function Type quantifies only the omitted Origin of its own direct inputs, under the syntax
// that declares it (BoundType.Binder). When a named function's `b` is solved to a required signature's per-call Origin, or to a
// written Type argument's, a nested Function Type that names `b` holds a fixed Origin, even where it sits at its own slot index:
// `(ref/i32 during b) -> i32` (FX(b)) is not `(ref/i32) -> i32` (PC). The rows are the unit's coverage table by id (slot indexes,
// layers, entries, generic forms, Closures; the o rows are the Owned judgment of a Function Type, SPEC 15.2.3, whose call signature is
// its own at every instance). Each invalid row has one record at the converted function or the call. The rows under an invariant
// layer (`uniq/`, `raw/`) also need PLAN G900's two-way comparison and join this table when both rules are on one base. The rows
// marked PLAN G913 (SPEC 15.3.4) cover the call's bounds: a nested Function Type's own per-call Origins are never a bound of the
// call's Origins, with `b` or the caller's Origin at slot 0 or 1, at depths 0 to 3, in both orders, at a local, a call and a generic,
// in a mixed nested Type, a result meet and with an Origin introduced by the result.
public partial class TypeBindingTest
{
    public static TheoryData<string, string, string> PerCallBinderRows => new()
    {
        // s0v (valid, SPEC 10.7, 15.3.5): the valid twin: both fixed at an enclosing Origin c (f1v).
        {
            "s0v",
            """
            func h(b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) -> i32 => 0

            func f(c: ref/i32) -> i32
                let k: (ref/i32 during c, uniq/((ref/i32 during c) -> i32)) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // s0p (valid, SPEC 10.7, 8.10): control: the nested Function Type's own per-call input on both sides.
        {
            "s0p",
            """
            func h(b: ref/i32, u: uniq/((ref/i32) -> i32)) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, uniq/((ref/i32) -> i32)) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // s1i (invalid, SPEC 10.7, 15.3.5, 8.10): outer per-call at slot 1, nested fixed input at slot 0 (f1k).
        {
            "s1i",
            """
            func h(x: i32, b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) -> i32 => 0

            func f() -> i32
                let k: (i32, ref/i32, uniq/((ref/i32) -> i32)) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // s1v (valid, SPEC 10.7, 15.3.5): the valid twin of s1j at an enclosing Origin c.
        {
            "s1v",
            """
            func h(x: i32, b: ref/i32, u: uniq/((i32, ref/i32 during b) -> i32)) -> i32 => 0

            func f(c: ref/i32) -> i32
                let k: (i32, ref/i32 during c, uniq/((i32, ref/i32 during c) -> i32)) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // s3j (invalid, SPEC 10.7, 15.3.5, 8.10): two outer per-call Origins; the nested input fixed at the second (slot 1 at index 0).
        {
            "s3j",
            """
            func h(a: ref/i32, b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, ref/i32, uniq/((ref/i32) -> i32)) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // s5i (invalid, SPEC 10.7, 15.3.5, 8.10): two levels: uniq/((uniq/FX(b)) -> i32).
        {
            "s5i",
            """
            func h(b: ref/i32, u: uniq/((uniq/((ref/i32 during b) -> i32)) -> i32)) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, uniq/((uniq/((ref/i32) -> i32)) -> i32)) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // l0v (valid, SPEC 10.7, 3.8): a bare (owned) nested Function input: required PC, implementation FX(b).
        {
            "l0v",
            """
            func h(b: ref/i32, u: (ref/i32 during b) -> i32) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, (ref/i32) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // l1v (valid, SPEC 10.7, 3.8): ref/ layer: required ref/PC, implementation ref/FX(b).
        {
            "l1v",
            """
            func h(b: ref/i32, u: ref/((ref/i32 during b) -> i32)) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, ref/((ref/i32) -> i32)) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // l3i (invalid, SPEC 10.7, 8.10): the result: implementation returns FX(b), required result PC.
        {
            "l3i",
            """
            func h(b: ref/i32, u: (ref/i32 during b) -> i32) -> (ref/i32 during b) -> i32 => u@move

            func f() -> i32
                let k: (ref/i32, (ref/i32) -> i32) -> (ref/i32) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // l3v (valid, SPEC 10.7): the result: implementation returns PC, required result PC.
        {
            "l3v",
            """
            func h(b: ref/i32, u: (ref/i32) -> i32) -> (ref/i32) -> i32 => u@move

            func f() -> i32
                let k: (ref/i32, (ref/i32) -> i32) -> (ref/i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // a0v (valid, SPEC 10.7, 15.3.5): call argument, the valid twin at c.
        {
            "a0v",
            """
            func h(b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) -> i32 => 0

            func take(c: ref/i32, k: (ref/i32 during c, uniq/((ref/i32 during c) -> i32)) -> i32) -> i32 => 0

            func f(c: ref/i32) -> i32 => take(c, h)
            """,
            string.Empty
        },
        // g1v (valid, SPEC 10.7, 15.3.5): a generic implementation, the valid twin at c.
        {
            "g1v",
            """
            func h<T>(b: ref/T, u: uniq/((ref/T during b) -> i32)) -> i32 => 0

            func f(c: ref/i32) -> i32
                let k: (ref/i32 during c, uniq/((ref/i32 during c) -> i32)) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // g2v (valid, SPEC 10.7, 8.10): a generic Function Type parameter bound to PC by a written Type argument: the nested Type keeps its own per-call input.
        {
            "g2v",
            """
            func g<T>(k: (ref/i32, uniq/T) -> i32) -> i32 => 0

            func h(b: ref/i32, u: uniq/((ref/i32) -> i32)) -> i32 => 0

            func f() -> i32 => g<(ref/i32) -> i32>(h)
            """,
            string.Empty
        },
        // n3i (invalid, SPEC 10.7, 8.10, 3.8): three levels, no invariant layer: u: (((FX(b)) -> i32) -> i32) -> i32 against (((PC) -> i32) -> i32) -> i32.
        {
            "n3i",
            """
            func h(b: ref/i32, u: ((((ref/i32 during b) -> i32) -> i32) -> i32) -> i32) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, ((((ref/i32) -> i32) -> i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // n4i (invalid, SPEC 10.7, 8.10, 3.8): the result three levels deep: ((FX(b)) -> i32) -> i32 against ((PC) -> i32) -> i32 (the parameter u as n0i's, valid).
        {
            "n4i",
            """
            func h(b: ref/i32, u: (((ref/i32 during b) -> i32) -> i32) -> i32) -> (((ref/i32 during b) -> i32) -> i32) -> i32 => u@move

            func f() -> i32
                let k: (ref/i32, (((ref/i32) -> i32) -> i32) -> i32) -> (((ref/i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // n0i (valid, SPEC 10.7, 8.10, 3.8): two levels, no invariant layer: u: ((FX(b)) -> i32) -> i32 against ((PC) -> i32) -> i32.
        {
            "n0i",
            """
            func h(b: ref/i32, u: (((ref/i32 during b) -> i32) -> i32) -> i32) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, (((ref/i32) -> i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // n0v (valid, SPEC 10.7, 3.8): the valid twin of n0i at c.
        {
            "n0v",
            """
            func h(b: ref/i32, u: (((ref/i32 during b) -> i32) -> i32) -> i32) -> i32 => 0

            func f(c: ref/i32) -> i32
                let k: (ref/i32 during c, (((ref/i32 during c) -> i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // n1v (valid, SPEC 10.7, 8.10): control: ((PC) -> i32) -> i32 on both sides.
        {
            "n1v",
            """
            func h(b: ref/i32, u: (((ref/i32) -> i32) -> i32) -> i32) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, (((ref/i32) -> i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // n2i (invalid, SPEC 10.7, 8.10, 3.8): the result holds FX(b) in a Tuple against PC.
        {
            "n2i",
            """
            func h(b: ref/i32, u: (ref/i32 during b) -> i32) -> ((ref/i32 during b) -> i32, i32) => (u@move, 0)

            func f() -> i32
                let k: (ref/i32, (ref/i32) -> i32) -> ((ref/i32) -> i32, i32) = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // d0v (valid, SPEC 15.3.5): a direct call: argument uniq/FX(a).
        {
            "d0v",
            """
            func h(b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) -> i32 => 0

            func f(a: ref/i32, w: uniq/((ref/i32 during a) -> i32)) -> i32 => h(a, w)
            """,
            string.Empty
        },
        // s0i (invalid, SPEC 10.7, 15.3.5, 8.10; with PLAN G900): outer per-call at slot 0, nested fixed input at slot 0, uniq/ (f1j, the PLAN row).
        {
            "s0i",
            """
            func h(b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, uniq/((ref/i32) -> i32)) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // s1j (invalid, SPEC 10.7, 15.3.5, 8.10; with PLAN G900): outer per-call at slot 1, nested fixed input at slot 1.
        {
            "s1j",
            """
            func h(x: i32, b: ref/i32, u: uniq/((i32, ref/i32 during b) -> i32)) -> i32 => 0

            func f() -> i32
                let k: (i32, ref/i32, uniq/((i32, ref/i32) -> i32)) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // s2i (invalid, SPEC 10.7, 15.3.5, 8.10; with PLAN G900): nested Function Type of the outer one's arity (2 and 2), slot 0.
        {
            "s2i",
            """
            func h(b: ref/i32, u: uniq/((ref/i32 during b, i32) -> i32)) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, uniq/((ref/i32, i32) -> i32)) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // s3i (invalid, SPEC 10.7, 15.3.5, 8.10; with PLAN G900): two outer per-call Origins; the nested input fixed at the first (slot 0).
        {
            "s3i",
            """
            func h(a: ref/i32, b: ref/i32, u: uniq/((ref/i32 during a) -> i32)) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, ref/i32, uniq/((ref/i32) -> i32)) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // s4i (invalid, SPEC 10.7, 15.3.5, 8.10; with PLAN G900): an Origin restriction on the implementation (origin a outlives b), nested fixed at a.
        {
            "s4i",
            """
            func h(a: ref/i32, b: ref/i32, u: uniq/((ref/i32 during a) -> i32)) -> i32
                origin a outlives b
                return 0

            func f() -> i32
                let k: (ref/i32, ref/i32, uniq/((ref/i32) -> i32)) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // l2i (invalid, SPEC 10.7, 15.3.5, 8.10; with PLAN G900): raw/ layer: required raw/PC, implementation raw/FX(b).
        {
            "l2i",
            """
            func h(b: ref/i32, u: raw/((ref/i32 during b) -> i32)) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, raw/((ref/i32) -> i32)) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // l4i (invalid, SPEC 10.7, 15.3.5, 8.10; with PLAN G900): a Tuple under uniq/: uniq/(FX(b), i32).
        {
            "l4i",
            """
            func h(b: ref/i32, u: uniq/((ref/i32 during b) -> i32, i32)) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, uniq/((ref/i32) -> i32, i32)) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // a0i (invalid, SPEC 10.7, 15.3.5, 8.10; with PLAN G900): call argument.
        {
            "a0i",
            """
            func h(b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) -> i32 => 0

            func take(k: (ref/i32, uniq/((ref/i32) -> i32)) -> i32) -> i32 => 0

            func f() -> i32 => take(h)
            """,
            "NoApplicableOverload_Kd take(h)"
        },
        // a1i (invalid, SPEC 10.7, 15.3.5, 8.10; with PLAN G900): return.
        {
            "a1i",
            """
            func h(b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) -> i32 => 0

            func f() -> (ref/i32, uniq/((ref/i32) -> i32)) -> i32 => h
            """,
            "UnprovenOriginContract_Kd h"
        },
        // g0i (invalid, SPEC 10.7, 15.3.5, 8.10; with PLAN G900): inside a generic function.
        {
            "g0i",
            """
            func h(b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) -> i32 => 0

            func f<T>(t: T) -> i32
                let k: (ref/i32, uniq/((ref/i32) -> i32)) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // g1i (invalid, SPEC 10.7, 15.3.5, 8.10; with PLAN G900): a generic implementation, T bound to i32.
        {
            "g1i",
            """
            func h<T>(b: ref/T, u: uniq/((ref/T during b) -> i32)) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, uniq/((ref/i32) -> i32)) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // g3i (invalid, SPEC 10.7, 15.3.5, 8.10; with PLAN G900): a generic Function Type parameter bound to PC by a written Type argument, the implementation's nested input fixed.
        {
            "g3i",
            """
            func g<T>(k: (ref/i32, uniq/T) -> i32) -> i32 => 0

            func h(b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) -> i32 => 0

            func f() -> i32 => g<(ref/i32) -> i32>(h)
            """,
            "NoApplicableOverload_Kd g<(ref/i32) -> i32>(h)"
        },
        // m0i (invalid, SPEC 10.7, 15.3.5, 8.10; with PLAN G900): a nested Type with its own per-call input and a fixed `during b` input, required uniq/PC2, b at the outer slot 1 (the fixed input at its own index 1).
        {
            "m0i",
            """
            func h(x: ref/i32, b: ref/i32, u: uniq/((ref/i32, ref/i32 during b) -> i32)) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, ref/i32, uniq/((ref/i32, ref/i32) -> i32)) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // c1v (valid, SPEC 15.2.3, 3.2.1): Owned: o1v with the instance built from a Function Item and called through its Field.
        {
            "c1v",
            """
            func need<T>(value: T) -> i32
                T is Owned
                return 1

            struct S<T>
                public let call: (ref/i32, T) -> i32

                public init(call: (ref/i32, T) -> i32)
                    self.call = call@move

            func add(a: ref/i32, x: i32) -> i32 => a@follow + x

            func f(s: S<i32>) -> i32 => need(s.call@move)

            func run(s: S<i32>) -> i32
                let n: i32 = 40
                return s.call(n@ref, 2)
            """,
            string.Empty
        },
        // c2v (valid, SPEC 15.2.3, 7.7.4): Owned: the instance's Function Type Field captured by a Closure erased to () -> i32.
        {
            "c2v",
            """
            struct S<T>
                public let call: (ref/i32, T) -> i32

                public init(call: (ref/i32, T) -> i32)
                    self.call = call@move

            func add(a: ref/i32, x: i32) -> i32 => a@follow + x

            func f(s: S<i32>) -> () -> i32
                let call = s.call@move
                return func [call@move] () -> i32 => 7
            """,
            string.Empty
        },
        // c2i (invalid, SPEC 15.2.3, 7.7.4): c2v at S<ref/i32 during c>: the environment holds the fixed c (the record names c, not the per-call input).
        {
            "c2i",
            """
            struct S<T>
                public let call: (ref/i32, T) -> i32

                public init(call: (ref/i32, T) -> i32)
                    self.call = call@move

            func f(c: ref/i32, s: S<ref/i32 during c>) -> () -> i32
                let call = s.call@move
                return func [call@move] () -> i32 => 7
            """,
            "UnprovenConstraint_Kd func [call@move] () -> i32 => 7"
        },
        // c4v (valid, SPEC 15.2.3, 8.15): Owned: a generic body's S<T>.call under the premise T is Owned.
        {
            "c4v",
            """
            func need<T>(value: T) -> i32
                T is Owned
                return 1

            struct S<T>
                public let call: (ref/i32, T) -> i32

                public init(call: (ref/i32, T) -> i32)
                    self.call = call@move

            func add(a: ref/i32, x: i32) -> i32 => a@follow + x

            func g<T>(s: S<T>) -> i32
                T is Owned
                return need(s.call@move)
            """,
            string.Empty
        },
        // c5v (valid, SPEC 15.2.3, 15.3.4): Owned: S<(ref/i32) -> i32>.call, the nested Function Type's input its own per-call Origin.
        {
            "c5v",
            """
            func need<T>(value: T) -> i32
                T is Owned
                return 1

            struct S<T>
                public let call: (ref/i32, T) -> i32

                public init(call: (ref/i32, T) -> i32)
                    self.call = call@move

            func f(s: S<(ref/i32) -> i32>) -> i32 => need(s.call@move)
            """,
            string.Empty
        },
        // c6v (valid, SPEC 15.2.3): Owned: S2<i32>.call, (T, ref/i32) -> i32 with the per-call input at slot 1.
        {
            "c6v",
            """
            func need<T>(value: T) -> i32
                T is Owned
                return 1

            struct S2<T>
                public let call: (T, ref/i32) -> i32

            func f(s: S2<i32>) -> i32 => need(s.call@move)
            """,
            string.Empty
        },
        // c8v (valid, SPEC 15.2.3, 8.15, 7.7.4): Owned: c2v in a generic body under the premise T is Owned.
        {
            "c8v",
            """
            struct S<T>
                public let call: (ref/i32, T) -> i32

                public init(call: (ref/i32, T) -> i32)
                    self.call = call@move

            func add(a: ref/i32, x: i32) -> i32 => a@follow + x

            func g<T>(s: S<T>) -> () -> i32
                T is Owned
                let call = s.call@move
                return func [call@move] () -> i32 => 7
            """,
            string.Empty
        },
        // t01i (invalid, SPEC 10.7, 3.8, 15.3.4): the result (ref/i32 during b, ref/i32) -> i32 against (ref/i32, ref/i32) -> i32, b at slot 0 (the record names the result's 1st parameter, the fixed input).
        {
            "t01i",
            """
            func h(b: ref/i32, u: (ref/i32 during b, ref/i32) -> i32) -> (ref/i32 during b, ref/i32) -> i32 => u@move

            func f() -> i32
                let k: (ref/i32, (ref/i32, ref/i32) -> i32) -> (ref/i32, ref/i32) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // t05i (invalid, SPEC 10.7, 3.8): the input ((ref/i32 during b, ref/i32) -> i32) -> i32 against ((ref/i32, ref/i32) -> i32) -> i32, b at slot 0 (the record names the fixed input).
        {
            "t05i",
            """
            func h(b: ref/i32, u: ((ref/i32 during b, ref/i32) -> i32) -> i32) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, ((ref/i32, ref/i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // t06i (invalid, SPEC 10.7, 3.8, 15.3.5): the result Option<FX(b)> against Option<PC> (a covariant payload).
        {
            "t06i",
            """
            func h(b: ref/i32, u: (ref/i32 during b) -> i32) -> Option<(ref/i32 during b) -> i32> => .Some(u@move)

            func f() -> i32
                let k: (ref/i32, (ref/i32) -> i32) -> Option<(ref/i32) -> i32> = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // t08i (invalid, SPEC 10.7, 10.8): x0i with one input: T written PC, h's result FX(b).
        {
            "t08i",
            """
            func g<T>(k: (ref/i32, T) -> T) -> i32 => 0

            func h(b: ref/i32, u: (ref/i32 during b) -> i32) -> (ref/i32 during b) -> i32 => u@move

            func f() -> i32 => g<(ref/i32) -> i32>(h)
            """,
            "NoApplicableOverload_Kd g<(ref/i32) -> i32>(h)"
        },
        // t09i (invalid, SPEC 10.7, 10.8.1): t08i at a constructor's Function Type parameter, Box<PC>.init(h).
        {
            "t09i",
            """
            struct Box<T>
                public let k: (ref/i32, T) -> T

                public init(k: (ref/i32, T) -> T)
                    self.k = k@move

            func h(b: ref/i32, u: (ref/i32 during b) -> i32) -> (ref/i32 during b) -> i32 => u@move

            func f() -> i32
                let box = Box<(ref/i32) -> i32>.init(h)
                return 0
            """,
            "NoApplicableOverload_Kd Box<(ref/i32) -> i32>.init(h)"
        },
        // t10i (invalid, SPEC 10.8, 10.7): t08i with T inferred from a second argument w: PC (no T makes h fit).
        {
            "t10i",
            """
            func g<T>(k: (ref/i32, T) -> T, t: T) -> i32 => 0

            func h(b: ref/i32, u: (ref/i32 during b) -> i32) -> (ref/i32 during b) -> i32 => u@move

            func f(w: (ref/i32) -> i32) -> i32 => g(h, w@move)
            """,
            "NoApplicableOverload_Kd g(h, w@move)"
        },
        // cb1i (invalid, SPEC 8.10, 10.7): l3i's h through F is Callable<(ref/i32, PC) -> PC>.
        {
            "cb1i",
            """
            func use<F>(f: ref/F) -> i32
                F is Callable<(ref/i32, (ref/i32) -> i32) -> (ref/i32) -> i32>
                return 0

            func h(b: ref/i32, u: (ref/i32 during b) -> i32) -> (ref/i32 during b) -> i32 => u@move

            func f() -> i32
                let item = h
                return use(item@ref)
            """,
            "UnprovenConstraint_Kd use(item@ref)"
        },
        // a2i (invalid, SPEC 10.7, 15.3.5, 8.10): call argument, the outer per-call Origin at slot 1 (s1i at an argument).
        {
            "a2i",
            """
            func h(x: i32, b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) -> i32 => 0

            func take(k: (i32, ref/i32, uniq/((ref/i32) -> i32)) -> i32) -> i32 => 0

            func f() -> i32 => take(h)
            """,
            "NoApplicableOverload_Kd take(h)"
        },
        // x0i (invalid, SPEC 10.7, 8.10, 3.8): generic O, T written as (ref/i32, i32) -> i32; the implementation returns (ref/i32 during b, i32) -> i32.
        {
            "x0i",
            """
            func g<T>(k: (ref/i32, T) -> T) -> i32 => 0

            func h(b: ref/i32, u: (ref/i32 during b, i32) -> i32) -> (ref/i32 during b, i32) -> i32 => u@move

            func f() -> i32 => g<(ref/i32, i32) -> i32>(h)
            """,
            "NoApplicableOverload_Kd g<(ref/i32, i32) -> i32>(h)"
        },
        // x0v (valid, SPEC 10.7, 8.10): generic O, the valid twin: the implementation's nested Types keep their own per-call input.
        {
            "x0v",
            """
            func g<T>(k: (ref/i32, T) -> T) -> i32 => 0

            func h(b: ref/i32, u: (ref/i32, i32) -> i32) -> (ref/i32, i32) -> i32 => u@move

            func f() -> i32 => g<(ref/i32, i32) -> i32>(h)
            """,
            string.Empty
        },
        // m1i (invalid, SPEC 10.7, 8.10): a nested Type with its own per-call input and a fixed `during b` input as the result, required PC2, b at the outer slot 1.
        {
            "m1i",
            """
            func h(x: ref/i32, b: ref/i32, u: (ref/i32, ref/i32 during b) -> i32) -> (ref/i32, ref/i32 during b) -> i32 => u@move

            func f() -> i32
                let k: (ref/i32, ref/i32, (ref/i32, ref/i32) -> i32) -> (ref/i32, ref/i32) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // m4v (valid, SPEC 10.7, 8.10): nested (i32, ref/i32 during b) -> i32 (no own per-call input), b at the outer slot 1 = its index, b fixed at c.
        {
            "m4v",
            """
            func h(x: ref/i32, b: ref/i32, u: (i32, ref/i32 during b) -> i32) -> i32 => 0

            func f(c: ref/i32) -> i32
                let k: (ref/i32, ref/i32 during c, (i32, ref/i32 during c) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // r1v (valid, SPEC 10.7, 10.5): a contextual Closure against a required signature with a nested uniq/PC.
        {
            "r1v",
            """
            func f() -> i32
                let k: (ref/i32, uniq/((ref/i32) -> i32)) -> i32 = func (a, w) => 0
                return 0
            """,
            string.Empty
        },
        // r2v (valid, SPEC 10.7, 8.10): a contextual Closure calling through its nested input.
        {
            "r2v",
            """
            func f() -> i32
                let k: (ref/i32, ref/((ref/i32) -> i32)) -> i32 = func (a, w) => w(a)
                return 0
            """,
            string.Empty
        },
        // r3v (valid, SPEC 10.7, 15.3.5): a written Closure with a nested uniq/PC parameter.
        {
            "r3v",
            """
            func f() -> i32
                let k: (ref/i32, uniq/((ref/i32) -> i32)) -> i32 = func (a: ref/i32, w: uniq/((ref/i32) -> i32)) => 0
                return 0
            """,
            string.Empty
        },
        // e0v (valid, SPEC 10.7, 10.8, 3.8): generic inference: T written PC, the implementation's nested input FX(b), no invariant layer.
        {
            "e0v",
            """
            func g<T>(k: (ref/i32, T) -> i32) -> i32 => 0

            func h(b: ref/i32, u: (ref/i32 during b) -> i32) -> i32 => 0

            func f() -> i32 => g<(ref/i32) -> i32>(h)
            """,
            string.Empty
        },
        // e1v (valid, SPEC 10.7, 10.8, 15.3.5): e0v under ref/.
        {
            "e1v",
            """
            func g<T>(k: (ref/i32, ref/T) -> i32) -> i32 => 0

            func h(b: ref/i32, u: ref/((ref/i32 during b) -> i32)) -> i32 => 0

            func f() -> i32 => g<(ref/i32) -> i32>(h)
            """,
            string.Empty
        },
        // b2v (valid, SPEC 10.7, 15.3.5): s0v with the required side's fixed Origin c at f's slot 1 (no slot coincidence on the required side).
        {
            "b2v",
            """
            func h(b: ref/i32, u: uniq/((ref/i32 during b) -> i32)) -> i32 => 0

            func f(x: i32, c: ref/i32) -> i32
                let k: (ref/i32 during c, uniq/((ref/i32 during c) -> i32)) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // b3i (invalid, SPEC 10.7, 8.10, 3.8): the result FX(b) against PC with the outer per-call Origin at slot 1 (l3i without slot coincidence).
        {
            "b3i",
            """
            func h(x: i32, b: ref/i32, u: (ref/i32 during b) -> i32) -> (ref/i32 during b) -> i32 => u@move

            func f() -> i32
                let k: (i32, ref/i32, (ref/i32) -> i32) -> (ref/i32) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // o0v (valid, SPEC 15.2.3, 15.3.4): Owned: a written Function Type with its own per-call input.
        {
            "o0v",
            """
            func need<T>(value: T) -> i32
                T is Owned
                return 1

            func f(k: (ref/i32) -> i32) -> i32 => need(k@move)
            """,
            string.Empty
        },
        // o0i (invalid, SPEC 15.2.3, 15.3.4): Owned: a written Function Type with a fixed input Origin c.
        {
            "o0i",
            """
            func need<T>(value: T) -> i32
                T is Owned
                return 1

            func f(c: ref/i32, k: (ref/i32 during c) -> i32) -> i32 => need(k@move)
            """,
            "UnprovenConstraint_Kd need(k@move)"
        },
        // o1v (valid, SPEC 15.2.3, 15.3.4, 3.8): Owned: a generic struct's Function Type field at an instance (T := i32), which keeps its binder.
        {
            "o1v",
            """
            func need<T>(value: T) -> i32
                T is Owned
                return 1

            struct S<T>
                public let call: (ref/i32, T) -> i32

            func f(s: S<i32>) -> i32 => need(s.call@move)
            """,
            string.Empty
        },
        // o1i (invalid, SPEC 15.2.3, 15.3.4, 3.8): Owned: the same field at T := ref/i32 during c, a fixed Origin beside its own per-call input.
        {
            "o1i",
            """
            func need<T>(value: T) -> i32
                T is Owned
                return 1

            struct S<T>
                public let call: (ref/i32, T) -> i32

            func f(c: ref/i32, s: S<ref/i32 during c>) -> i32 => need(s.call@move)
            """,
            "UnprovenConstraint_Kd need(s.call@move)"
        },
        // o2v (valid, SPEC 15.2.3, 15.3.4, 10.8): Owned: a generic function's Function Type parameter under the premise T is Owned.
        {
            "o2v",
            """
            func need<T>(value: T) -> i32
                T is Owned
                return 1

            func g<T>(k: (ref/i32, T) -> i32) -> i32
                T is Owned
                return need(k@move)

            func h(a: ref/i32, x: i32) -> i32 => 0

            func f() -> i32 => g<i32>(h)
            """,
            string.Empty
        },
        // o3v (valid, SPEC 15.2.3, 15.3.4): Owned: a Function Type with a static fixed Origin, in a Tuple.
        {
            "o3v",
            """
            func need<T>(value: T) -> i32
                T is Owned
                return 1

            func f(k: (ref/i32, ref/i32 during static) -> i32) -> i32 => need((k@move, 1))
            """,
            string.Empty
        },
        // n3j (invalid, SPEC 10.7, 8.10, 3.8; PLAN G913): n3i with the outer per-call Origin at slot 1 (no slot coincidence: the collector descends).
        {
            "n3j",
            """
            func h(x: i32, b: ref/i32, u: ((((ref/i32 during b) -> i32) -> i32) -> i32) -> i32) -> i32 => 0

            func f() -> i32
                let k: (i32, ref/i32, ((((ref/i32) -> i32) -> i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // n0j (valid, SPEC 10.7, 8.10, 3.8; PLAN G913): n0i with the outer per-call Origin at slot 1.
        {
            "n0j",
            """
            func h(x: i32, b: ref/i32, u: (((ref/i32 during b) -> i32) -> i32) -> i32) -> i32 => 0

            func f() -> i32
                let k: (i32, ref/i32, (((ref/i32) -> i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // n4j (invalid, SPEC 10.7, 8.10, 3.8; PLAN G913): n4i with b at slot 1.
        {
            "n4j",
            """
            func h(x: i32, b: ref/i32, u: (((ref/i32 during b) -> i32) -> i32) -> i32) -> (((ref/i32 during b) -> i32) -> i32) -> i32 => u@move

            func f() -> i32
                let k: (i32, ref/i32, (((ref/i32) -> i32) -> i32) -> i32) -> (((ref/i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // b0v (valid, SPEC 10.7, 3.8, 8.10; PLAN G913): l0v with the outer per-call Origin at slot 1 (the nested fixed input at index 0: no slot coincidence).
        {
            "b0v",
            """
            func h(x: i32, b: ref/i32, u: (ref/i32 during b) -> i32) -> i32 => 0

            func f() -> i32
                let k: (i32, ref/i32, (ref/i32) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // b1v (valid, SPEC 10.7, 3.8, 8.10; PLAN G913): b0v under ref/.
        {
            "b1v",
            """
            func h(x: i32, b: ref/i32, u: ref/((ref/i32 during b) -> i32)) -> i32 => 0

            func f() -> i32
                let k: (i32, ref/i32, ref/((ref/i32) -> i32)) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // m0v (valid, SPEC 10.7, 15.3.5, 8.10; PLAN G913): a nested Function Type with its own per-call input AND a fixed during b input, uniq/, b fixed at c.
        {
            "m0v",
            """
            func h(x: ref/i32, b: ref/i32, u: uniq/((ref/i32, ref/i32 during b) -> i32)) -> i32 => 0

            func f(c: ref/i32) -> i32
                let k: (ref/i32, ref/i32 during c, uniq/((ref/i32, ref/i32 during c) -> i32)) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // m1v (valid, SPEC 10.7, 8.10; PLAN G913): MX(b) as the result, no invariant layer, b fixed at c.
        {
            "m1v",
            """
            func h(x: ref/i32, b: ref/i32, u: (ref/i32, ref/i32 during b) -> i32) -> (ref/i32, ref/i32 during b) -> i32 => u@move

            func f(c: ref/i32) -> i32
                let k: (ref/i32, ref/i32 during c, (ref/i32, ref/i32 during c) -> i32) -> (ref/i32, ref/i32 during c) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // m2v (valid, SPEC 10.7, 8.10; PLAN G913): MX-like nested Type, own input at 0, fixed during b at 1, b at the outer slot 0 (index mismatch), b fixed at c, no invariant layer.
        {
            "m2v",
            """
            func h(b: ref/i32, x: ref/i32, u: (ref/i32, ref/i32 during b) -> i32) -> i32 => 0

            func f(c: ref/i32) -> i32
                let k: (ref/i32 during c, ref/i32, (ref/i32, ref/i32 during c) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // m3v (valid, SPEC 10.7, 8.10; PLAN G913): nested (ref/i32 during b, ref/i32) -> i32 (fixed at 0, own at 1), b at the outer slot 1, b fixed at c.
        {
            "m3v",
            """
            func h(x: ref/i32, b: ref/i32, u: (ref/i32 during b, ref/i32) -> i32) -> i32 => 0

            func f(c: ref/i32) -> i32
                let k: (ref/i32, ref/i32 during c, (ref/i32 during c, ref/i32) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // s5v (valid, SPEC 10.7, 15.3.5; PLAN G913): two levels, the valid twin at c.
        {
            "s5v",
            """
            func h(b: ref/i32, u: uniq/((uniq/((ref/i32 during b) -> i32)) -> i32)) -> i32 => 0

            func f(c: ref/i32) -> i32
                let k: (ref/i32 during c, uniq/((uniq/((ref/i32 during c) -> i32)) -> i32)) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // q1i0 (invalid, SPEC 10.7, 8.10, 3.8; PLAN G913): parameter, d = 1, b at slot 0: u: (FX(b)) -> i32 against (PC) -> i32.
        {
            "q1i0",
            """
            func h(b: ref/i32, u: ((ref/i32 during b) -> i32) -> i32) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, ((ref/i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // q1i1 (invalid, SPEC 10.7, 8.10, 3.8; PLAN G913): q1i0 with b at slot 1.
        {
            "q1i1",
            """
            func h(x: i32, b: ref/i32, u: ((ref/i32 during b) -> i32) -> i32) -> i32 => 0

            func f() -> i32
                let k: (i32, ref/i32, ((ref/i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // q1v1 (valid, SPEC 10.7, 3.8; PLAN G913): q1i1 with both sides fixed at c (b := c).
        {
            "q1v1",
            """
            func h(x: i32, b: ref/i32, u: ((ref/i32 during b) -> i32) -> i32) -> i32 => 0

            func f(c: ref/i32) -> i32
                let k: (i32, ref/i32 during c, ((ref/i32 during c) -> i32) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // q3v1 (valid, SPEC 10.7, 3.8; PLAN G913): n3j with both sides fixed at c.
        {
            "q3v1",
            """
            func h(x: i32, b: ref/i32, u: ((((ref/i32 during b) -> i32) -> i32) -> i32) -> i32) -> i32 => 0

            func f(c: ref/i32) -> i32
                let k: (i32, ref/i32 during c, ((((ref/i32 during c) -> i32) -> i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // q4v1 (valid, SPEC 10.7, 3.8; PLAN G913): n4j (result d = 2, parameter d = 2) with both sides fixed at c.
        {
            "q4v1",
            """
            func h(x: i32, b: ref/i32, u: (((ref/i32 during b) -> i32) -> i32) -> i32) -> (((ref/i32 during b) -> i32) -> i32) -> i32 => u@move

            func f(c: ref/i32) -> i32
                let k: (i32, ref/i32 during c, (((ref/i32 during c) -> i32) -> i32) -> i32) -> (((ref/i32 during c) -> i32) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // q6i (invalid, SPEC 10.7, 3.8, 8.10; PLAN G913): the reverse order: h's nested input is its own (u: PC), O's fixed (FX(c)), d = 0, c at f's slot 0.
        {
            "q6i",
            """
            func h(b: ref/i32, u: (ref/i32) -> i32) -> i32 => 0

            func f(c: ref/i32) -> i32
                let k: (ref/i32 during c, (ref/i32 during c) -> i32) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // q6j (invalid, SPEC 10.7, 3.8, 8.10; PLAN G913): q6i with c at f's slot 1.
        {
            "q6j",
            """
            func h(b: ref/i32, u: (ref/i32) -> i32) -> i32 => 0

            func f(x: i32, c: ref/i32) -> i32
                let k: (ref/i32 during c, (ref/i32 during c) -> i32) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // q6v (valid, SPEC 10.7, 3.8, 8.10; PLAN G913): the reverse order at d = 1: u: (PC) -> i32 against (FX(c)) -> i32, c at f's slot 0.
        {
            "q6v",
            """
            func h(b: ref/i32, u: ((ref/i32) -> i32) -> i32) -> i32 => 0

            func f(c: ref/i32) -> i32
                let k: (ref/i32 during c, ((ref/i32 during c) -> i32) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // q6w (valid, SPEC 10.7, 3.8, 8.10; PLAN G913): q6v with c at f's slot 1.
        {
            "q6w",
            """
            func h(b: ref/i32, u: ((ref/i32) -> i32) -> i32) -> i32 => 0

            func f(x: i32, c: ref/i32) -> i32
                let k: (ref/i32 during c, ((ref/i32 during c) -> i32) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // q7i (invalid, SPEC 15.6.4, 10.7, 3.8; PLAN G913): a direct call h(1, c, k@move), h's u at d = 3 with b at slot 1, k's Type the PC form.
        {
            "q7i",
            """
            func h(x: i32, b: ref/i32, u: ((((ref/i32 during b) -> i32) -> i32) -> i32) -> i32) -> i32 => 0

            func f(c: ref/i32, k: ((((ref/i32) -> i32) -> i32) -> i32) -> i32) -> i32 => h(1, c, k@move)
            """,
            "UnprovenOriginContract_Kd k@move"
        },
        // q7v (valid, SPEC 15.6.4, 10.7, 3.8; PLAN G913): the same call at d = 2.
        {
            "q7v",
            """
            func h(x: i32, b: ref/i32, u: (((ref/i32 during b) -> i32) -> i32) -> i32) -> i32 => 0

            func f(c: ref/i32, k: (((ref/i32) -> i32) -> i32) -> i32) -> i32 => h(1, c, k@move)
            """,
            string.Empty
        },
        // q8v (valid, SPEC 10.8, 10.7, 3.8; PLAN G913): generic g<T>(k: (i32, ref/i32, T) -> i32), T written PC, given h with u: FX(b), b at slot 1 (e0v at slot 1).
        {
            "q8v",
            """
            func g<T>(k: (i32, ref/i32, T) -> i32) -> i32 => 0

            func h(x: i32, b: ref/i32, u: (ref/i32 during b) -> i32) -> i32 => 0

            func f() -> i32 => g<(ref/i32) -> i32>(h)
            """,
            string.Empty
        },
        // ra2v (valid, SPEC 10.7, 3.8, 8.10; PLAN G913): a mixed nested Type MX(b) = (ref/i32, ref/i32 during b) -> i32 at d = 2, b at slot 0: its own per-call input beside the solved Origin.
        {
            "ra2v",
            """
            func h(b: ref/i32, u: (((ref/i32, ref/i32 during b) -> i32) -> i32) -> i32) -> i32 => 0

            func f() -> i32
                let k: (ref/i32, (((ref/i32, ref/i32) -> i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // rm0w (valid, SPEC 15.4, 10.7, 3.8; PLAN G913): a result meet holding a nested own per-call Origin and the solved one: u: (ref/i32, ref/i32 during b) -> ref/i32, b at slot 1.
        {
            "rm0w",
            """
            func h(x: i32, b: ref/i32, u: (ref/i32, ref/i32 during b) -> ref/i32) -> i32 => 0

            func f() -> i32
                let k: (i32, ref/i32, (ref/i32, ref/i32) -> ref/i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // rg1j (invalid, SPEC 10.7, 8.10, 3.8, 10.8; PLAN G913): q1i1 as a generic implementation h<T>(x: T, ...) (T := i32).
        {
            "rg1j",
            """
            func h<T>(x: T, b: ref/i32, u: ((ref/i32 during b) -> i32) -> i32) -> i32 => 0

            func f() -> i32
                let k: (i32, ref/i32, ((ref/i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // rg2w (valid, SPEC 10.7, 8.10, 3.8, 10.8; PLAN G913): rg1j at d = 2.
        {
            "rg2w",
            """
            func h<T>(x: T, b: ref/i32, u: (((ref/i32 during b) -> i32) -> i32) -> i32) -> i32 => 0

            func f() -> i32
                let k: (i32, ref/i32, (((ref/i32) -> i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            string.Empty
        },
        // rg3j (invalid, SPEC 10.7, 8.10, 3.8, 10.8; PLAN G913): n3j as a generic implementation, the nested Type over ref/T (T := i32).
        {
            "rg3j",
            """
            func h<T>(x: i32, b: ref/T, u: ((((ref/T during b) -> i32) -> i32) -> i32) -> i32) -> i32 => 0

            func f() -> i32
                let k: (i32, ref/i32, ((((ref/i32) -> i32) -> i32) -> i32) -> i32) -> i32 = h
                return 0
            """,
            "UnprovenOriginContract_Kd h"
        },
        // ru4v (valid, SPEC 15.3.4, 10.7, 3.8; PLAN G913): o introduced by the result and named inside a nested Type at d = 2: bounded only at the result (o := c).
        {
            "ru4v",
            """
            func h(u: (((ref/i32 during o) -> i32) -> i32) -> i32) -> ref/i32 during o => $abort("no")

            func f(c: ref/i32) -> i32
                let k: ((((ref/i32) -> i32) -> i32) -> i32) -> ref/i32 during c = h
                return 0
            """,
            string.Empty
        },
    };

    [Theory]
    [MemberData(nameof(PerCallBinderRows))]
    public void AFunctionTypeBindsOnlyItsOwnPerCallInputs(string row, string source, string records)
    {
        var c = Parse(source);
        _ = c.Bind();
        var found = c.Binding.Issues.Select(x => $"{x.Code} {source.Substring(x.Node.Span.Start, x.Node.Span.Length)}").Order(StringComparer.Ordinal).ToArray();
        Assert.True(records.Split('|', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).SequenceEqual(found), $"{row}: {string.Join(" | ", found)}");
    }
}
