// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// PLAN G963 (SPEC 15.6.4: the abstract-Type sentence and route 2; 5.7 Calls; DECISIONS R4-M154 as amended by G963): what a part hides
// is storage the call may write wherever an input's walk meets the part through no shared layer and outside a callable, so a callable
// the contract hands the body, writing through such a part, may keep an argument there, used after every point: a by-value abstract
// part (a bare T, a Field, a Tuple element, an Option payload, a nested Type argument, a Closure's capture), a writer that takes the
// T by value (`set: (T, E)`, also called directly), and a `during self` layer's AnyOf content of a by-value holder. Each invalid row
// has A5's one record at its later write; each valid twin (a writer through `ref/T` only or past a shared layer, a writer handed
// nothing of y, none, a holder admitting nothing) has none. Rows: G963's coverage table, labelled from SPEC before any output; the
// writable `x: uniq/T` controls of writerFn and writerReq are MirBorrowsTest.Wk's. Fails on 0658a2b82: every invalid row but the
// `x: uniq/T` controls, rawUniqTFnBad and anyUniqBad is accepted.
public sealed partial class MirBorrowsTest
{
    private static readonly Dictionary<string, string> HiddenWritableRows = new()
    {
        ["byVFnBad"] = "func f<T, E>(x: T, y: E, set: (uniq/T, E) -> ()) -> ()\n    return\nfunc byVFnBad<T>(value: uniq/i32 during o, other: T, set: (uniq/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(other@move, child@move, set@move)\n    v@follow = 5\n",
        ["byVReqBad"] = "contract Absorb<E>\n    func absorb(self: uniq/Self, r: E) -> ()\nfunc f<T, E>(x: T, y: E) -> ()\n    T is Absorb<E>\n    return\nfunc byVReqBad<T>(value: uniq/i32 during o, other: T) -> ()\n    T is Absorb<uniq/i32 during o>\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(other@move, child@move)\n    v@follow = 5\n",
        ["byVFieldBad"] = "struct Wr<T, E>\n    public var w: (uniq/T, E) -> ()\n    public init(w: (uniq/T, E) -> ()) => self.w = w@move\nfunc f<T, E>(x: T, y: E, h: Wr<T, E>) -> ()\n    return\nfunc byVFieldBad<T>(value: uniq/i32 during o, other: T, set: (uniq/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(other@move, child@move, Wr.init(w: set@move))\n    v@follow = 5\n",
        ["byVRefOk"] = "func f<T, E>(x: T, y: E, look: (ref/T, E) -> ()) -> ()\n    return\nfunc byVRefOk<T>(value: uniq/i32 during o, other: T, look: (ref/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(other@move, child@move, look@move)\n    v@follow = 5\n",
        ["byVTOnlyOk"] = "func f<T, E>(x: T, y: E, set: (uniq/T) -> ()) -> ()\n    return\nfunc byVTOnlyOk<T>(value: uniq/i32 during o, other: T, set: (uniq/T) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(other@move, child@move, set@move)\n    v@follow = 5\n",
        ["byVNoneOk"] = "func f<T, E>(x: T, y: E) -> ()\n    return\nfunc byVNoneOk<T>(value: uniq/i32 during o, other: T) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(other@move, child@move)\n    v@follow = 5\n",
        ["byBxFnBad"] = "struct Bx<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\nfunc f<T, E>(x: Bx<T>, y: E, set: (uniq/T, E) -> ()) -> ()\n    return\nfunc byBxFnBad<T>(value: uniq/i32 during o, other: T, set: (uniq/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(Bx.init(item: other@move), child@move, set@move)\n    v@follow = 5\n",
        ["byBxReqBad"] = "struct Bx<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\ncontract Absorb<E>\n    func absorb(self: uniq/Self, r: E) -> ()\nfunc f<T, E>(x: Bx<T>, y: E) -> ()\n    T is Absorb<E>\n    return\nfunc byBxReqBad<T>(value: uniq/i32 during o, other: T) -> ()\n    T is Absorb<uniq/i32 during o>\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(Bx.init(item: other@move), child@move)\n    v@follow = 5\n",
        ["byBxFieldBad"] = "struct Bx<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\nstruct Wr<T, E>\n    public var w: (uniq/T, E) -> ()\n    public init(w: (uniq/T, E) -> ()) => self.w = w@move\nfunc f<T, E>(x: Bx<T>, y: E, h: Wr<T, E>) -> ()\n    return\nfunc byBxFieldBad<T>(value: uniq/i32 during o, other: T, set: (uniq/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(Bx.init(item: other@move), child@move, Wr.init(w: set@move))\n    v@follow = 5\n",
        ["byBxRefOk"] = "struct Bx<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\nfunc f<T, E>(x: Bx<T>, y: E, look: (ref/T, E) -> ()) -> ()\n    return\nfunc byBxRefOk<T>(value: uniq/i32 during o, other: T, look: (ref/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(Bx.init(item: other@move), child@move, look@move)\n    v@follow = 5\n",
        ["byBxTOnlyOk"] = "struct Bx<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\nfunc f<T, E>(x: Bx<T>, y: E, set: (uniq/T) -> ()) -> ()\n    return\nfunc byBxTOnlyOk<T>(value: uniq/i32 during o, other: T, set: (uniq/T) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(Bx.init(item: other@move), child@move, set@move)\n    v@follow = 5\n",
        ["byBxNoneOk"] = "struct Bx<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\nfunc f<T, E>(x: Bx<T>, y: E) -> ()\n    return\nfunc byBxNoneOk<T>(value: uniq/i32 during o, other: T) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(Bx.init(item: other@move), child@move)\n    v@follow = 5\n",
        ["byTupFnBad"] = "func f<T, E>(x: (T, i32), y: E, set: (uniq/T, E) -> ()) -> ()\n    return\nfunc byTupFnBad<T>(value: uniq/i32 during o, other: T, set: (uniq/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>((other@move, 1), child@move, set@move)\n    v@follow = 5\n",
        ["byTupReqBad"] = "contract Absorb<E>\n    func absorb(self: uniq/Self, r: E) -> ()\nfunc f<T, E>(x: (T, i32), y: E) -> ()\n    T is Absorb<E>\n    return\nfunc byTupReqBad<T>(value: uniq/i32 during o, other: T) -> ()\n    T is Absorb<uniq/i32 during o>\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>((other@move, 1), child@move)\n    v@follow = 5\n",
        ["byTupFieldBad"] = "struct Wr<T, E>\n    public var w: (uniq/T, E) -> ()\n    public init(w: (uniq/T, E) -> ()) => self.w = w@move\nfunc f<T, E>(x: (T, i32), y: E, h: Wr<T, E>) -> ()\n    return\nfunc byTupFieldBad<T>(value: uniq/i32 during o, other: T, set: (uniq/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>((other@move, 1), child@move, Wr.init(w: set@move))\n    v@follow = 5\n",
        ["byTupRefOk"] = "func f<T, E>(x: (T, i32), y: E, look: (ref/T, E) -> ()) -> ()\n    return\nfunc byTupRefOk<T>(value: uniq/i32 during o, other: T, look: (ref/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>((other@move, 1), child@move, look@move)\n    v@follow = 5\n",
        ["byTupTOnlyOk"] = "func f<T, E>(x: (T, i32), y: E, set: (uniq/T) -> ()) -> ()\n    return\nfunc byTupTOnlyOk<T>(value: uniq/i32 during o, other: T, set: (uniq/T) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>((other@move, 1), child@move, set@move)\n    v@follow = 5\n",
        ["byTupNoneOk"] = "func f<T, E>(x: (T, i32), y: E) -> ()\n    return\nfunc byTupNoneOk<T>(value: uniq/i32 during o, other: T) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>((other@move, 1), child@move)\n    v@follow = 5\n",
        ["byOptFnBad"] = "func f<T, E>(x: Option<T>, y: E, set: (uniq/T, E) -> ()) -> ()\n    return\nfunc byOptFnBad<T>(value: uniq/i32 during o, other: T, set: (uniq/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(.Some(other@move), child@move, set@move)\n    v@follow = 5\n",
        ["byOptReqBad"] = "contract Absorb<E>\n    func absorb(self: uniq/Self, r: E) -> ()\nfunc f<T, E>(x: Option<T>, y: E) -> ()\n    T is Absorb<E>\n    return\nfunc byOptReqBad<T>(value: uniq/i32 during o, other: T) -> ()\n    T is Absorb<uniq/i32 during o>\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(.Some(other@move), child@move)\n    v@follow = 5\n",
        ["byOptFieldBad"] = "struct Wr<T, E>\n    public var w: (uniq/T, E) -> ()\n    public init(w: (uniq/T, E) -> ()) => self.w = w@move\nfunc f<T, E>(x: Option<T>, y: E, h: Wr<T, E>) -> ()\n    return\nfunc byOptFieldBad<T>(value: uniq/i32 during o, other: T, set: (uniq/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(.Some(other@move), child@move, Wr.init(w: set@move))\n    v@follow = 5\n",
        ["byOptRefOk"] = "func f<T, E>(x: Option<T>, y: E, look: (ref/T, E) -> ()) -> ()\n    return\nfunc byOptRefOk<T>(value: uniq/i32 during o, other: T, look: (ref/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(.Some(other@move), child@move, look@move)\n    v@follow = 5\n",
        ["byOptTOnlyOk"] = "func f<T, E>(x: Option<T>, y: E, set: (uniq/T) -> ()) -> ()\n    return\nfunc byOptTOnlyOk<T>(value: uniq/i32 during o, other: T, set: (uniq/T) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(.Some(other@move), child@move, set@move)\n    v@follow = 5\n",
        ["byOptNoneOk"] = "func f<T, E>(x: Option<T>, y: E) -> ()\n    return\nfunc byOptNoneOk<T>(value: uniq/i32 during o, other: T) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(.Some(other@move), child@move)\n    v@follow = 5\n",
        ["byBxBxFnBad"] = "struct Bx<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\nfunc f<T, E>(x: Bx<Bx<T>>, y: E, set: (uniq/T, E) -> ()) -> ()\n    return\nfunc byBxBxFnBad<T>(value: uniq/i32 during o, other: T, set: (uniq/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(Bx.init(item: Bx.init(item: other@move)), child@move, set@move)\n    v@follow = 5\n",
        ["byBxBxReqBad"] = "struct Bx<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\ncontract Absorb<E>\n    func absorb(self: uniq/Self, r: E) -> ()\nfunc f<T, E>(x: Bx<Bx<T>>, y: E) -> ()\n    T is Absorb<E>\n    return\nfunc byBxBxReqBad<T>(value: uniq/i32 during o, other: T) -> ()\n    T is Absorb<uniq/i32 during o>\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(Bx.init(item: Bx.init(item: other@move)), child@move)\n    v@follow = 5\n",
        ["byBxBxFieldBad"] = "struct Bx<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\nstruct Wr<T, E>\n    public var w: (uniq/T, E) -> ()\n    public init(w: (uniq/T, E) -> ()) => self.w = w@move\nfunc f<T, E>(x: Bx<Bx<T>>, y: E, h: Wr<T, E>) -> ()\n    return\nfunc byBxBxFieldBad<T>(value: uniq/i32 during o, other: T, set: (uniq/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(Bx.init(item: Bx.init(item: other@move)), child@move, Wr.init(w: set@move))\n    v@follow = 5\n",
        ["byBxBxRefOk"] = "struct Bx<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\nfunc f<T, E>(x: Bx<Bx<T>>, y: E, look: (ref/T, E) -> ()) -> ()\n    return\nfunc byBxBxRefOk<T>(value: uniq/i32 during o, other: T, look: (ref/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(Bx.init(item: Bx.init(item: other@move)), child@move, look@move)\n    v@follow = 5\n",
        ["byBxBxTOnlyOk"] = "struct Bx<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\nfunc f<T, E>(x: Bx<Bx<T>>, y: E, set: (uniq/T) -> ()) -> ()\n    return\nfunc byBxBxTOnlyOk<T>(value: uniq/i32 during o, other: T, set: (uniq/T) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(Bx.init(item: Bx.init(item: other@move)), child@move, set@move)\n    v@follow = 5\n",
        ["byBxBxNoneOk"] = "struct Bx<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\nfunc f<T, E>(x: Bx<Bx<T>>, y: E) -> ()\n    return\nfunc byBxBxNoneOk<T>(value: uniq/i32 during o, other: T) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(Bx.init(item: Bx.init(item: other@move)), child@move)\n    v@follow = 5\n",
        ["uniqTFieldBad"] = "struct Wr<T, E>\n    public var w: (uniq/T, E) -> ()\n    public init(w: (uniq/T, E) -> ()) => self.w = w@move\nfunc f<T, E>(x: uniq/T, y: E, h: Wr<T, E>) -> ()\n    return\nfunc uniqTFieldBad<T>(value: uniq/i32 during o, other: T, set: (uniq/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    var s = other@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(s@uniq, child@move, Wr.init(w: set@move))\n    v@follow = 5\n",
        ["uniqTRefOk"] = "func f<T, E>(x: uniq/T, y: E, look: (ref/T, E) -> ()) -> ()\n    return\nfunc uniqTRefOk<T>(value: uniq/i32 during o, other: T, look: (ref/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    var s = other@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(s@uniq, child@move, look@move)\n    v@follow = 5\n",
        ["uniqTTOnlyOk"] = "func f<T, E>(x: uniq/T, y: E, set: (uniq/T) -> ()) -> ()\n    return\nfunc uniqTTOnlyOk<T>(value: uniq/i32 during o, other: T, set: (uniq/T) -> ()) -> ()\n    var v = value@move\n    var s = other@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(s@uniq, child@move, set@move)\n    v@follow = 5\n",
        ["uniqTNoneOk"] = "func f<T, E>(x: uniq/T, y: E) -> ()\n    return\nfunc uniqTNoneOk<T>(value: uniq/i32 during o, other: T) -> ()\n    var v = value@move\n    var s = other@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(s@uniq, child@move)\n    v@follow = 5\n",
        ["refTFnOk"] = "func f<T, E>(x: ref/T, y: E, set: (uniq/T, E) -> ()) -> ()\n    return\nfunc refTFnOk<T>(value: uniq/i32 during o, other: T, set: (uniq/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(other@ref, child@move, set@move)\n    v@follow = 5\n",
        ["rawVFnBad"] = "func f<T, E>(x: T, y: E, set: (uniq/T, E) -> ()) -> ()\n    return\nfunc rawVFnBad<T>(y: uniq/i32, other: T, set: (uniq/T, raw/u8) -> ()) -> ()\n    let w = y@follow@ref\n    f<T, raw/u8>(other@move, w@raw@raw/u8, set@move)\n    y@follow = 5\n",
        ["rawVNoneOk"] = "func f<T, E>(x: T, y: E) -> ()\n    return\nfunc rawVNoneOk<T>(y: uniq/i32, other: T) -> ()\n    let w = y@follow@ref\n    f<T, raw/u8>(other@move, w@raw@raw/u8)\n    y@follow = 5\n",
        ["rawUniqTFnBad"] = "func f<T, E>(x: uniq/T, y: E, set: (uniq/T, E) -> ()) -> ()\n    return\nfunc rawUniqTFnBad<T>(y: uniq/i32, other: T, set: (uniq/T, raw/u8) -> ()) -> ()\n    let w = y@follow@ref\n    var s = other@move\n    f<T, raw/u8>(s@uniq, w@raw@raw/u8, set@move)\n    y@follow = 5\n",
        ["closFnBad"] = "func f<C, T, E>(c: C, y: E, set: (uniq/T, E) -> ()) -> ()\n    return\nfunc closFnBad<T>(value: uniq/i32 during o, other: T, set: (uniq/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    let c = func [other@move] () => ()\n    f(c@move, child@move, set@move)\n    v@follow = 5\n",
        ["closNoneOk"] = "func f<C, E>(c: C, y: E) -> ()\n    return\nfunc closNoneOk<T>(value: uniq/i32 during o, other: T) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    let c = func [other@move] () => ()\n    f(c@move, child@move)\n    v@follow = 5\n",
        ["byVFnVBad"] = "func f<T, E>(x: T, y: E, set: (T, E) -> ()) -> ()\n    return\nfunc byVFnVBad<T>(value: uniq/i32 during o, other: T, set: (T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(other@move, child@move, set@move)\n    v@follow = 5\n",
        ["uniqTFnVBad"] = "func f<T, E>(x: uniq/T, y: E, set: (T, E) -> ()) -> ()\n    return\nfunc uniqTFnVBad<T>(value: uniq/i32 during o, other: T, set: (T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    var s = other@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(s@uniq, child@move, set@move)\n    v@follow = 5\n",
        ["byVNestedOk"] = "func f<T, E>(x: T, y: E, set: ((T) -> (), E) -> ()) -> ()\n    return\nfunc byVNestedOk<T>(value: uniq/i32 during o, other: T, set: ((T) -> (), uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(other@move, child@move, set@move)\n    v@follow = 5\n",
        ["byVDirectBad"] = "func byVDirectBad<T>(value: uniq/i32 during o, other: T, set: (T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    set(other@move, child@move)\n    v@follow = 5\n",
        ["byVDirectRefOk"] = "func byVDirectRefOk<T>(value: uniq/i32 during o, other: T, set: (ref/T, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    set(other@ref, child@move)\n    v@follow = 5\n",
        ["anyByValueBad"] = "struct H {a}\n    public var data: raw/i32 during self\n    public var peer: ref/i32 during a\n    public init(peer: ref/i32 during a)\n        self.data = null\n        self.peer = peer\nfunc f(h: H during o, y: uniq/i32 during o) => ()\nfunc anyByValueBad(value: uniq/i32 during o, p: ref/i32 during o) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f(H.init(peer: p), child@move)\n    v@follow = 5\n",
        ["anyUniqBad"] = "struct H {a}\n    public var data: raw/i32 during self\n    public var peer: ref/i32 during a\n    public init(peer: ref/i32 during a)\n        self.data = null\n        self.peer = peer\nfunc f(h: uniq/(H during o), y: uniq/i32 during o) => ()\nfunc anyUniqBad(value: uniq/i32 during o, p: ref/i32 during o) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    var h = H.init(peer: p)\n    f(h@uniq, child@move)\n    v@follow = 5\n",
        ["anyByValueNoOriginOk"] = "struct H2\n    public var data: raw/i32 during self\n    public var n: i32\n    public init()\n        self.data = null\n        self.n = 0\nfunc f(h: H2, y: uniq/i32 during o) => ()\nfunc anyByValueNoOriginOk(value: uniq/i32 during o) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f(H2.init(), child@move)\n    v@follow = 5\n",
        ["anyByValueCopyOk"] = "struct H {a}\n    public var data: raw/i32 during self\n    public var peer: ref/i32 during a\n    public init(peer: ref/i32 during a)\n        self.data = null\n        self.peer = peer\nfunc f(h: H during o, y: i32) => ()\nfunc anyByValueCopyOk(value: uniq/i32 during o, p: ref/i32 during o) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f(H.init(peer: p), 3)\n    v@follow = 5\n",
        ["byVUniqRefFnOk"] = "func f<T, E>(x: T, y: E, ka: ref/i32 during a, ku: ref/i32 during u, set: (uniq/(ref/T during a) during u, E) -> ()) -> ()\n    return\nfunc byVUniqRefFnOk<T>(value: uniq/i32 during o, other: T, ka: ref/i32 during a, ku: ref/i32 during u, set: (uniq/(ref/T during a) during u, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(other@move, child@move, ka, ku, set@move)\n    v@follow = 5\n",
        ["byVUniqUniqFnBad"] = "func f<T, E>(x: T, y: E, ka: ref/i32 during a, ku: ref/i32 during u, set: (uniq/(uniq/T during a) during u, E) -> ()) -> ()\n    return\nfunc byVUniqUniqFnBad<T>(value: uniq/i32 during o, other: T, ka: ref/i32 during a, ku: ref/i32 during u, set: (uniq/(uniq/T during a) during u, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(other@move, child@move, ka, ku, set@move)\n    v@follow = 5\n",
        ["uniqTUniqUniqFnBad"] = "func f<T, E>(x: uniq/T, y: E, ka: ref/i32 during a, ku: ref/i32 during u, set: (uniq/(uniq/T during a) during u, E) -> ()) -> ()\n    return\nfunc uniqTUniqUniqFnBad<T>(value: uniq/i32 during o, other: T, ka: ref/i32 during a, ku: ref/i32 during u, set: (uniq/(uniq/T during a) during u, uniq/i32 during o) -> ()) -> ()\n    var v = value@move\n    var s = other@move\n    let child: uniq/i32 during o = v\n    f<T, uniq/i32 during o>(s@uniq, child@move, ka, ku, set@move)\n    v@follow = 5\n",
    };

