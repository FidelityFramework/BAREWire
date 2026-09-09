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

    let private powerOfTwo (value: int) : bool =
        value > 0 && (value &&& (value - 1)) = 0

    // Subtract before adding: both alignment padding and endpoints must remain
    // exact even for declarations at the limit of this implementation's int64.
    let private alignUp (value: int64) (alignment: int) : int64 option =
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
