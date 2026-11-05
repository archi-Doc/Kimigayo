# Writes round 2x-P7's case programs into cases/ (one .kimi per case). Run: python -I make_cases.py <cases-dir> <g227-probe-dir>
import os
import sys

out = sys.argv[1]
g227 = sys.argv[2]
os.makedirs(out, exist_ok=True)

TEXT = 'func makeText() -> string => "a"\nfunc inspectText(value: ref/string) => ()\nfunc consumeText(value: string) => ()\n'
INT = 'func inspect(value: ref/i32) => ()\n'
RES = ('struct Resource\n    public var n: i32\n    public init(n: i32) => self.n = n\n'
       'func makeResource() -> Resource => Resource.init(1)\nfunc consume(r: Resource) => ()\n')
MAIN = 'public func main() => ()\n'

cases = {}

# ---- f7a-f7n: the plan's 2x-P7 list (A5 step 2 plan row 2x-P7) ----
cases['f7a-622-exchange'] = '''func use(z: uniq/i32, x: ref/i32) -> i32
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
''' + MAIN
cases['f7a2-622-exchange-ok'] = '''func use(z: uniq/i32, x: ref/i32) -> i32
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
''' + MAIN
cases['f7b-1379-branch-store'] = '''func use() -> i32
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
''' + MAIN
cases['f7c-575-uniq-capture-store'] = '''func use(z: uniq/i32, x: ref/i32) -> i32
    origin z outlives x
    var holder: ref/i32 during x = x
    var store = func [holder@uniq] (b: ref/i32 during x) -> i32
        holder@follow = b
        return 0
    store(z@follow@ref)
    z@follow = 42
    return holder@follow
''' + MAIN
cases['f7d-576-uniq-capture-store-moved-callee'] = '''func use(z: uniq/i32, x: ref/i32) -> i32
    origin z outlives x
    var holder: ref/i32 during x = x
    var store = func [holder@uniq] (b: ref/i32 during x) -> i32
        holder@follow = b
        return 0
    store@move(z@follow@ref)
    z@follow = 42
    return holder@follow
''' + MAIN
cases['f7e1-15.8.2-as-written'] = TEXT + '''public func main()
    let text = makeText()
    let get = func [text@move] () => text@ref   // Result depends on the call receiver.
    let view = get()
    // let moved = get@move                     // Error: view is used below.
    inspectText(view)

    let other = makeText()
    let invalid = func [other@move] () -> ref/string => other@ref
    // Error: the annotated result defaults to static, not the environment borrow.
'''
cases['f7e2-15.8.2-moved'] = TEXT + '''public func main()
    let text = makeText()
    let get = func [text@move] () => text@ref
    let view = get()
    let moved = get@move
    inspectText(view)
'''
cases['f7f-tref-valid'] = TEXT + '''public func main()
    var t = makeText()
    let c = func [t@ref] () => ()
    c()
    t = makeText()
'''
cases['f7g-row18-moved-closure-keeps-loan'] = TEXT + '''public func main()
    let t = makeText()
    let c = func [t@ref] () => ()
    let m = c@move
    consumeText(t@move)
    m()
'''
cases['f7h1-A3-closure'] = '''func f<s/T>(value: s/T, other: s/T, x: T) -> ()
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
public func main() -> ()
    var n: i32 = 1
    var m: i32 = 2
    f(n, m, 9)
'''
cases['f7h2-A3-closure-ok'] = '''func f<s/T>(value: s/T, other: s/T, x: T) -> ()
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
public func main() -> ()
    var n: i32 = 1
    var m: i32 = 2
    f(n, m, 9)
'''
cases['f7i1-A9-keep-ok'] = '''func keep<F>(f: F) -> F => f@move
func f<s/T>(value: s/T, x: T) -> ()
    s is owner or uniq
    T is Copy
    var v = value@move
    let h = func [v] () -> ()
        _ = v@follow
    let kept = keep(h@move)
    kept()
    v@follow = x
public func main() -> ()
    var n: i32 = 1
    f(n, 9)
'''
cases['f7i2-A9-keep'] = '''func keep<F>(f: F) -> F => f@move
func f<s/T>(value: s/T, x: T) -> ()
    s is owner or uniq
    T is Copy
    var v = value@move
    let h = func [v] () -> ()
        _ = v@follow
    let kept = keep(h@move)
    v@follow = x
    kept()
public func main() -> ()
    var n: i32 = 1
    f(n, 9)
'''
cases['f7j1-nested-uniq'] = '''public func main()
    var count: i32 = 0
    var outer = func [var count] () -> i32
        var inner = func [count@uniq] () => count@follow += 1
        inner()
        return count
    let a = outer()
'''
cases['f7j2-nested-uniq-let-capture'] = '''public func main()
    var count: i32 = 0
    var outer = func [count] () -> i32
        var inner = func [count@uniq] () => count@follow += 1
        inner()
        return count
    let a = outer()
'''
cases['f7j3-nested-uniq-let-outer'] = '''public func main()
    var count: i32 = 0
    let outer = func [var count] () -> i32
        var inner = func [count@uniq] () => count@follow += 1
        inner()
        return count
    let a = outer()
'''
cases['f7k1-shared-call-outer-write'] = '''public func main()
    var x: i32 = 1
    let c = func [x@ref] () -> i32 => x@follow
    x = 2
    let y = c()
'''
cases['f7k2-shared-call-outer-write-ok'] = '''public func main()
    var x: i32 = 1
    let c = func [x@ref] () -> i32 => x@follow
    let y = c()
    x = 2
'''
cases['f7l1-per-case-receiver-var'] = '''func f<s/T>(value: s/T) -> ()
    s is owner or uniq
    T is Copy
    var h = func [value] () -> ()
        let local = value
        _ = local
    h()
    h()
public func main() -> ()
    var n: i32 = 1
    f(n)
'''
cases['f7l2-per-case-receiver-let'] = '''func f<s/T>(value: s/T) -> ()
    s is owner or uniq
    T is Copy
    let h = func [value] () -> ()
        let local = value
        _ = local
    h()
    h()
public func main() -> ()
    var n: i32 = 1
    f(n)
'''
cases['f7m-var-capture-snapshot'] = '''public func main()
    var count: i32 = 0
    var next = func [var count] () -> i32
        count += 1
        return count
    count = 5
    let first = next()
    let second = next()
'''
cases['f7n1-capture-store-through-type-argument'] = '''struct Cell<T>
    public var item: T
    public init(item: T) => self.item = item@move
func f<s/T>(value: s/T, other: s/T, x: T) -> ()
    s is owner or uniq
    T is Copy
    var v = value@move
    var cell = Cell.init(item: other@move)
    var set = func [cell@uniq] (y: s/T) -> ()
        cell@follow.item = y@move
    let child = v
    set(child@move)
    v@follow = x
    _ = cell.item@follow
public func main() -> ()
    var n: i32 = 1
    var m: i32 = 2
    f(n, m, 9)
'''
cases['f7n2-capture-store-through-type-argument-ok'] = '''struct Cell<T>
    public var item: T
    public init(item: T) => self.item = item@move
func f<s/T>(value: s/T, other: s/T, x: T) -> ()
    s is owner or uniq
    T is Copy
    var v = value@move
    var cell = Cell.init(item: other@move)
    var set = func [cell@uniq] (y: s/T) -> ()
        cell@follow.item = y@move
    let child = v
    set(child@move)
    _ = cell.item@follow
    v@follow = x
public func main() -> ()
    var n: i32 = 1
    var m: i32 = 2
    f(n, m, 9)
'''

