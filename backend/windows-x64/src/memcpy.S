// Copyright (c) All contributors. MIT license.
#include "copy.inc"
.text
.globl memcpy
.def memcpy; .scl 2; .type 32; .endef
.p2align 4
.seh_proc memcpy
memcpy:
    .seh_endprologue
    mov %rcx, %rax
    copy_forward
.seh_endproc
