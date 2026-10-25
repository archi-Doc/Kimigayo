# Writes the case programs of the 2a-2 prototype round (rule W) into proto/cases/<id>.kimi; <id>.rN.kimi is a repair variant.
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'cases')
os.makedirs(OUT, exist_ok=True)

MAIN = 'public func main() => ()\n'
INSPECT = 'func inspect<T>(value: ref/T) => ()\n'
PACKET = '''struct Data
    public var value: i32 = 0
enum Packet {a}
    Data(Data)
    Link(ref/Data during a)
    Empty
func keep(slot: uniq/(ref/Data during s), value: ref/Data during s) -> bool => true
func use<T>(value: ref/T) => ()
'''
KEEPI = '''func keepI(slot: uniq/(ref/i32 during a), value: ref/i32 during a) -> bool
    slot@follow = value
    return true
'''

cases = {}

def case(name, text, prelude=''):
    cases[name] = prelude + text + MAIN

# ---- j-bad (2a-2 list; §3 row 1)
case('j-bad', '''func bad(x: ref/i32) -> ref/i32 during x
    let local: i32 = 1
    return local@ref
''')
case('j-bad.r1', '''func bad(x: ref/i32) -> ref/i32 during x
    let local: i32 = 1
    let r = local@ref
    inspect(r)
    return x
''', INSPECT)

# ---- j-region (2a-2 list)
case('j-region', '''func bad(input: ref/i32) -> ref/i32 during input
    var view = input
    let local = 5
    view = local@ref
    return view
''')
case('j-region.r1', '''func bad(input: ref/i32) -> ref/i32 during input
    var view = input
    let local = 5
    view = local@ref
    inspect(view)
    return input
''', INSPECT)

# ---- §3 row 2: an annotated var assigned a borrow, returned
case('row2', '''func r2(a: ref/i32) -> ref/i32 during a
    var x = 1
    var r: ref/i32 during a = a
    r = x@ref
    return r
''')
case('row2.r1', '''func r2(a: ref/i32) -> ref/i32 during a
    var x = 1
    var r: ref/i32 during a = a
    r = x@ref
    inspect(r)
    return a
''', INSPECT)
case('row2.r2', '''func r2(a: ref/i32) -> ref/i32 during a
    var x = 1
    var r = a
    r = x@ref
    return r
''')

# ---- m-write (2a-2 list; §3 row 3), its var forms, m-write3, m-write4
case('m-write', '''func w(p: ref/i32) -> ref/i32 during p
    var x = 1
    let r = x@ref
    x = 1
    return r
''')
case('m-write.r1', '''func w(p: ref/i32) -> ref/i32 during p
    var x = 1
    let r = x@ref
    x = 1
    inspect(r)
    return p
''', INSPECT)
case('m-write-var', '''func w(p: ref/i32) -> ref/i32 during p
    var x = 1
    var r = x@ref
    x = 1
    return r
''')
case('m-write-var.r1', '''func w(p: ref/i32) -> ref/i32 during p
    var x = 1
    var r = x@ref
    x = 1
    inspect(r)
    return p
''', INSPECT)
case('m-write-var2', '''func w(p: ref/i32) -> ref/i32 during p
    var x = 1
    var r: ref/i32 during p = p
    r = x@ref
    x = 1
    return r
''')
case('m-write-var2.r1', '''func w(p: ref/i32) -> ref/i32 during p
    var x = 1
    var r = p
    r = x@ref
    x = 1
    inspect(r)
    return p
''', INSPECT)
case('m-write3', '''func w3(p: ref/i32) -> ref/i32 during p
    var x = 1
    let r: ref/i32 during p = x@ref
    x = 2
    return r
''')
case('m-write3.r1', '''func w3(p: ref/i32) -> ref/i32 during p
    var x = 1
    let r = x@ref
    x = 2
    inspect(r)
    return p
''', INSPECT)
case('m-write4', '''func w4(slot: uniq/(ref/i32 during a))
    var x = 1
    slot@follow = x@ref
    x = 2
''')
case('m-write4.r1', '''func w4(slot: uniq/(ref/i32 during a))
    var x = 1
    let r = x@ref
    inspect(r)
    x = 2
''', INSPECT)
case('m-write4b', '''func w4(slot: uniq/(ref/i32 during a))
    var x = 1
    let r = x@ref
    slot@follow = r
    x = 2
''')