# ---- SPEC 7.7 examples (as written, then each commented error line uncommented) ----
cases['s771-as-written'] = '''public func main()
    let twice = func (value: i32) => value * 2              // Result: i32.
    let positive: (i32) -> bool = func (value) => value > 0
    let invalid = func (value) => value * 2                 // Error: no fixed input signature.
'''
cases['s772a-static-storage'] = '''group Totals
    public var count: i32 = 0
        get() -> i32
            let read = func () -> i32 => storage   // Valid: a static Property's storage is a static access.
            return read()
''' + MAIN
S772B = TEXT + '''public func main()
    let number: i32 = 10
    let copied = func () => number              // Copy capture.
    let text = makeText()                       // string: Non-Copy.
    // let bad = func () => text                // Error: an explicit list is required.
    let holder = func [text@move] () => ()      // The transfer happens even if unused.

    var value: i32 = 1
    let r = value@uniq
    var bump = func [r] () => r@follow += 1     // [r] Reborrows r as uniq/i32.
    bump()                                      // An Exclusive call.
'''
cases['s772b-as-written'] = S772B
cases['s772b-bad'] = S772B.replace('    // let bad = func () => text', '    let bad = func () => text')
S773 = RES + '''public func main()
    let count: i32 = 0
    var next = func [var count] () -> i32
        count += 1
        return count
    let first = next()        // 1: an Exclusive call; the outer count stays 0.

    let item = makeResource() // Non-Copy.
    let take = func [item@move] () => item@move
    let taken = take@move()   // Consuming: transfers item out.
    // take()                 // Error: take was consumed.
'''
cases['s773-as-written'] = S773
cases['s773-take'] = S773.replace('    // take()  ', '    take()  ')
S774 = '''func inc(value: i32) -> i32 => value + 1
func twice(action: ref/((i32) -> i32), value: i32) -> i32 => action(action(value))

public func main()
    let ready = true
    let text = "hello"
    let closure = func [text@move] () => ()
    let erased: () -> () = closure@move                    // Transfer, then erasure; closure is Moved.
    // let view: ref/(() -> ()) = func () => ()             // Error: erasure cannot then borrow.

    var handler: (i32) -> i32 = inc                        // Erased: Non-Copy.
    handler = inc                                          // Erased again; the old value is destroyed.
    let next = handler@move
    // let mixed = if ready => next@move else => inc       // Error: no common Function Type.
    // let result = twice(inc, 1)                          // Error: erasure and a borrow are two operations.
'''
cases['s774-as-written'] = S774
cases['s774-view'] = S774.replace('    // let view:', '    let view:')
cases['s774-mixed'] = S774.replace('    // let mixed', '    let mixed')
cases['s774-result'] = S774.replace('    // let result', '    let result')

