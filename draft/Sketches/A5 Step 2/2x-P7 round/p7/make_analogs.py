# Writes round 2x-P7's method analogs into analogs/ (one .kimi per case): each Closure case rewritten as R4-M99 reads it, a struct
# whose Fields are the environment bindings (their let/var and Types) and an instance method whose receiver is the case's call
# receiver (ref/Self, uniq/Self, Self), with the body's environment names as self.<name>. The real pipelines then apply the receiver
# rules as implemented for methods (Binding's Origin judge, TyCtxt.Retains, MirBorrows' W). Run: python -I make_analogs.py <dir>
import os
import sys

out = sys.argv[1]
os.makedirs(out, exist_ok=True)
INT = 'func inspect(value: ref/i32) => ()\n'
MAIN = 'public func main() => ()\n'
TEXT = 'func makeText() -> string => "a"\nfunc inspectText(value: ref/string) => ()\nfunc consumeText(value: string) => ()\n'
RES = ('struct Resource\n    public var n: i32\n    public init(n: i32) => self.n = n\n'
       'group H\n    public func makeResource() -> Resource => Resource.init(1)\n    public func consume(r: Resource) => ()\n')
cases = {}

cases['m-f7a-622-exchange'] = '''struct Env {o}
    public var state: ref/i32 during o
    public init(state: ref/i32 during o) => self.state = state
    public func call(self: uniq/Self, value: ref/i32 during o) -> ref/i32 during o
        let old = self.state
        self.state = value
        return old
func use(z: uniq/i32, x: ref/i32) -> i32
    origin z outlives x
    var exchange = Env.init(x)
    _ = exchange.call(z@follow@ref)
    let result = exchange.call(x)
    z@follow = 42
    return result@follow
''' + MAIN
cases['m-f7b-1379-branch-store'] = '''struct Env {o}
    public var start: ref/i32 during o
    public init(start: ref/i32 during o) => self.start = start
    public func call(self: uniq/Self, n: ref/i32 during o) -> ref/i32 during o
        let old = self.start
        self.start = n
        return old
func use() -> i32
    let x: i32 = 4
    let r = x@ref
    var c = Env.init(r)
    if x > 0
        let y: i32 = 5
        c.call(y@ref)
    let z = c.call(r)
    return z@follow
''' + MAIN
STORE = '''struct Store {o, q}
    origin o outlives q
    public let holder: uniq/(ref/i32 during o) during q
    public init(holder: uniq/(ref/i32 during o) during q) => self.holder = holder@move
    public func call(self: uniq/Self, b: ref/i32 during o) -> i32
        self.holder@follow = b
        return 0
func use(z: uniq/i32, x: ref/i32) -> i32
    origin z outlives x
    var holder: ref/i32 during x = x
    var store = Store.init(holder@uniq)
    CALL
    z@follow = 42
    return holder@follow
''' + MAIN
cases['m-f7c-575-uniq-capture-store'] = STORE.replace('CALL', 'store.call(z@follow@ref)')
cases['m-f7d-576-moved-receiver'] = STORE.replace('CALL', 'store@move.call(z@follow@ref)')
cases['m-f7k3-g147-3'] = '''struct Env
    public var n: i32
    public init(n: i32) => self.n = n
    public func call(self: uniq/Self) -> i32
        let r = self.n@uniq
        let q = self.n
        r@follow = 3
        return q
public func main() -> ()
    var f = Env.init(7)
    let v = f.call()
'''
cases['m-t1a-env-borrow-shared'] = INT + '''struct Env
    public let n: i32
    public init(n: i32) => self.n = n
    public func call(self: ref/Self) -> ref/i32 => self.n@ref
public func main()
    let get = Env.init(4)
    let v = get.call()
    inspect(v)
'''
cases['m-t1b-env-borrow-shared-move'] = cases['m-t1a-env-borrow-shared'].replace('    inspect(v)\n', '    let moved = get@move\n    inspect(v)\n')
cases['m-t1c-env-borrow-exclusive'] = INT + '''struct Env
    public var n: i32
    public init(n: i32) => self.n = n
    public func call(self: uniq/Self) -> ref/i32
        self.n += 1
        return self.n@ref
public func main()
    var bump = Env.init(4)
    let a = bump.call()
    let b = bump.call()
    inspect(a)
'''
cases['m-t1d-receiver-result-carries-every-capture-loan'] = INT + '''struct Env {o}
    public let n: i32
    public let r: ref/i32 during o
    public init(n: i32, r: ref/i32 during o)
        self.n = n
        self.r = r
    public func call(self: ref/Self) -> ref/i32 => self.n@ref
public func main()
    var y: i32 = 2
    let get = Env.init(4, y@ref)
    let v = get.call()
    y = 5
    inspect(v)
'''
cases['m-t2a-captured-ref'] = INT + '''struct Env {o}
    public let r: ref/i32 during o
    public init(r: ref/i32 during o) => self.r = r
    public func call(self: ref/Self) -> ref/i32 during o => self.r
public func main()
    let x: i32 = 1
    let c = Env.init(x@ref)
    let v = c.call()
    let m = c@move
    inspect(v)
'''
UNIQ = '''struct Env {o}
    public let u: uniq/i32 during o
    public init(u: uniq/i32 during o) => self.u = u@move
    METHOD
public func main()
    var x: i32 = 1
    var c = Env.init(x@uniq)
    BODY
'''
cases['m-t3a-uniq-reborrow-exclusive'] = UNIQ.replace('METHOD', 'public func call(self: uniq/Self) -> uniq/i32 => self.u').replace(
    'BODY', 'let a = c.call()\n    let m = c@move\n    a@follow = 2')