# ---- m-store (2a-2 list; §3 row 5)
case('m-store', '''func leakL(target: uniq/(ref/i32 during a))
    let local = 1
    target@follow = local@ref
''')
case('m-store.r1', '''func leakL(target: uniq/(ref/i32 during a))
    let local = 1
    let r = local@ref
    inspect(r)
''', INSPECT)
case('m-store-b', '''func leakL(target: uniq/(ref/i32 during a))
    let local = 1
    let r = local@ref
    target@follow = r
''')

# ---- Two flows (§3 row 6) and cf/p/jregion2
case('two-flows', '''func tf(p: uniq/(ref/i32 during a)) -> ref/i32 during a
    let local = 1
    let r = local@ref
    p@follow = r
    return r
''')
case('two-flows.r1', '''func tf(p: uniq/(ref/i32 during a)) -> ref/i32 during a
    let local = 1
    let r = local@ref
    return r
''')
case('two-flows.r2', '''func tf(p: uniq/(ref/i32 during a), q: ref/i32 during a) -> ref/i32 during a
    let local = 1
    let r = local@ref
    p@follow = r
    return q
''')
case('jregion2', '''func jr2(input: ref/i32, slot: uniq/(ref/i32 during input)) -> ref/i32 during input
    let local = 1
    var r = local@ref
    slot@follow = r
    return r
''')
case('jregion2.r1', '''func jr2(input: ref/i32, slot: uniq/(ref/i32 during input)) -> ref/i32 during input
    let local = 1
    var r = local@ref
    inspect(r)
    return input
''', INSPECT)

# ---- A store in a loop (§3 row 7)
case('loop-store', '''func ls(p: uniq/(uniq/i32 during a), flag: bool)
    var x = 1
    while flag
        p@follow = x@uniq
''')
case('loop-store.r1', '''func ls(p: uniq/(uniq/i32 during a), q: uniq/i32 during a, flag: bool)
    var x = 1
    while flag
        let t = x@uniq
        inspect(t)
''', INSPECT)

# ---- m-twice / same-root (§3 row 8), m-pair (row 9)
case('m-twice', '''func tw(p: ref/i32) -> ref/i32 during p
    var x = 1
    let a = x@ref
    let b = x@ref
    let pair = (a, b)
    return pair.0
''')
case('m-twice.r1', '''func tw(p: ref/i32) -> ref/i32 during p
    var x = 1
    let a = x@ref
    let b = x@ref
    let pair = (p, b)
    return pair.0
''')
FIELDS = '''struct F
    public var f: i32 = 0
    public var g: i32 = 0
    public init() => ()
'''
case('same-root', '''func sr(p: ref/i32) -> ref/i32 during p
    var x = F.init()
    let pair = (x.f@ref, x.g@ref)
    return pair.0
''', FIELDS)
case('same-root.r1', '''func sr(p: ref/i32) -> ref/i32 during p
    var x = F.init()
    let pair = (p, x.g@ref)
    return pair.0
''', FIELDS)
case('m-pair', '''func pr(p: ref/i32) -> ref/i32 during p
    var x = 1
    var y = 2
    let pair = (x@ref, y@ref)
    return pair.0
''')
case('m-pair.r1', '''func pr(p: ref/i32) -> ref/i32 during p
    var x = 1
    var y = 2
    let pair = (p, y@ref)
    return pair.0
''')

