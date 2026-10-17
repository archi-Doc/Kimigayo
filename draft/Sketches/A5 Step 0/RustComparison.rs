// Rust comparison for prototype round 8 (case 23a proposal). Outside the repository:
//   rustc --edition 2021 --crate-type lib RustComparison.rs              -> compiles: 23a, 34c, 35a and 35b are valid
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
