module BAREWire.Tests.DispatchRegionTests

open System.Numerics
open BAREWire.Hardware
open BAREWire.Platform
open BAREWire.Tests.Harness

let private slice allocation offset length : ByteSlice =
    { Allocation = allocation; Offset = offset; Length = length }

let private allocation id length access : DispatchAllocation =
    { Id = id; Space = "host-arena"; ByteLength = length; Access = access; Lifetime = Lifetime.Request }

let private fixture () : DispatchRegion =
    let layout = derived Abi.sysvAmd64 "MapEnvironment" [|
        { Name = "table"; Repr = Repr.Pointer; Count = 1 }
        { Name = "tableLength"; Repr = Repr.U64; Count = 1 }
        { Name = "output"; Repr = Repr.Pointer; Count = 1 } |]
    let reads = [| slice "table" 0L 32L; slice "table" 64L 16L |]
    let partition id lo hi : DispatchPartition =
        { Id = id; Lower = lo; Upper = hi; Reads = reads; Writes = [| slice "image" (lo * 4L) ((hi - lo) * 4L) |] }
    { Id = "map-frame"; Source = "graph:region:17"; WorkerEntry = "graph:worker:18"
      Target = Abi.sysvAmd64; IterationCount = 11L
      Allocations = [| allocation "table" 128L AccessKind.ReadOnly; allocation "image" 44L AccessKind.ReadWrite |]
      Inputs = [| { Id = "complete-table"; Required = reads; Supplied = [| slice "table" 0L 80L |] } |]
      Partitions = [| partition "first" 0L 4L; partition "middle" 4L 8L; partition "tail" 8L 11L |]
      CaptureLayout = layout.Layout; WorkerCaptureLayout = layout.Layout }

let private reject name kind region =
    let verdict = DispatchRegions.validate region
    check name (not verdict.SpatiallyValid && Array.exists (fun (f: DispatchCheck) -> f.Kind = kind) verdict.Findings)
        (sprintf "expected %s, got %A" kind verdict.Findings)

let private accept name region =
    let verdict = DispatchRegions.validate region
    check name verdict.SpatiallyValid (sprintf "%A" verdict.Findings)

