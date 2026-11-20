// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>A5-F8's coverage of match and `for` Subjects and guards on MIR (SPEC 15.1.6, 14.6.2, 14.8.3; the A5 Step 2 plan's 2d-4, PLAN
/// G396 (1)): the Subject's Loan follows its bindings' uses and its iterator, and a guard's Subject protection ends at guard cleanup. Each
/// row's label is from SPEC; the rows passed on arrival, as coverage rows do.</summary>
public sealed partial class MirBorrowsTest
{
    // The callees of the Subject and guard rows: exclusive uses of an i32, an Option and an Array, a guard's exclusive call, an owned
    // Subject's producer, and a holder of an Array beside an i32.
    private const string SubjectShapes = """
        func touchO(value: uniq/Option<i32>) => ()
        func touchA(value: uniq/Array<i32>) => ()
        func resetO(value: uniq/Option<i32>) -> bool => true
        func bump(value: uniq/i32) -> bool => true
        func makeO() -> Option<i32> => .Some(1)
        struct SubjectBag
            public var items: Array<i32> = []
            public var count: i32 = 0
            public init() => ()

        """;

    // SPEC 15.1.6, 14.6.2: a bare Place Subject is a shared borrow (or the written borrow) of the Place, and its Loan follows the uses of
    // the arm's bindings, so an arm whose borrowed binding is dead (m3v), or that binds nothing (m3w), may replace the Place, and a write
    // after the match is free (m1v); a binding live across the replacement conflicts (m2i), and so does another live Loan of the Place
    // (m8i). The Subject borrow meets a live exclusive Loan (m4i) and an exclusive Subject a live shared one (m5i, also at its binding's
    // write); their twins end the Loan first (m4v, m5v). A borrow through a borrowed Subject depends on the original Place, not the
    // internal slot (m6v; m6i writes the Place while it is live), and a borrow of an owned Subject's moved payload cannot leave the arm
    // (m7i). A `for` Subject's Loan lives while the iterator or an escaped item needs it: appending in the body (f1i, f5i through a Field)
    // or after an item escaped (f4i) conflicts, appending after the loop (f1v) or writing a disjoint Field in the body (f5v) does not; an
    // exclusive Subject meets a live shared Loan (f2i, at the Subject and at the item's write) and a shared Subject a live exclusive one
    // (f3i); f2v and f4v are valid.
    [Fact]
    public void ASubjectsLoanFollowsItsBindingsAndItsIterator()
        => Assert.Equal(
            [
                "m2i: o = .None", "m4i: o", "m5i: o@uniq", "m5i: n@follow += 1 PlacementActivationConflict_Kd", "m6i: o = .None",
                "m7i: let n", "m8i: o = .None", "f1i: values.append(1) CallActivationConflict_Kd",
                "f2i: for v in values@uniq => v@follow += 1 CallActivationConflict_Kd", "f2i: v@follow += 1 PlacementActivationConflict_Kd",
                "f3i: values", "f4i: values.append(1) CallActivationConflict_Kd", "f5i: b.items.append(1) CallActivationConflict_Kd",
            ],
            Problems(SubjectShapes + """
                func m1v()
                    var o: Option<i32> = .Some(1)
                    match o
                        .Some(let n) => inspect(n)
                        .None => ()
                    o = .None
                func m2i()
                    var o: Option<i32> = .Some(1)
                    match o
                        .Some(let n)
                            o = .None
                            inspect(n)
                        .None => ()
                func m3v()
                    var o: Option<i32> = .Some(1)
                    match o
                        .Some(let n)
                            inspect(n)
                            o = .None
                        .None => o = .Some(2)
                func m3w()
                    var o: Option<i32> = .Some(1)
                    match o
                        .Some(_) => o = .None
                        .None => ()
                func m4i()
                    var o: Option<i32> = .Some(1)
                    let u = o@uniq
                    match o
                        .Some(let n) => inspect(n)
                        .None => ()
                    touchO(u)
                func m4v()
                    var o: Option<i32> = .Some(1)
                    let u = o@uniq
                    touchO(u)
                    match o
                        .Some(let n) => inspect(n)
                        .None => ()
                func m5i()
                    var o: Option<i32> = .Some(1)
                    let r = o@ref
                    match o@uniq
                        .Some(let n) => n@follow += 1
                        .None => ()
                    inspect(r)
                func m5v()
                    var o: Option<i32> = .Some(1)
                    let r = o@ref
                    inspect(r)
                    match o@uniq
                        .Some(let n) => n@follow += 1
                        .None => ()
                func m6v()
                    var z = 0
                    var o: Option<i32> = .Some(1)
                    var keep: ref/i32 = z@ref
                    match o
                        .Some(let n) => keep = n
                        .None => ()
                    inspect(keep)
                func m6i()
                    var z = 0
                    var o: Option<i32> = .Some(1)
                    var keep: ref/i32 = z@ref
                    match o
                        .Some(let n) => keep = n
                        .None => ()
                    o = .None
                    inspect(keep)
                func m7i()
                    var z = 0
                    var keep: ref/i32 = z@ref
                    match makeO()
                        .Some(let n) => keep = n@ref
                        .None => ()
                    inspect(keep)
                func m8i()
                    var o: Option<i32> = .Some(1)
                    let r = o@ref
                    match o
                        .Some(let n) => o = .None
                        .None => ()
                    inspect(r)
                func f1i()
                    var values: Array<i32> = [1, 2]
                    for v in values => values.append(1)
                func f1v()
                    var values: Array<i32> = [1, 2]
                    for v in values => inspect(v)
                    values.append(1)
                func f2v()
                    var values: Array<i32> = [1, 2]
                    for v in values@uniq => v@follow += 1
                func f2i()
                    var values: Array<i32> = [1, 2]
                    let r = values@ref
                    for v in values@uniq => v@follow += 1
                    inspect(r)
                func f3i()
                    var values: Array<i32> = [1, 2]
                    let u = values@uniq
                    for v in values => inspect(v)
                    touchA(u)
                func f4v()
                    var z = 0
                    var values: Array<i32> = [1, 2]
                    var keep: ref/i32 = z@ref
                    for v in values => keep = v
                    inspect(keep)
                func f4i()
                    var z = 0
                    var values: Array<i32> = [1, 2]
                    var keep: ref/i32 = z@ref
                    for v in values => keep = v
                    values.append(1)
                    inspect(keep)
                func f5i()
                    var b = SubjectBag.init()
                    for v in b.items => b.items.append(1)
                func f5v()
                    var b = SubjectBag.init()
                    for v in b.items => b.count += 1
                """));

