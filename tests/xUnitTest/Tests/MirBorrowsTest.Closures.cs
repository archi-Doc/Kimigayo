// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A5-F7's rule (SPEC 15.8.2, 7.7.2, 7.7.3, 15.6.4; docs/dev/DECISIONS.md R4-M126, R4-M148): a Closure call is a receiver call. Its
/// contract's input 0 is its call receiver, the callee operand, a store into which takes W as any argument's does; its result follows the
/// contract's rows; and a view whose case admits a Shared receiver while the call acquires an Exclusive one is held (PD13).</summary>
public sealed partial class MirBorrowsTest
{
    // SPEC 15.8.2, 7.7.2: an Exclusive call may retain an argument in its `var` bindings and in what its captured exclusive references
    // refer to, never in a `let` binding itself. The four forms of the difference ledger (exchangeBad, blockBad, holderBad, holderMovedBad),
    // a captured parameter (slotBad, slotMovedBad), an unwritten `var` binding (varBad), SPEC 15.8.2's `update` with its commented line
    // (updateBad) and a Closure called through a reference (refStoreBad, refExchangeBad, refTwiceBad: the receiver read through one
    // Semantics layer) keep the argument's Loan in the environment, as a direct call storing a captured value through a captured exclusive
    // referent keeps that value's Loan in the referent (directStoreBad, directStoreTypedBad: PLAN G269's class B), where the dev MIR
    // accepted them all; a store past a captured exclusive reference also stores the receiver's own content (rule G's self-store, OQ-F7-3
    // (a)), so the read of `holder` or `slot` after the call errs.
    // A Closure captured into another Closure is read as its body sees it too (nestedVarStoreBad: the `var` local's own Type names the
    // Origins its initializer infers, so its store into `last` was lost on the dev MIR and at 5c5d47ff2; BU's review B1).
    // A `let` binding (letOk, updateOk), a read before the write (exchangeOk), a referent no later use reads (directStoreOk,
    // directStoreTypedOk) and a Closure no later use reads (setOk, nestedVarStoreOk) are valid: the
    // Closure holds its environment's parts, so W stops at a holder whose parts no call may write past (with an opaque Closure W reached E
    // through `state`'s Φ_E and setOk and exchangeOk erred).
    [Fact]
    public void AClosureCallStoresIntoItsReceiver()
        => Assert.Equal(
            ["exchangeBad: z@follow = 42", "blockBad: let y: i32 = 5", "holderBad: z@follow = 42", "holderBad: holder@follow",
                "holderMovedBad: z@follow = 42", "holderMovedBad: holder@follow", "slotBad: z@follow = 42", "slotMovedBad: z@follow = 42",
                "varBad: z@follow = 42", "updateBad: z@follow = 43", "refStoreBad: z@follow = 42", "refStoreBad: holder@follow",
                "refExchangeBad: z@follow = 42", "refTwiceBad: z@follow = 42", "refTwiceBad: holder@follow",
                "directStoreBad: let short = 2", "directStoreBad: slot@follow", "directStoreTypedBad: let short = 2",
                "directStoreTypedBad: slot@follow", "nestedVarStoreBad: z@follow = 42"],
            Problems("""
                func exchangeBad(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    var state: ref/i32 during x = x
                    var exchange = func [var state] (value: ref/i32 during x) -> ref/i32 during x
                        let old = state
                        state = value
                        return old
                    _ = exchange(z@follow@ref)
                    let result = exchange(x)
                    z@follow = 42
                    return result@follow
                func exchangeOk(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    var state: ref/i32 during x = x
                    var exchange = func [var state] (value: ref/i32 during x) -> ref/i32 during x
                        let old = state
                        state = value
                        return old
                    _ = exchange(z@follow@ref)
                    let result = exchange(x)
                    let n = result@follow
                    z@follow = 42
                    return n
                func blockBad() -> i32
                    let x: i32 = 4
                    let r = x@ref
                    let start = r
                    var c = func [var start] (n: ref/i32 during r) -> ref/i32 during r
                        let old = start
                        start = n
                        return old
                    if x > 0
                        let y: i32 = 5
                        c(y@ref)
                    let z = c(r)
                    return z@follow
                func holderBad(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    var holder: ref/i32 during x = x
                    var store = func [holder@uniq] (b: ref/i32 during x) -> i32
                        holder@follow = b
                        return 0
                    store(z@follow@ref)
                    z@follow = 42
                    return holder@follow
                func holderMovedBad(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    var holder: ref/i32 during x = x
                    var store = func [holder@uniq] (b: ref/i32 during x) -> i32
                        holder@follow = b
                        return 0
                    store@move(z@follow@ref)
                    z@follow = 42
                    return holder@follow
                func slotBad(z: uniq/i32, x: ref/i32, slot: uniq/(ref/i32 during x)) -> i32
                    origin z outlives x
                    var store = func [slot@move] (b: ref/i32 during x) -> i32
                        slot@follow = b
                        return 0
                    store(z@follow@ref)
                    z@follow = 42
                    return 0
                func slotMovedBad(z: uniq/i32, x: ref/i32, slot: uniq/(ref/i32 during x)) -> i32
                    origin z outlives x
                    var store = func [slot@move] (b: ref/i32 during x) -> i32
                        slot@follow = b
                        return 0
                    store@move(z@follow@ref)
                    z@follow = 42
                    return 0
                func letOk(z: uniq/i32, x: ref/i32, w: uniq/i32) -> i32
                    origin z outlives x
                    let r: ref/i32 during x = x
                    var c = func [r, w] (v: ref/i32 during x) -> i32
                        w@follow = v@follow
                        return r@follow
                    _ = c(z@follow@ref)
                    z@follow = 42
                    return c(x)
                func varBad(z: uniq/i32, x: ref/i32, w: uniq/i32) -> i32
                    origin z outlives x
                    let r: ref/i32 during x = x
                    var c = func [var r, w] (v: ref/i32 during x) -> i32
                        w@follow = v@follow
                        return r@follow
                    _ = c(z@follow@ref)
                    z@follow = 42
                    return c(x)
                func setOk(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    var state: ref/i32 during x = x
                    var set = func [var state] (value: ref/i32 during x) => state = value
                    set(z@follow@ref)
                    z@follow = 42
                    return 0
                func updateOk(z: uniq/i32, x: ref/i32, out: uniq/i32)
                    origin z outlives x
                    let first: ref/i32 during x = x
                    var copyOut = func [first, out] (value: ref/i32 during x) => out@follow = value@follow + first@follow
                    copyOut(z@follow@ref)
                    z@follow = 42
                    copyOut(x)
                    var last: ref/i32 during x = x
                    var remember = func [var last] (value: ref/i32 during x) => last = value
                    remember(z@follow@ref)
                    remember(x)
                func updateBad(z: uniq/i32, x: ref/i32, out: uniq/i32)
                    origin z outlives x
                    let first: ref/i32 during x = x
                    var copyOut = func [first, out] (value: ref/i32 during x) => out@follow = value@follow + first@follow
                    copyOut(z@follow@ref)
                    z@follow = 42
                    copyOut(x)
                    var last: ref/i32 during x = x
                    var remember = func [var last] (value: ref/i32 during x) => last = value
                    remember(z@follow@ref)
                    z@follow = 43
                    remember(x)
                func refStoreBad(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    var holder: ref/i32 during x = x
                    var store = func [holder@uniq] (b: ref/i32 during x) -> i32
                        holder@follow = b
                        return 0
                    let u = store@uniq
                    u(z@follow@ref)
                    z@follow = 42
                    return holder@follow
                func refExchangeBad(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    var state: ref/i32 during x = x
                    var exchange = func [var state] (value: ref/i32 during x) -> ref/i32 during x
                        let old = state
                        state = value
                        return old
                    let u = exchange@uniq
                    _ = u(z@follow@ref)
                    let result = u(x)
                    z@follow = 42
                    return result@follow
                func refTwiceBad(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    var holder: ref/i32 during x = x
                    var store = func [holder@uniq] (b: ref/i32 during x) -> i32
                        holder@follow = b
                        return 0
                    let u = store@uniq
                    let v = u@follow@uniq
                    v(z@follow@ref)
                    z@follow = 42
                    return holder@follow
                func directStoreBad() -> i32
                    let long = 1
                    var slot = long@ref
                    if true
                        let short = 2
                        let sq = short@ref
                        var c = func [sq, slot@uniq] (p: Option<ref/(ref/i32 during sq) during slot>) -> i32
                            slot@follow = sq
                            return 0
                        let n = c(Option.None)
                    return slot@follow
                func directStoreOk() -> i32
                    let long = 1
                    var slot = long@ref
                    if true
                        let short = 2
                        let sq = short@ref
                        var c = func [sq, slot@uniq] (p: Option<ref/(ref/i32 during sq) during slot>) -> i32
                            slot@follow = sq
                            return 0
                        let n = c(Option.None)
                    return 0
                func directStoreTypedBad() -> i32
                    let long = 1
                    var slot = long@ref
                    if true
                        let short = 2
                        let sq = short@ref
                        var c = func [sq, slot@uniq] (p: Option<ref/(ref/i32 during sq) during slot>) -> i32
                            slot@follow = sq
                            return 0
                        let none: Option<ref/(ref/i32 during sq) during slot> = Option.None
                        let n = c(none)
                    return slot@follow
                func directStoreTypedOk() -> i32
                    let long = 1
                    var slot = long@ref
                    if true
                        let short = 2
                        let sq = short@ref
                        var c = func [sq, slot@uniq] (p: Option<ref/(ref/i32 during sq) during slot>) -> i32
                            slot@follow = sq
                            return 0
                        let none: Option<ref/(ref/i32 during sq) during slot> = Option.None
                        let n = c(none)
                    return 0
                func nestedVarStoreBad(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    var last: ref/i32 during x = x
                    var remember = func [var last] (value: ref/i32 during x) -> ref/i32 during x
                        let old = last
                        last = value
                        return old
                    var outer = func [var remember@move] (b: ref/i32 during x) -> ref/i32 during x => remember(b)
                    _ = outer(z@follow@ref)
                    z@follow = 42
                    let r = outer(x)
                    return r@follow
                func nestedVarStoreOk(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    var last: ref/i32 during x = x
                    var remember = func [var last] (value: ref/i32 during x) -> ref/i32 during x
                        let old = last
                        last = value
                        return old
                    var outer = func [var remember@move] (b: ref/i32 during x) -> ref/i32 during x => remember(b)
                    _ = outer(z@follow@ref)
                    z@follow = 42
                    return 0
                """));

