; Dedicated single-case runtime. Fixed storage; no allocation on a passing verification.
declare dllimport i32 @GetEnvironmentVariableA(ptr, ptr, i32)
declare dllimport i32 @SetHandleInformation(ptr, i32, i32)
@__kimi_test_pipe_name = private constant [15 x i8] c"KIMI_TEST_PIPE\00"
@__kimi_test_case_name = private constant [15 x i8] c"KIMI_TEST_CASE\00"
@__kimi_test_limit_name = private constant [16 x i8] c"KIMI_TEST_LIMIT\00"
@__kimi_test_bytes_name = private constant [16 x i8] c"KIMI_TEST_BYTES\00"
@__kimi_test_pipe = internal global ptr null
@__kimi_test_count = internal global i64 0
@__kimi_test_limit = internal global i64 0
@__kimi_test_bytes = internal global i64 0
@__kimi_test_phase = internal global i32 1
@__kimi_test_temp_name = private constant [15 x i8] c"KIMI_TEST_TEMP\00"
@__kimi_test_temp_value = internal global %kimi.string zeroinitializer

define internal void @__kimi_test_temp(ptr %result) #0 {
entry:
  %template = load %kimi.string, ptr @__kimi_test_temp_value, align 8
  %source = extractvalue %kimi.string %template, 0
  %length = extractvalue %kimi.string %template, 1
  %missing = icmp eq ptr %source, null
  br i1 %missing, label %invalid, label %allocate
allocate:
  %heap = call ptr @GetProcessHeap()
  %buffer = call ptr @HeapAlloc(ptr %heap, i32 0, i64 %length)
  %null = icmp eq ptr %buffer, null
  br i1 %null, label %invalid, label %copy
copy:
  call void @llvm.memcpy.p0.p0.i64(ptr %buffer, ptr %source, i64 %length, i1 false)
  %value = insertvalue %kimi.string %template, ptr %buffer, 0
  store %kimi.string %value, ptr %result, align 8
  ret void
invalid:
  call void @ExitProcess(i32 125)
  unreachable
}

define internal void @__kimi_test_load_temp(ptr %result) #0 {
entry:
  %active = load ptr, ptr @__kimi_test_pipe
  %inactive = icmp eq ptr %active, null
  br i1 %inactive, label %invalid, label %size
size:
  %required = call i32 @GetEnvironmentVariableA(ptr @__kimi_test_temp_name, ptr null, i32 0)
  %valid = icmp ugt i32 %required, 1
  br i1 %valid, label %allocate, label %invalid
allocate:
  %bytes = zext i32 %required to i64
  %heap = call ptr @GetProcessHeap()
  %buffer = call ptr @HeapAlloc(ptr %heap, i32 0, i64 %bytes)
  %null = icmp eq ptr %buffer, null
  br i1 %null, label %invalid, label %read
read:
  %length = call i32 @GetEnvironmentVariableA(ptr @__kimi_test_temp_name, ptr %buffer, i32 %required)
  %expected = sub i32 %required, 1
  %matches = icmp eq i32 %length, %expected
  %odd = and i32 %length, 1
  %even = icmp eq i32 %odd, 0
  %ok = and i1 %matches, %even
  br i1 %ok, label %loop, label %invalid
loop:
  %index = phi i32 [0, %read], [%next, %loop]
  %offset = mul i32 %index, 2
  %p = getelementptr i8, ptr %buffer, i32 %offset
  %q = getelementptr i8, ptr %p, i32 1
  %a = load i8, ptr %p
  %b = load i8, ptr %q
  %ad = icmp ule i8 %a, 57
  %bd = icmp ule i8 %b, 57
  %as = select i1 %ad, i8 48, i8 55
  %bs = select i1 %bd, i8 48, i8 55
  %av = sub i8 %a, %as
  %bv = sub i8 %b, %bs
  %high = shl i8 %av, 4
  %value = or i8 %high, %bv
  %destination = getelementptr i8, ptr %buffer, i32 %index
  store i8 %value, ptr %destination
  %next = add i32 %index, 1
  %count = udiv i32 %length, 2
  %done = icmp eq i32 %next, %count
  br i1 %done, label %exit, label %loop
exit:
  %count64 = zext i32 %count to i64
  %v1 = insertvalue %kimi.string zeroinitializer, ptr %buffer, 0
  %v2 = insertvalue %kimi.string %v1, i64 %count64, 1
  %v3 = insertvalue %kimi.string %v2, i8 1, 2
  store %kimi.string %v3, ptr %result, align 8
  ret void
invalid:
  call void @ExitProcess(i32 125)
  unreachable
}

