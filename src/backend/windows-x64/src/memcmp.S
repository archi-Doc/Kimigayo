// Copyright (c) All contributors. MIT license.
// Microsoft x64: rcx/rdx pointers, r8 byte count, eax signed result.
.text
.globl memcmp
.def memcmp; .scl 2; .type 32; .endef
.p2align 4
.seh_proc memcmp
memcmp:
    .seh_endprologue
    xor %eax, %eax
    test %r8, %r8
    jz .Ldone
.Lwords:
    cmp $8, %r8
    jb .Lbytes
    mov (%rcx), %r9
    xor (%rdx), %r9
    jnz .LdifferentWord
    add $8, %rcx
    add $8, %rdx
    sub $8, %r8
    jnz .Lwords
    ret
.LdifferentWord:
    // Little endian: the least differing bit locates the first differing byte.
    bsf %r9, %r9
    shr $3, %r9
    movzbl (%rcx,%r9), %eax
    movzbl (%rdx,%r9), %r10d
    sub %r10d, %eax
    ret
.Lbytes:
    movzbl (%rcx), %eax
    movzbl (%rdx), %r9d
    sub %r9d, %eax
    jnz .Ldone
    inc %rcx
    inc %rdx
    dec %r8
    jnz .Lbytes
.Ldone:
    ret
.seh_endproc
