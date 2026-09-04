namespace BAREWire.Platform

open BAREWire.Encoding

/// One consistency finding about a description: the declaration it is about,
/// the kind of problem, and a sentence a person can act on.
type Finding = {
    Subject: string
    Kind: string
    Message: string
}

/// The kinds of finding `Check.run` reports.
[<RequireQualifiedAccess>]
module FindingKind =
    [<Literal>]
    let UnknownSpace: string = "unknown-space"
    [<Literal>]
    let CapacityExceedsSpace: string = "capacity-exceeds-space"
    [<Literal>]
    let NonPositiveCapacity: string = "non-positive-capacity"
    [<Literal>]
    let AlignmentNotPowerOfTwo: string = "alignment-not-power-of-two"
    [<Literal>]
    let OverlappingSpaces: string = "overlapping-spaces"
    [<Literal>]
    let MissingDelimiter: string = "missing-delimiter"
    [<Literal>]
    let UnknownEndpoint: string = "unknown-endpoint"
    [<Literal>]
    let DuplicateName: string = "duplicate-name"
    [<Literal>]
    let EmptyName: string = "empty-name"
    [<Literal>]
    let UnknownTag: string = "unknown-tag"
    [<Literal>]
    let InvalidAvailability: string = "invalid-availability"
    [<Literal>]
    let PointerInSharedLayout: string = "pointer-in-shared-layout"
    [<Literal>]
    let NegativeLimit: string = "negative-limit"
    [<Literal>]
    let InvalidSlot: string = "invalid-slot"
    [<Literal>]
    let NegativeRate: string = "negative-rate"
    [<Literal>]
    let NonPositiveFrequency: string = "non-positive-frequency"
    [<Literal>]
    let BaseMisaligned: string = "base-misaligned"