# ---- SPEC 15.8.2's table rows, each as one source (the proposal's §3 derivation rows) ----
cases['t1a-env-borrow-shared'] = INT + '''public func main()
    let n: i32 = 4
    let get = func [n] () => n@ref
    let v = get()
    inspect(v)
'''
cases['t1b-env-borrow-shared-move'] = INT + '''public func main()
    let n: i32 = 4
    let get = func [n] () => n@ref
    let v = get()
    let moved = get@move
    inspect(v)
'''
cases['t1c-env-borrow-exclusive'] = INT + '''public func main()
    let n: i32 = 4
    var bump = func [var n] ()
        n += 1
        return n@ref
    let a = bump()
    let b = bump()
    inspect(a)
'''
cases['t2a-captured-ref'] = INT + '''public func main()
    let x: i32 = 1
    let r = x@ref
    let c = func [r] () => r
    let v = c()
    let m = c@move
    inspect(v)
'''
cases['t2b-shared-borrow-through-captured-ref'] = INT + '''public func main()
    let x: i32 = 1
    let r = x@ref
    let c = func [r] () => r@follow@ref
    let v = c()
    let m = c@move
    inspect(v)
'''
cases['t3a-uniq-reborrow-exclusive-call'] = '''public func main()
    var x: i32 = 1
    let u = x@uniq
    var c = func [u] () => u
    let a = c()
    let m = c@move
    a@follow = 2
'''
cases['t3b-uniq-reborrow-exclusive-call-ok'] = '''public func main()
    var x: i32 = 1
    let u = x@uniq
    var c = func [u] () => u
    let a = c()
    a@follow = 2
    let m = c@move
'''
cases['t3c-shared-borrow-through-captured-uniq'] = INT + '''public func main()
    var x: i32 = 1
    let u = x@uniq
    let c = func [u] () => u@follow@ref
    let a = c()
    let m = c@move
    inspect(a)
'''
cases['t3d-shared-borrow-through-captured-uniq-ok'] = INT + '''public func main()
    var x: i32 = 1
    let u = x@uniq
    let c = func [u] () => u@follow@ref
    let a = c()
    inspect(a)
    let m = c@move
'''
cases['t4a-consuming-reborrow'] = TEXT + '''public func main()
    var x: i32 = 1
    let u = x@uniq
    let t = makeText()
    let c = func [u, t@move] () -> uniq/i32 during u
        consumeText(t@move)
        return u
    let a = c@move()
    a@follow = 3
'''
cases['t4b-consuming-reborrow-inferred'] = TEXT + '''public func main()
    var x: i32 = 1
    let u = x@uniq
    let t = makeText()
    let c = func [u, t@move] ()
        consumeText(t@move)
        return u
    let a = c@move()
    a@follow = 3
'''
cases['t4c-consuming-reborrow-outer-write'] = TEXT + '''public func main()
    var x: i32 = 1
    let u = x@uniq
    let t = makeText()
    let c = func [u, t@move] ()
        consumeText(t@move)
        return u
    let a = c@move()
    x = 5
    a@follow = 3
'''
cases['t5-moved-out'] = '''public func main()
    var x: i32 = 1
    let u = x@uniq
    let c = func [u] () => u@move
    let a = c@move()
    a@follow = 3
'''
cases['t6-borrowed-argument'] = INT + '''public func main()
    let x: i32 = 1
    let c = func (y: ref/i32) => y
    let a = c(x@ref)
    let m = c@move
    inspect(a)
'''
cases['t7a-call-local'] = INT + '''public func main()
    let c = func ()
        let l: i32 = 1
        return l@ref
    inspect(c())
'''
cases['t7b-consumed-environment'] = TEXT + '''public func main()
    let text = makeText()
    let other = makeText()
    let c = func [text@move, other@move] ()
        consumeText(other@move)
        return text@ref
    inspectText(c@move())
'''
cases['cc1-copy-closure-consuming-call'] = '''public func main()
    let n: i32 = 1
    let c = func [n] () -> i32 => n@move
    let a = c()
    let b = c()
'''

