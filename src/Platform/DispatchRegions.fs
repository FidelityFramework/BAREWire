namespace BAREWire.Platform

open BAREWire.Hardware

/// A half-open byte extent relative to a canonical allocation, never an address.
/// All aliases of the same backing storage must use the same Allocation identity.
type ByteSlice = {
    Allocation: string
    Offset: int64
    Length: int64
}

/// One allocation instance supplied by the platform boundary. Space names the
/// platform memory space; Lifetime is a declaration, not evidence of retirement.
type DispatchAllocation = {
    Id: string
    Space: string
    ByteLength: int64
    Access: AccessKind
    Lifetime: Lifetime
}

/// Required includes indirect reads as well as the directly indexed prefix.
/// Supplied may describe several adjacent uploads/views of the same allocation.
/// These inputs are immutable throughout the dispatch, including through aliases.
type DispatchInput = {
    Id: string
    Required: ByteSlice array
    Supplied: ByteSlice array
}

/// A partition of [0, IterationCount) and its complete byte footprints. The
/// producer must establish that these footprints describe the actual operations.
type DispatchPartition = {
    Id: string
    Lower: int64
    Upper: int64
    Reads: ByteSlice array
    Writes: ByteSlice array
}

/// Graph/host description, separate from the allocation-free scalar guards.
/// WorkerEntry and Source are evidence references; they are not symbol lookup or
/// reachability authority. Layouts come from settlement and the emitted entry.
type DispatchRegion = {
    Id: string
    Source: string
    WorkerEntry: string
    Target: AbiProfile
    IterationCount: int64
    Allocations: DispatchAllocation array
    Inputs: DispatchInput array
    Partitions: DispatchPartition array
    CaptureLayout: PeripheralLayout
    WorkerCaptureLayout: PeripheralLayout
}

/// A located, concrete spatial check for a graph/evidence consumer. Holds is a
/// check over the supplied description, not a solver or implementation verdict.
type DispatchCheck = {
    Kind: string
    Subject: string
    Related: string array
    Holds: bool
    Message: string
}

type DispatchVerdict = {
    Region: string
    Source: string
    WorkerEntry: string
    SpatiallyValid: bool
    Checks: DispatchCheck array
    Findings: DispatchCheck array
}