    // SPEC 15.8.2, 15.6.4: a Closure call's result follows its contract's rows: a parameter's (resultArgumentOk, where the dev MIR's
    // temporary rule carried the whole environment into the result and erred at `n = 5`), the receiver's content for a capture's Origin
    // (localBad, byRefBad, fixedBad, the raw forms, which the temporary rule alone rejected), and a fixed signature Origin through the
    // argument (signatureResultBad) or a capture the Origin or a premise relates to it (signatureCaptureBad, signaturePremiseBad): the
    // receiver's atoms are its environment's parts, and a Loan a fixed signature Origin carries reaches the result through those rows
    // (DECISIONS R4-M148; the Closure Type's fixed signature Origins, SPEC 15.2.3, are F1's); a Closure captured into another, from a `var`
    // local whose Type names its initializer's inferred Origins, is read as its body sees it (nestedVarResultBad, which the dev MIR's
    // temporary rule rejected and 5c5d47ff2 accepted, BU's review B1), also through a Consuming receiver, by value (nestedConsumingBad,
    // likewise). A Function value keeps C8's contract, with no receiver input (functionBad, functionOk).
    [Fact]
    public void AClosureCallsResultFollowsItsRows()
        => Assert.Equal(
            ["localBad: n = 5", "byRefBad: n = 5", "fixedBad: x@follow = 5", "rawBad: y = 5", "rawTupleBad: y = 5",
                "signatureResultBad: z@follow = 42", "signatureCaptureBad: z@follow = 42", "signaturePremiseBad: z@follow = 42",
                "functionBad: n = 5", "nestedVarResultBad: z@follow = 42", "nestedConsumingBad: z@follow = 42"],
            Problems("""
                func usep2<T>(p: raw/T) => ()
                func resultArgumentOk() -> i32
                    var n: i32 = 1
                    let m: i32 = 2
                    let r = n@ref
                    let c = func [r] (b: ref/i32) -> ref/i32 during b => b
                    let v = c(m@ref)
                    n = 5
                    return v@follow
                func localBad() -> i32
                    var n: i32 = 1
                    let rv = n@ref
                    let f = func [rv] () => rv
                    let r = f()
                    n = 5
                    return r@follow
                func localOk() -> i32
                    var n: i32 = 1
                    let rv = n@ref
                    let f = func [rv] () => rv
                    let r = f()
                    let k = r@follow
                    n = 5
                    return k
                func byRefBad() -> i32
                    var n: i32 = 1
                    let rv = n@ref
                    let f = func [rv] () => rv
                    let h = f@ref
                    let r = h()
                    n = 5
                    return r@follow
                func fixedBad(x: uniq/i32) -> i32
                    let rv: ref/i32 during x = x@follow@ref
                    let f = func [rv] () => rv
                    let r = f()
                    x@follow = 5
                    return r@follow
                func rawBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    let p = w@raw@raw/u8
                    let f = func [p] () => p
                    let q = f()
                    y = 5
                    usep2(q)
                func rawTupleBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    let p = w@raw@raw/u8
                    let f = func [p] () => (p, 1)
                    let q = f()
                    y = 5
                    usep2(q.0)
                func signatureResultBad(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    let f = func (b: ref/i32 during x) -> ref/i32 during x => b
                    let r = f(z@follow@ref)
                    z@follow = 42
                    return r@follow
                func signatureResultOk(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    let f = func (b: ref/i32 during x) -> ref/i32 during x => b
                    let r = f(z@follow@ref)
                    let n = r@follow
                    z@follow = 42
                    return n
                func signatureCaptureBad(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    let r: ref/i32 during x = z@follow@ref
                    let f = func [r] () -> ref/i32 during x => r
                    let v = f()
                    z@follow = 42
                    return v@follow
                func signatureCaptureOk(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    let r: ref/i32 during x = z@follow@ref
                    let f = func [r] () -> ref/i32 during x => r
                    let v = f()
                    let n = v@follow
                    z@follow = 42
                    return n
                func signaturePremiseBad(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    let r: ref/i32 during z = z@follow@ref
                    let f = func [r] () -> ref/i32 during x => r
                    let v = f()
                    z@follow = 42
                    return v@follow
                func signaturePremiseOk(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    let r: ref/i32 during z = z@follow@ref
                    let f = func [r] () -> ref/i32 during x => r
                    let v = f()
                    let n = v@follow
                    z@follow = 42
                    return n
                func functionBad(g: (ref/i32) -> ref/i32) -> i32
                    var n: i32 = 1
                    let r = g(n@ref)
                    n = 5
                    return r@follow
                func functionOk(g: (ref/i32) -> ref/i32 during a, a: ref/i32) -> i32
                    var n: i32 = 1
                    let r = g(n@ref)
                    n = 5
                    return r@follow
                func nestedVarResultBad(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    let r: ref/i32 during x = z@follow@ref
                    var get = func [r] () -> ref/i32 during x => r
                    let outer = func [get@move] () -> ref/i32 during x => get()
                    let v = outer()
                    z@follow = 42
                    return v@follow
                func nestedVarResultOk(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    let r: ref/i32 during x = z@follow@ref
                    var get = func [r] () -> ref/i32 during x => r
                    let outer = func [get@move] () -> ref/i32 during x => get()
                    let v = outer()
                    let n = v@follow
                    z@follow = 42
                    return n
                func nestedConsumingBad(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    let r: ref/i32 during x = z@follow@ref
                    var get = func [r] () -> ref/i32 during x => r
                    let outer = func [get@move] () -> ref/i32 during x => get@move()
                    let v = outer@move()
                    z@follow = 42
                    return v@follow
                func nestedConsumingOk(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    let r: ref/i32 during x = z@follow@ref
                    var get = func [r] () -> ref/i32 during x => r
                    let outer = func [get@move] () -> ref/i32 during x => get@move()
                    let v = outer@move()
                    let n = v@follow
                    z@follow = 42
                    return n
                """));