/// The consistency check on a description (docs/11): every tag is one the
/// vocabulary names, every name reference resolves, capacities are positive
/// and fit their spaces, alignments are powers of two, spaces with declared
/// bases do not overlap, and a delimited buffer declares its delimiter. An
/// empty result means the description is consistent. The check is total and
/// deterministic: findings come out in declaration order.
module Check =

    let private push (acc: Finding array) (f: Finding) : Finding array =
        let n = Array.length acc
        let m = n + 1
        let out : Finding array = Array.zeroCreate m
        let mutable i = 0
        while i < n do
            Array.set out i (Array.get acc i)
            i <- i + 1
        Array.set out n f
        out

    let private finding (subject: string) (kind: string) (message: string) : Finding =
        { Subject = subject; Kind = kind; Message = message }

    let private isPowerOfTwo (v: int) : bool =
        v > 0 && (v &&& (v - 1)) = 0

    let private dotted (a: string) (b: string) : string =
        Text.append (Text.append a ".") b

    /// Report an empty name for a declaration of the given role.
    let private checkName (acc: Finding array) (role: string) (name: string) : Finding array =
        if String.length name = 0 then
            push acc (finding role FindingKind.EmptyName (Text.append role " has an empty name"))
        else acc

    /// Report a tag the vocabulary does not name.
    let private checkTag (acc: Finding array) (subject: string) (field: string) (valid: bool) (value: string) : Finding array =
        if valid then acc
        else
            let msg = Text.append (Text.append (Text.append field " '") value) "' is not a tag the vocabulary names"
            push acc (finding subject FindingKind.UnknownTag msg)

    let private checkContract (acc: Finding array) (owner: string) (c: Contract) : Finding array =
        let subject = dotted owner c.Name
        let acc1 = checkName acc (Text.append owner " contract") c.Name
        checkTag acc1 subject "logic" (Logic.isValid c.Logic) c.Logic

    let private checkContracts (acc: Finding array) (owner: string) (contracts: Contract array) : Finding array =
        let n = Array.length contracts
        let mutable out = acc
        let mutable i = 0
        while i < n do
            out <- checkContract out owner (Array.get contracts i)
            i <- i + 1
        out

    /// Report a name that an earlier declaration in the same table already used.
    let private checkDuplicate (acc: Finding array) (role: string) (name: string) (seenBefore: bool) : Finding array =
        if seenBefore then
            push acc (finding name FindingKind.DuplicateName (Text.append (Text.append role " '") (Text.append name "' is declared more than once")))
        else acc

    let private spaceSeenBefore (spaces: MemorySpace array) (upto: int) (name: string) : bool =
        let mutable seen = false
        let mutable j = 0
        while not seen && j < upto do
            seen <- (Array.get spaces j).Name = name
            j <- j + 1
        seen

    let private bufferSeenBefore (buffers: BufferSchema array) (upto: int) (name: string) : bool =
        let mutable seen = false
        let mutable j = 0
        while not seen && j < upto do
            seen <- (Array.get buffers j).Name = name
            j <- j + 1
        seen

    let private surfaceSeenBefore (surfaces: BoundarySurface array) (upto: int) (name: string) : bool =
        let mutable seen = false
        let mutable j = 0
        while not seen && j < upto do
            seen <- (Array.get surfaces j).Name = name
            j <- j + 1
        seen

    let private indexOfEndpointIn (endpoints: Endpoint array) (name: string) : int =
        let n = Array.length endpoints
        let mutable i = 0
        let mutable found = -1
        while found < 0 && i < n do
            if (Array.get endpoints i).Name = name then found <- i
            i <- i + 1
        found

    let private endpointSeenBefore (endpoints: Endpoint array) (upto: int) (name: string) : bool =
        let mutable seen = false
        let mutable j = 0
        while not seen && j < upto do
            seen <- (Array.get endpoints j).Name = name
            j <- j + 1
        seen

    let private transportSeenBefore (transports: Transport array) (upto: int) (name: string) : bool =
        let mutable seen = false
        let mutable j = 0
        while not seen && j < upto do
            seen <- (Array.get transports j).Name = name
            j <- j + 1
        seen

    let private checkSpace (acc: Finding array) (spaces: MemorySpace array) (index: int) : Finding array =
        let s = Array.get spaces index
        let acc1 = checkName acc "space" s.Name
        let acc2 = checkDuplicate acc1 "space" s.Name (spaceSeenBefore spaces index s.Name)
        let acc3 = checkTag acc2 s.Name "kind" (MemoryKind.isValid s.Kind) s.Kind
        let acc4 = checkTag acc3 s.Name "growth" (Growth.isValid s.Growth) s.Growth
        let acc5 = checkTag acc4 s.Name "access" (Access.isValid s.Access) s.Access
        let acc6 =
            if s.Capacity <= 0L then
                push acc5 (finding s.Name FindingKind.NonPositiveCapacity (Text.append "space capacity is not positive: " (Fmt.ofInt64 s.Capacity)))
            else acc5
        let acc7 =
            if isPowerOfTwo s.Alignment then acc6
            else push acc6 (finding s.Name FindingKind.AlignmentNotPowerOfTwo (Text.append "space alignment is not a power of two: " (Fmt.ofInt s.Alignment)))
        let acc8 =
            if isPowerOfTwo s.Granularity then acc7
            else push acc7 (finding s.Name FindingKind.AlignmentNotPowerOfTwo (Text.append "space granularity is not a power of two: " (Fmt.ofInt s.Granularity)))
        let acc9 =
            if s.Kind = MemoryKind.Map then checkTag acc8 s.Name "map kind" (MapKind.isValid s.MapKind) s.MapKind
            elif String.length s.MapKind > 0 then push acc8 (finding s.Name FindingKind.UnknownTag "map kind declared on a space that is not a map")
            else acc8
        let baseValue =
            match s.Base with
            | Some b -> b
            | None -> 0L
        let acc10 =
            if s.Alignment > 0 && baseValue % int64 s.Alignment <> 0L then
                push acc9 (finding s.Name FindingKind.BaseMisaligned (Text.append (Text.append "declared base " (Fmt.hex64 (uint64 baseValue))) (Text.append " is not a multiple of the declared alignment " (Fmt.ofInt s.Alignment))))
            else acc9
        if Availability.wellFormed s.Since s.Until then acc10
        else push acc10 (finding s.Name FindingKind.InvalidAvailability (Text.append (Text.append "available since " s.Since) (Text.append " until " s.Until)))

    /// Two spaces with declared bases overlap when their ranges intersect.
    let private overlaps (a: MemorySpace) (b: MemorySpace) : bool =
        let ab =
            match a.Base with
            | Some v -> v
            | None -> 0L
        let bb =
            match b.Base with
            | Some v -> v
            | None -> 0L
        let aHas =
            match a.Base with
            | Some _ -> true
            | None -> false
        let bHas =
            match b.Base with
            | Some _ -> true
            | None -> false
        aHas && bHas && ab < bb + b.Capacity && bb < ab + a.Capacity

    let private checkOverlaps (acc: Finding array) (spaces: MemorySpace array) : Finding array =
        let n = Array.length spaces
        let mutable out = acc
        let mutable i = 0
        while i < n do
            let a = Array.get spaces i
            let mutable j = i + 1
            while j < n do
                let b = Array.get spaces j
                if overlaps a b then
                    let subject = Text.append (Text.append a.Name "/") b.Name
                    out <- push out (finding subject FindingKind.OverlappingSpaces "spaces with declared bases overlap")
                j <- j + 1
            i <- i + 1
        out

    let private checkBuffer (acc: Finding array) (desc: PlatformDescription) (index: int) : Finding array =
        let b = Array.get desc.Buffers index
        let acc1 = checkName acc "buffer" b.Name
        let acc2 = checkDuplicate acc1 "buffer" b.Name (bufferSeenBefore desc.Buffers index b.Name)
        let acc3 = checkName acc2 (Text.append "buffer schema of " b.Name) b.Schema
        let acc4 = checkTag acc3 b.Name "framing" (Framing.isValid b.Framing) b.Framing
        let acc5 = checkTag acc4 b.Name "lifetime" (Lifetime.isValid b.Lifetime) b.Lifetime
        let acc6 = checkTag acc5 b.Name "access" (Access.isValid b.Access) b.Access
        let acc7 =
            if b.Capacity <= 0L then
                push acc6 (finding b.Name FindingKind.NonPositiveCapacity (Text.append "buffer capacity is not positive: " (Fmt.ofInt64 b.Capacity)))
            else acc6
        let space = PlatformDescription.spaceOfBuffer desc b
        let acc8 =
            match space with
            | None ->
                push acc7 (finding b.Name FindingKind.UnknownSpace (Text.append (Text.append "buffer names space '" b.Space) "', which is not declared"))
            | Some s ->
                if b.Capacity > s.Capacity then
                    let msg = Text.append (Text.append (Text.append "buffer capacity " (Fmt.ofInt64 b.Capacity)) " exceeds the capacity of its space: ") (Fmt.ofInt64 s.Capacity)
                    push acc7 (finding b.Name FindingKind.CapacityExceedsSpace msg)
                else acc7
        let acc9 =
            if Framing.hasDelimiter b.Framing && not (BufferSchema.hasDelimiter b) then
                push acc8 (finding b.Name FindingKind.MissingDelimiter (Text.append "delimited framing declares no delimiter byte (0..255): " (Fmt.ofInt b.Delimiter)))
            else acc8
        let ringOk = b.Framing <> Framing.Ring || (b.Slot > 0 && int64 b.Slot <= b.Capacity && b.Capacity % int64 b.Slot = 0L)
        if ringOk then acc9
        else push acc9 (finding b.Name FindingKind.InvalidSlot (Text.append "ring framing needs a positive slot that divides the capacity: " (Fmt.ofInt b.Slot)))

    let private checkEndpoint (acc: Finding array) (surface: BoundarySurface) (index: int) : Finding array =
        let e = Array.get surface.Endpoints index
        let subject = dotted surface.Name e.Name
        let acc1 = checkName acc (Text.append "endpoint of " surface.Name) e.Name
        let acc2 = checkDuplicate acc1 (Text.append "endpoint of " surface.Name) e.Name (endpointSeenBefore surface.Endpoints index e.Name)
        let acc3 = checkTag acc2 subject "location" (EndpointKind.isValid e.Location) e.Location
        let acc4 =
            if Availability.wellFormed e.Since e.Until then acc3
            else push acc3 (finding subject FindingKind.InvalidAvailability (Text.append (Text.append "available since " e.Since) (Text.append " until " e.Until)))
        checkContracts acc4 subject e.Contracts

    let private checkSurface (acc: Finding array) (surfaces: BoundarySurface array) (index: int) : Finding array =
        let s = Array.get surfaces index
        let acc1 = checkName acc "surface" s.Name
        let acc2 = checkDuplicate acc1 "surface" s.Name (surfaceSeenBefore surfaces index s.Name)
        let acc3 = checkTag acc2 s.Name "kind" (SurfaceKind.isValid s.Kind) s.Kind
        let n = Array.length s.Endpoints
        let mutable out = acc3
        let mutable i = 0
        while i < n do
            out <- checkEndpoint out s i
            i <- i + 1
        checkContracts out s.Name s.Contracts

    /// True when an endpoint with this name is declared on a surface before `upto`.
    let private endpointOnEarlierSurface (surfaces: BoundarySurface array) (upto: int) (name: string) : bool =
        let mutable i = 0
        let mutable seen = false
        while not seen && i < upto do
            seen <- indexOfEndpointIn (Array.get surfaces i).Endpoints name >= 0
            i <- i + 1
        seen

    let private checkEndpointNamesAcrossSurfaces (acc: Finding array) (surfaces: BoundarySurface array) : Finding array =
        let n = Array.length surfaces
        let mutable out = acc
        let mutable i = 1
        while i < n do
            let s = Array.get surfaces i
            let ne = Array.length s.Endpoints
            let mutable j = 0
            while j < ne do
                let e = Array.get s.Endpoints j
                if endpointOnEarlierSurface surfaces i e.Name then
                    out <- push out (finding (dotted s.Name e.Name) FindingKind.DuplicateName "endpoint name is declared on another surface too; transports resolve endpoints by name")
                j <- j + 1
            i <- i + 1
        out

    let private checkTransport (acc: Finding array) (desc: PlatformDescription) (index: int) : Finding array =
        let t = Array.get desc.Transports index
        let acc1 = checkName acc "transport" t.Name
        let acc2 = checkDuplicate acc1 "transport" t.Name (transportSeenBefore desc.Transports index t.Name)
        let acc3 = checkTag acc2 t.Name "kind" (TransportKind.isValid t.Kind) t.Kind
        let n = Array.length t.Endpoints
        let mutable out = acc3
        let mutable i = 0
        while i < n do
            let name = Array.get t.Endpoints i
            if not (PlatformDescription.hasEndpoint desc name) then
                out <- push out (finding t.Name FindingKind.UnknownEndpoint (Text.append (Text.append "transport names endpoint '" name) "', which no surface declares"))
            i <- i + 1
        let out2 =
            if t.RateHz < 0L || t.MaxUnit < 0L then push out (finding t.Name FindingKind.NegativeRate "transport rate and largest unit must be zero (undeclared) or positive")
            else out
        if Availability.wellFormed t.Since t.Until then out2
        else push out2 (finding t.Name FindingKind.InvalidAvailability (Text.append (Text.append "available since " t.Since) (Text.append " until " t.Until)))

    let private limitSeenBefore (limits: Limit array) (upto: int) (name: string) : bool =
        let mutable i = 0
        let mutable seen = false
        while not seen && i < upto do
            seen <- (Array.get limits i).Name = name
            i <- i + 1
        seen

    let private checkLimit (acc: Finding array) (limits: Limit array) (index: int) : Finding array =
        let l = Array.get limits index
        let acc1 = checkName acc "limit" l.Name
        let acc2 = checkDuplicate acc1 "limit" l.Name (limitSeenBefore limits index l.Name)
        if l.Value < 0L then push acc2 (finding l.Name FindingKind.NegativeLimit (Text.append "limit is negative: " (Fmt.ofInt64 l.Value)))
        else acc2

    let private checkLifecycle (acc: Finding array) (l: LifecycleFacts) : Finding array =
        let acc1 = checkTag acc "lifecycle" "persistence" (Persistence.isValid l.Persistence) l.Persistence
        let nc = Array.length l.Clocks
        let mutable out = acc1
        let mutable i = 0
        while i < nc do
            let c = Array.get l.Clocks i
            out <- checkName out "clock" c.Name
            if c.FrequencyHz <= 0L then
                out <- push out (finding c.Name FindingKind.NonPositiveFrequency (Text.append "clock frequency is not positive: " (Fmt.ofInt64 c.FrequencyHz)))
            i <- i + 1
        let nr = Array.length l.Resets
        let mutable j = 0
        while j < nr do
            let r = Array.get l.Resets j
            out <- checkName out "reset" r.Name
            j <- j + 1
        out

    let private checkCore (acc: Finding array) (c: TargetCore) : Finding array =
        let acc1 = checkName acc "core os" c.Os
        let acc2 = checkName acc1 "core arch" c.Arch
        let endianOk = c.Endianness = "little" || c.Endianness = "big" || String.length c.Endianness = 0
        let acc3 = checkTag acc2 "core" "endianness" endianOk c.Endianness
        if isPowerOfTwo c.WordSizeBits then acc3
        else push acc3 (finding "core" FindingKind.AlignmentNotPowerOfTwo (Text.append "word size in bits is not a power of two: " (Fmt.ofInt c.WordSizeBits)))

    /// Every finding about the description, in declaration order; empty when
    /// the description is consistent.
    let run (desc: PlatformDescription) : Finding array =
        let start : Finding array = Array.zeroCreate 0
        let acc0 = checkName start "description" desc.Id
        let acc1 =
            match desc.Core with
            | Some c -> checkCore acc0 c
            | None -> acc0
        let ns = Array.length desc.Spaces
        let mutable acc = acc1
        let mutable i = 0
        while i < ns do
            acc <- checkSpace acc desc.Spaces i
            i <- i + 1
        acc <- checkOverlaps acc desc.Spaces
        let nb = Array.length desc.Buffers
        let mutable j = 0
        while j < nb do
            acc <- checkBuffer acc desc j
            j <- j + 1
        let nsf = Array.length desc.Surfaces
        let mutable k = 0
        while k < nsf do
            acc <- checkSurface acc desc.Surfaces k
            k <- k + 1
        acc <- checkEndpointNamesAcrossSurfaces acc desc.Surfaces
        let nt = Array.length desc.Transports
        let mutable m = 0
        while m < nt do
            acc <- checkTransport acc desc m
            m <- m + 1
        let nl = Array.length desc.Limits
        let mutable q = 0
        while q < nl do
            acc <- checkLimit acc desc.Limits q
            q <- q + 1
        checkLifecycle acc desc.Lifecycle

    /// True when a buffer's space is one the far side reads directly: a
    /// kernel map, a ring, or a view over a host buffer.
    let private sharedSpace (desc: PlatformDescription) (b: BufferSchema) : bool =
        let s = PlatformDescription.spaceOfBuffer desc b
        let kind =
            match s with
            | Some sp -> sp.Kind
            | None -> ""
        kind = MemoryKind.Map || kind = MemoryKind.Ring || kind = MemoryKind.ViewBacked

    /// `run`, plus the pointer-free rule for every buffer on a shared space
    /// whose schema resolves to a layout: a layout with a Pointer
    /// representation is a `PointerInSharedLayout` finding (docs/11, "Map
    /// values are pointer-free by construction"). `layoutOf` maps a schema
    /// name to its descriptor layout, or None when the schema is not a
    /// descriptor.
    let runWithLayouts (desc: PlatformDescription) (layoutOf: string -> BAREWire.Hardware.PeripheralLayout option) : Finding array =
        let mutable acc = run desc
        let nb = Array.length desc.Buffers
        let mutable i = 0
        while i < nb do
            let b = Array.get desc.Buffers i
            let layout = layoutOf b.Schema
            let pointerFree =
                match layout with
                | Some l -> BAREWire.Hardware.Layout.isPointerFree l
                | None -> true
            if sharedSpace desc b && not pointerFree then
                acc <- push acc (finding b.Name FindingKind.PointerInSharedLayout (Text.append (Text.append "buffer on shared space " b.Space) " has a layout with a pointer field"))
            i <- i + 1
        acc

    /// One line per finding: `<kind> <subject>: <message>`; "no findings"
    /// when there are none.
    let explain (findings: Finding array) : string =
        let n = Array.length findings
        if n = 0 then "no findings"
        else
            let lines : string array = Array.zeroCreate n
            let mutable i = 0
            while i < n do
                let f = Array.get findings i
                let line = Text.append (Text.append (Text.append (Text.append f.Kind " ") f.Subject) ": ") f.Message
                Array.set lines i line
                i <- i + 1
            Fmt.join "\n" lines