# ---- id (§3 row 10) and fK (row 11)
ID = 'func id(x: ref/i32) -> ref/i32 during x => x\n'
case('id-return', '''func ir(p: ref/i32) -> ref/i32 during p
    let local = 1
    return id(local@ref)
''', ID)
case('id-return.r1', '''func ir(p: ref/i32) -> ref/i32 during p
    let local = 1
    let r = id(local@ref)
    inspect(r)
    return p
''', ID + INSPECT)
K = '''func k(x: (ref/i32 during o, ref/i32 during q)) -> ref/i32 during q
    return x.1
'''
case('fK', '''func fK(p: uniq/(ref/i32 during a), p0: ref/i32 during a) -> ref/i32 during a
    var x = 1
    let r = x@ref
    p@follow = r
    return k((r, p0))
''', K)
case('fK.r1', '''func fK(p: uniq/(ref/i32 during a), p0: ref/i32 during a) -> ref/i32 during a
    var x = 1
    let r = x@ref
    return k((r, p0))
''', K)

# ---- Guards: rv-g-param (row 12), rv-g-annot-esc (13), g-two (14), g-return (15), cf-g-reassign (16), rv/guard-annot
case('rv-g-param', '''func gp(packet: ref/Packet, target: uniq/(ref/Data during t)) -> i32
    match packet@follow
        .Data(let d) if keep(target, d) => return 1
        _ => return 0
''', PACKET)
case('rv-g-param.r1', '''func gp(packet: ref/Packet, target: uniq/(ref/Data during t), fb: ref/Data during t) -> i32
    match packet@follow
        .Data(let d) if keep(target, fb) => return 1
        _ => return 0
''', PACKET)
case('rv-g-param-i', '''func gp(target: uniq/(ref/i32 during a), x: Option<i32>) -> i32
    match x
        .Some(let v) if keepI(target, v@ref) => return 1
        _ => return 0
''', KEEPI)
case('rv-guard-annot', '''func g(anchor: ref/i32, x: Option<i32>) -> i32
    var t: ref/i32 during anchor = anchor
    match x
        .Some(let v) if keepI(t@uniq, v@ref) => return 1
        _ => return 0
''', KEEPI)
case('rv-g-annot-esc', '''func g(anchor: ref/i32, x: Option<i32>) -> i32
    var t: ref/i32 during anchor = anchor
    match x
        .Some(let v) if keepI(t@uniq, v@ref) => ()
        _ => ()
    inspect(t)
    return 0
''', KEEPI + INSPECT)
case('rv-g-annot-esc.r1', '''func g(anchor: ref/i32, x: Option<i32>) -> i32
    var t = anchor
    match x
        .Some(let v) if keepI(t@uniq, v@ref) => ()
        _ => ()
    inspect(t)
    return 0
''', KEEPI + INSPECT)
case('g-return', '''func guardReturn(packet: ref/Packet, fallback: ref/Data) -> ref/Data during packet
    var saved = fallback
    match packet@follow
        .Data(let d) if keep(saved@uniq, d) => ()
        _ => ()
    return saved
''', PACKET)
case('g-return.r1', '''func guardReturn(packet: ref/Packet, fallback: ref/Data) -> ref/Data during fallback
    var saved = fallback
    match packet@follow
        .Data(let d) if keep(saved@uniq, d) => ()
        _ => ()
    use(saved)
    return fallback
''', PACKET)
case('g-two', '''func guardTwo(packet: ref/Packet, fallback: ref/Data) -> ref/Data during packet
    var saved = fallback
    var other = fallback
    match packet@follow
        .Data(let d) if keep(saved@uniq, d) and keep(other@uniq, d) => ()
        _ => ()
    use(other)
    return saved
''', PACKET)
case('g-two.r1', '''func guardTwo(packet: ref/Packet, fallback: ref/Data) -> ref/Data during fallback
    var saved = fallback
    var other = fallback
    match packet@follow
        .Data(let d) if keep(saved@uniq, d) and keep(other@uniq, d) => ()
        _ => ()
    use(other)
    return fallback
''', PACKET)
case('cf-g-reassign', '''func guardRe(packet: ref/Packet, fallback: ref/Data, fb2: ref/Data) -> ref/Data during packet
    var saved = fallback
    var other = fallback
    match packet@follow
        .Data(let d) if keep(saved@uniq, d) and keep(other@uniq, d) => ()
        _ => ()
    use(other)
    saved = fb2
    return saved
''', PACKET)
case('cf-g-reassign.r1', '''func guardRe(packet: ref/Packet, fallback: ref/Data, fb2: ref/Data) -> ref/Data during fb2
    var saved = fallback
    var other = fallback
    match packet@follow
        .Data(let d) if keep(saved@uniq, d) and keep(other@uniq, d) => ()
        _ => ()
    use(other)
    saved = fb2
    return fb2
''', PACKET)