let run () =
    let max = System.Int64.MaxValue
    let min = System.Int64.MinValue
    // Independent unbounded-integer oracles exercise signed endpoints. Neither
    // oracle copies the subtraction/division implementation used by the guards.
    let points = [| min; -1L; 0L; 1L; 4L; max - 1L; max |]
    let mathematicalContains length offset count =
        length >= 0L && offset >= 0L && count >= 0L && BigInteger offset + BigInteger count <= BigInteger length
    let mutable containsAgrees = true
    let mutable productAgrees = true
    let mutable disjointAgrees = true
    for length in points do
        for offset in points do
            for count in points do
                containsAgrees <- containsAgrees &&
                    DispatchRegions.contains length offset count = mathematicalContains length offset count
                productAgrees <- productAgrees &&
                    DispatchRegions.fitsByteLength length offset count =
                        (length >= 0L && offset >= 0L && count > 0L && BigInteger offset * BigInteger count <= BigInteger length)
    for a in points do
        for al in points do
            for b in points do
                for bl in points do
                    let expected =
                        mathematicalContains max a al && mathematicalContains max b bl &&
                        (al = 0L || bl = 0L || BigInteger a + BigInteger al <= BigInteger b || BigInteger b + BigInteger bl <= BigInteger a)
                    disjointAgrees <- disjointAgrees && DispatchRegions.disjoint a al b bl = expected
    check "dispatch containment agrees with unbounded arithmetic at signed boundaries" containsAgrees "boundary matrix differs"
    check "dispatch byte product agrees with unbounded arithmetic at signed boundaries" productAgrees "boundary matrix differs"
    check "dispatch disjointness agrees with unbounded arithmetic at signed boundaries" disjointAgrees "boundary matrix differs"
    equal "dispatch empty slice at endpoint" true (DispatchRegions.contains max max 0L)
    equal "dispatch adjacent byte slices" true (DispatchRegions.disjoint (max - 4L) 2L (max - 2L) 2L)
    equal "dispatch 64-bit byte multiplication overflow" None (DispatchRegions.tryByteLength Abi.sysvAmd64 (max / 4L + 1L) 4L)
    equal "dispatch 64-bit exact byte multiplication" (Some (max - 3L)) (DispatchRegions.tryByteLength Abi.sysvAmd64 (max / 4L) 4L)
    equal "dispatch 32-bit byte multiplication overflow" None (DispatchRegions.tryByteLength Abi.wasm32 1073741824L 4L)
    equal "dispatch 32-bit exact byte multiplication" (Some 4294967292L) (DispatchRegions.tryByteLength Abi.wasm32 1073741823L 4L)
    equal "dispatch empty byte product" (Some 0L) (DispatchRegions.tryByteLength Abi.sysvAmd64 0L 4L)
    equal "dispatch zero element width rejected" None (DispatchRegions.tryByteLength Abi.sysvAmd64 1L 0L)
    equal "dispatch invalid ABI rejected by byte conversion" None (DispatchRegions.tryByteLength { Abi.sysvAmd64 with PointerAlign = 0 } 1L 4L)
    let region = fixture ()
    accept "dispatch covers all eleven elements including three-element tail" region
    accept "dispatch unordered partitions preserve coverage" { region with Partitions = Array.rev region.Partitions }
    let verdict = DispatchRegions.validate region
    equal "dispatch evidence retains source reference" region.Source verdict.Source
    equal "dispatch evidence retains worker reference" region.WorkerEntry verdict.WorkerEntry
    check "dispatch evidence records successful checks" (verdict.Checks.Length > 0 && Array.forall (fun c -> c.Holds) verdict.Checks) "missing successful checks"
    let replaceFirst first = { region with Partitions = Array.append [| first |] region.Partitions.[1..] }
    let first = region.Partitions.[0]
    let input = region.Inputs.[0]
    let inputs supplied = { region with Inputs = [| { input with Supplied = supplied } |] }
    accept "dispatch complete input can be supplied in unordered adjacent slices" (inputs [| slice "table" 40L 40L; slice "table" 0L 40L |])
    accept "dispatch required indirect ranges need not include unread gap" (inputs [| slice "table" 0L 32L; slice "table" 64L 16L |])
    reject "dispatch omitted indirect shadow candidate lists" "complete-input" (inputs [| slice "table" 0L 32L |])
    reject "dispatch supplied input with internal hole" "complete-input" (inputs [| slice "table" 0L 68L; slice "table" 72L 8L |])
    reject "dispatch supplied input from unrelated allocation" "complete-input" (inputs [| slice "image" 0L 44L |])
    reject "dispatch supplied input extending beyond backing allocation" "slice-containment" (inputs [| slice "table" 0L 129L |])
    reject "dispatch omitted declared worker read" "read-input-coverage"
        { region with Inputs = [| { input with Required = [| slice "table" 0L 32L |] } |] }
    reject "dispatch input allocation aliases output" "immutable-input"
        (replaceFirst { first with Writes = [| slice "table" 16L 16L |] })
    let mutableTable = { region with Allocations = [| { region.Allocations.[0] with Access = AccessKind.ReadWrite }; region.Allocations.[1] |] }
    reject "dispatch writable alias still cannot mutate immutable supplied input" "immutable-input"
        { mutableTable with Partitions = [| { first with Writes = [| slice "table" 16L 16L |] }; region.Partitions.[1]; region.Partitions.[2] |] }
    reject "dispatch distinct partitions overlap output aliases" "exclusive-output"
        (replaceFirst { first with Writes = [| slice "image" 0L 17L |] })
    reject "dispatch unknown allocation identity" "slice-containment"
        (replaceFirst { first with Writes = [| slice "unbound-native-pointer" 0L 16L |] })
    reject "dispatch read-only backing cannot be written" "slice-access"
        { region with Allocations = [| region.Allocations.[0]; { region.Allocations.[1] with Access = AccessKind.ReadOnly } |] }
    reject "dispatch write-only backing cannot be read" "slice-access"
        { region with Allocations = [| { region.Allocations.[0] with Access = AccessKind.WriteOnly }; region.Allocations.[1] |] }
    reject "dispatch duplicate allocation identity" "allocation-identity"
        { region with Allocations = Array.append region.Allocations [| region.Allocations.[0] |] }
    reject "dispatch missing allocation address-space name" "allocation-declaration"
        { region with Allocations = [| { region.Allocations.[0] with Space = "" }; region.Allocations.[1] |] }
    reject "dispatch wrapped byte slice cannot enter the contract" "slice-containment"
        (replaceFirst { first with Writes = [| slice "image" (max - 4L) 12L |] })
    reject "dispatch negative slice length" "slice-containment"
        (replaceFirst { first with Writes = [| slice "image" 0L -1L |] })
    reject "dispatch output exceeds allocation" "slice-containment"
        (replaceFirst { first with Writes = [| slice "image" 40L 8L |] })
    reject "dispatch dropped tail partition" "partition-coverage" { region with Partitions = region.Partitions.[0..1] }
    reject "dispatch partition gap" "partition-coverage" (replaceFirst { first with Upper = 3L })
    reject "dispatch partition overlap" "partition-disjointness" (replaceFirst { first with Upper = 5L })
    reject "dispatch duplicate partition identity" "partition-identity"
        (replaceFirst { first with Id = region.Partitions.[1].Id })
    reject "dispatch inverted partition bounds" "partition-bounds" (replaceFirst { first with Lower = 4L; Upper = 0L })
    reject "dispatch bounds cannot overflow before rejection" "partition-bounds" (replaceFirst { first with Lower = min; Upper = max })
    reject "dispatch negative domain" "iteration-domain" { region with IterationCount = -1L }
    let emptyPartition : DispatchPartition = { Id = "empty"; Lower = 0L; Upper = 0L; Reads = [||]; Writes = [||] }
    let empty = { region with IterationCount = 0L; Partitions = [||]; Inputs = [||]; Allocations = [||] }
    accept "dispatch zero work with no participants" empty
    accept "dispatch zero work with empty participants" { empty with Partitions = [| emptyPartition |] }
    accept "dispatch empty tail at endpoint" { region with Partitions = Array.append region.Partitions [| { emptyPartition with Lower = 11L; Upper = 11L } |] }
    reject "dispatch empty partition cannot declare writes" "empty-partition-footprint"
        { region with IterationCount = 0L; Partitions = [| { emptyPartition with Writes = [| slice "image" 0L 4L |] } |] }
    reject "dispatch empty domain cannot accept nonempty partition" "partition-bounds" { region with IterationCount = 0L }
    let changedFields = Array.copy region.WorkerCaptureLayout.Fields
    changedFields.[0] <- { changedFields.[0] with Repr = Repr.U64 }
    reject "dispatch equal-sized integer cannot replace captured pointer" "capture-layout-match"
        { region with WorkerCaptureLayout = { region.WorkerCaptureLayout with Fields = changedFields } }
    let renamed = Array.copy region.WorkerCaptureLayout.Fields
    renamed.[0] <- { renamed.[0] with Name = "unrelatedTable" }
    reject "dispatch capture binding identity cannot change at same offset" "capture-layout-match"
        { region with WorkerCaptureLayout = { region.WorkerCaptureLayout with Fields = renamed } }
    reject "dispatch matching but invalid layouts both rejected" "capture-layout"
        { region with CaptureLayout = { region.CaptureLayout with Size = 8 }; WorkerCaptureLayout = { region.WorkerCaptureLayout with Size = 8 } }
    reject "dispatch target capture layout is not host layout" "capture-layout" { region with Target = Abi.i386SysV }
    reject "dispatch 32-bit backing allocation overflow" "allocation-extent"
        { region with Target = Abi.wasm32; Allocations = [| { region.Allocations.[0] with ByteLength = 4294967296L }; region.Allocations.[1] |] }
    let large = { region with Allocations = [| allocation "table" max AccessKind.ReadOnly; region.Allocations.[1] |]
                              Inputs = [| { input with Required = [| slice "table" (max - 8L) 8L |]; Supplied = [| slice "table" (max - 8L) 4L; slice "table" (max - 4L) 4L |] } |]
                              Partitions = Array.map (fun p -> { p with Reads = [| slice "table" (max - 8L) 8L |] }) region.Partitions }
    accept "dispatch exact high int64 input coverage" large
    reject "dispatch high int64 input gap cannot round away" "complete-input"
        { large with Inputs = [| { large.Inputs.[0] with Supplied = [| slice "table" (max - 8L) 3L; slice "table" (max - 4L) 4L |] } |] }