define internal i64 @__kimi_test_number(ptr %name) #0 {
entry:
  %buffer = alloca [24 x i8], align 1
  %size = call i32 @GetEnvironmentVariableA(ptr %name, ptr %buffer, i32 24)
  %empty = icmp eq i32 %size, 0
  %large = icmp uge i32 %size, 24
  %bad = or i1 %empty, %large
  br i1 %bad, label %invalid, label %loop
loop:
  %index = phi i32 [0, %entry], [%next, %advance]
  %total = phi i64 [0, %entry], [%value, %advance]
  %p = getelementptr i8, ptr %buffer, i32 %index
  %c = load i8, ptr %p
  %digit = sub i8 %c, 48
  %valid = icmp ule i8 %digit, 9
  %room = icmp ule i64 %total, 1844674407370955160
  %ok = and i1 %valid, %room
  br i1 %ok, label %advance, label %invalid
advance:
  %d = zext i8 %digit to i64
  %scaled = mul i64 %total, 10
  %value = add i64 %scaled, %d
  %next = add i32 %index, 1
  %done = icmp eq i32 %next, %size
  br i1 %done, label %exit, label %loop
exit:
  ret i64 %value
invalid:
  call void @ExitProcess(i32 125)
  unreachable
}

define internal void @__kimi_test_write(ptr %data, i32 %length) #0 {
entry:
  %written = alloca i32, align 4
  %handle = load ptr, ptr @__kimi_test_pipe
  %empty = icmp eq i32 %length, 0
  br i1 %empty, label %exit, label %loop
loop:
  %cursor = phi ptr [%data, %entry], [%next, %advance]
  %remaining = phi i32 [%length, %entry], [%rest, %advance]
  %ok = call i32 @WriteFile(ptr %handle, ptr %cursor, i32 %remaining, ptr %written, ptr null)
  %n = load i32, ptr %written
  %failed = icmp eq i32 %ok, 0
  %zero = icmp eq i32 %n, 0
  %excess = icmp ugt i32 %n, %remaining
  %bad1 = or i1 %failed, %zero
  %bad = or i1 %bad1, %excess
  br i1 %bad, label %invalid, label %advance
advance:
  %rest = sub i32 %remaining, %n
  %next = getelementptr i8, ptr %cursor, i32 %n
  %done = icmp eq i32 %rest, 0
  br i1 %done, label %exit, label %loop
exit:
  ret void
invalid:
  call void @ExitProcess(i32 125)
  unreachable
}

; 32-byte little-endian header: total length, version, kind, IssueId, SiteId,
; phase (body=1), total failures. Payload never exceeds 65504 bytes.
define internal void @__kimi_test_frame(i16 %kind, i64 %issue, i32 %site, ptr %payload, i32 %size) #0 {
entry:
  %header = alloca {i32, i16, i16, i64, i32, i32, i64}, align 8
  %length = add i32 %size, 32
  %a = insertvalue {i32, i16, i16, i64, i32, i32, i64} zeroinitializer, i32 %length, 0
  %b = insertvalue {i32, i16, i16, i64, i32, i32, i64} %a, i16 1, 1
  %c = insertvalue {i32, i16, i16, i64, i32, i32, i64} %b, i16 %kind, 2
  %d = insertvalue {i32, i16, i16, i64, i32, i32, i64} %c, i64 %issue, 3
  %e = insertvalue {i32, i16, i16, i64, i32, i32, i64} %d, i32 %site, 4
  %phase = load i32, ptr @__kimi_test_phase
  %f = insertvalue {i32, i16, i16, i64, i32, i32, i64} %e, i32 %phase, 5
  %count = load i64, ptr @__kimi_test_count
  %g = insertvalue {i32, i16, i16, i64, i32, i32, i64} %f, i64 %count, 6
  store {i32, i16, i16, i64, i32, i32, i64} %g, ptr %header
  call void @__kimi_test_write(ptr %header, i32 32)
  call void @__kimi_test_write(ptr %payload, i32 %size)
  ret void
}

define internal i64 @__kimi_test_begin() #0 {
entry:
  %handle = call i64 @__kimi_test_number(ptr @__kimi_test_pipe_name)
  %pipe = inttoptr i64 %handle to ptr
  store ptr %pipe, ptr @__kimi_test_pipe
  %ok = call i32 @SetHandleInformation(ptr %pipe, i32 1, i32 0)
  %bad = icmp eq i32 %ok, 0
  br i1 %bad, label %invalid, label %ready