# ---- m-scope, m-scope-use (§3 row m-scope)
case('m-scope', '''func f(x: ref/i32) -> ref/i32 during x
    var r = x
    do
        let local = 1
        r = local@ref
    return r
''')
case('m-scope.r1', '''func f(x: ref/i32) -> ref/i32 during x
    var r = x
    do
        let local = 1
        r = local@ref
    inspect(r)
    return x
''', INSPECT)
case('m-scope-use', '''func f(x: ref/i32) -> ref/i32 during x
    var r = x
    do
        let local = 1
        r = local@ref
    inspect(r)
    return r
''', INSPECT)
case('m-scope-use.r1', '''func f(x: ref/i32) -> ref/i32 during x
    var r = x
    do
        let local = 1
        r = local@ref
    inspect(r)
    return x
''', INSPECT)

# ---- m-36f (36f without its write)
case('m-36f', '''contract Take
    associate Item
    func take(self: uniq/Self) -> Option<Self.Item>
struct H<S> {o}
    public var inner: uniq/S during o
    public var other: i32
    public init(inner: uniq/S during o, other: i32)
        self.inner = inner
        self.other = other
func f<S>(src: uniq/S) -> Option<S.Item>
    S is Take
    var h = H.init(inner: src, other: 0)
    var r = h@uniq
    let first = r.inner.take()
    return first@move
''')

# ---- PreparedInput (PD11, 2g-5)
case('prepared', '''func f(n: i32, y: ref/i32 = n@ref) -> i32 => y@follow
func caller() -> i32 => f(3)
''')

# ---- review-2's B1 (a), (b)
case('r2-b1a', '''func b1a(p: uniq/((ref/i32 during a, ref/i32 during a)), q: uniq/i32 during a)
    let local = 1
    p@follow = (local@ref, q@follow@ref)
    q@follow = 2
''')
case('r2-b1a.r1', '''func b1a(p: uniq/((ref/i32 during a, ref/i32 during a)), q: uniq/i32 during a)
    let local = 1
    p@follow = (q@follow@ref, q@follow@ref)
    q@follow = 2
''')
case('r2-b1b', '''func b1b(p: uniq/(ref/i32 during a), q: uniq/i32 during b)
    p@follow = q@follow@ref
    q@follow = 2
''')