    // SPEC 15.8.2, 15.6.4, 7.7.3 (DECISIONS R4-M152, R4-M126 P8, P9): a result over the environment is bound, R1's located Unsupported_Kd
    // gone from Binding (the old analysis's gate keeps it on the default pipeline). A result naming the receiver ρ takes the acquisition's
    // Origin and carries the receiver's Loan, so the Closure's Move (envMoveBad), a second Exclusive call (envTwiceBad) and the end of a
    // temporary receiver (temporaryBad, movedTemporaryBad) conflict while it lives, as does a write of what a read through the receiver
    // reaches (envReadBad); a Reborrow through a captured `uniq` is contained in ρ too (reborrowBad, sharedThroughUniqBad), while one under a
    // Consuming receiver keeps the capture's Origin (consumingOk, consumingTypedOk) and its parent Loans (consumingBad); a moved-out capture
    // is the capture's value (movedOk), carrying every Loan of the owned receiver (movedBad). Each valid twin uses the result first (envOk,
    // reborrowOk, sharedThroughUniqOk, movedTemporaryOk); a nested Closure called in its maker's body (nestedCallOk) and a returned Closure
    // left uncalled (returnedOk) are valid.
    [Fact]
    public void AClosureCallsResultOverItsEnvironmentFollowsItsReceiver()
        => Assert.Equal(
            ["envMoveBad: get", "envTwiceBad: bump", "envReadBad: y = 5", "reborrowBad: c", "sharedThroughUniqBad: c", "consumingBad: x = 5",
                "movedBad: y = 5", "temporaryBad: (", "movedTemporaryBad: c@move"],
            Problems("""
                func drop1(s: string) => ()
                func envOk()
                    let n: i32 = 4
                    let get = func [n] () => n@ref
                    let v = get()
                    inspect(v)
                func envMoveBad()
                    let n: i32 = 4
                    let get = func [n] () => n@ref
                    let v = get()
                    let moved = get@move
                    inspect(v)
                func envTwiceBad()
                    let n: i32 = 4
                    var bump = func [var n] ()
                        n += 1
                        return n@ref
                    let a = bump()
                    let b = bump()
                    inspect(a)
                func envReadBad()
                    let n: i32 = 4
                    var y: i32 = 2
                    let r = y@ref
                    let get = func [n, r] () => n@ref
                    let v = get()
                    y = 5
                    inspect(v)
                func reborrowBad()
                    var x: i32 = 1
                    let u = x@uniq
                    var c = func [u] () => u
                    let a = c()
                    let m = c@move
                    a@follow = 2
                func reborrowOk()
                    var x: i32 = 1
                    let u = x@uniq
                    var c = func [u] () => u
                    let a = c()
                    a@follow = 2
                    let m = c@move
                func sharedThroughUniqBad()
                    var x: i32 = 1
                    let u = x@uniq
                    let c = func [u] () => u@follow@ref
                    let a = c()
                    let m = c@move
                    inspect(a)
                func sharedThroughUniqOk()
                    var x: i32 = 1
                    let u = x@uniq
                    let c = func [u] () => u@follow@ref
                    let a = c()
                    inspect(a)
                    let m = c@move
                func consumingTypedOk()
                    var x: i32 = 1
                    let u = x@uniq
                    let t = "a"
                    let c = func [u, t@move] () -> uniq/i32 during u
                        drop1(t@move)
                        return u
                    let a = c@move()
                    a@follow = 3
                func consumingOk()
                    var x: i32 = 1
                    let u = x@uniq
                    let t = "a"
                    let c = func [u, t@move] ()
                        drop1(t@move)
                        return u
                    let a = c@move()
                    a@follow = 3
                func consumingBad()
                    var x: i32 = 1
                    let u = x@uniq
                    let t = "a"
                    let c = func [u, t@move] ()
                        drop1(t@move)
                        return u
                    let a = c@move()
                    x = 5
                    a@follow = 3
                func movedOk()
                    var x: i32 = 1
                    let u = x@uniq
                    let c = func [u] () => u@move
                    let a = c@move()
                    a@follow = 3
                func movedBad()
                    var x: i32 = 1
                    var y: i32 = 2
                    let u = x@uniq
                    let r = y@ref
                    let c = func [u, r] () => u@move
                    let a = c@move()
                    y = 5
                    a@follow = 3
                func temporaryBad()
                    let n: i32 = 7
                    let r = (func [n] () => n@ref)()
                    let s = r@follow
                func movedTemporaryBad()
                    var x: i32 = 1
                    let u = x@uniq
                    var c = func [u] () => u
                    let a = c@move()
                    a@follow = 2
                func movedTemporaryOk()
                    var x: i32 = 1
                    let u = x@uniq
                    var c = func [u] () => u
                    c@move()@follow = 2
                func nestedCallOk() -> i32
                    var x: i32 = 1
                    let u = x@uniq
                    var outer = func [u] () -> i32
                        let i = func [u] () => u@move
                        let p = i@move()
                        p@follow = 2
                        return 0
                    return outer()
                func returnedOk()
                    let n = 7
                    let outer = func [n] () => func [n@ref] () => n
                    let inner = outer()
                """));