# ---- review-1's Consuming var forms (P1-B1) ----
cases['rc1-cycle-var-reinit'] = RES + '''public func main()
    let first = makeResource()
    let cycle = func [var first@move] () -> Resource
        let old = first@move
        first = makeResource()
        return old@move
    let r = cycle@move()
'''
cases['rc1b-cycle-let-reinit'] = RES + '''public func main()
    let first = makeResource()
    let cycle = func [first@move] () -> Resource
        let old = first@move
        first = makeResource()
        return old@move
    let r = cycle@move()
'''
cases['rc2-count-in-consuming'] = RES + '''public func main()
    let count: i32 = 0
    let item = makeResource()
    let f = func [var count, item@move] () -> i32
        count += 1
        consume(item@move)
        return count
    let n = f@move()
'''
cases['rc3-uniq-method-in-consuming'] = RES + '''struct Counter
    public var n: i32 = 0
    public init() => ()
    public func bump(self: uniq/Self) => self.n += 1
public func main()
    let counter = Counter.init()
    let item = makeResource()
    let f = func [var counter@move, item@move] () -> i32
        counter.bump()
        consume(item@move)
        return counter.n
    let n = f@move()
'''
cases['rc4-nested-uniq-in-consuming'] = RES + '''public func main()
    let count: i32 = 0
    let item = makeResource()
    let outer = func [var count, item@move] () -> i32
        var inner = func [count@uniq] () => count@follow += 1
        inner()
        consume(item@move)
        return count
    let n = outer@move()
'''

# ---- a no-capture Closure with a fixed signature Origin where T is Owned (review-1 P1-B2) ----
cases['ow1-fixed-signature-origin-not-owned'] = '''func keep<T>(value: T) -> ()
    T is Owned
    return
func use(x: ref/i32) -> ()
    let f = func (b: ref/i32 during x) -> ref/i32 during x => b
    keep(f@move)
''' + MAIN
cases['ow2-no-fixed-origin-owned'] = '''func keep<T>(value: T) -> ()
    T is Owned
    return
func use(x: ref/i32) -> ()
    let f = func (b: ref/i32) -> i32 => b@follow
    keep(f@move)
''' + MAIN