# ---- round 3's B1 (b), (c), (d)
case('r3-b1b', '''func keepA(p: uniq/(ref/i32 during a), q: uniq/i32 during a, c: bool)
    let local: i32 = 1
    var v = q@follow@ref
    if c
        v = local@ref
    p@follow = v
    q@follow = 2
''')
case('r3-b1b.r1', '''func keepA(p: uniq/(ref/i32 during a), q: uniq/i32 during a, c: bool)
    let local: i32 = 1
    var v = q@follow@ref
    p@follow = v
    q@follow = 2
''')
case('r3-b1c', '''func fC(p: uniq/(ref/i32 during a), slot: uniq/(ref/i32 during a), p0: ref/i32 during a)
    var x = 1
    let r = x@ref
    p@follow = r
    let pair = (r, p0)
    slot@follow = pair.1
''')
case('r3-b1c.r1', '''func fC(p: uniq/(ref/i32 during a), slot: uniq/(ref/i32 during a), p0: ref/i32 during a)
    var x = 1
    let r = x@ref
    let pair = (r, p0)
    slot@follow = pair.1
''')
MAKE = '''func make() -> uniq/(ref/i32 during q) during q
    loop => ()
'''
case('r3-b1d', '''func fB(anchor: ref/i32)
    var x = 1
    var r: ref/i32 during anchor = anchor
    r = x@ref
    let s = make()
    s@follow = r
''', MAKE)
case('r3-b1d.r1', '''func fB(anchor: ref/i32)
    var x = 1
    var r = anchor
    r = x@ref
    let s = make()
    s@follow = r
''', MAKE)
case('r3-b1d2', '''func fB2(p: uniq/(ref/i32 during a))
    var x = 1
    let v = x@ref
    p@follow = v
    make()@follow = v
''', MAKE)

# ---- re-check R1: fT and the flow-insensitive case
case('rc-fT', '''func fT(slot: uniq/(ref/i32 during a), p0: ref/i32 during a)
    var x = 1
    var t: (ref/i32 during a, ref/i32 during a) = (p0, p0)
    t.0 = x@ref
    slot@follow = t.1
''')
case('rc-fT.r1', '''func fT(slot: uniq/(ref/i32 during a), p0: ref/i32 during a)
    var x = 1
    var t = (p0, p0)
    t.0 = x@ref
    slot@follow = t.1
''')
case('rc-flow', '''func fI(slot: uniq/(ref/i32 during a), p0: ref/i32 during a)
    var x = 1
    var v = p0
    v = x@ref
    let keep = v
    v = p0
    let pair = (v, keep)
    slot@follow = pair.0
''')
case('rc-flow.r1', '''func fI(slot: uniq/(ref/i32 during a), p0: ref/i32 during a)
    var x = 1
    var v = p0
    v = x@ref
    let keep = v
    v = p0
    let pair = (p0, keep)
    slot@follow = pair.0
''')

# ---- a conflict without a Refuted twin
case('no-twin', '''func nt() -> i32
    var x = 1
    let r = x@ref
    x = 2
    inspect(r)
    return 0
''', INSPECT)

# ---- ResultFit through a block, an if and a match result (2a-2 list)
case('rf-block', '''func rb(x: ref/i32) -> ref/i32 during x
    let local = 1
    return label b: do
        exit to b local@ref
''')
case('rf-if', '''func ri(x: ref/i32, c: bool) -> ref/i32 during x
    let local = 1
    return if c => local@ref else => x
''')
case('rf-match', '''func rm(x: ref/i32, o: Option<i32>) -> ref/i32 during x
    let local = 1
    return match o
        .Some(_) => local@ref
        _ => x
''')
case('rf-if.r1', '''func ri(x: ref/i32, c: bool) -> ref/i32 during x
    let local = 1
    let r = if c => local@ref else => x
    inspect(r)
    return x
''', INSPECT)

