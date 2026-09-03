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
    let sockaddr = Validator.derive abi "sockaddr_in" [| named "sin_family" Repr.U16; named "sin_port" Repr.U16; named "sin_addr" Repr.U32; counted "sin_zero" Repr.U8 8 |]
    equal "sockaddr_in size" 16 sockaddr.Layout.Size
    equal "sockaddr_in alignment" 4 sockaddr.Layout.Alignment
    let v = Validator.validate abi sockaddr
    equal "derived descriptor agrees" true v.Agrees
    // hipExternalMemoryHandleDesc-shaped: type (i32), handle (union as widest member: two pointers), size (u64), flags (u32)
    let hip = Validator.derive abi "hipExternalMemoryHandleDesc" [| named "type" Repr.I32; counted "handle" Repr.Pointer 2; named "size" Repr.U64; named "flags" Repr.U32 |]
    equal "hip handle at 8" 8 hip.Layout.Fields.[1].Offset
    equal "hip size at 24" 24 hip.Layout.Fields.[2].Offset
    equal "hip struct size" 40 hip.Layout.Size
    // a hand-padded descriptor that puts the handle at 4 is misaligned
    let hand = { hip with Layout = { hip.Layout with Fields = [| hip.Layout.Fields.[0]; { hip.Layout.Fields.[1] with Offset = 4 }; hip.Layout.Fields.[2]; hip.Layout.Fields.[3] |] } }
    let hv = Validator.validate abi hand
    equal "misaligned handle disagrees" false hv.Agrees
    check "misaligned finding named" (hv.Findings |> Array.exists (fun f -> f.Kind = FindingKind.Misaligned || f.Kind = FindingKind.Overlap)) (Validator.explain hv)
    // EpollEvent: declared 16 on x86-64 by the platform tree, packed to 12 by the kernel
    let epollDeclared = StructLayout.create "epoll_event" (Layout.create 16 8 [| Field.simple "events" 0 Repr.U32 AccessKind.ReadWrite; Field.simple "data" 8 Repr.U64 AccessKind.ReadWrite |]) None
    let ev = Validator.validate abi epollDeclared
    equal "natural epoll_event agrees at 16" true ev.Agrees
    let epollPacked = StructLayout.create "epoll_event_packed" (Layout.create 12 4 [| Field.simple "events" 0 Repr.U32 AccessKind.ReadWrite; Field.array "data" 4 Repr.U32 2 AccessKind.ReadWrite |]) None
    let pv = Validator.validate abi epollPacked
    equal "packed epoll_event as two u32 agrees at 12" true pv.Agrees
    // 32-bit ARM AAPCS aligns i64 to 8; i386 SysV to 4
    let pair32 = Validator.derive Abi.i386SysV "pair" [| named "a" Repr.U32; named "b" Repr.I64 |]
    equal "i386 i64 at 4" 4 pair32.Layout.Fields.[1].Offset
    let pairArm = Validator.derive Abi.armAapcs "pair" [| named "a" Repr.U32; named "b" Repr.I64 |]
    equal "aapcs i64 at 8" 8 pairArm.Layout.Fields.[1].Offset

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