# ---- G65: a Borrow of an environment binding returned under an annotated result over an enclosing Origin ----
cases['g65a-env-borrow-annotated-during-p'] = '''func use(p: ref/i32) -> i32
    let n: i32 = 1
    let c = func [n] () -> ref/i32 during p => n@ref
    return c()@follow
''' + MAIN

# ---- G216: Closure headers with an omitted inner Origin (PLAN G216) ----
cases['h1-header-unused'] = '''func g() -> i32
    let h = func [] (p: ref/(ref/i32)) -> i32 => 1
    return 1
''' + MAIN
cases['h2-header-called'] = '''func k(q: ref/i32) -> i32
    let h = func [] (p: ref/(ref/i32)) -> i32 => p@follow@follow
    let x = 5
    return h(x@ref@ref)
''' + MAIN

cases['ow3-capture-not-owned'] = """func keep<T>(value: T) -> ()
    T is Owned
    return
func use(x: ref/i32) -> ()
    let f = func [x] () -> i32 => x@follow
    keep(f@move)
""" + MAIN
cases['ow4-reference-not-owned'] = """func keep<T>(value: T) -> ()
    T is Owned
    return
func use(x: ref/i32) -> ()
    keep(x)
""" + MAIN
cases['ow5-fixed-signature-origin-erased'] = """func use(x: ref/i32) -> ()
    let f = func (b: ref/i32 during x) -> ref/i32 during x => b
    let g: (ref/i32 during x) -> ref/i32 during x = f@move
""" + MAIN
cases['t7c-call-local-annotated'] = INT + """public func main()
    let c = func () -> ref/i32
        let l: i32 = 1
        return l@ref
    inspect(c())
"""
cases['t7d-call-local-expression'] = INT + """public func main()
    let n: i32 = 4
    let c = func [n] () => (1 + n)@ref
    inspect(c())
"""

cases['f7k3-g147-3-exclusive-use-of-var-capture'] = """public func main() -> ()
    let n = 7
    var f = func [var n] () -> i32
        let r = n@uniq
        let q = n
        r@follow = 3
        return q
    let v = f()
"""
cases['f7k4-g147-3-ok'] = """public func main() -> ()
    let n = 7
    var f = func [var n] () -> i32
        let r = n@uniq
        r@follow = 3
        let q = n
        return q
    let v = f()
"""
cases['f7o-let-binding-no-retention'] = """func use(z: uniq/i32, x: ref/i32, w: uniq/i32) -> i32
    origin z outlives x
    let r: ref/i32 during x = x
    var c = func [r, w] (v: ref/i32 during x) -> i32
        w@follow = v@follow
        return r@follow
    _ = c(z@follow@ref)
    z@follow = 42
    return c(x)
""" + MAIN
cases['t1d-receiver-result-carries-every-capture-loan'] = INT + """public func main()
    let n: i32 = 4
    var y: i32 = 2
    let r = y@ref
    let get = func [n, r] () => n@ref
    let v = get()
    y = 5
    inspect(v)
"""
cases['t5b-moved-out-carries-every-capture-loan'] = """public func main()
    var x: i32 = 1
    var y: i32 = 2
    let u = x@uniq
    let r = y@ref
    let c = func [u, r] () => u@move
    let a = c@move()
    y = 5
    a@follow = 3
"""

for name, source in cases.items():
    with open(os.path.join(out, name + '.kimi'), 'w', encoding='utf-8', newline='') as f:
        f.write(source)

# ---- G229 class S (BU-28's census; the BU probes as written) ----
for probe, name in [('p3-param-origin-valid', 's-p3'), ('p3d-capture-p-append', 's-p3d'), ('p3e-nocall', 's-p3e'),
                    ('k5-setname-param-retain', 's-k5'), ('r4-annot-during-local', 's-r4'),
                    ('r6-param-store-two-caps-valid-premise', 's-r6'), ('p5-two-env-regions', 's-x-p5'),
                    ('e1-env-through-local-static', 's-x-e1'), ('n6-receiver-field-ref-append', 's-x-n6')]:
    with open(os.path.join(g227, probe + '.kimi'), encoding='utf-8', newline='') as f:
        text = f.read()
    with open(os.path.join(out, name + '.kimi'), 'w', encoding='utf-8', newline='') as f:
        f.write(text)

print(len(cases), 'cases written, plus 9 G229 probes')