# ---- var forms: the same Loan flow where the literal form's failing node is a Binding twin (an Opaque, 2a0-D §5.1), so its Borrow
# or its escaping value is not built on today's MIR; a `var` local's region is a body region, so its relation is a row A5 judges.
case('j-bad-v', '''func bad(x: ref/i32) -> ref/i32 during x
    let local: i32 = 1
    var r = local@ref
    return r
''')
case('j-bad-v.r1', '''func bad(x: ref/i32) -> ref/i32 during x
    let local: i32 = 1
    var r = local@ref
    inspect(r)
    return x
''', INSPECT)
case('row2-v', '''func r2(a: ref/i32) -> ref/i32 during a
    var x = 1
    var r: ref/i32 during a = a
    var u = x@ref
    r = u
    return r
''')
case('row2-v.r1', '''func r2(a: ref/i32) -> ref/i32 during a
    var x = 1
    var r: ref/i32 during a = a
    var u = x@ref
    r = u
    inspect(r)
    return a
''', INSPECT)
case('m-write-var2-v', '''func w(p: ref/i32) -> ref/i32 during p
    var x = 1
    var r: ref/i32 during p = p
    var u = x@ref
    r = u
    x = 1
    return r
''')
case('m-write4-v', '''func w4(slot: uniq/(ref/i32 during a))
    var x = 1
    var r = x@ref
    slot@follow = r
    x = 2
''')
case('m-write4-v.r1', '''func w4(slot: uniq/(ref/i32 during a))
    var x = 1
    var r = x@ref
    inspect(r)
    x = 2
''', INSPECT)
case('m-store-v', '''func leakL(target: uniq/(ref/i32 during a))
    let local = 1
    var r = local@ref
    target@follow = r
''')
case('m-store-v.r1', '''func leakL(target: uniq/(ref/i32 during a))
    let local = 1
    var r = local@ref
    inspect(r)
''', INSPECT)
case('two-flows-v', '''func tf(p: uniq/(ref/i32 during a)) -> ref/i32 during a
    let local = 1
    var r = local@ref
    p@follow = r
    return r
''')
case('two-flows-v.r1', '''func tf(p: uniq/(ref/i32 during a)) -> ref/i32 during a
    let local = 1
    var r = local@ref
    return r
''')
case('two-flows-v.r2', '''func tf(p: uniq/(ref/i32 during a), q: ref/i32 during a) -> ref/i32 during a
    let local = 1
    var r = local@ref
    p@follow = r
    return q
''')
case('two-flows-v.r3', '''func tf(p: uniq/(ref/i32 during a), q: ref/i32 during a) -> ref/i32 during a
    let local = 1
    var r = local@ref
    inspect(r)
    return q
''', INSPECT)
case('loop-store-v', '''func ls(p: uniq/(uniq/i32 during a), flag: bool)
    var x = 1
    while flag
        var r = x@uniq
        p@follow = r
''')
case('loop-store-v.r1', '''func ls(p: uniq/(uniq/i32 during a), flag: bool)
    var x = 1
    while flag
        var r = x@uniq
        inspect(r)
''', INSPECT)
case('id-return-v', '''func ir(p: ref/i32) -> ref/i32 during p
    let local = 1
    var r = id(local@ref)
    return r
''', ID)
case('id-return-v.r1', '''func ir(p: ref/i32) -> ref/i32 during p
    let local = 1
    var r = id(local@ref)
    inspect(r)
    return p
''', ID + INSPECT)
case('m-twice-v', '''func tw(p: ref/i32) -> ref/i32 during p
    var x = 1
    var a = x@ref
    var b = x@ref
    var pair = (a, b)
    return pair.0
''')
case('m-twice-v.r1', '''func tw(p: ref/i32) -> ref/i32 during p
    var x = 1
    var a = x@ref
    var b = x@ref
    var pair = (p, b)
    return pair.0
''')
case('m-pair-v', '''func pr(p: ref/i32) -> ref/i32 during p
    var x = 1
    var y = 2
    var pair = (x@ref, y@ref)
    return pair.0
''')
case('m-pair-v.r1', '''func pr(p: ref/i32) -> ref/i32 during p
    var x = 1
    var y = 2
    var pair = (p, y@ref)
    return pair.0
''')
case('same-root-v', '''func sr(p: ref/i32) -> ref/i32 during p
    var x = F.init()
    var pair = (x.f@ref, x.g@ref)
    return pair.0
''', FIELDS)
case('same-root-v.r1', '''func sr(p: ref/i32) -> ref/i32 during p
    var x = F.init()
    var pair = (p, x.g@ref)
    return pair.0
''', FIELDS)
case('rc-fT-v', '''func fT(slot: uniq/(ref/i32 during a), p0: ref/i32 during a)
    var x = 1
    var t: (ref/i32 during a, ref/i32 during a) = (p0, p0)
    var u = x@ref
    t.0 = u
    slot@follow = t.1
''')
case('rc-fT-v.r1', '''func fT(slot: uniq/(ref/i32 during a), p0: ref/i32 during a)
    var x = 1
    var t = (p0, p0)
    var u = x@ref
    t.0 = u
    slot@follow = t.1
''')
case('r2-b1a-v', '''func b1a(p: uniq/((ref/i32 during a, ref/i32 during a)), q: uniq/i32 during a)
    let local = 1
    var l = local@ref
    p@follow = (l, q@follow@ref)
    q@follow = 2
''')
case('r3-b1d-v', '''func fB(anchor: ref/i32)
    var x = 1
    var r: ref/i32 during anchor = anchor
    var u = x@ref
    r = u
    let s = make()
    s@follow = r
''', MAKE)
case('rf-block-v', '''func rb(x: ref/i32) -> ref/i32 during x
    let local = 1
    var l = local@ref
    return label b: do
        exit to b l
''')
case('rf-if-v', '''func ri(x: ref/i32, c: bool) -> ref/i32 during x
    let local = 1
    var l = local@ref
    return if c => l else => x
''')
case('rf-if-v.r1', '''func ri(x: ref/i32, c: bool) -> ref/i32 during x
    let local = 1
    var l = local@ref
    let r = if c => l else => x
    inspect(r)
    return x
''', INSPECT)
case('rf-match-v', '''func rm(x: ref/i32, o: Option<i32>) -> ref/i32 during x
    let local = 1
    var l = local@ref
    return match o
        .Some(_) => l
        _ => x
''')

