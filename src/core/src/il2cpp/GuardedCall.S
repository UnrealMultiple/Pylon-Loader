// int bl_eh_try(void (*body)(void*), void* arg, void* guard)
//
// 在本帧里保存被调用方保存寄存器（x19-x28, d8-d15, x29, x30）并调 body(arg)。
// body 正常返回时返回 0。
//
// 帧声明个人风格 bl_eh_personality（GuardedCall.cpp）。为了让它在不碰 thread_local 的
// 前提下找到本帧（minSdk 24 的 __emutls_get_address 每次调用要 ~50ns），trampoline 在
// 帧里写一条标记：
//
//     [sp+160] = kMarkMagic ^ (sp+160)     值随地址变
//     [sp+168] = guard                     个人风格把 Raw 写到这里
//
// 并在两个出口都擦掉标记（死栈上残留的标记会被误认）。个人风格从自己的帧往上找
// 第一条标记：那是最内层的 trampoline、正在处理的那个。它调 bl_eh_land(mark - 160)，
// 把 sp 放回去并从同一个尾奏带着 1 返回：被调用方保存寄存器回到调用 bl_eh_try 时的值，
// 对调用方来说就是"一个返回 1 的函数"。
//
// kMarkMagic 必须与 GuardedCall.cpp 一致：0x626c5f65685f6d6b。

#if defined(__aarch64__)

    .text
    .p2align 2
    .globl  bl_eh_try
    .hidden bl_eh_try
    .type   bl_eh_try, %function
bl_eh_try:
    .cfi_startproc
    // 0x9b = 间接 | 相对 pc | sdata4：.eh_frame 指向存着个人风格地址的格子。
    .cfi_personality 0x9b, bl_eh_personality_ref
    stp     x29, x30, [sp, #-176]!
    .cfi_def_cfa_offset 176
    .cfi_offset w30, -168
    .cfi_offset w29, -176
    mov     x29, sp
    .cfi_def_cfa w29, 176
    stp     x19, x20, [sp, #16]
    stp     x21, x22, [sp, #32]
    stp     x23, x24, [sp, #48]
    stp     x25, x26, [sp, #64]
    stp     x27, x28, [sp, #80]
    stp     d8, d9, [sp, #96]
    stp     d10, d11, [sp, #112]
    stp     d12, d13, [sp, #128]
    stp     d14, d15, [sp, #144]
    .cfi_offset w19, -160
    .cfi_offset w20, -152
    .cfi_offset w21, -144
    .cfi_offset w22, -136
    .cfi_offset w23, -128
    .cfi_offset w24, -120
    .cfi_offset w25, -112
    .cfi_offset w26, -104
    .cfi_offset w27, -96
    .cfi_offset w28, -88
    .cfi_offset b8, -80
    .cfi_offset b9, -72
    .cfi_offset b10, -64
    .cfi_offset b11, -56
    .cfi_offset b12, -48
    .cfi_offset b13, -40
    .cfi_offset b14, -32
    .cfi_offset b15, -24
    movz    x11, #0x6d6b
    movk    x11, #0x685f, lsl #16
    movk    x11, #0x5f65, lsl #32
    movk    x11, #0x626c, lsl #48
    add     x10, sp, #160
    eor     x11, x11, x10
    stp     x11, x2, [sp, #160]
    mov     x9, x0
    mov     x0, x1
    blr     x9
    mov     w0, #0
.Leh_restore:
    str     xzr, [sp, #160]
    ldp     d14, d15, [sp, #144]
    ldp     d12, d13, [sp, #128]
    ldp     d10, d11, [sp, #112]
    ldp     d8, d9, [sp, #96]
    ldp     x27, x28, [sp, #80]
    ldp     x25, x26, [sp, #64]
    ldp     x23, x24, [sp, #48]
    ldp     x21, x22, [sp, #32]
    ldp     x19, x20, [sp, #16]
    ldp     x29, x30, [sp], #176
    ret
    .cfi_endproc
    .size   bl_eh_try, .-bl_eh_try

// [[noreturn]] void bl_eh_land(uintptr_t sp)
//
// 从个人风格里调用，在展开器栈的最底部。trampoline 之下的所有帧都被丢弃。
    .p2align 2
    .globl  bl_eh_land
    .hidden bl_eh_land
    .type   bl_eh_land, %function
bl_eh_land:
    mov     sp, x0
    mov     w0, #1
    b       .Leh_restore
    .size   bl_eh_land, .-bl_eh_land

    .section .data.rel.ro.bl_eh_personality_ref, "aw", %progbits
    .p2align 3
    .hidden bl_eh_personality_ref
bl_eh_personality_ref:
    .xword  bl_eh_personality
    .size   bl_eh_personality_ref, 8

#endif

    .section .note.GNU-stack, "", %progbits