    // SPEC 14.8.3 beside its examples (TheSpecExamplesGetTheirLoanCodes): Subject protection reaches the referent Pattern testing reaches
    // through a reference (g2i), and a guard's write is an ordinary write, meeting a Loan live across the match (g4i); a guard may change a
    // Place disjoint from the Subject (g1v, g4v, read again by the arm). Guard cleanup ends the guard's Loans, so the arm may write a Place
    // the guard read (g3v) or the Subject when the body binding is dead (g3w), but not while it is live (g3i, SPEC 15.1.6). A transfer out
    // of a guard returns a value read from the candidate (g5v, SPEC 16.2.1).
    [Fact]
    public void AGuardProtectsItsSubjectUntilItsCleanup()
        => Assert.Equal(
            ["g2i: resetO(o@uniq) CallActivationConflict_Kd", "g3i: o = .None", "g4i: bump(x@uniq) CallActivationConflict_Kd"],
            Problems(SubjectShapes + """
                func g1v()
                    var other = 0
                    var pair: (Option<i32>, i32) = (.Some(1), 2)
                    match pair
                        (.Some(let a), _) if bump(other@uniq) => ()
                        _ => ()
                func g2i()
                    var o: Option<i32> = .Some(1)
                    let r = o@ref
                    match r
                        .Some(let n) if resetO(o@uniq) => ()
                        _ => ()
                func g3v()
                    var x = 1
                    var o: Option<i32> = .Some(1)
                    match o
                        .Some(let n) if x > 0 => x = 2
                        _ => x = 3
                func g3w()
                    var o: Option<i32> = .Some(1)
                    match o
                        .Some(let n) if n > 0 => o = .None
                        _ => ()
                func g3i()
                    var o: Option<i32> = .Some(1)
                    match o
                        .Some(let n) if n > 0
                            o = .None
                            inspect(n)
                        _ => ()
                func g4i()
                    var x = 1
                    let r = x@ref
                    var o: Option<i32> = .Some(1)
                    match o
                        .Some(let n) if bump(x@uniq) => ()
                        _ => ()
                    inspect(r)
                func g4v()
                    var x = 1
                    var o: Option<i32> = .Some(1)
                    match o
                        .Some(let n) if bump(x@uniq) => inspect(x@ref)
                        _ => x = 2
                func g5v(o: ref/Option<i32>) -> i32
                    match o
                        .Some(let n) if (return n@follow) => ()
                        _ => ()
                    return 0
                """));
}
