// AAPCS64 register executor. Exceptions unwind through this frame to Guard.S.
#if defined(__aarch64__)
    .text
    .p2align 2
    .globl pylon_call_frame
    .hidden pylon_call_frame
    .type pylon_call_frame, %function
pylon_call_frame:
    .cfi_startproc
    sub sp, sp, #96
    .cfi_def_cfa_offset 96
    stp x19, x20, [sp, #64]
    .cfi_offset w19, -32
    .cfi_offset w20, -24
    stp x29, x30, [sp, #80]
    .cfi_offset w29, -16
    .cfi_offset w30, -8
    add x29, sp, #80
    mov x19, x1
    mov x16, x0

    ldp x9, x10, [x19, #64]
    stp x9, x10, [sp, #0]
    ldp x9, x10, [x19, #80]
    stp x9, x10, [sp, #16]
    ldp x9, x10, [x19, #96]
    stp x9, x10, [sp, #32]
    ldp x9, x10, [x19, #112]
    stp x9, x10, [sp, #48]
    ldp d0, d1, [x19, #128]
    ldp d2, d3, [x19, #144]
    ldp d4, d5, [x19, #160]
    ldp d6, d7, [x19, #176]
    ldr x8, [x19, #192]
    ldp x0, x1, [x19, #0]
    ldp x2, x3, [x19, #16]
    ldp x4, x5, [x19, #32]
    ldp x6, x7, [x19, #48]
    blr x16

    ldr w9, [x19, #200]
    cbz w9, .Linteger
    cmp w9, #3
    b.eq .Linteger
    cmp w9, #12
    b.eq .Lrestore
    cmp w9, #1
    b.eq .Lfloats
    cmp w9, #2
    b.eq .Ldoubles
    cmp w9, #7
    b.gt .Ldoubles
.Lfloats:
    stp s0, s1, [x19, #208]
    stp s2, s3, [x19, #216]
    b .Lrestore
.Ldoubles:
    stp d0, d1, [x19, #208]
    stp d2, d3, [x19, #224]
    b .Lrestore
.Linteger:
    stp x0, x1, [x19, #208]
.Lrestore:
    ldp x19, x20, [sp, #64]
    ldp x29, x30, [sp, #80]
    add sp, sp, #96
    .cfi_def_cfa_offset 0
    .cfi_restore w19
    .cfi_restore w20
    .cfi_restore w29
    .cfi_restore w30
    ret
    .cfi_endproc
    .size pylon_call_frame, .-pylon_call_frame
#endif
    .section .note.GNU-stack, "", %progbits