/// Pure spatial checks. Publication, immutable input ownership, capture lifetime,
/// completion and retirement remain obligations of the compiler and scheduling layer.
module DispatchRegions =

    [<Literal>]
    let private MaxExtent = 9223372036854775807L

    /// Subtract before adding so even a malicious offset/length cannot wrap.
    /// A zero-length slice at the allocation endpoint is valid.
    let contains (byteLength: int64) (offset: int64) (length: int64) : bool =
        byteLength >= 0L && offset >= 0L && length >= 0L
        && offset <= byteLength && length <= byteLength - offset

    /// Same-allocation half-open extents are disjoint. Malformed or unrepresentable
    /// extents fail; empty extents do not overlap any valid extent.
    let disjoint (firstOffset: int64) (firstLength: int64) (secondOffset: int64) (secondLength: int64) : bool =
        if not (contains MaxExtent firstOffset firstLength) || not (contains MaxExtent secondOffset secondLength) then false
        elif firstLength = 0L || secondLength = 0L then true
        elif firstOffset <= secondOffset then firstLength <= secondOffset - firstOffset
        else secondLength <= firstOffset - secondOffset

    /// Guard a count-to-byte multiplication without evaluating the product.
    /// maxByteLength is a settled target/allocation limit, not the host word size.
    let fitsByteLength (maxByteLength: int64) (count: int64) (elementSize: int64) : bool =
        maxByteLength >= 0L && count >= 0L && elementSize > 0L
        && count <= maxByteLength / elementSize

    // This implementation represents nonnegative extents with signed int64.
    // The 64-bit target's upper unsigned half is explicitly unsupported; 32-bit
    // targets are additionally bounded by their unsigned address representation.
    let private targetLimit (target: AbiProfile) : int64 =
        if not (Abi.isValid target) then 0L
        elif target.PointerSize = 4 then 4294967295L
        elif target.PointerSize = 8 then MaxExtent
        else 0L

    /// The exact byte product, only when valid for this target and implementation.
    let tryByteLength (target: AbiProfile) (count: int64) (elementSize: int64) : int64 option =
        let limit = targetLimit target
        if limit > 0L && fitsByteLength limit count elementSize then Some (count * elementSize)
        else None

    let private push (items: DispatchCheck array) (item: DispatchCheck) : DispatchCheck array =
        let n = Array.length items
        let result : DispatchCheck array = Array.zeroCreate (n + 1)
        let mutable i = 0
        while i < n do
            Array.set result i (Array.get items i)
            i <- i + 1
        Array.set result n item
        result

    let private check kind subject related holds message : DispatchCheck =
        { Kind = kind; Subject = subject; Related = related; Holds = holds; Message = message }

    let private allocationIndex (allocations: DispatchAllocation array) (id: string) : int =
        let mutable found = -1
        let mutable i = 0
        while i < Array.length allocations do
            if (Array.get allocations i).Id = id then found <- i
            i <- i + 1
        found

    let private validSlice (limit: int64) (allocations: DispatchAllocation array) (slice: ByteSlice) : bool =
        let index = allocationIndex allocations slice.Allocation
        if index < 0 then false
        else
            let allocation = Array.get allocations index
            allocation.ByteLength <= limit && contains allocation.ByteLength slice.Offset slice.Length

    let private permitted (allocations: DispatchAllocation array) (write: bool) (slice: ByteSlice) : bool =
        let index = allocationIndex allocations slice.Allocation
        if index < 0 then false
        else
            let access = (Array.get allocations index).Access
            if write then access = AccessKind.WriteOnly || access = AccessKind.ReadWrite
            else access = AccessKind.ReadOnly || access = AccessKind.ReadWrite

    /// Union coverage, permitting unordered and adjacent supplies without treating
    /// a hole as initialized input. Only already bounded slices reach this check.
    let private covered (required: ByteSlice) (supplied: ByteSlice array) : bool =
        let endpoint = required.Offset + required.Length
        let mutable cursor = required.Offset
        let mutable advancing = true
        while cursor < endpoint && advancing do
            let mutable next = cursor
            let mutable i = 0
            while i < Array.length supplied do
                let slice = Array.get supplied i
                if slice.Allocation = required.Allocation && slice.Offset <= cursor then
                    let finish = slice.Offset + slice.Length
                    if finish > next then next <- finish
                i <- i + 1
            advancing <- next > cursor
            cursor <- next
        cursor >= endpoint

    let private separates (first: ByteSlice) (second: ByteSlice) : bool =
        first.Allocation <> second.Allocation || disjoint first.Offset first.Length second.Offset second.Length

    let private sameLayout (first: PeripheralLayout) (second: PeripheralLayout) : bool =
        let mutable agrees = first.Size = second.Size && first.Alignment = second.Alignment
        let count = Array.length first.Fields
        agrees <- agrees && count = Array.length second.Fields
        let mutable i = 0
        while i < count && agrees do
            let a = Array.get first.Fields i
            let b = Array.get second.Fields i
            agrees <- a.Name = b.Name && a.Offset = b.Offset && a.Repr = b.Repr
                      && a.Count = b.Count && a.Access = b.Access
                      && Array.length a.BitFields = Array.length b.BitFields
            let mutable j = 0
            while j < Array.length a.BitFields && agrees do
                let x = Array.get a.BitFields j
                let y = Array.get b.BitFields j
                agrees <- x.Name = y.Name && x.Position = y.Position && x.Width = y.Width && x.Access = y.Access
                j <- j + 1
            i <- i + 1
        agrees

    /// Validate concrete supplied metadata. Empty work may have no partitions or
    /// empty partitions. Every nonempty domain element must be assigned once.
    /// All reads must be covered by a declared immutable input requirement.
    let validate (region: DispatchRegion) : DispatchVerdict =
        let limit = targetLimit region.Target
        let allocations = region.Allocations
        let mutable checks : DispatchCheck array = [||]
        let add kind subject related holds message =
            checks <- push checks (check kind subject related holds message)
        add "region-identity" region.Id [| region.WorkerEntry |]
            (region.Id <> "" && region.Source <> "" && region.WorkerEntry <> "") "region, source and worker entry must be identified"
        add "target-representation" region.Id [| region.Target.Name |] (limit > 0L) "target must have a valid supported pointer representation"
        let validDomain = region.IterationCount >= 0L && region.IterationCount <= limit
        add "iteration-domain" region.Id [||] validDomain "iteration count must fit the supported target representation"
        let mutable i = 0
        while i < Array.length allocations do
            let allocation = Array.get allocations i
            let mutable unique = allocation.Id <> ""
            let mutable j = 0
            while j < i do
                if (Array.get allocations j).Id = allocation.Id then unique <- false
                j <- j + 1
            add "allocation-identity" allocation.Id [| allocation.Space |] unique "canonical allocation identities must be nonempty and unique"
            add "allocation-extent" allocation.Id [| allocation.Space |]
                (allocation.ByteLength >= 0L && allocation.ByteLength <= limit) "allocation byte extent must fit the supported target representation"
            add "allocation-declaration" allocation.Id [| allocation.Space |]
                (allocation.Space <> "" && AccessKind.isValid allocation.Access && Lifetime.isValid allocation.Lifetime)
                "allocation must name its space, access rights and lifetime category"
            i <- i + 1
        let validateSlices subject write (slices: ByteSlice array) =
            let mutable valid = true
            let mutable j = 0
            while j < Array.length slices do
                let slice = Array.get slices j
                let bounded = validSlice limit allocations slice
                add "slice-containment" subject [| slice.Allocation |] bounded "slice must fit its identified backing allocation without byte overflow"
                add "slice-access" subject [| slice.Allocation |] (permitted allocations write slice) "slice access must be permitted by its backing allocation"
                valid <- valid && bounded
                j <- j + 1
            valid
        let mutable required : ByteSlice array = [||]
        let mutable immutable : ByteSlice array = [||]
        let appendSlices (first: ByteSlice array) (second: ByteSlice array) : ByteSlice array =
            let n = Array.length first
            let result : ByteSlice array = Array.zeroCreate (n + Array.length second)
            let mutable j = 0
            while j < Array.length result do
                Array.set result j (if j < n then Array.get first j else Array.get second (j - n))
                j <- j + 1
            result
        let mutable allInputsBounded = true
        i <- 0
        while i < Array.length region.Inputs do
            let input = Array.get region.Inputs i
            let mutable unique = input.Id <> ""
            let mutable j = 0
            while j < i do
                if (Array.get region.Inputs j).Id = input.Id then unique <- false
                j <- j + 1
            add "input-identity" input.Id [||] unique "input identities must be nonempty and unique"
            let requiredBounded = validateSlices input.Id false input.Required
            let suppliedBounded = validateSlices input.Id false input.Supplied
            allInputsBounded <- allInputsBounded && requiredBounded && suppliedBounded
            j <- 0
            while j < Array.length input.Required do
                let slice = Array.get input.Required j
                let complete = requiredBounded && suppliedBounded && covered slice input.Supplied
                add "complete-input" input.Id [| slice.Allocation |] complete "supplied input must cover every required byte, including indirect reads"
                j <- j + 1
            required <- appendSlices required input.Required
            immutable <- appendSlices immutable input.Supplied
            i <- i + 1
        let partitions = region.Partitions
        let mutable coveredCount = 0L
        let mutable exactCoverage = validDomain
        i <- 0
        while i < Array.length partitions do
            let partition = Array.get partitions i
            let bounded = validDomain && partition.Lower >= 0L
                          && partition.Upper >= partition.Lower && partition.Upper <= region.IterationCount
            let mutable unique = partition.Id <> ""
            let mutable j = 0
            while j < i do
                if (Array.get partitions j).Id = partition.Id then unique <- false
                j <- j + 1
            add "partition-identity" partition.Id [||] unique "partition identities must be nonempty and unique"
            add "partition-bounds" partition.Id [| region.Id |] bounded "partition must be a half-open subrange of the iteration domain"
            if bounded then
                let length = partition.Upper - partition.Lower
                if coveredCount <= region.IterationCount && length <= region.IterationCount - coveredCount then
                    coveredCount <- coveredCount + length
                else exactCoverage <- false
            else exactCoverage <- false
            let readsBounded = validateSlices partition.Id false partition.Reads
            let writesBounded = validateSlices partition.Id true partition.Writes
            if bounded && partition.Lower = partition.Upper then
                let footprints = appendSlices partition.Reads partition.Writes
                let mutable empty = true
                j <- 0
                while j < Array.length footprints do
                    if (Array.get footprints j).Length <> 0L then empty <- false
                    j <- j + 1
                add "empty-partition-footprint" partition.Id [||] empty "an empty partition must have no nonempty byte footprint"
            j <- 0
            while j < Array.length partition.Reads do
                let slice = Array.get partition.Reads j
                add "read-input-coverage" partition.Id [| slice.Allocation |]
                    (allInputsBounded && readsBounded && covered slice required) "each worker read must belong to the complete declared input footprint"
                j <- j + 1
            j <- 0
            while j < Array.length partition.Writes do
                let write = Array.get partition.Writes j
                let mutable k = 0
                while k < Array.length immutable do
                    let input = Array.get immutable k
                    add "immutable-input" partition.Id [| write.Allocation; input.Allocation |]
                        (writesBounded && allInputsBounded && separates write input) "writes must not overlap supplied immutable input through any alias"
                    k <- k + 1
                j <- j + 1
            j <- 0
            while j < i do
                let other = Array.get partitions j
                let apart = bounded && other.Lower >= 0L && other.Upper >= other.Lower
                            && other.Upper <= region.IterationCount
                            && disjoint partition.Lower (partition.Upper - partition.Lower) other.Lower (other.Upper - other.Lower)
                add "partition-disjointness" partition.Id [| other.Id |] apart "partitions must assign each iteration at most once"
                exactCoverage <- exactCoverage && apart
                let mutable a = 0
                while a < Array.length partition.Writes do
                    let write = Array.get partition.Writes a
                    let mutable b = 0
                    while b < Array.length other.Writes do
                        let otherWrite = Array.get other.Writes b
                        add "exclusive-output" partition.Id [| other.Id; write.Allocation; otherWrite.Allocation |]
                            (separates write otherWrite) "writes from distinct partitions must not overlap through aliases"
                        b <- b + 1
                    a <- a + 1
                j <- j + 1
            i <- i + 1
        add "partition-coverage" region.Id [||] (exactCoverage && coveredCount = region.IterationCount) "partitions must cover the whole domain exactly once, including the tail"
        let capture = StructLayout.create region.Id region.CaptureLayout None
        let workerCapture = StructLayout.create region.WorkerEntry region.WorkerCaptureLayout None
        add "capture-layout" region.Id [| region.Target.Name |] (Validator.validate region.Target capture).Agrees "captured environment must agree with the target ABI"
        add "worker-capture-layout" region.WorkerEntry [| region.Target.Name |] (Validator.validate region.Target workerCapture).Agrees "worker environment must agree with the target ABI"
        add "capture-layout-match" region.Id [| region.WorkerEntry |] (sameLayout region.CaptureLayout region.WorkerCaptureLayout) "captured environment must exactly match the settled worker field representations and offsets"
        let mutable findings : DispatchCheck array = [||]
        i <- 0
        while i < Array.length checks do
            let item = Array.get checks i
            if not item.Holds then findings <- push findings item
            i <- i + 1
        { Region = region.Id; Source = region.Source; WorkerEntry = region.WorkerEntry
          SpatiallyValid = Array.length findings = 0; Checks = checks; Findings = findings }
