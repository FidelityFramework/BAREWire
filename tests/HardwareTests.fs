module BAREWire.Tests.HardwareTests

open System.IO
open System.Diagnostics
open BAREWire.Hardware
open BAREWire.Tests.Harness

let private named (name: string) (repr: Repr) : NamedRepr = { Name = name; Repr = repr; Count = 1 }
let private counted (name: string) (repr: Repr) (count: int) : NamedRepr = { Name = name; Repr = repr; Count = count }

/// The layout validator on the cases docs/10 names, the pointer-free
/// predicate, and the BTF round trip with `bpftool` as the independent oracle
/// when it is installed.
let run () =
    let abi = Abi.sysvAmd64
    // sockaddr_in: 2 + 2 + 4 + 8 = 16 under SysV AMD64
    let sockaddr = derived abi "sockaddr_in" [| named "sin_family" Repr.U16; named "sin_port" Repr.U16; named "sin_addr" Repr.U32; counted "sin_zero" Repr.U8 8 |]
    equal "sockaddr_in size" 16 sockaddr.Layout.Size
    equal "sockaddr_in alignment" 4 sockaddr.Layout.Alignment
    let v = Validator.validate abi sockaddr
    equal "derived descriptor agrees" true v.Agrees
    // hipExternalMemoryHandleDesc-shaped: type (i32), handle (union as widest member: two pointers), size (u64), flags (u32)
    let hip = derived abi "hipExternalMemoryHandleDesc" [| named "type" Repr.I32; counted "handle" Repr.Pointer 2; named "size" Repr.U64; named "flags" Repr.U32 |]
    equal "hip handle at 8" 8 hip.Layout.Fields.[1].Offset
    equal "hip size at 24" 24 hip.Layout.Fields.[2].Offset
    equal "hip struct size" 40 hip.Layout.Size
    // a hand-padded descriptor that puts the handle at 4 is misaligned
    let hand = { hip with Layout = { hip.Layout with Fields = [| hip.Layout.Fields.[0]; { hip.Layout.Fields.[1] with Offset = 4 }; hip.Layout.Fields.[2]; hip.Layout.Fields.[3] |] } }
    let hv = Validator.validate abi hand
    equal "misaligned handle disagrees" false hv.Agrees
    check "misaligned finding named" (hv.Findings |> Array.exists (fun f -> f.Kind = FindingKind.Misaligned)) (Validator.explain hv)
    // EpollEvent: declared 16 on x86-64 by the platform tree, packed to 12 by the kernel
    let epollDeclared = StructLayout.create "epoll_event" (Layout.create 16 8 [| Field.simple "events" 0 Repr.U32 AccessKind.ReadWrite; Field.simple "data" 8 Repr.U64 AccessKind.ReadWrite |]) None
    let ev = Validator.validate abi epollDeclared
    equal "natural epoll_event agrees at 16" true ev.Agrees
    let epollPacked = StructLayout.create "epoll_event_packed" (Layout.create 12 4 [| Field.simple "events" 0 Repr.U32 AccessKind.ReadWrite; Field.array "data" 4 Repr.U32 2 AccessKind.ReadWrite |]) None
    let pv = Validator.validate abi epollPacked
    equal "packed epoll_event as two u32 agrees at 12" true pv.Agrees
    // 32-bit ARM AAPCS aligns i64 to 8; i386 SysV to 4
    let pair32 = derived Abi.i386SysV "pair" [| named "a" Repr.U32; named "b" Repr.I64 |]
    equal "i386 i64 at 4" 4 pair32.Layout.Fields.[1].Offset
    let pairArm = derived Abi.armAapcs "pair" [| named "a" Repr.U32; named "b" Repr.I64 |]
    equal "aapcs i64 at 8" 8 pairArm.Layout.Fields.[1].Offset

    // A large count is a small descriptor, not a large allocation. Its byte
    // arithmetic must not wrap into a layout that validation calls sound.
    let huge = StructLayout.create "huge" (Layout.create 0 8 [| Field.array "data" 0 Repr.U64 536870912 AccessKind.ReadWrite |]) None
    let hugeVerdict = Validator.validate abi huge
    equal "4 GiB field is not a valid zero-byte layout" false hugeVerdict.Agrees
    equal "overflow supplies no fabricated expected size" None hugeVerdict.ExpectedSize
    let rejects name fields kind =
        match Validator.derive abi name fields with
        | Ok layout -> check name false (sprintf "fabricated layout: %A" layout)
        | Error findings -> check name (findings |> Array.exists (fun f -> f.Kind = kind)) (sprintf "%A" findings)
    rejects "derivation rejects 4 GiB field" [| counted "data" Repr.U64 536870912 |] FindingKind.ExtentOverflow
    rejects "derivation rejects offset plus extent overflow" [| counted "prefix" Repr.U8 (System.Int32.MaxValue - 1); counted "suffix" Repr.U8 2 |] FindingKind.ExtentOverflow
    rejects "derivation rejects alignment overflow" [| counted "prefix" Repr.U8 System.Int32.MaxValue; named "aligned" Repr.U64 |] FindingKind.ExtentOverflow
    rejects "derivation rejects trailing padding overflow" [| named "aligned" Repr.U64; counted "suffix" Repr.U8 (System.Int32.MaxValue - 8) |] FindingKind.ExtentOverflow
    rejects "derivation rejects unknown representation" [| named "unknown" "future" |] FindingKind.UnknownRepr
    for count in [ 0; -1 ] do
        rejects (sprintf "derivation rejects count %d" count) [| counted "data" Repr.U8 count |] FindingKind.ZeroCount
    rejects "derivation enforces maximum field count" (Array.init (Layout.MaxFields + 1) (fun _ -> named "field" Repr.U8)) FindingKind.TooManyFields
    equal "largest representable byte extent remains valid" System.Int32.MaxValue (derived abi "largest" [| counted "bytes" Repr.U8 System.Int32.MaxValue |]).Layout.Size
    for invalid in [ { abi with PointerAlign = 0 }; { abi with I64Align = 3 }; { abi with PointerSize = -1 } ] do
        match Validator.derive invalid "invalid ABI" [| named "ptr" Repr.Pointer |] with
        | Error findings -> check "invalid ABI is diagnosed" (findings |> Array.exists (fun f -> f.Kind = FindingKind.InvalidAbi)) (sprintf "%A" findings)
        | Ok _ -> check "invalid ABI is diagnosed" false "accepted"
        equal "validation rejects invalid ABI without division by zero" false (Validator.validate invalid sockaddr).Agrees
    let limit = derived abi "max fields" (Array.init Layout.MaxFields (fun i -> named (string i) Repr.U8))
    equal "maximum permitted field count is valid" true (Validator.validate abi limit).Agrees
    let many = { limit with Layout = { limit.Layout with Fields = Array.append limit.Layout.Fields [| limit.Layout.Fields.[0] |] } }
    check "validation enforces maximum field count" ((Validator.validate abi many).Findings |> Array.exists (fun f -> f.Kind = FindingKind.TooManyFields)) "too many fields accepted"
    let register = StructLayout.create "register" (Layout.create 8 8 [| Field.simple "value" 0 Repr.U64 AccessKind.ReadWrite |]) None
    let invalidBits position width =
        let bits = { Name = "bits"; Position = position; Width = width; Access = AccessKind.ReadOnly }
        let field = { register.Layout.Fields.[0] with BitFields = [| bits |] }
        { register with Layout = { register.Layout with Fields = [| field |] } }
    for position, width in [ System.Int32.MaxValue, 1; 1, System.Int32.MaxValue; -1, 1; 0, 0; 63, 2 ] do
        let verdict = Validator.validate abi (invalidBits position width)
        check (sprintf "bit range %d + %d is rejected" position width)
            (not verdict.Agrees && verdict.Findings |> Array.exists (fun f -> f.Kind = FindingKind.BitFieldOutOfRange)) (Validator.explain verdict)
    equal "last register bit remains valid" true (Validator.validate abi (invalidBits 63 1)).Agrees

    // pointer-free: the kernel's information-flow rule at the type level
    equal "sockaddr_in is pointer-free" true (Layout.isPointerFree sockaddr.Layout)
    equal "hip desc is not pointer-free" false (Layout.isPointerFree hip.Layout)

    // BTF: emit, read back, check the struct, then ask bpftool
    let blob = Btf.emit abi [| sockaddr; epollDeclared |]
    let image = Btf.read blob
    equal "btf parses" true image.Ok
    match Btf.tryStruct image "sockaddr_in" with
    | None -> check "btf has sockaddr_in" false "missing"
    | Some t ->
        equal "btf struct size" 16 t.SizeOrType
        equal "btf member count" 4 (Array.length t.MemberNames)
        equal "btf member name" "sin_addr" t.MemberNames.[2]
        equal "btf member bit offset" 32 t.MemberBitOffsets.[2]
    match Btf.tryStruct image "epoll_event" with
    | None -> check "btf has epoll_event" false "missing"
    | Some t -> equal "btf epoll data at bit 64" 64 t.MemberBitOffsets.[1]
    // a crafted name offset or an entry past the type section never raises
    let crafted = Array.copy blob
    crafted.[24] <- 0xFFuy; crafted.[25] <- 0xFFuy; crafted.[26] <- 0xFFuy; crafted.[27] <- 0xFFuy
    let craftedImage = Btf.read crafted
    check "crafted name offset reads as empty name, no raise" (craftedImage.Ok) "parse failed"
    let overrun = Array.copy blob
    // type_len larger than the section that follows
    overrun.[12] <- 0xFFuy; overrun.[13] <- 0xFFuy
    equal "type section overrun is refused" false (Btf.read overrun).Ok
    let path = Path.Combine(Path.GetTempPath(), "barewire-test.btf")
    File.WriteAllBytes(path, blob)
    try
        let psi = ProcessStartInfo("bpftool", sprintf "btf dump file %s" path)
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd()
        let err = p.StandardError.ReadToEnd()
        p.WaitForExit()
        check "bpftool accepts the blob" (p.ExitCode = 0) err
        check "bpftool sees sockaddr_in" (out.Contains "STRUCT 'sockaddr_in' size=16 vlen=4") out
        check "bpftool sees sin_zero array" (out.Contains "'sin_zero' type_id=") out
        check "bpftool sees epoll data at bit 64" (out.Contains "'data' type_id=4 bits_offset=64") out
    with _ ->
        printfn "note: bpftool not available; BTF checked by the reader only"
