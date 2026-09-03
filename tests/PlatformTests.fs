module BAREWire.Tests.PlatformTests

open System.IO
open System.Diagnostics
open BAREWire.Encoding
open BAREWire.Hardware
open BAREWire.Platform
open BAREWire.Tests.Harness

/// Dispatch one obligation to cvc5 and return its verdict line.
let private cvc5 (o: Obligation) : string =
    let path = Path.Combine(Path.GetTempPath(), sprintf "barewire-%s.smt2" o.Id)
    File.WriteAllText(path, (Obligations.smtLib o).Replace("(reset)", ""))
    try
        let psi = ProcessStartInfo("cvc5", path)
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd().Trim()
        p.WaitForExit()
        out
    with _ -> "cvc5-unavailable"

/// The Linux x86_64 description the phase-2 platform tree declares, an eBPF
/// description in the same vocabulary, and the ThreeBody close-encounter
/// frame; every obligation the three generate is dispatched to cvc5.
let run () =
    // ---- Linux x86_64: the HelloProof target ----
    let readContract = Contract.assumed "read-bound" "writes at most count bytes into buf; returns n <= count; n = 0 is end of input" [| "CWE-120" |]
    let linux : PlatformDescription =
        { Id = "cpu-linux-x86_64"; DisplayName = "Linux x86-64 (libc)"; Substrate = "CPU"
          Core = Some (TargetCore.withTriple (TargetCore.create "linux" "x86_64" 64 "little" "libc") "x86_64-unknown-linux-gnu")
          Spaces = [|
            MemorySpace.withAccess (MemorySpace.withBase (MemorySpace.create "text" MemoryKind.Text 4096L 4096) 0x401000L) Access.ReadExecute
            MemorySpace.withAccess (MemorySpace.withBase (MemorySpace.create "rodata" MemoryKind.Rodata 4096L 4096) 0x402000L) Access.ReadOnly
            MemorySpace.withBase (MemorySpace.create "data" MemoryKind.Data 4096L 4096) 0x403000L
            MemorySpace.withGrowth (MemorySpace.create "stack" MemoryKind.Stack 8388608L 16) Growth.Down
            MemorySpace.withGrowth (MemorySpace.create "arena" MemoryKind.Arena 4096L 16) Growth.Up |]
          Surfaces = [| BoundarySurface.create "syscalls" SurfaceKind.Syscall [| Endpoint.syscall "read" 0 [| readContract |]; Endpoint.syscall "write" 1 [||]; Endpoint.syscall "exit_group" 231 [||] |] |]
          Buffers = [| BufferSchema.delimited "consoleReadln" "str" 1024L 10 true "arena" |]
          Transports = [| Transport.withSchema (Transport.create "stdio" TransportKind.Stream [| "read"; "write" |]) "str" |]
          Lifecycle = Lifecycle.process' "_start" "exit_group" Persistence.Volatile
          Notes = [| "bases are those of a non-PIE static ELF" |]
          Limits = [||] }
    equal "linux description consistent" 0 (Array.length (Check.run linux))
    let manifest = Manifest.emit linux
    check "manifest declares consoleReadln" (manifest.Contains "buffer consoleReadln schema=str capacity=1024 framing=delimited delimiter=10 trim=true space=arena") manifest
    check "manifest MEMORY block" (manifest.Contains "rodata (r) : ORIGIN = 0x402000, LENGTH = 4096") manifest
    check "manifest space availability" (manifest.Contains "since=none until=none") manifest
    let obs = Obligations.ofDescription linux
    let ids = obs |> Array.map (fun o -> o.Id)
    check "input bounds against the declaration" (Array.contains "input_bound_consolereadln" ids && Array.contains "input_copy_bound_consolereadln" ids) (String.concat "," ids)
    check "capacity against the arena" (Array.contains "capacity_consolereadln" ids) (String.concat "," ids)
    check "spaces disjoint" (Array.contains "spaces_disjoint" ids) (String.concat "," ids)
    for o in obs do
        equal (sprintf "cvc5 %s" o.Id) "unsat" (cvc5 o)
    // an inconsistent description is refuted, not hidden
    let orphan = { linux with Buffers = [| BufferSchema.fixed' "orphan" "data" 65536L "arena" |] }
    let findings = Check.run orphan
    check "capacity beyond space is a finding" (findings |> Array.exists (fun f -> f.Kind = FindingKind.CapacityExceedsSpace)) (Check.explain findings)
    let orphanObs = Obligations.ofDescription orphan |> Array.filter (fun o -> o.Id = "capacity_orphan")
    equal "orphan capacity obligation exists" 1 (Array.length orphanObs)
    if Array.length orphanObs = 1 then equal "capacity beyond space is refutable" "sat" (cvc5 orphanObs.[0])

    // ---- eBPF: the kernel as a described platform (nominal coverage) ----
    let helpers =
        BoundarySurface.create "helpers" SurfaceKind.HostApi [|
            Endpoint.helper "bpf_map_lookup_elem" 1 "3.19" [||]
            Endpoint.helper "bpf_ringbuf_output" 130 "5.8" [||]
            Endpoint.helper "bpf_loop" 181 "5.17" [||] |]
    let hooks =
        BoundarySurface.create "hooks" SurfaceKind.HostApi [|
            Endpoint.hook "xdp" "xdp_md -> xdp_action" ""
            Endpoint.hook "lsm" "lsm_ctx -> errno" "5.7" |]
    let kernel : PlatformDescription =
        { Id = "bpf-linux-6.x"; DisplayName = "Linux kernel 6.x, eBPF surface"; Substrate = "Kernel"; Core = None
          Spaces = [|
            MemorySpace.create "stack" MemoryKind.Stack 512L 8
            MemorySpace.map "counters" MapKind.PerCpuArray 4096L 8
            MemorySpace.withAvailability (MemorySpace.create "events" MemoryKind.Ring 262144L 8) "5.8" "" |]
          Surfaces = [| helpers; hooks |]
          Buffers = [|
            BufferSchema.fixed' "counter" "Counter" 8L "counters"
            BufferSchema.ring "eventRing" "Event" 262144L 64 "events" |]
          Transports = [||]
          Lifecycle = Lifecycle.process' "" "" Persistence.Volatile
          Notes = [||]
          Limits = [| Limit.create Limit.StackBytes 512L; Limit.create Limit.InstructionBudget 1000000L; Limit.create Limit.IsaLevel 3L |] }
    equal "kernel description consistent" 0 (Array.length (Check.run kernel))
    equal "bpf_loop admitted at 6.1" true (Availability.admits "5.17" "" "6.1")
    equal "bpf_loop refused at 5.4" false (Availability.admits "5.17" "" "5.4")
    equal "bpf_loop admitted at its floor" true (Availability.admits "5.17" "" "5.17")
    equal "6.1 is after 5.17" 1 (Availability.compareVersions "6.1" "5.17")
    equal "5.17 is after 5.8" 1 (Availability.compareVersions "5.17" "5.8")
    equal "until bounds" false (Availability.admits "5.8" "6.0" "6.2")
    let km = Manifest.emit kernel
    check "manifest map kind" (km.Contains "space counters kind=map capacity=4096 align=8 granularity=1 growth=fixed access=rw base=dynamic mapkind=per-cpu-array") km
    check "manifest helper since" (km.Contains "endpoint bpf_loop location=helper-number address=181 signature=none since=5.17 until=none") km
    check "manifest hook signature" (km.Contains "endpoint xdp location=symbol address=xdp signature=xdp_md -> xdp_action") km
    let misalignedBase = { linux with Spaces = [| MemorySpace.withBase (MemorySpace.create "odd" MemoryKind.Rodata 4096L 4096) 0x401010L |] }
    check "base must respect its alignment" (Check.run misalignedBase |> Array.exists (fun f -> f.Kind = FindingKind.BaseMisaligned)) (Check.explain (Check.run misalignedBase))
    let twoSurfaces = { linux with Surfaces = [| BoundarySurface.create "a" SurfaceKind.Syscall [| Endpoint.syscall "read" 0 [||] |]; BoundarySurface.create "b" SurfaceKind.HostApi [| Endpoint.hook "read" "" "" |] |] }
    check "endpoint names are unique across surfaces" (Check.run twoSurfaces |> Array.exists (fun f -> f.Kind = FindingKind.DuplicateName)) (Check.explain (Check.run twoSurfaces))
    check "manifest limits" (km.Contains "limit stack-bytes value=512") km
    check "manifest ring slot" (km.Contains "framing=ring delimiter=none trim=false space=events lifetime=session access=rw slot=64") km
    let badRing = { kernel with Buffers = [| BufferSchema.ring "r" "Event" 100L 64 "events" |] }
    check "ring slot must divide the capacity" (Check.run badRing |> Array.exists (fun f -> f.Kind = FindingKind.InvalidSlot)) (Check.explain (Check.run badRing))
    // slug collisions get distinct anchors
    let twins = { linux with Buffers = [| BufferSchema.fixed' "console-readln" "str" 8L "arena"; BufferSchema.fixed' "Console Readln" "str" 8L "arena" |] }
    let twinIds = Obligations.ofDescription twins |> Array.map (fun o -> o.Id)
    equal "distinct anchors for colliding slugs" (Array.length twinIds) (Array.length (Array.distinct twinIds))
    let kobs = Obligations.ofDescription kernel
    let kids = kobs |> Array.map (fun o -> o.Id)
    check "stack within the verifier limit" (Array.contains "stack_within_limit_stack" kids) (String.concat "," kids)
    for o in kobs do
        equal (sprintf "cvc5 %s" o.Id) "unsat" (cvc5 o)
    // pointer-free: a map value with a pointer field is a finding
    let counterLayout = Layout.create 8 8 [| Field.simple "hits" 0 Repr.U64 AccessKind.ReadWrite |]
    let leaky = Layout.create 16 8 [| Field.simple "hits" 0 Repr.U64 AccessKind.ReadWrite; Field.simple "owner" 8 Repr.Pointer AccessKind.ReadWrite |]
    let layoutOf (name: string) : PeripheralLayout option = if name = "Counter" then Some counterLayout else None
    equal "pointer-free map value passes" 0 (Array.length (Check.runWithLayouts kernel layoutOf))
    let leakyOf (name: string) : PeripheralLayout option = if name = "Counter" then Some leaky else None
    let leakFindings = Check.runWithLayouts kernel leakyOf
    check "pointer in map value is a finding" (leakFindings |> Array.exists (fun f -> f.Kind = FindingKind.PointerInSharedLayout)) (Check.explain leakFindings)
    // a bad availability range is a finding
    let badRange = { kernel with Surfaces = [| BoundarySurface.create "h" SurfaceKind.HostApi [| Endpoint.withAvailability (Endpoint.create "x" EndpointKind.Symbol "x" [||]) "6.0" "5.8" |] |] }
    check "inverted availability is a finding" (Check.run badRange |> Array.exists (fun f -> f.Kind = FindingKind.InvalidAvailability)) (Check.explain (Check.run badRange))

    // ---- ThreeBody: the close-encounter frame over Layer 2 ----
    let abi = Abi.sysvAmd64
    let named (n: string) (r: Repr) : NamedRepr = { Name = n; Repr = r; Count = 1 }
    let frame =
        Validator.derive abi "CloseEncounterFrame" [|
            named "bodyA" Repr.U32; named "bodyB" Repr.U32; named "timestep" Repr.U32
            { Name = "force"; Repr = Repr.U32; Count = 3 }            // three b-posit32 components
            { Name = "quire"; Repr = Repr.U32; Count = 25 } |]        // the 800-bit quire: 25 32-bit words
    equal "frame size is a schema fact" 124 frame.Layout.Size
    equal "frame is pointer-free" true (Layout.isPointerFree frame.Layout)
    let l2 : PlatformDescription =
        { Id = "threebody-host"; DisplayName = "Strix Halo host, Arty A7 sidecar over Layer 2"; Substrate = "CPU"; Core = None
          Spaces = [| MemorySpace.create "umem" MemoryKind.Ring 4194304L 4096 |]
          Surfaces = [| BoundarySurface.create "nic" SurfaceKind.HostApi [| Endpoint.create "eth0" EndpointKind.Symbol "af_xdp" [||] |] |]
          Buffers = [| BufferSchema.fixed' "closeEncounter" "CloseEncounterFrame" (int64 frame.Layout.Size) "umem" |]
          Transports = [| Transport.withMaxUnit (Transport.withRate (Transport.withSchema (Transport.create "layer2" TransportKind.Ethernet [| "eth0" |]) "CloseEncounterFrame") 100000000L) 1500L |]
          Lifecycle = Lifecycle.process' "" "" Persistence.Volatile; Notes = [||]; Limits = [||] }
    equal "threebody description consistent" 0 (Array.length (Check.run l2))
    let tobs = Obligations.ofDescription l2
    let fit = tobs |> Array.filter (fun o -> o.Id = "fits_layer2_closeencounter")
    equal "frame-fits-unit obligation exists" 1 (Array.length fit)
    if Array.length fit = 1 then equal "cvc5 frame fits an Ethernet unit" "unsat" (cvc5 fit.[0])
    let oversize = { l2 with Buffers = [| BufferSchema.fixed' "closeEncounter" "CloseEncounterFrame" 2048L "umem" |] }
    let bad = Obligations.ofDescription oversize |> Array.filter (fun o -> o.Id = "fits_layer2_closeencounter")
    equal "oversize frame obligation exists" 1 (Array.length bad)
    if Array.length bad = 1 then equal "cvc5 refutes an oversize frame" "sat" (cvc5 bad.[0])
    // and the frame's BTF, so the kernel-side redirect program can name it
    let blob = Btf.emit abi [| frame |]
    let image = Btf.read blob
    check "frame btf parses" image.Ok "btf"
    match Btf.tryStruct image "CloseEncounterFrame" with
    | Some t -> equal "quire at bit 192" 192 t.MemberBitOffsets.[4]
    | None -> check "frame btf struct" false "missing"