    [Theory]
    [InlineData("byVFnBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byVReqBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byVFieldBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byVRefOk", "")]
    [InlineData("byVTOnlyOk", "")]
    [InlineData("byVNoneOk", "")]
    [InlineData("byBxFnBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byBxReqBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byBxFieldBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byBxRefOk", "")]
    [InlineData("byBxTOnlyOk", "")]
    [InlineData("byBxNoneOk", "")]
    [InlineData("byTupFnBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byTupReqBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byTupFieldBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byTupRefOk", "")]
    [InlineData("byTupTOnlyOk", "")]
    [InlineData("byTupNoneOk", "")]
    [InlineData("byOptFnBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byOptReqBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byOptFieldBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byOptRefOk", "")]
    [InlineData("byOptTOnlyOk", "")]
    [InlineData("byOptNoneOk", "")]
    [InlineData("byBxBxFnBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byBxBxReqBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byBxBxFieldBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byBxBxRefOk", "")]
    [InlineData("byBxBxTOnlyOk", "")]
    [InlineData("byBxBxNoneOk", "")]
    [InlineData("uniqTFieldBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("uniqTRefOk", "")]
    [InlineData("uniqTTOnlyOk", "")]
    [InlineData("uniqTNoneOk", "")]
    [InlineData("refTFnOk", "")]
    [InlineData("rawVFnBad", "ComparisonLoanConflict_Kd y@follow = 5")]
    [InlineData("rawVNoneOk", "")]
    [InlineData("rawUniqTFnBad", "ComparisonLoanConflict_Kd y@follow = 5")]
    [InlineData("closFnBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("closNoneOk", "")]
    [InlineData("byVFnVBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("uniqTFnVBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byVNestedOk", "")]
    [InlineData("byVDirectBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("byVDirectRefOk", "")]
    [InlineData("anyByValueBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("anyUniqBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("anyByValueNoOriginOk", "")]
    [InlineData("anyByValueCopyOk", "")]
    [InlineData("byVUniqRefFnOk", "")]
    [InlineData("byVUniqUniqFnBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    [InlineData("uniqTUniqUniqFnBad", "ComparisonLoanConflict_Kd v@follow = 5")]
    public void AWriterOfWhatAPartHidesKeepsTheArgumentThere(string row, string expected)
        => Assert.Equal(expected, Records(HiddenWritableRows[row] + "public func main() => ()\n"));
}