cases['m-t3b-uniq-reborrow-exclusive-ok'] = UNIQ.replace('METHOD', 'public func call(self: uniq/Self) -> uniq/i32 => self.u').replace(
    'BODY', 'let a = c.call()\n    a@follow = 2\n    let m = c@move')
cases['m-t3c-shared-through-captured-uniq'] = INT + UNIQ.replace('METHOD', 'public func call(self: ref/Self) -> ref/i32 => self.u@follow@ref').replace(
    'BODY', 'let a = c.call()\n    let m = c@move\n    inspect(a)')
cases['m-t3d-shared-through-captured-uniq-ok'] = INT + UNIQ.replace('METHOD', 'public func call(self: ref/Self) -> ref/i32 => self.u@follow@ref').replace(
    'BODY', 'let a = c.call()\n    inspect(a)\n    let m = c@move')
cases['m-t5-moved-out'] = UNIQ.replace('METHOD', 'public func call(self: Self) -> uniq/i32 during o => self.u@move').replace(
    'BODY', 'let a = c@move.call()\n    a@follow = 3')
cases['m-t5b-moved-out-carries-every-capture-loan'] = '''struct Env {o, q}
    public let u: uniq/i32 during o
    public let r: ref/i32 during q
    public init(u: uniq/i32 during o, r: ref/i32 during q)
        self.u = u@move
        self.r = r
    public func call(self: Self) -> uniq/i32 during o => self.u@move
public func main()
    var x: i32 = 1
    var y: i32 = 2
    var c = Env.init(x@uniq, y@ref)
    let a = c@move.call()
    y = 5
    a@follow = 3
'''
cases['m-g65a-env-borrow-annotated-during-p'] = '''struct Env {p}
    public let n: i32
    public let anchor: ref/i32 during p
    public init(n: i32, anchor: ref/i32 during p)
        self.n = n
        self.anchor = anchor
    public func call(self: ref/Self) -> ref/i32 during p => self.n@ref
''' + MAIN
cases['m-r1e-reborrow-annotated-capture-origin'] = '''struct Env {p}
    public let u: uniq/i32 during p
    public init(u: uniq/i32 during p) => self.u = u@move
    public func call(self: uniq/Self) -> uniq/i32 during p => self.u
''' + MAIN
cases['m-rc1-cycle-var-reinit'] = RES + '''struct Env
    public var first: Resource
    public init(first: Resource) => self.first = first@move
    public func call(self: Self) -> Resource
        let old = self.first@move
        self.first = H.makeResource()
        return old@move
public func main()
    let c = Env.init(H.makeResource())
    let r = c@move.call()
'''
cases['m-rc2-count-in-consuming'] = RES + '''struct Env
    public var count: i32
    public var item: Resource
    public init(count: i32, item: Resource)
        self.count = count
        self.item = item@move
    public func call(self: Self) -> i32
        self.count += 1
        H.consume(self.item@move)
        return self.count
public func main()
    let c = Env.init(0, H.makeResource())
    let n = c@move.call()
'''
cases['m-A3-closure'] = '''struct SetEnv<V> {q}
    public let slot: uniq/V during q
    public init(slot: uniq/V during q) => self.slot = slot@move
    public func call(self: uniq/Self, y: V) -> ()
        self.slot@follow = y@move
func f<s/T>(value: s/T, other: s/T, x: T) -> ()
    s is owner or uniq
    T is Copy
    var v = value@move
    var slot = other@move
    var set = SetEnv.init(slot@uniq)
    let child = v
    set.call(child@move)
    v@follow = x
    _ = slot@follow
public func main() -> ()
    var n: i32 = 1
    var m: i32 = 2
    f(n, m, 9)
'''
cases['m-A3-closure-ok'] = cases['m-A3-closure'].replace('    v@follow = x\n    _ = slot@follow\n', '    _ = slot@follow\n    v@follow = x\n')

for name, source in cases.items():
    with open(os.path.join(out, name + '.kimi'), 'w', encoding='utf-8', newline='') as f:
        f.write(source)
print(len(cases), 'analogs written')
