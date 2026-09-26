namespace BAREWire.Platform

/// One live byte-storage allocation. Length includes any terminator or padding
/// that belongs to this allocation; Alignment is its required byte alignment.
type StorageRequest = {
    Name: string
    Length: int64
    Alignment: int
}

/// An allocation's exact extent relative to the beginning of its emitted pool.
type StoragePlacement = {
    Name: string
    Offset: int64
    Length: int64
    Alignment: int
}

/// The allocation plan consumed by both code generation and proof observers.
/// The pool has AllocationSize bytes, including inter-allocation and tail
/// padding, and its origin must meet Alignment. No absolute address is implied.
type StoragePlan = {
    Space: string
    Placements: StoragePlacement array
    UsedSize: int64
    AllocationSize: int64
    Alignment: int
}

type StorageFinding = {
    Subject: string
    Kind: string
    Message: string
}

/// Static read-only byte pools in a linker-placed memory space. This is a
/// placement authority: an emitter must allocate the returned pool and use its
/// offsets. Applying its theorem to independently placed globals is invalid.
module StaticStorage =

    [<Literal>]
    let private MaxOffset = 9223372036854775807L

    let internal powerOfTwo (value: int) : bool =
        value > 0 && (value &&& (value - 1)) = 0

    // Subtract before adding: both alignment padding and endpoints must remain
    // exact even for declarations at the limit of this implementation's int64.
    let internal alignUp (value: int64) (alignment: int) : int64 option =
        let unit = int64 alignment
        let remainder = value % unit
        let padding = if remainder = 0L then 0L else unit - remainder
        if value > MaxOffset - padding then None else Some (value + padding)

    let private finding (subject: string) (kind: string) (message: string) : StorageFinding array =
        [| { Subject = subject; Kind = kind; Message = message } |]

    /// Plan allocations in request order, preserving every name and extent.
    /// This slice accepts fixed, read-only rodata with no declared base. Other
    /// placement disciplines fail explicitly; none are reinterpreted as rodata.
    /// Space capacity bounds the whole allocation rounded to its granularity.
    let plan (space: MemorySpace) (requests: StorageRequest array) : Result<StoragePlan, StorageFinding array> =
        let hasBase = match space.Base with Some _ -> true | None -> false
        let initial =
            if space.Name = "" then finding space.Name "invalid-space" "a storage space must have a name"
            elif space.Kind <> MemoryKind.Rodata then finding space.Name "unsupported-space" "static byte pools require a rodata space"
            elif space.Growth <> Growth.Fixed then finding space.Name "unsupported-growth" "static byte pools require fixed growth"
            elif space.Access <> Access.ReadOnly then finding space.Name "unsupported-access" "static byte pools require read-only access"
            elif hasBase then finding space.Name "unsupported-base" "this placement discipline requires a linker-assigned base"
            elif space.Capacity <= 0L then finding space.Name "invalid-capacity" "space capacity must be positive"
            elif not (powerOfTwo space.Alignment) then finding space.Name "invalid-alignment" "space alignment must be a positive power of two"
            elif not (powerOfTwo space.Granularity) then finding space.Name "invalid-granularity" "space granularity must be a positive power of two"
            else [||]
        if Array.length initial > 0 then Error initial
        else
            let count = Array.length requests
            let placements : StoragePlacement array = Array.zeroCreate count
            let mutable issues : StorageFinding array = [||]
            let mutable cursor = 0L
            let mutable i = 0
            while i < count && Array.length issues = 0 do
                let request = Array.get requests i
                let mutable duplicate = false
                let mutable previous = 0
                while previous < i && not duplicate do
                    duplicate <- (Array.get requests previous).Name = request.Name
                    previous <- previous + 1
                if request.Name = "" then
                    issues <- finding request.Name "invalid-name" "an allocation must have a name"
                elif duplicate then
                    issues <- finding request.Name "duplicate-name" "allocation names must be unique within a pool"
                elif request.Length <= 0L then
                    issues <- finding request.Name "invalid-length" "an allocation must have a positive byte length"
                elif not (powerOfTwo request.Alignment) then
                    issues <- finding request.Name "invalid-alignment" "allocation alignment must be a positive power of two"
                elif space.Alignment % request.Alignment <> 0 then
                    issues <- finding request.Name "unsupported-alignment" "the space's origin alignment does not guarantee this allocation's alignment"
                else
                    match alignUp cursor request.Alignment with
                    | None -> issues <- finding request.Name "extent-overflow" "alignment exceeds the exact offset representation"
                    | Some offset ->
                        if request.Length > MaxOffset - offset then
                            issues <- finding request.Name "extent-overflow" "allocation endpoint exceeds the exact offset representation"
                        else
                            let endpoint = offset + request.Length
                            if endpoint > space.Capacity then
                                issues <- finding request.Name "capacity-exceeded" "allocation endpoint exceeds the declared space capacity"
                            else
                                Array.set placements i { Name = request.Name; Offset = offset; Length = request.Length; Alignment = request.Alignment }
                                cursor <- endpoint
                i <- i + 1
            if Array.length issues > 0 then Error issues
            else
                match alignUp cursor space.Granularity with
                | None -> Error (finding space.Name "extent-overflow" "allocation granularity exceeds the exact offset representation")
                | Some allocated when allocated > space.Capacity ->
                    Error (finding space.Name "capacity-exceeded" "granularity padding exceeds the declared space capacity")
                | Some allocated ->
                    Ok { Space = space.Name; Placements = placements; UsedSize = cursor; AllocationSize = allocated; Alignment = space.Alignment }