ready:
  %limit = call i64 @__kimi_test_number(ptr @__kimi_test_limit_name)
  store i64 %limit, ptr @__kimi_test_limit
  %bytes = call i64 @__kimi_test_number(ptr @__kimi_test_bytes_name)
  store i64 %bytes, ptr @__kimi_test_bytes
  call void @__kimi_test_load_temp(ptr @__kimi_test_temp_value)
  call void @__kimi_test_frame(i16 0, i64 0, i32 -1, ptr @__kimi_test_artifact, i32 67)
  %case = call i64 @__kimi_test_number(ptr @__kimi_test_case_name)
  ret i64 %case
invalid:
  call void @ExitProcess(i32 125)
  unreachable
}

define internal i64 @__kimi_test_observe(i1 %condition, i32 %site) #0 {
entry:
  %active = load ptr, ptr @__kimi_test_pipe
  %inactive = icmp eq ptr %active, null
  br i1 %inactive, label %invalid, label %check
check:
  br i1 %condition, label %pass, label %failure
pass:
  ret i64 0
failure:
  %previous = load i64, ptr @__kimi_test_count
  %overflow = icmp eq i64 %previous, -1
  br i1 %overflow, label %invalid, label %latch
latch:
  %issue = add i64 %previous, 1
  store i64 %issue, ptr @__kimi_test_count
  %limit = load i64, ptr @__kimi_test_limit
  %bytes = load i64, ptr @__kimi_test_bytes
  %within = icmp ule i64 %issue, %limit
  %room = icmp uge i64 %bytes, 32
  %keep = and i1 %within, %room
  %first = icmp eq i64 %issue, 1
  %send = or i1 %keep, %first
  br i1 %send, label %report, label %omit
report:
  call void @__kimi_test_frame(i16 2, i64 %issue, i32 %site, ptr null, i32 0)
  %remaining = sub i64 %bytes, 32
  %budget = select i1 %room, i64 %remaining, i64 0
  store i64 %budget, ptr @__kimi_test_bytes
  %retained = select i1 %keep, i64 %issue, i64 0
  ret i64 %retained
omit:
  ret i64 0
invalid:
  call void @ExitProcess(i32 125)
  unreachable
}

define internal void @__kimi_test_message(i64 %issue, ptr %text) #0 {
entry:
  %none = icmp eq i64 %issue, 0
  %bytes = load i64, ptr @__kimi_test_bytes
  %room = icmp ugt i64 %bytes, 32
  %some = xor i1 %none, true
  %keep = and i1 %some, %room
  br i1 %keep, label %message, label %exit
message:
  %data = load ptr, ptr %text
  %lp = getelementptr %kimi.string, ptr %text, i32 0, i32 1
  %length = load i64, ptr %lp
  %available = sub i64 %bytes, 32
  %fits = icmp ult i64 %length, %available
  %bounded = select i1 %fits, i64 %length, i64 %available
  %small = icmp ult i64 %bounded, 65504
  %count64 = select i1 %small, i64 %bounded, i64 65504
  %count = trunc i64 %count64 to i32
  %remaining = sub i64 %available, %count64
  store i64 %remaining, ptr @__kimi_test_bytes
  %truncated = icmp ult i64 %count64, %length
  %flags = zext i1 %truncated to i32
  call void @__kimi_test_frame(i16 3, i64 %issue, i32 %flags, ptr %data, i32 %count)
  br label %exit
exit:
  ret void
}

define internal void @__kimi_test_require_abort(i64 %issue, i32 %site) noreturn #0 {
entry:
  call void @__kimi_test_frame(i16 4, i64 %issue, i32 %site, ptr null, i32 0)
  call void @ExitProcess(i32 1)
  unreachable
}

define internal void @__kimi_test_aborted() #0 {
entry:
  call void @__kimi_test_frame(i16 5, i64 0, i32 -1, ptr null, i32 0)
  ret void
}

define internal void @__kimi_test_scalar(i64 %issue, i32 %which, i16 %kind, i16 %width, i128 %bits) #0 {
entry:
  %remaining = load i64, ptr @__kimi_test_bytes
  %room = icmp uge i64 %remaining, 56
  %failed = icmp ne i64 %issue, 0
  %keep = and i1 %room, %failed
  br i1 %keep, label %report, label %exit
report:
  %payload = alloca [24 x i8], align 8
  store i32 %which, ptr %payload, align 1
  %kindp = getelementptr i8, ptr %payload, i32 4
  store i16 %kind, ptr %kindp, align 1
  %widthp = getelementptr i8, ptr %payload, i32 6
  store i16 %width, ptr %widthp, align 1
  %bitsp = getelementptr i8, ptr %payload, i32 8
  store i128 %bits, ptr %bitsp, align 1
  %rest = sub i64 %remaining, 56
  store i64 %rest, ptr @__kimi_test_bytes
  call void @__kimi_test_frame(i16 7, i64 %issue, i32 -1, ptr %payload, i32 24)
  br label %exit
exit:
  ret void
}