    // SPEC 15.8.2, 7.7.2 (DECISIONS R4-M152, R4-M123 F3; PLAN G273): the maker's calls of a returned Closure bind and carry its receiver's
    // Loan, and the returned Closures' own calls bind with the maker call's images (ClosureResultOriginTest's G273 rows), so the second
    // Exclusive `outer()` conflicts while the first one's Closure lives (n1).
    [Fact]
    public void AClosureReturnedFromAClosureCallKeepsItsMakersReceiverLoan()
        => Assert.Equal(["n1: outer"], Problems("""
            func n1()
                var x: i32 = 1
                let u = x@uniq
                var outer = func [u] () => func [u] () => u@move
                let a = outer()
                let b = outer()
                let pa = a@move()
                let pb = b@move()
                pa@follow = 1
                pb@follow = 2
            """));

    // PD13 (SPEC 7.7.3; DECISIONS R4-M126 (iii), R4-M148): a call acquires a Closure in its strongest receiver, so where the view's case
    // admits a Shared one (`value` Copies in the owner case and the raw case, Reborrows in the uniq case) every rejection of the view is the
    // located Unsupported_Kd at its subject, the call's own conflict (heldBad, heldNested) and an independent one alike (heldIndependent,
    // heldBefore), while the uniq view keeps its Language error. A body no view decides follows view 0, which stands for every view: the
    // owner case held (heldMove, heldMoveRef: view 0 Shared), the uniq case not (exclusiveMoveRaw: view 0 Exclusive, its Language error for
    // every view). A held view with no rejection is unchanged (caseOk), and a Shared Closure called through `u: uniq/Env` is acquired by a
    // shared reference to its referent, so no view is held (sharedThroughUniqOk, sharedThroughUniqLiveOk).
    [Fact]
    public void AViewWhoseCaseAdmitsLessThanTheCallAcquiresIsHeld()
    {
        var source = """
            func heldBad<s/T>(value: s/T) -> ()
                s is owner or uniq
                T is Copy
                var h = func [value] () -> ()
                    let local = value
                    _ = local
                let r = h@ref
                h()
                inspect(r)
            func heldIndependent<s/T>(value: s/T) -> ()
                s is owner or uniq
                T is Copy
                var h = func [value] () -> ()
                    let local = value
                    _ = local
                h()
                var n: i32 = 1
                let p = n@ref
                n = 2
                inspect(p)
            func heldBefore<s/T>(value: s/T) -> ()
                s is owner or uniq
                T is Copy
                var n: i32 = 1
                let p = n@ref
                n = 2
                inspect(p)
                var h = func [value] () -> ()
                    let local = value
                    _ = local
                h()
            func heldNested<s/T>(value: s/T) -> ()
                s is owner or uniq
                T is Copy
                var outer = func [value] () -> ()
                    var inner = func [value] () -> ()
                        let local = value
                        _ = local
                    inner()
                let r = outer@ref
                outer()
                inspect(r)
            func heldMove<s/T>(value: s/T) -> ()
                s is owner or uniq
                T is Copy
                var h = func [value@move] () -> ()
                    let local = value
                    _ = local
                let r = h@ref
                h()
                inspect(r)
            func heldMoveRef<s/T>(value: s/T) -> ()
                s is ref or uniq
                T is Copy
                var h = func [value@move] () -> ()
                    let local = value
                    _ = local
                let r = h@ref
                h()
                inspect(r)
            func exclusiveMoveRaw<s/T>(value: s/T) -> ()
                s is uniq or raw
                T is Copy
                var h = func [value@move] () -> ()
                    let local = value
                    _ = local
                let r = h@ref
                h()
                inspect(r)
            func heldRowRaw<s/T>(value: s/T) -> ()
                s is uniq or raw
                T is Copy
                var h = func [value] () -> ()
                    let local = value
                    _ = local
                let r = h@ref
                h()
                inspect(r)
            func caseOk<s/T>(value: s/T) -> ()
                s is owner or uniq
                T is Copy
                var h = func [value] () -> ()
                    let local = value
                    _ = local
                h()
                h()
            func sharedThroughUniqOk() -> i32
                var n: i32 = 1
                var c = func [n] () -> i32 => n
                let u = c@uniq
                let a = u()
                return a
            func sharedThroughUniqLiveOk() -> i32
                var n: i32 = 1
                var c = func [n] () -> i32 => n
                let u = c@uniq
                let s = u@follow@ref
                let a = u()
                inspect(s)
                return a
            """;
        Assert.Equal(
            ["exclusiveMoveRaw: h() CallActivationConflict_Kd views 3", "heldBad: h() CallActivationConflict_Kd views 2",
                "heldBad: h() Unsupported_Kd views 1", "heldBefore: n = 2 ComparisonLoanConflict_Kd views 2",
                "heldBefore: n = 2 Unsupported_Kd views 1", "heldIndependent: n = 2 ComparisonLoanConflict_Kd views 2",
                "heldIndependent: n = 2 Unsupported_Kd views 1", "heldMove: h() Unsupported_Kd views 3", "heldMoveRef: h() Unsupported_Kd views 3",
                "heldNested: outer() CallActivationConflict_Kd views 2", "heldNested: outer() Unsupported_Kd views 1",
                "heldRowRaw: h() CallActivationConflict_Kd views 1", "heldRowRaw: h() Unsupported_Kd views 2"],
            ViewProblems(source));
        var program = MirBuildTest.Compile(Prelude + source).BuildMir();
        var shared = Body(program, "sharedThroughUniqOk");
        var call = shared.Blocks.ToArray().Single(static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee.Kind: MirCalleeKind.Value }).Terminator;
        Assert.Equal(Kimi.Compiler.Parsing.SemanticsKind.Ref, shared.TypeOf(call.Operand.Place)!.Semantics);
    }

    // Rule G's self-store reaches a Closure receiver as a method's `self` (OQ-F7-3 (a); DECISIONS R4-M148): a call through a receiver whose
    // content is exclusively borrowed storage stores that content, the capture's own Loan, past it, so a read of the captured binding after
    // the call errs (holderReadBad, by SPEC 15.6.4's per-argument letter; PLAN G148 (2)'s GXS would recover it), also where the stored part
    // is abstract and so may be an exclusive reference (R4-M147's Q1 (a)): in every case view (selfPair, selfPairChild), as for the method
    // analog (methodAbstract, unchanged), and beside the argument's true store into the environment (pairStoreBad, pairCellBad: `v@follow
    // = x` in the uniq view) and the argument's store into the environment given one kind for every holder W reaches, E past the written
    // capture included (pairStoreRead, pairCellRead: PLAN G276, WK), with the receiver's Content rows. A concrete referent with no
    // Loan-capable part stores nothing (selfScalarOk, selfAbstractOk read `slot` itself).
    [Fact]
    public void RuleGsSelfStoreReachesAClosureReceiver()
        => Assert.Equal(
            ["holderReadBad: holder@follow 1", "methodAbstract: slot 1", "methodAbstract: var slot = other 1",
                "pairCellBad: cell.item@follow 3", "pairCellBad: v@follow = x 2", "pairCellBad: var cell = Cell2c3.init(item: other@move) 3",
                "pairCellRead: cell.item@follow 3", "pairCellRead: v@follow = x 2", "pairCellRead: var cell = Cell2c3.init(item: other@move) 3",
                "pairStoreBad: slot@follow 3", "pairStoreBad: v@follow = x 2", "pairStoreBad: var slot = other@move 3",
                "pairStoreRead: slot@follow 3", "pairStoreRead: v@follow = x 2", "pairStoreRead: var slot = other@move 3",
                "selfPair: slot@follow 3", "selfPair: var slot = other@move 3",
                "selfPairChild: slot@follow 3", "selfPairChild: var slot = other@move 3"],
            ViewProblems("""
                struct Cell2c3<T>
                    public var item: T
                    public init(item: T) => self.item = item@move
                struct Keep2c3<T> {a}
                    var slot: uniq/T during a
                    public init(slot: uniq/T during a) => self.slot = slot
                    public func set(self: uniq/Self) => ()
                func holderReadBad(z: uniq/i32, x: ref/i32) -> i32
                    origin z outlives x
                    var holder: ref/i32 during x = x
                    var store = func [holder@uniq] (b: ref/i32 during x) -> i32
                        holder@follow = b
                        return 0
                    store(x)
                    return holder@follow
                func pairStoreBad<s/T>(value: s/T, other: s/T, x: T) -> ()
                    s is owner or uniq
                    T is Copy
                    var v = value@move
                    var slot = other@move
                    var set = func [slot@uniq] (y: s/T) -> ()
                        slot@follow = y@move
                    let child = v
                    set(child@move)
                    v@follow = x
                    _ = slot@follow
                func pairStoreRead<s/T>(value: s/T, other: s/T, x: T) -> ()
                    s is owner or uniq
                    T is Copy
                    var v = value@move
                    var slot = other@move
                    var set = func [slot@uniq] (y: s/T) -> ()
                        slot@follow = y@move
                    let child = v
                    set(child@move)
                    _ = slot@follow
                    v@follow = x
                func pairCellBad<s/T>(value: s/T, other: s/T, x: T) -> ()
                    s is owner or uniq
                    T is Copy
                    var v = value@move
                    var cell = Cell2c3.init(item: other@move)
                    var set = func [cell@uniq] (y: s/T) -> ()
                        cell@follow.item = y@move
                    let child = v
                    set(child@move)
                    v@follow = x
                    _ = cell.item@follow
                func pairCellRead<s/T>(value: s/T, other: s/T, x: T) -> ()
                    s is owner or uniq
                    T is Copy
                    var v = value@move
                    var cell = Cell2c3.init(item: other@move)
                    var set = func [cell@uniq] (y: s/T) -> ()
                        cell@follow.item = y@move
                    let child = v
                    set(child@move)
                    _ = cell.item@follow
                    v@follow = x
                func selfPair<s/T>(other: s/T) -> ()
                    s is owner or uniq
                    T is Copy
                    var slot = other@move
                    var set = func [slot@uniq] () -> ()
                        _ = slot@follow
                    set()
                    _ = slot@follow
                func selfPairChild<s/T>(value: s/T, other: s/T, x: T) -> ()
                    s is owner or uniq
                    T is Copy
                    var v = value@move
                    var slot = other@move
                    var set = func [slot@uniq] () -> ()
                        _ = slot@follow
                    let child = v
                    set()
                    _ = slot@follow
                    v@follow = x
                func selfAbstractOk<T>(other: T) -> ()
                    T is Copy
                    var slot = other
                    var set = func [slot@uniq] () -> ()
                        _ = slot@follow
                    set()
                    _ = slot
                func selfScalarOk(other: i32) -> ()
                    var slot = other
                    var set = func [slot@uniq] () -> ()
                        _ = slot@follow
                    set()
                    _ = slot
                func methodAbstract<T>(other: T) -> ()
                    T is Copy
                    var slot = other
                    var h = Keep2c3.init(slot@uniq)
                    h.set()
                    _ = slot
                """).Select(static x => x.Replace(" ComparisonLoanConflict_Kd views", string.Empty, StringComparison.Ordinal)).ToArray());

    // SPEC 15.6.4, 8.1.2 (PLAN G147 (8i); A5 step 2 plan 2c-4, unit 0c; DECISIONS BU-49): a by-value owner-or-uniq pair carrier has no
    // Value self-store row, only the borrow case's Content one, so a call moving the child `let child = v` of a pair binding into a result
    // (A6-keep-ok, B7-second), as the bare capture of a by-value Closure (A9-keep-ok) or twice by value (takeTwice,
    // ClosureReceiverClassificationTest's body) stores no Loan on `v` into the parameter's referent: `v@follow = x` and the second
    // `take0c` are valid in the owner and the uniq view, as the corpus
    // (tests/diagnostics/pair-cases.json) and the old path accept. A call storing the child through `put0c` (A2-put-ok, B4-chained,
    // B5-result-put) is valid too: only the written local `slot` (its root, Own) can hold the child's own value, and the storage past
    // it keeps what a read through the child carries (the owner's WK decision, DECISIONS R4-M154; on dev W carried `1 Value store 0`
    // past `slot` into E). A3-closure, which reads the capture `slot` after the write, stays rejected (pairStoreBad above).
    [Fact]
    public void AByValuePairCarrierIsNotStoredIntoItsOwnReferent()
        => Assert.Equal(
            [],
            ViewProblems("""
            func put0c<U>(slot: uniq/U, y: U) -> () => slot@follow = y@move
            func keep0c<F>(f: F) -> F => f@move
            func second0c<A, B>(a: A, b: B) -> B => b@move
            func take0c<t/U>(y: t/U) -> ()
                t is owner or uniq
                U is Copy
                return
            func takeTwice<s/T>(value: s/T) -> ()
                s is owner or uniq
                T is Copy
                var h = func [value] () -> ()
                    take0c(value)
                    take0c((value))
                h()
            func a2PutOk<s/T>(value: s/T, other: s/T, x: T) -> ()
                s is owner or uniq
                T is Copy
                var v = value@move
                var slot = other@move
                let child = v
                put0c(slot@uniq, child@move)
                _ = slot@follow
                v@follow = x
            func a6KeepOk<s/T>(value: s/T, x: T) -> ()
                s is owner or uniq
                T is Copy
                var v = value@move
                let child = v
                let kept = keep0c(child@move)
                _ = kept@follow
                v@follow = x
            func a9KeepOk<s/T>(value: s/T, x: T) -> ()
                s is owner or uniq
                T is Copy
                var v = value@move
                let h = func [v] () -> ()
                    _ = v@follow
                let kept = keep0c(h@move)
                kept()
                v@follow = x
            func b4Chained<s/T>(value: s/T, other: s/T, third: s/T, x: T) -> ()
                s is owner or uniq
                T is Copy
                var v = value@move
                var a = other@move
                var b = third@move
                let child = v
                put0c(a@uniq, child@move)
                put0c(b@uniq, a@move)
                _ = b@follow
                v@follow = x
            func b5ResultPut<s/T>(value: s/T, other: s/T, x: T) -> ()
                s is owner or uniq
                T is Copy
                var v = value@move
                var slot = other@move
                let child = v
                put0c(slot@uniq, keep0c(child@move))
                _ = slot@follow
                v@follow = x
            func b7Second<s/T, t/U>(value: s/T, other: t/U, x: T) -> ()
                s is owner or uniq
                t is owner or uniq
                T is Copy
                U is Copy
                var v = value@move
                let child = v
                let r = second0c(child@move, other@move)
                v@follow = x
                _ = r@follow
            """).Select(static x => x.Replace(" ComparisonLoanConflict_Kd views", string.Empty, StringComparison.Ordinal)).ToArray());

    // SPEC 15.6.4 (DECISIONS BU-49, superseded in part by R4-M154): a call given `.Some(x@uniq)` by value stores into x what a read
    // through the borrow carries, never the borrow's own Loan: x's referent `ref/i32 during q` can hold neither the borrow nor a borrow
    // formed through it, whatever its Origins (the owner's WK decision; BU-49's own-Origin row kept it with `o` the referent's `q`), so
    // reading x after the call is valid in use1 and use2.
    [Fact]
    public void AnExclusiveLayerHeldByValueKeepsItsOwnOriginsLoan()
        => Assert.Equal(
            [],
            ViewProblems("""
            func tie0c1(a: ref/i32 during q, c: ref/i32 during o, b: Option<uniq/(ref/i32 during q) during o>) -> ()
                return
            func tie0c2(a: ref/i32 during q, b: Option<uniq/(ref/i32 during q) during q>) -> ()
                return
            func use1()
                let y = 5
                let z = 6
                var x = y@ref
                tie0c1(z@ref, z@ref, .Some(x@uniq))
                let w = x@follow
            func use2()
                let y = 5
                let z = 6
                var x = y@ref
                tie0c2(z@ref, .Some(x@uniq))
                let w = x@follow
            """).Select(static x => x.Replace(" ComparisonLoanConflict_Kd views", string.Empty, StringComparison.Ordinal)).ToArray());

    // R4-M147's Limit through a Closure (DECISIONS R4-M148): a Closure holds its environment's parts, so one whose only Loan-capable part is a
    // stored raw layer stepping Exclusive with unpublished members (Wrap2c3<raw/u8>) leaves a call's store past it undecided, the located
    // Unsupported_Kd at the call, as its Tuple twin does; the dev MIR's opaque Closure wrote past it and erred at `inspect(w)`.
    [Fact]
    public void AStoreTheGuardLeavesUndecidedPastAClosureIsUnsupported()
        => Assert.Equal(
            ["limitClosure: putAny2c3(c@uniq, z@ref) Unsupported_Kd views 1", "limitTuple: putAny2c3(t@uniq, z@ref) Unsupported_Kd views 1"],
            ViewProblems("""
                struct Wrap2c3<E>
                    var value: E
                    public init(value: E) => self.value = value
                func putAny2c3<U, V>(t: uniq/U, v: V) => ()
                func limitClosure()
                    var x = 1
                    var z = 3
                    var w: ref/i32 = x@ref
                    var r = w@uniq
                    var h = Wrap2c3.init(r@follow@raw@raw/u8)
                    var c = func [h@move] () => ()
                    putAny2c3(c@uniq, z@ref)
                    z = 5
                    inspect(w)
                func limitTuple()
                    var x = 1
                    var z = 3
                    var w: ref/i32 = x@ref
                    var r = w@uniq
                    var t = (Wrap2c3.init(r@follow@raw@raw/u8), 1)
                    putAny2c3(t@uniq, z@ref)
                    z = 5
                    inspect(w)
                """));

    // SPEC 23.1.2: a Closure Binding rejected before its environment holds nothing, so a rejected program keeps its independent Loan
    // problems, at a call of it too.
    [Fact]
    public void ARejectedClosureKeepsItsProgramsIndependentProblems()
        => Assert.Equal(
            ["failedClosure: n = 2", "failedCall: n = 2"],
            RejectedProblems("""
                func failedClosure() -> i32
                    var n: i32 = 1
                    let p = n@ref
                    let c = func [missing] () -> i32 => 1
                    n = 2
                    inspect(p)
                    return 0
                func failedCall() -> i32
                    var n: i32 = 1
                    let p = n@ref
                    var c = func [missing] (v: ref/i32) -> i32 => 1
                    let a = c(p)
                    n = 2
                    inspect(p)
                    return a
                """));

    // G136 (DECISIONS R4-M148): a Value call whose declared signature numbers inputs other than its operands is a broken contract: A5 maps
    // no row of it and records InternalInvariant_Kd at the call (a constructed body: the builder and validator never give one).
    [Fact]
    public void AValueCallWhoseSignatureDoesNotNumberItsOperandsIsABrokenContract()
    {
        var c = MirBuildTest.Compile(Prelude + "func callIt(g: (ref/i32) -> ref/i32, x: ref/i32) -> ref/i32 => g(x)\n");
        var program = c.BuildMir();
        var body = Body(program, "callIt");
        var block = Array.FindIndex(body.Blocks.ToArray(), static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee.Kind: MirCalleeKind.Value });
        var call = body.Blocks[block].Terminator;
        var declared = body.Consts[call.Callee.Arg].Type;
        var input = declared.Components[0].Components[0];
        var inputs = c.Types.Tuple([input, input]);
        var two = c.Types.Intern(BoundTypeKind.Function, null, Kimi.Compiler.Parsing.SemanticsKind.Owner, [inputs, declared.Components[1]]);
        body.Terminate(block, call with { Callee = call.Callee with { Arg = body.Const(new(MirConstKind.TypeToken, two)) } });
        new Stratum(c.Types).Run(program, body);
        Assert.Equal(["callIt: g(x) InternalInvariant_Kd"], Problems(c, program, [DiagnosticCode.InternalInvariant_Kd]));
    }

    // V16 "F7 captures" (A3 6.2; DECISIONS R4-M148): K calls of an Exclusive Closure with N + 1 `var` captures storing their argument into
    // one. A call reads its receiver's rows over the memoized contract and walks the Closure's environment once per program version (the
    // holding memo), so the work in N at K = 8 and in K at N = 8 stays within the bound of the other shapes.
    [Fact]
    public void AClosureCallsWorkStaysLinearInItsCapturesAndCalls()
    {
        const string Shape = "func shape(z: uniq/i32, x: ref/i32)\n    origin z outlives x\n    var last: ref/i32 during x = x\n" +
            "«N:    var r{0}: ref/i32 during x = x\n»    var c = func [var last«N:, var r{0}»] (v: ref/i32 during x) => last = v\n«K:    c(x)\n»";
        long[] inN = [.. new[] { 8, 16, 32 }.Select(n => this.Measured(Prelude + Expand(Shape, n, 8)).Work)];
        long[] inK = [.. new[] { 8, 16, 32 }.Select(k => this.Measured(Prelude + Expand(Shape, 8, k)).Work)];
        output.WriteLine($"N 8, 16, 32 at K 8: work {string.Join(", ", inN)}; K 8, 16, 32 at N 8: work {string.Join(", ", inK)}");
        Assert.True(Grows(inN, 5), $"work in N {string.Join(", ", inN)}");
        Assert.True(Grows(inK, 5), $"work in K {string.Join(", ", inK)}");
    }

    // V16 (PLAN G273; R4 notes g273\problem-draft.md §4.3, the scaling row): a chain of D + 1 makers, the innermost result naming the
    // outermost maker's receiver, called D + 1 times: A5's work stays linear in D (N = D - 1 = 8, 16 and 32).
    [Fact]
    public void AMakerChainsWorkStaysLinearInItsDepth()
    {
        const string Shape = "func shape()\n    let n: i32 = 4\n    let mk = func [n] () => func [n@ref] () => «N:func [n] () => »n\n" +
            "    let r = mk()()«N:()»\n    inspect(r)\n";
        long[] work = [.. new[] { 8, 16, 32 }.Select(n => this.Measured(Prelude + Expand(Shape, n, 0)).Work)];
        output.WriteLine($"N 8, 16, 32: work {string.Join(", ", work)}");
        Assert.True(Grows(work, 2), $"work {string.Join(", ", work)}");
    }

    // V16 (DECISIONS R4-M148): A5 on a program version it checked before allocates nothing at Closure calls: the receiver's rows over the
    // memoized contract (its key the instance's environment; no A5 callee carries a closure context, R4-M148, R4-M152), a store into
    // the receiver, PD13's hold and a Limit Closure.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmClosureCallAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Prelude + """
            struct Wrap2c3<E>
                var value: E
                public init(value: E) => self.value = value
            func putAny2c3<U, V>(t: uniq/U, v: V) => ()
            func stores(z: uniq/i32, x: ref/i32) -> i32
                origin z outlives x
                var holder: ref/i32 during x = x
                var store = func [holder@uniq] (b: ref/i32 during x) -> i32
                    holder@follow = b
                    return 0
                let u = store@uniq
                u(z@follow@ref)
                z@follow = 42
                return holder@follow
            func held<s/T>(value: s/T) -> ()
                s is owner or uniq
                T is Copy
                var h = func [value] () -> ()
                    let local = value
                    _ = local
                let r = h@ref
                h()
                inspect(r)
            func limited()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                var h = Wrap2c3.init(r@follow@raw@raw/u8)
                var c = func [h@move] () => ()
                putAny2c3(c@uniq, z@ref)
            """);
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        // stores' two Language problems and held's uniq view's; held's owner view and limited record located Unsupported_Kd.
        Assert.Equal(3, program.Problems.All.ToArray().Count(static x => LoanCodes.Contains(x.Code) && x.Subject.Module == 0));
    }

    // A program's Loan problems and located Unsupported_Kd in its own bodies, each with its function, subject, code and views (bit i for view
    // i), in ordinal order.
    private static string[] ViewProblems(string source)
    {
        var c = MirBuildTest.Compile(Prelude + source);
        DiagnosticCode[] codes = [.. LoanCodes, DiagnosticCode.Unsupported_Kd];
        return [.. Analyze(c).Problems.All.ToArray().Where(x => codes.Contains(x.Code) && x.Subject.Module == 0)
            .Select(x => $"{Function(c.SyntaxOf(x.Subject))}: {Line(c, x.Subject)} {x.Code} views {x.Views}").Order(StringComparer.Ordinal)];
    }
}
