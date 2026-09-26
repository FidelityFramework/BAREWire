module BAREWire.Tests.StaticStorageTests

open BAREWire.Platform
open BAREWire.Tests.Harness

let run () =
    let space : MemorySpace = {
        Name = "rodata"; Kind = MemoryKind.Rodata; Capacity = 4096L; Alignment = 4096; Granularity = 4096
        Growth = Growth.Fixed; Access = Access.ReadOnly; Base = None; Notes = ""
        MapKind = ""; Since = ""; Until = ""
    }
    let request name length alignment : StorageRequest = { Name = name; Length = length; Alignment = alignment }
    let planned storage requests =
        match StaticStorage.plan storage requests with
        | Ok layout -> layout
        | Error findings -> failwithf "invalid storage fixture: %A" findings
    let refuses name kind storage requests =
        match StaticStorage.plan storage requests with
        | Error findings -> check name (findings |> Array.exists (fun f -> f.Kind = kind)) (sprintf "%A" findings)
        | Ok layout -> check name false (sprintf "fabricated layout: %A" layout)
    let requests = [| request "comma" 3L 1; request "value" 8L 8; request "empty-string" 1L 1 |]
    let retained = Array.copy requests
    let layout = planned space requests
    equal "static storage preserves request order, names and lengths"
        [| "comma", 0L, 3L; "value", 8L, 8L; "empty-string", 16L, 1L |]
        (layout.Placements |> Array.map (fun p -> p.Name, p.Offset, p.Length))
    equal "static storage includes alignment padding in used extent" 17L layout.UsedSize
    equal "static storage reserves declared granularity" 4096L layout.AllocationSize
    equal "static storage carries origin alignment" 4096 layout.Alignment
    equal "static storage cites the memory space" "rodata" layout.Space
    equal "planning leaves requests unchanged" retained requests
    equal "static storage planning is deterministic" layout (planned space requests)
    let exact = planned { space with Capacity = 16L; Alignment = 8; Granularity = 8 } [| request "a" 3L 1; request "b" 8L 8 |]
    equal "aligned endpoint at capacity is accepted" 16L exact.AllocationSize
    let empty = planned space [||]
    equal "empty request set needs no pool bytes" (0L, 0L, 0) (empty.UsedSize, empty.AllocationSize, empty.Placements.Length)
    refuses "tail padding must fit capacity" "capacity-exceeded" { space with Capacity = 17L; Granularity = 8 } requests
    refuses "request extent must fit capacity" "capacity-exceeded" { space with Capacity = 8L; Granularity = 1 } requests
    refuses "duplicate allocation names are errors" "duplicate-name" space [| request "a" 1L 1; request "a" 1L 1 |]
    refuses "zero byte allocation is explicit error" "invalid-length" space [| request "a" 0L 1 |]
    refuses "negative byte allocation is explicit error" "invalid-length" space [| request "a" -1L 1 |]
    refuses "unnamed allocation is explicit error" "invalid-name" space [| request "" 1L 1 |]
    refuses "invalid allocation alignment is explicit error" "invalid-alignment" space [| request "a" 1L 3 |]
    refuses "pool cannot promise stronger alignment than its space" "unsupported-alignment" { space with Alignment = 4 } [| request "a" 1L 8 |]
    for label, kind, invalid in [
        "space name", "invalid-space", { space with Name = "" }
        "space kind", "unsupported-space", { space with Kind = MemoryKind.Heap }
        "growth", "unsupported-growth", { space with Growth = Growth.Down }
        "access", "unsupported-access", { space with Access = Access.ReadWrite }
        "base", "unsupported-base", { space with Base = Some 0L }
        "capacity", "invalid-capacity", { space with Capacity = 0L }
        "origin alignment", "invalid-alignment", { space with Alignment = 3 }
        "granularity", "invalid-granularity", { space with Granularity = 0 } ] do
        refuses ("unsupported or invalid " + label) kind invalid [||]
    let huge = { space with Capacity = System.Int64.MaxValue; Alignment = 8; Granularity = 1 }
    let max = System.Int64.MaxValue
    equal "int64 maximum stays exact" max (planned huge [| request "huge" max 1 |]).AllocationSize
    refuses "addition cannot wrap into capacity" "extent-overflow" huge [| request "huge" max 1; request "tail" 1L 1 |]
    refuses "alignment cannot wrap into capacity" "extent-overflow" huge [| request "huge" max 1; request "tail" 1L 8 |]
    refuses "granularity cannot wrap into capacity" "extent-overflow" { huge with Granularity = 8 } [| request "huge" max 1 |]

    // Exercise the plan as actual backing storage, including gaps and tail
    // padding. No independent per-object placement supplies these addresses.
    let bytes = Array.zeroCreate<byte> (int layout.AllocationSize)
    let contents = [| [| 44uy; 32uy; 0uy |]; [| 1uy; 2uy; 3uy; 4uy; 5uy; 6uy; 7uy; 8uy |]; [| 0uy |] |]
    for i in 0 .. layout.Placements.Length - 1 do
        let p = layout.Placements[i]
        Array.blit contents[i] 0 bytes (int p.Offset) (int p.Length)
    bytesEqual "placement maps a value into its actual pool bytes" contents[1] bytes[8..15]
    bytesEqual "placement leaves inter-allocation padding" (Array.zeroCreate 5) bytes[3..7]
    equal "placement reserves untouched tail padding" true (bytes[17..] |> Array.forall ((=) 0uy))

    // Independently placed writable globals do not inherit the read-only pool
    // offsets. Reservation is a preflight; only the actual whole-region check
    // commits capacity, padding, permissions and source-object correspondence.
    let writable = { space with Name = "data"; Kind = MemoryKind.Data; Access = Access.ReadWrite }
    let reserved =
        match WritableStorage.reserve writable [| request "first" 24L 8; request "second" 24L 8 |] with
        | Ok value -> value
        | Error findings -> failwithf "invalid writable fixture: %A" findings
    equal "writable reservation accounts all requested payload" 48L reserved.PayloadSize
    let region: WritableRegion = {
        Section = ".data"; Address = 8192L; Length = 128L; Allocated = true; Access = Access.ReadWrite
        Objects = [| { Name = "first"; Address = 8200L; Length = 24L }
                     { Name = "second"; Address = 8256L; Length = 24L }
                     { Name = "runtime"; Address = 8192L; Length = 8L } |]
    }
    let committed =
        match WritableStorage.commit reserved ".data" region with
        | Ok value -> value
        | Error findings -> failwithf "invalid observed writable region: %A" findings
    equal "artifact commitment includes runtime bytes and padding" 128L committed.UsedSize
    equal "artifact commitment rounds the entire region" 4096L committed.AllocationSize
    equal "artifact commitment retains actual independently placed addresses"
        [| 8200L; 8256L |] (committed.Objects |> Array.map (fun value -> value.Address))
    let refusesReservation label kind storage requests =
        match WritableStorage.reserve storage requests with
        | Error findings -> check label (Array.exists (fun finding -> finding.Kind = kind) findings) (sprintf "%A" findings)
        | Ok value -> check label false (sprintf "unexpected reservation %A" value)
    let refusesCommit label kind reservation actual =
        match WritableStorage.commit reservation ".data" actual with
        | Error findings -> check label (Array.exists (fun finding -> finding.Kind = kind) findings) (sprintf "%A" findings)
        | Ok value -> check label false (sprintf "unexpected commitment %A" value)
    refusesReservation "two objects each fitting can jointly exceed capacity" "capacity-exceeded"
        { writable with Capacity = 40L; Granularity = 1 } [| request "a" 24L 8; request "b" 24L 8 |]
    refusesReservation "writable inventory rejects duplicate names" "duplicate-name" writable [| request "a" 8L 8; request "a" 8L 8 |]
    refusesReservation "writable inventory rejects nonpositive extent" "invalid-length" writable [| request "a" 0L 8 |]
    refusesReservation "writable inventory rejects unprovided alignment" "unsupported-alignment" { writable with Alignment = 4 } [| request "a" 8L 8 |]
    refusesReservation "read-only declaration cannot authorize mutable storage" "unsupported-access" { writable with Access = Access.ReadOnly } [| request "a" 1L 1 |]
    refusesCommit "data reservation cannot silently map to bss" "wrong-section" reserved { region with Section = ".bss" }
    refusesCommit "nonallocated section cannot supply program storage" "wrong-permissions" reserved { region with Allocated = false }
    refusesCommit "read-only artifact cannot supply initialized program storage" "wrong-permissions" reserved { region with Access = Access.ReadOnly }
    refusesCommit "region origin must preserve declared alignment" "unaligned-region" reserved { region with Address = 8193L }
    refusesCommit "complete region exceeds capacity although objects fit" "capacity-exceeded" reserved { region with Length = 4097L }
    refusesCommit "tail granularity belongs to whole region" "capacity-exceeded"
        { reserved with Space = { writable with Capacity = 128L } } region
    refusesCommit "missing artifact source allocation fails" "missing-or-duplicate-object" reserved { region with Objects = [| region.Objects[0] |] }
    refusesCommit "duplicate artifact source allocation fails" "missing-or-duplicate-object" reserved { region with Objects = Array.append region.Objects [| region.Objects[0] |] }
    refusesCommit "changed artifact object extent fails" "wrong-extent" reserved
        { region with Objects = [| { region.Objects[0] with Length = 16L }; region.Objects[1] |] }
    refusesCommit "misaligned actual object fails" "unaligned-object" reserved
        { region with Objects = [| { region.Objects[0] with Address = 8201L }; region.Objects[1] |] }
    refusesCommit "out-of-region actual object fails" "outside-region" reserved
        { region with Objects = [| region.Objects[0]; { region.Objects[1] with Address = 8320L } |] }
    refusesCommit "different source objects may not overlap" "overlap" reserved
        { region with Objects = [| region.Objects[0]; { region.Objects[1] with Address = 8216L } |] }
    refusesCommit "runtime storage cannot alias source storage" "overlap" reserved
        { region with Objects = Array.append region.Objects [| { Name = "runtime-other"; Address = 8208L; Length = 8L } |] }
    refusesCommit "partially outside runtime object cannot hide overlap" "outside-region" reserved
        { region with Objects = Array.append region.Objects [| { Name = "runtime-other"; Address = 8191L; Length = 20L } |] }
    refusesCommit "runtime object extent cannot overflow" "outside-region" reserved
        { region with Objects = Array.append region.Objects [| { Name = "runtime-other"; Address = 8319L; Length = System.Int64.MaxValue } |] }
    refusesCommit "observed object extent must be positive" "outside-region" reserved
        { region with Objects = Array.append region.Objects [| { Name = "runtime-other"; Address = 8192L; Length = -1L } |] }
    refusesCommit "stale reservation payload fails" "stale-reservation" { reserved with PayloadSize = 1L } region
    refusesCommit "declared fixed base remains authoritative" "wrong-base"
        { reserved with Space = { writable with Base = Some 4096L } } region
    refusesCommit "observed address endpoint cannot overflow" "invalid-region" reserved
        { region with Address = System.Int64.MaxValue - 3L; Length = 8L }
    let maximum = { writable with Capacity = System.Int64.MaxValue; Granularity = 1; Alignment = 1 }
    refusesReservation "inventory summation cannot wrap" "capacity-exceeded" maximum [| request "a" System.Int64.MaxValue 1; request "b" 1L 1 |]
