// Rust comparison for prototype rounds 8 (case 23a proposal) and 9 (G113). Outside the repository:
//   rustc --edition 2021 --crate-type lib RustComparison.rs              -> compiles: 23a, 34c, 35a, 35b, 36f, R5, R6 valid
//   rustc --edition 2021 --crate-type lib --cfg neg RustComparison.rs    -> two E0506 errors: 23a's old slot, 34a
#![allow(unused_assignments, unused_variables, dead_code)]

struct Node<'a> { value: i32, next: Option<&'a mut Node<'a>> }

fn case23a() {
    let mut x = 1; let mut z = 3;
    let mut slot: &mut i32 = &mut x;
    let mut slot2: &mut i32 = &mut z;
    let mut rr: &mut &mut i32 = &mut slot;
    let c: &mut i32 = &mut **rr;
    rr = &mut slot2;
    **rr = 7;
    *c = 1;
}

fn case34c(i: usize) {
    let mut x = 1; let mut y = 2; let mut z = 3; let mut w = 4;
    let mut arr: [&mut i32; 2] = [&mut x, &mut y];
    let c: &mut i32 = &mut *arr[i];
    arr = [&mut z, &mut w];
    *arr[i] = 7;
    *c = 1;
}

fn case35a<'a>(head: &'a mut Node<'a>) {
    let mut node: &mut Node<'a> = head;
    let t: &mut Node<'a> = match node.next { Some(ref mut n) => &mut **n, None => return };
    node = t;
    let b: &mut i32 = &mut node.value;
    let t2: &mut Node<'a> = match node.next { Some(ref mut n) => &mut **n, None => return };
    node = t2;
    *b = 1;
    node.value = 2;
}

fn case35b<'a>(head: &'a mut Node<'a>) {
    let mut node: &mut Node<'a> = head;
    let mut keep: Option<&mut i32> = None;
    loop {
        keep = Some(&mut node.value);
        let t: &mut Node<'a> = match node.next { Some(ref mut n) => &mut **n, None => break };
        node = t;
    }
    if let Some(k) = keep { *k = 1; }
}

// Round 9 (G113), 36f: the item does not borrow `r`, so writing another Field of `h` is valid; design A's desc(c)
// rejects it (loss P12).
trait Take { type Item; fn take(&mut self) -> Option<Self::Item>; }
struct H<'s, S> { inner: &'s mut S, other: i32 }

fn case36f<S: Take>(src: &mut S) -> Option<S::Item> {
    let mut h = H { inner: src, other: 0 };
    let r = &mut h;
    let first = r.inner.take();
    h.other = 5;
    first
}

// Review R5: a second call on the same value through another name; valid by value identity (S2 rejects it: another
// receiver Place). R6: a write to the receiver's Field by another name. Both are P12 forms.
fn review_r5<S: Take>(src: &mut S) -> (Option<S::Item>, Option<S::Item>) {
    let mut h = H { inner: src, other: 0 };
    let r = &mut h;
    let first = r.inner.take();
    let second = h.inner.take();
    (first, second)
}

fn review_r6<'s, S: Take>(src: &'s mut S, src2: &'s mut S) -> Option<S::Item> {
    let mut h = H { inner: src, other: 0 };
    let r = &mut h;
    let first = r.inner.take();
    h.inner = src2;
    first
}

#[cfg(neg)]
fn case23a_old_slot() {
    let mut x = 1; let mut z = 3;
    let mut slot: &mut i32 = &mut x;
    let mut slot2: &mut i32 = &mut z;
    let mut rr: &mut &mut i32 = &mut slot;
    let c: &mut i32 = &mut **rr;
    rr = &mut slot2;
    *slot = 5;
    *c = 1;
}

#[cfg(neg)]
fn case34a(i: usize) {
    let mut x = 1; let mut y = 2; let mut z = 3;
    let mut arr: [&mut i32; 2] = [&mut x, &mut y];
    let c: &mut i32 = &mut *arr[i];
    arr[0] = &mut z;
    *arr[i] = 7;
    *c = 1;
}
