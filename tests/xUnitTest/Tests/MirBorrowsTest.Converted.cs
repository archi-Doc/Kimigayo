// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>A5's converted route (PLAN F2 unit A): a raw pointer converted to another pointee Type views the storage it reaches at the
/// callee's view, which a holder's declared Type does not give.</summary>
public sealed partial class MirBorrowsTest
{
    // The converted route's helpers: keep2C views *slot at raw/(raw/u8), so **slot is a raw layer it may write (SPEC 5.7 Calls); keep4C at
    // raw/u8 (u8 past it); keepTC and keepAC read a punned Tuple's or fixed array's element as such a pointer; keep2uC views a raw pointer
    // part at raw/(raw/u16).
    private const string ConvertedShapes = """
        func usepC<T>(p: raw/T) => ()
        func keep2C(slot: raw/(raw/(raw/u8)), value: raw/u8) => ()
        func keep4C(slot: raw/(raw/u8), value: raw/u8) => ()
        func keepTC(slot: raw/(raw/(raw/u8), u64), value: raw/u8) => ()
        func keepT2C(slot: raw/(raw/u8, u64), value: raw/u8) => ()
        func keepAC(slot: raw/[2 of raw/(raw/u8)], value: raw/u8) => ()
        func keepA2C(slot: raw/[2 of raw/u8], value: raw/u8) => ()
        func keep2uC(slot: raw/(raw/(raw/u16)), value: raw/u8) => ()

        """;

    // SPEC 15.6 Holders (D-437: a raw pointer read from a punned Place, alone or as an element of a punned Tuple or fixed array, reaches
    // storage the body does not own when any byte it is read from lies in a part of an `owner` Scalar Type, whatever else the holder
    // carries), 15.6.4 item 4, 5.7 Calls: through a converted pointer into a Loan-holding holder, the callee reads a pointer from Scalar
    // bytes (a u64 element, a `[8 of u8]` buffer, a `(u32, u32)` pair, a punned Tuple's or array's element, bytes spanning a u64 and a raw
    // pointer Field), so a raw layer it may write past that pointer lies in storage the body does not own, used after every point: kk = 2
    // conflicts (fail-before: dev accepted each, its W stopping at the holder's declared guard). Each Ok twin's view past the read pointer
    // is u8 (only a u8 may be stored there), and ptrPartOk reads only a raw pointer part (5.2.1 condition 1), so none is rejected.
    // mixedPunChainBad also records its z, which the pun reaches (record selection on an invalid program).
    [Fact]
    public void AConvertedViewReadsAPointerFromScalarBytes()
    {
        var problems = Problems(ConvertedShapes + """
                func mixedPunChainBad()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var other: raw/u8 = null
                    var z: u8 = 5
                    var w = (z@ref, 3@u64)
                    let q = w.1@raw@raw/(raw/(raw/u8))
                    unsafe => *q = other@raw
                    keep2C(q, spare)
                    kk = 2
                    usepC(other)
                func intChainMixedBad()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var w: (raw/u8, u64) = (null, 3@u64)
                    keep2C(w.1@raw@raw/(raw/(raw/u8)), spare)
                    kk = 2
                func partPunBad()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var other: raw/u8 = null
                    var w: (raw/(raw/u8), u64) = (other@raw, 3@u64)
                    keep2C(w.1@raw@raw/(raw/(raw/u8)), spare)
                    kk = 2
                func spanBad()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var other: raw/u8 = null
                    var w: (u64, raw/(raw/u8)) = (0@u64, other@raw)
                    let b = w@raw@raw/u8
                    var q: raw/(raw/(raw/u8)) = null
                    unsafe => q = (b + 4)@raw/(raw/(raw/u8))
                    keep2C(q, spare)
                    kk = 2
                func spanOk()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var other: raw/u8 = null
                    var w: (u64, raw/(raw/u8)) = (0@u64, other@raw)
                    let b = w@raw@raw/u8
                    var q: raw/(raw/u8) = null
                    unsafe => q = (b + 4)@raw/(raw/u8)
                    keep4C(q, spare)
                    kk = 2
                func hBufBad()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var other: u8 = 0
                    var w: ([8 of u8], raw/u8) = ([8 of 0@u8], other@raw)
                    keep2C(w.0@raw@raw/(raw/(raw/u8)), spare)
                    kk = 2
                func hBufOk()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var other: u8 = 0
                    var w: ([8 of u8], raw/u8) = ([8 of 0@u8], other@raw)
                    keep4C(w.0@raw@raw/(raw/u8), spare)
                    kk = 2
                func hPairBad()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var other: u8 = 0
                    var w: ((u32, u32), raw/u8) = ((1@u32, 2@u32), other@raw)
                    keep2C(w.0@raw@raw/(raw/(raw/u8)), spare)
                    kk = 2
                func hPairOk()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var other: u8 = 0
                    var w: ((u32, u32), raw/u8) = ((1@u32, 2@u32), other@raw)
                    keep4C(w.0@raw@raw/(raw/u8), spare)
                    kk = 2
                func hTupElemBad()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var other: u8 = 0
                    var w: ((u64, u64), raw/u8) = ((3@u64, 4@u64), other@raw)
                    keepTC(w.0@raw@raw/(raw/(raw/u8), u64), spare)
                    kk = 2
                func hTupElemOk()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var other: u8 = 0
                    var w: ((u64, u64), raw/u8) = ((3@u64, 4@u64), other@raw)
                    keepT2C(w.0@raw@raw/(raw/u8, u64), spare)
                    kk = 2
                func hArrElemBad()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var other: u8 = 0
                    var w: ((u64, u64), raw/u8) = ((3@u64, 4@u64), other@raw)
                    keepAC(w.0@raw@raw/[2 of raw/(raw/u8)], spare)
                    kk = 2
                func hArrElemOk()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var other: u8 = 0
                    var w: ((u64, u64), raw/u8) = ((3@u64, 4@u64), other@raw)
                    keepA2C(w.0@raw@raw/[2 of raw/u8], spare)
                    kk = 2
                func ptrPartOk()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var other: raw/u8 = null
                    var w: (raw/(raw/u8), u64) = (other@raw, 3@u64)
                    keep2uC(w.0@raw@raw/(raw/(raw/u16)), spare)
                    kk = 2
                func ptrPartBad()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var other: raw/u8 = null
                    var w: (raw/(raw/u8), u64) = (other@raw, 3@u64)
                    keep2uC(w.0@raw@raw/(raw/(raw/u16)), spare)
                    kk = 2
                    usepC(other)
                """);
        output.WriteLine(string.Join("; ", problems));
        Assert.Equal(
            ["mixedPunChainBad: kk = 2", "mixedPunChainBad: var z: u8 = 5", "intChainMixedBad: kk = 2", "partPunBad: kk = 2", "spanBad: kk = 2",
                "hBufBad: kk = 2", "hPairBad: kk = 2", "hTupElemBad: kk = 2", "hArrElemBad: kk = 2", "ptrPartBad: kk = 2"],
            problems);
    }
}