/// A complete source inventory awaiting physical placement. PayloadSize is the
/// sum of object extents only. Neither it nor successful reservation establishes
/// section padding, addresses, or the capacity of the final linked image.
type WritableReservation = {
    Space: MemorySpace
    Requests: StorageRequest array
    PayloadSize: int64
}

/// One actual object in a linked writable region. The backend obtains this
/// record from its artifact, not from the reservation it is checking.
type WritableObject = {
    Name: string
    Address: int64
    Length: int64
}

/// The complete mapped region, including linker/runtime objects and padding.
/// Section identifies the backend's explicit correspondence for the declared
/// space. A .data declaration cannot stand for an observed .bss by permission.
type WritableRegion = {
    Section: string
    Address: int64
    Length: int64
    Allocated: bool
    Access: Access
    Objects: WritableObject array
}

type WritableCommitment = {
    Space: string
    Section: string
    Address: int64
    UsedSize: int64
    AllocationSize: int64
    Objects: WritableObject array
}

/// Writable program storage has two boundaries: source inventory reservation,
/// then exact artifact commitment. Independent globals acquire no pooled
/// offsets from the read-only StaticStorage plan.
module WritableStorage =
    let private fail subject kind message =
        Error [| { Subject = subject; Kind = kind; Message = message } |]

    /// Reject impossible inventories without claiming an eventual placement.
    let reserve (space: MemorySpace) (requests: StorageRequest array) : Result<WritableReservation, StorageFinding array> =
        if space.Name = "" then fail space.Name "invalid-space" "a storage space must have a name"
        elif space.Kind <> MemoryKind.Data && space.Kind <> MemoryKind.Bss && space.Kind <> MemoryKind.Sram then
            fail space.Name "unsupported-space" "writable program storage requires a data, bss or sram space"
        elif space.Growth <> Growth.Fixed then fail space.Name "unsupported-growth" "program storage requires fixed growth"
        elif space.Access <> Access.ReadWrite then fail space.Name "unsupported-access" "program storage requires read-write access"
        elif space.Capacity <= 0L then fail space.Name "invalid-capacity" "space capacity must be positive"
        elif not (StaticStorage.powerOfTwo space.Alignment) then fail space.Name "invalid-alignment" "space alignment must be a positive power of two"
        elif not (StaticStorage.powerOfTwo space.Granularity) then fail space.Name "invalid-granularity" "space granularity must be a positive power of two"
        elif space.Base |> Option.exists (fun address -> address < 0L || address % int64 space.Alignment <> 0L) then
            fail space.Name "invalid-base" "a declared origin must be nonnegative and aligned"
        else
            let mutable total = 0L
            let mutable issues = [||]
            let mutable names: string array = [||]
            for request in requests do
                if Array.isEmpty issues then
                    if request.Name = "" then issues <- [| { Subject = request.Name; Kind = "invalid-name"; Message = "an allocation must have a name" } |]
                    elif Array.contains request.Name names then issues <- [| { Subject = request.Name; Kind = "duplicate-name"; Message = "allocation names must be unique within a space" } |]
                    elif request.Length <= 0L then issues <- [| { Subject = request.Name; Kind = "invalid-length"; Message = "an allocation must have a positive extent" } |]
                    elif not (StaticStorage.powerOfTwo request.Alignment) || space.Alignment % request.Alignment <> 0 then
                        issues <- [| { Subject = request.Name; Kind = "unsupported-alignment"; Message = "the declared space does not guarantee this object's alignment" } |]
                    elif request.Length > space.Capacity - total then
                        issues <- [| { Subject = request.Name; Kind = "capacity-exceeded"; Message = "the complete inventory payload exceeds the space capacity before placement" } |]
                    else
                        names <- Array.append names [| request.Name |]
                        total <- total + request.Length
            if Array.isEmpty issues then Ok { Space = space; Requests = Array.copy requests; PayloadSize = total }
            else Error issues

    /// Commit only an independently observed complete region. expectedSection
    /// is supplied by the selected backend's explicit declared-space mapping.
    /// Additional runtime objects are covered by the whole region extent.
    let commit (reservation: WritableReservation) (expectedSection: string) (region: WritableRegion) : Result<WritableCommitment, StorageFinding array> =
        let space = reservation.Space
        match reserve space reservation.Requests with
        | Error errors -> Error errors
        | Ok current when current.PayloadSize <> reservation.PayloadSize -> fail space.Name "stale-reservation" "the reservation payload no longer agrees with its inventory"
        | Ok _ ->
            if expectedSection = "" || region.Section <> expectedSection then fail space.Name "wrong-section" "the observed region does not match the declared-space section correspondence"
            elif not region.Allocated || region.Access <> Access.ReadWrite then fail space.Name "wrong-permissions" "the observed region must be allocated and read-write"
            elif region.Address < 0L || region.Length < 0L || region.Length > System.Int64.MaxValue - region.Address then fail space.Name "invalid-region" "the region must have an exact nonnegative address and extent"
            elif region.Address % int64 space.Alignment <> 0L then fail space.Name "unaligned-region" "the observed region origin does not meet the declared alignment"
            elif space.Base |> Option.exists ((<>) region.Address) then fail space.Name "wrong-base" "the observed region does not begin at the declared base"
            else
                match StaticStorage.alignUp region.Length space.Granularity with
                | None -> fail space.Name "extent-overflow" "region granularity exceeds the exact extent representation"
                | Some allocated when allocated > space.Capacity -> fail space.Name "capacity-exceeded" "the complete observed region including padding exceeds the declared capacity"
                | Some allocated ->
                    let mutable issues = [||]
                    let mutable selected: WritableObject array = [||]
                    // Every observed object belongs to this complete region,
                    // including runtime objects outside the source inventory.
                    // Malformed extents cannot hide a partial overlap.
                    for actual in region.Objects do
                        if Array.isEmpty issues &&
                           (actual.Length <= 0L || actual.Address < region.Address ||
                            actual.Address > region.Address + region.Length ||
                            actual.Length > region.Address + region.Length - actual.Address) then
                            issues <- [| { Subject = actual.Name; Kind = "outside-region"; Message = "an observed object has no complete extent within its advertised region" } |]
                    for request in reservation.Requests do
                        if Array.isEmpty issues then
                            let found = region.Objects |> Array.filter (fun item -> item.Name = request.Name)
                            let issue kind message = issues <- [| { Subject = request.Name; Kind = kind; Message = message } |]
                            if Array.length found = 1 then
                                let actual = Array.get found 0
                                if actual.Length <> request.Length then issue "wrong-extent" "the emitted object extent differs from its source inventory"
                                elif actual.Address < region.Address || actual.Address > region.Address + region.Length || actual.Length > region.Address + region.Length - actual.Address then
                                    issue "outside-region" "the emitted object is not contained in its declared region"
                                elif actual.Address % int64 request.Alignment <> 0L then issue "unaligned-object" "the emitted object does not meet its source alignment"
                                elif selected |> Array.exists (fun other -> actual.Address < other.Address + other.Length && other.Address < actual.Address + actual.Length) then
                                    issue "overlap" "distinct source allocations overlap in the artifact"
                                elif region.Objects |> Array.exists (fun other ->
                                    other.Name <> actual.Name && other.Length > 0L && other.Address >= region.Address &&
                                    other.Address <= region.Address + region.Length && other.Length <= region.Address + region.Length - other.Address &&
                                    actual.Address < other.Address + other.Length && other.Address < actual.Address + actual.Length) then
                                    issue "overlap" "a source allocation overlaps another artifact object"
                                else selected <- Array.append selected [| actual |]
                            else issue "missing-or-duplicate-object" "each source allocation must identify exactly one emitted object"
                    if not (Array.isEmpty issues) then Error issues
                    else Ok { Space = space.Name; Section = region.Section; Address = region.Address
                              UsedSize = region.Length; AllocationSize = allocated; Objects = selected }
