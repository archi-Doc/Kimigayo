// Copyright (c) All contributors. MIT license.
// RCX destination, RDX source, R8 count; RAX original destination.
// Only volatile R9 is scratch. No overread or stack storage.
.macro copy_forward
    cmp $8, %r8
    jb .Lbytes\@
.Lwords\@:
    mov (%rdx), %r9
    mov %r9, (%rcx)
    add $8, %rdx
    add $8, %rcx
    sub $8, %r8
    cmp $8, %r8
    jae .Lwords\@
.Lbytes\@:
    test %r8, %r8
    jz .Ldone\@
.Lbyte\@:
    mov (%rdx), %r9b
    mov %r9b, (%rcx)
    inc %rdx
    inc %rcx
    dec %r8
    jnz .Lbyte\@
.Ldone\@:
    ret
.endm