# The block, if and match result forms where the result expression's own fit is a row: the expression assigned to a var, then returned.
case('rf-if-v2', '''func ri(x: ref/i32, c: bool) -> ref/i32 during x
    let local = 1
    var l = local@ref
    var r = x
    r = if c => l else => x
    return r
''')
case('rf-match-v2', '''func rm(x: ref/i32, o: Option<i32>) -> ref/i32 during x
    let local = 1
    var l = local@ref
    var r = x
    r = match o
        .Some(_) => l
        _ => x
    return r
''')
case('rf-block-v2', '''func rb(x: ref/i32) -> ref/i32 during x
    let local = 1
    var l = local@ref
    var r = x
    r = label b: do
        exit to b l
    return r
''')
case('rf-if-v2.r1', '''func ri(x: ref/i32, c: bool) -> ref/i32 during x
    let local = 1
    var l = local@ref
    var r = x
    r = if c => l else => x
    inspect(r)
    return x
''', INSPECT)

# review-2's B1 (b) with the Reborrow in a var, so its relation is a row A5 judges (the literal form is Binding's fixed-fixed record).
case('r2-b1b-v', '''func b1b(p: uniq/(ref/i32 during a), q: uniq/i32 during b)
    var u = q@follow@ref
    p@follow = u
    q@follow = 2
''')
case('r2-b1b-v.r1', '''func b1b(p: uniq/(ref/i32 during a), q: uniq/i32 during a)
    var u = q@follow@ref
    p@follow = u
    q@follow = 2
''')

# PreparedInput (PD11): a default's borrow of an exclusive parameter, A5-F9's DefaultArgumentBorrow_Kd (not a Loan conflict).
case('prepared2', '''func f(x: uniq/i32 during a, y: ref/i32 during a = x) => ()
''')

for name, text in cases.items():
    with open(os.path.join(OUT, name + '.kimi'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
print(len(cases), 'files')
