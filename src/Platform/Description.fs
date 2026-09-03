namespace BAREWire.Platform

open BAREWire.Encoding

/// The platform description: the declared authority on memory layout,
/// boundary surfaces, buffer capacities, and transports for one processor
/// (docs/11). A Fidelity.Platform target tree is one `PlatformDescription`
/// value; the compiler extracts the same value structurally from the PSG and
/// runs the observers in `Check`, `Manifest`, and `Obligations` on it. Every
/// table is an array in declaration order, every tag a string alias from
/// `Tags.fs`, and every function here is total.

/// One memory space: a region of the address space, or of a device, with a
/// declared capacity, alignment, growth direction and permissions. `Base` is
/// the declared origin when the space has one (a section, a BRAM, a register
/// bank) and `None` when the loader or allocator assigns it. `Granularity`
/// is the allocation or access unit in bytes (a page for a heap, a word for a
/// register bank, one for a byte-addressed arena).
type MemorySpace = {
    Name: string
    Kind: MemoryKind
    Capacity: int64
    Alignment: int
    Granularity: int
    Growth: Growth
    Access: Access
    Base: int64 option
    Notes: string
    /// For a space of kind Map, the map kind; "" otherwise.
    MapKind: MapKind
    /// Host version from which the space exists; "" when unbounded.
    Since: string
    /// Host version from which the space is gone; "" when unbounded.
    Until: string
}

/// A contract on a boundary: a statement, its standing (`Logic.Assumed` for
/// a trusted-base fact on the far side, or the SMT logic it is decided in),
/// and the weakness ids it guards against (`"CWE-120"`).
type Contract = {
    Name: string
    Statement: string
    Logic: Logic
    Refs: string array
}

/// One endpoint of a surface: a syscall with its number, a pin with its
/// package location, a symbol, a path, a port. `Address` is the location as
/// text: a syscall number, a pin name, a path, a symbol.
type Endpoint = {
    Name: string
    Location: EndpointKind
    Address: string
    Contracts: Contract array
    /// The endpoint's signature where one is declared: a hook's context type
    /// and return convention (`xdp_md -> xdp_action`), a helper's parameter
    /// list, a syscall's argument shape; "" when none.
    Signature: string
    /// Host version from which the endpoint exists (a helper that arrived in
    /// kernel 5.17 is `Since = "5.17"`); "" when unbounded.
    Since: string
    /// Host version from which the endpoint is gone; "" when unbounded.
    Until: string
}

/// A surface-wide default, such as an IO standard for every pin.
type Default = {
    Key: string
    Value: string
}

/// A boundary surface: the syscall table, an FFI, a host API, a pin map, or
/// an IPC channel, with its endpoints, defaults, and surface-wide contracts.
type BoundarySurface = {
    Name: string
    Kind: SurfaceKind
    Endpoints: Endpoint array
    Defaults: Default array
    Contracts: Contract array
}

/// A declared buffer: the BARE type it carries (by name), its capacity in
/// bytes, how its content is framed, which space it lives in, and how long
/// it lives. `Delimiter` is the delimiter byte value for `Framing.Delimited`
/// and `-1` otherwise; `TrimDelimiter` says whether the delimiter is dropped
/// from the value handed on. This is the declared home of facts such as
/// `Console.readln`'s 1024 bytes and newline trim (Readiness Audit §2).
type BufferSchema = {
    Name: string
    Schema: string
    Capacity: int64
    Framing: Framing
    Delimiter: int
    TrimDelimiter: bool
    Space: string
    Lifetime: Lifetime
    Access: Access
    /// For `Ring` framing, the slot size in bytes; 0 otherwise.
    Slot: int
}

/// A transport: the medium, the endpoints it binds (by endpoint name), its
/// declared rate and largest unit (0 when undeclared), whether units arrive
/// in order, and the BARE type of a unit ("" when none).
type Transport = {
    Name: string
    Kind: TransportKind
    Endpoints: string array
    RateHz: int64
    MaxUnit: int64
    Ordered: bool
    Schema: string
    /// Host version from which the transport exists; "" when unbounded.
    Since: string
    /// Host version from which the transport is gone; "" when unbounded.
    Until: string
}

/// A clock and its frequency.
type Clock = {
    Name: string
    FrequencyHz: int64
}

/// A reset line: external or internal, and its active level.
type Reset = {
    Name: string
    External: bool
    ActiveHigh: bool
}

/// How the platform starts, stops, and keeps state: clocks and resets (a
/// device), the entry and teardown symbols (a process), and persistence.
type LifecycleFacts = {
    Clocks: Clock array
    Resets: Reset array
    Entry: string
    Teardown: string
    Persistence: Persistence
}

/// The ISA and ABI identity of a processor core: the `TargetCore` block of
/// `Fidelity.Platform/docs/CANONICAL_PLATFORM_SPEC.md`. `Triple` and
/// `CpuModel` are "" when the toolchain derives them.
type TargetCore = {
    Os: string
    Arch: string
    WordSizeBits: int
    Endianness: string
    Runtime: string
    Triple: string
    CpuModel: string
}

/// One named numeric limit the host imposes.
type Limit = {
    Name: string
    Value: int64
}

/// The whole description of one processor: identity, the core's ISA facts
/// when it has a core, and the declared spaces, surfaces, buffers, transports,
/// lifecycle, and limits the three observers read.
type PlatformDescription = {
    Id: string
    DisplayName: string
    Substrate: string
    Core: TargetCore option
    Spaces: MemorySpace array
    Surfaces: BoundarySurface array
    Buffers: BufferSchema array
    Transports: Transport array
    Lifecycle: LifecycleFacts
    Notes: string array
    /// Declared numeric limits of the host: a verifier's stack bytes and
    /// instruction budget, an ISA level, a message cap. Named so an
    /// obligation can cite them.
    Limits: Limit array
}

/// Lookups over a description. Each is a linear scan in declaration order and
/// returns the first match; `Check.run` reports duplicate names.
module PlatformDescription =

    let private indexOfSpace (spaces: MemorySpace array) (name: string) : int =
        let n = Array.length spaces
        let mutable i = 0
        let mutable found = -1
        while found < 0 && i < n do
            let s = Array.get spaces i
            if s.Name = name then found <- i
            i <- i + 1
        found

    let private indexOfBuffer (buffers: BufferSchema array) (name: string) : int =
        let n = Array.length buffers
        let mutable i = 0
        let mutable found = -1
        while found < 0 && i < n do
            let b = Array.get buffers i
            if b.Name = name then found <- i
            i <- i + 1
        found

    let private indexOfSurface (surfaces: BoundarySurface array) (name: string) : int =
        let n = Array.length surfaces
        let mutable i = 0
        let mutable found = -1
        while found < 0 && i < n do
            let s = Array.get surfaces i
            if s.Name = name then found <- i
            i <- i + 1
        found

    let private indexOfTransport (transports: Transport array) (name: string) : int =
        let n = Array.length transports
        let mutable i = 0
        let mutable found = -1
        while found < 0 && i < n do
            let t = Array.get transports i
            if t.Name = name then found <- i
            i <- i + 1
        found

    let private indexOfEndpoint (endpoints: Endpoint array) (name: string) : int =
        let n = Array.length endpoints
        let mutable i = 0
        let mutable found = -1
        while found < 0 && i < n do
            let e = Array.get endpoints i
            if e.Name = name then found <- i
            i <- i + 1
        found

    /// The memory space with this name.
    let tryFindSpace (desc: PlatformDescription) (name: string) : MemorySpace option =
        let i = indexOfSpace desc.Spaces name
        if i >= 0 then Some (Array.get desc.Spaces i) else None

    /// The buffer schema with this name.
    let tryFindBuffer (desc: PlatformDescription) (name: string) : BufferSchema option =
        let i = indexOfBuffer desc.Buffers name
        if i >= 0 then Some (Array.get desc.Buffers i) else None

    /// The boundary surface with this name.
    let tryFindSurface (desc: PlatformDescription) (name: string) : BoundarySurface option =
        let i = indexOfSurface desc.Surfaces name
        if i >= 0 then Some (Array.get desc.Surfaces i) else None

    /// The transport with this name.
    let tryFindTransport (desc: PlatformDescription) (name: string) : Transport option =
        let i = indexOfTransport desc.Transports name
        if i >= 0 then Some (Array.get desc.Transports i) else None

    /// The endpoint with this name on the named surface.
    let tryFindEndpoint (desc: PlatformDescription) (surfaceName: string) (endpointName: string) : Endpoint option =
        let si = indexOfSurface desc.Surfaces surfaceName
        let endpoints = if si >= 0 then (Array.get desc.Surfaces si).Endpoints else Array.zeroCreate 0
        let ei = indexOfEndpoint endpoints endpointName
        if ei >= 0 then Some (Array.get endpoints ei) else None

    /// True when some surface declares an endpoint with this name; this is
    /// how a transport's endpoint references resolve.
    let hasEndpoint (desc: PlatformDescription) (endpointName: string) : bool =
        let n = Array.length desc.Surfaces
        let mutable i = 0
        let mutable found = false
        while not found && i < n do
            let s = Array.get desc.Surfaces i
            found <- indexOfEndpoint s.Endpoints endpointName >= 0
            i <- i + 1
        found

    /// The memory space a buffer lives in.
    let spaceOfBuffer (desc: PlatformDescription) (buffer: BufferSchema) : MemorySpace option =
        tryFindSpace desc buffer.Space

    /// The declared capacity of the named buffer, or 0 when there is none.
    let capacityOf (desc: PlatformDescription) (bufferName: string) : int64 =
        let i = indexOfBuffer desc.Buffers bufferName
        if i >= 0 then (Array.get desc.Buffers i).Capacity else 0L

    /// The declared capacity of the named space, or 0 when there is none.
    let spaceCapacityOf (desc: PlatformDescription) (spaceName: string) : int64 =
        let i = indexOfSpace desc.Spaces spaceName
        if i >= 0 then (Array.get desc.Spaces i).Capacity else 0L

    let private indexOfLimit (limits: Limit array) (name: string) : int =
        let n = Array.length limits
        let mutable i = 0
        let mutable found = -1
        while found < 0 && i < n do
            let l = Array.get limits i
            if l.Name = name then found <- i
            i <- i + 1
        found

    /// The limit with this name.
    let tryFindLimit (desc: PlatformDescription) (name: string) : Limit option =
        let i = indexOfLimit desc.Limits name
        if i >= 0 then Some (Array.get desc.Limits i) else None

    /// The value of the named limit, or 0 when there is none.
    let limitOf (desc: PlatformDescription) (name: string) : int64 =
        let i = indexOfLimit desc.Limits name
        if i >= 0 then (Array.get desc.Limits i).Value else 0L

/// Host versions as dotted numerals ("5.17", "6.1.0"), and the version-ranged
/// availability every endpoint, space, and transport carries: the capability
/// matrix a project's pinned host floor is filtered against (docs/11, "The
/// kernel as a described platform"). A component is available at a host when
/// `Since` is empty or at most the host, and `Until` is empty or greater
/// than the host.
module Availability =

    /// Compare two dotted version strings numerically, component by
    /// component; a missing component is 0. Negative when `a < b`, zero when
    /// equal, positive when `a > b`. Non-digit characters end a component.
    let compareVersions (a: string) (b: string) : int =
        let la = String.length a
        let lb = String.length b
        let mutable ia = 0
        let mutable ib = 0
        let mutable result = 0
        let mutable fin = false
        while not fin do
            let mutable va = 0
            while ia < la && Text.charAt a ia >= '0' && Text.charAt a ia <= '9' do
                va <- va * 10 + (int (Text.charAt a ia) - 48)
                ia <- ia + 1
            let mutable vb = 0
            while ib < lb && Text.charAt b ib >= '0' && Text.charAt b ib <= '9' do
                vb <- vb * 10 + (int (Text.charAt b ib) - 48)
                ib <- ib + 1
            result <- (if va < vb then -1 elif va > vb then 1 else 0)
            let moreA = ia < la
            let moreB = ib < lb
            // skip one separator on each side
            ia <- (if moreA then ia + 1 else ia)
            ib <- (if moreB then ib + 1 else ib)
            fin <- result <> 0 || (not moreA && not moreB)
        result

    /// True when a component available from `since` until `until` exists on
    /// the host version. Empty bounds are unbounded.
    let admits (since: string) (until: string) (host: string) : bool =
        let afterSince = String.length since = 0 || compareVersions since host <= 0
        let beforeUntil = String.length until = 0 || compareVersions host until < 0
        afterSince && beforeUntil

    /// True when the range is well-formed: `until` empty, or `since` empty, or
    /// `since` before `until`.
    let wellFormed (since: string) (until: string) : bool =
        String.length since = 0 || String.length until = 0 || compareVersions since until < 0

/// Constructors for memory spaces. `create` gives a byte-granular,
/// read-write, fixed-extent space with no declared base; the `with*`
/// functions refine one field at a time.
module MemorySpace =

    /// A space with the given name, kind, capacity and alignment; granularity
    /// 1, fixed extent, read-write, no declared base.
    let create (name: string) (kind: MemoryKind) (capacity: int64) (alignment: int) : MemorySpace =
        { Name = name; Kind = kind; Capacity = capacity; Alignment = alignment; Granularity = 1
          Growth = Growth.Fixed; Access = Access.ReadWrite; Base = None; Notes = ""; MapKind = ""; Since = ""; Until = "" }

    /// A kernel map: a space of kind Map with its map kind, byte-granular,
    /// read-write, no base (the kernel places it).
    let map (name: string) (mapKind: MapKind) (capacity: int64) (alignment: int) : MemorySpace =
        { Name = name; Kind = MemoryKind.Map; Capacity = capacity; Alignment = alignment; Granularity = 1
          Growth = Growth.Fixed; Access = Access.ReadWrite; Base = None; Notes = ""; MapKind = mapKind; Since = ""; Until = "" }

    /// The same space available from `since` until `until` ("" unbounded).
    let withAvailability (space: MemorySpace) (since: string) (until: string) : MemorySpace =
        { space with Since = since; Until = until }

    /// The same space with a declared base address.
    let withBase (space: MemorySpace) (origin: int64) : MemorySpace =
        { space with Base = Some origin }

    /// The same space with a growth direction.
    let withGrowth (space: MemorySpace) (growth: Growth) : MemorySpace =
        { space with Growth = growth }

    /// The same space with permissions.
    let withAccess (space: MemorySpace) (access: Access) : MemorySpace =
        { space with Access = access }

    /// The same space with an allocation or access granularity.
    let withGranularity (space: MemorySpace) (granularity: int) : MemorySpace =
        { space with Granularity = granularity }

    /// The same space with a note.
    let withNotes (space: MemorySpace) (notes: string) : MemorySpace =
        { space with Notes = notes }

    /// The last byte offset plus one of a space with a declared base, or 0.
    let end' (space: MemorySpace) : int64 =
        let b =
            match space.Base with
            | Some v -> v + space.Capacity
            | None -> 0L
        b

/// Constructors for buffer schemas. Each names the framing it declares;
/// buffers default to scope lifetime and read-write access.
module BufferSchema =

    /// A buffer of a fixed extent: the whole capacity is the value. (Spelled
    /// with a prime because `fixed` is a keyword.)
    let fixed' (name: string) (schema: string) (capacity: int64) (space: string) : BufferSchema =
        { Name = name; Schema = schema; Capacity = capacity; Framing = Framing.Fixed; Delimiter = -1
          TrimDelimiter = false; Space = space; Lifetime = Lifetime.Scope; Access = Access.ReadWrite; Slot = 0 }

    /// A buffer whose content carries a length prefix.
    let lengthPrefixed (name: string) (schema: string) (capacity: int64) (space: string) : BufferSchema =
        { Name = name; Schema = schema; Capacity = capacity; Framing = Framing.LengthPrefixed; Delimiter = -1
          TrimDelimiter = false; Space = space; Lifetime = Lifetime.Scope; Access = Access.ReadWrite; Slot = 0 }

    /// A buffer whose content ends at a delimiter byte, dropped when `trim`.
    let delimited (name: string) (schema: string) (capacity: int64) (delimiter: int) (trim: bool) (space: string) : BufferSchema =
        { Name = name; Schema = schema; Capacity = capacity; Framing = Framing.Delimited; Delimiter = delimiter
          TrimDelimiter = trim; Space = space; Lifetime = Lifetime.Scope; Access = Access.ReadWrite; Slot = 0 }

    /// A ring of fixed slots over a space of kind Ring: `capacity` is the ring
    /// size in bytes, `slot` the slot size, consumed through producer and
    /// consumer indices.
    let ring (name: string) (schema: string) (capacity: int64) (slot: int) (space: string) : BufferSchema =
        { Name = name; Schema = schema; Capacity = capacity; Framing = Framing.Ring; Delimiter = -1
          TrimDelimiter = false; Space = space; Lifetime = Lifetime.Session; Access = Access.ReadWrite; Slot = slot }

    /// A buffer bounded only by its capacity, with no in-band marker.
    let capped (name: string) (schema: string) (capacity: int64) (space: string) : BufferSchema =
        { Name = name; Schema = schema; Capacity = capacity; Framing = Framing.Capped; Delimiter = -1
          TrimDelimiter = false; Space = space; Lifetime = Lifetime.Scope; Access = Access.ReadWrite; Slot = 0 }

    /// The same buffer with a lifetime.
    let withLifetime (buffer: BufferSchema) (lifetime: Lifetime) : BufferSchema =
        { buffer with Lifetime = lifetime }

    /// The same buffer with permissions.
    let withAccess (buffer: BufferSchema) (access: Access) : BufferSchema =
        { buffer with Access = access }

    /// True when the buffer's framing carries a delimiter and one is declared.
    let hasDelimiter (buffer: BufferSchema) : bool =
        Framing.hasDelimiter buffer.Framing && buffer.Delimiter >= 0 && buffer.Delimiter <= 255

/// Constructors for contracts.
module Contract =

    /// A trusted-base assumption: the far side of a boundary, stated and
    /// cited but not proven (docs/11, "The TCB, mechanized").
    let assumed (name: string) (statement: string) (refs: string array) : Contract =
        { Name = name; Statement = statement; Logic = Logic.Assumed; Refs = refs }

    /// A contract decided in the given logic.
    let proven (name: string) (statement: string) (logic: Logic) (refs: string array) : Contract =
        { Name = name; Statement = statement; Logic = logic; Refs = refs }

/// Constructors for endpoints and surfaces.
module Endpoint =

    /// An endpoint at a location with its contracts.
    let create (name: string) (location: EndpointKind) (address: string) (contracts: Contract array) : Endpoint =
        { Name = name; Location = location; Address = address; Contracts = contracts; Signature = ""; Since = ""; Until = "" }

    /// A syscall endpoint: the number as text, with its contracts.
    let syscall (name: string) (number: int) (contracts: Contract array) : Endpoint =
        { Name = name; Location = EndpointKind.SyscallNumber; Address = BAREWire.Encoding.Fmt.ofInt number; Contracts = contracts; Signature = ""; Since = ""; Until = "" }

    /// A numbered host helper, available from a host version: the BPF helper
    /// table's shape. The address is the helper number as text.
    let helper (name: string) (number: int) (since: string) (contracts: Contract array) : Endpoint =
        { Name = name; Location = EndpointKind.HelperNumber; Address = BAREWire.Encoding.Fmt.ofInt number; Contracts = contracts; Signature = ""; Since = since; Until = "" }

    /// The same endpoint available from `since` until `until` ("" unbounded).
    let withAvailability (endpoint: Endpoint) (since: string) (until: string) : Endpoint =
        { endpoint with Since = since; Until = until }

    /// The same endpoint with a declared signature.
    let withSignature (endpoint: Endpoint) (signature: string) : Endpoint =
        { endpoint with Signature = signature }

    /// An attach point on a host API surface: a hook is a pin (docs/11). The
    /// signature is the context type it hands the program and the return
    /// convention it expects, as text.
    let hook (name: string) (signature: string) (since: string) : Endpoint =
        { Name = name; Location = EndpointKind.Symbol; Address = name; Contracts = Array.zeroCreate 0; Signature = signature; Since = since; Until = "" }

/// Constructors for boundary surfaces.
module BoundarySurface =

    /// A surface with endpoints and no defaults or surface-wide contracts.
    let create (name: string) (kind: SurfaceKind) (endpoints: Endpoint array) : BoundarySurface =
        { Name = name; Kind = kind; Endpoints = endpoints; Defaults = Array.zeroCreate 0; Contracts = Array.zeroCreate 0 }

    /// The same surface with defaults.
    let withDefaults (surface: BoundarySurface) (defaults: Default array) : BoundarySurface =
        { surface with Defaults = defaults }

    /// The same surface with surface-wide contracts.
    let withContracts (surface: BoundarySurface) (contracts: Contract array) : BoundarySurface =
        { surface with Contracts = contracts }

/// Constructors for transports.
module Transport =

    /// A transport over a medium binding the named endpoints; rate and unit
    /// undeclared, ordered, no schema.
    let create (name: string) (kind: TransportKind) (endpoints: string array) : Transport =
        { Name = name; Kind = kind; Endpoints = endpoints; RateHz = 0L; MaxUnit = 0L; Ordered = true; Schema = ""; Since = ""; Until = "" }

    /// The same transport available from `since` until `until` ("" unbounded).
    let withAvailability (transport: Transport) (since: string) (until: string) : Transport =
        { transport with Since = since; Until = until }

    /// The same transport with a declared rate.
    let withRate (transport: Transport) (rateHz: int64) : Transport =
        { transport with RateHz = rateHz }

    /// The same transport with a largest unit.
    let withMaxUnit (transport: Transport) (maxUnit: int64) : Transport =
        { transport with MaxUnit = maxUnit }

    /// The same transport carrying the named BARE type.
    let withSchema (transport: Transport) (schema: string) : Transport =
        { transport with Schema = schema }

    /// The same transport with its ordering guarantee.
    let withOrdered (transport: Transport) (ordered: bool) : Transport =
        { transport with Ordered = ordered }

/// Constructors for lifecycle facts.
module Lifecycle =

    /// A process lifecycle: entry and teardown symbols, no clocks or resets.
    let process' (entry: string) (teardown: string) (persistence: Persistence) : LifecycleFacts =
        { Clocks = Array.zeroCreate 0; Resets = Array.zeroCreate 0; Entry = entry; Teardown = teardown; Persistence = persistence }

    /// A device lifecycle: clocks and resets, no entry or teardown.
    let device (clocks: Clock array) (resets: Reset array) (persistence: Persistence) : LifecycleFacts =
        { Clocks = clocks; Resets = resets; Entry = ""; Teardown = ""; Persistence = persistence }

    /// A clock.
    let clock (name: string) (frequencyHz: int64) : Clock =
        { Name = name; FrequencyHz = frequencyHz }

    /// A reset line.
    let reset (name: string) (external: bool) (activeHigh: bool) : Reset =
        { Name = name; External = external; ActiveHigh = activeHigh }

/// Constructors for the core identity block. The block follows
/// Fidelity.Platform's `CANONICAL_PLATFORM_SPEC.md` `TargetCore`; its two
/// optional fields (`TripleOverride`, `CpuModel`) are flat strings here, ""
/// when derived by the toolchain, so the record stays a plain table.
module TargetCore =

    /// A core with the toolchain deriving the triple and CPU model.
    let create (os: string) (arch: string) (wordSizeBits: int) (endianness: string) (runtime: string) : TargetCore =
        { Os = os; Arch = arch; WordSizeBits = wordSizeBits; Endianness = endianness; Runtime = runtime; Triple = ""; CpuModel = "" }

    /// The same core with an explicit triple.
    let withTriple (core: TargetCore) (triple: string) : TargetCore =
        { core with Triple = triple }

    /// The same core with an explicit CPU model.
    let withCpuModel (core: TargetCore) (cpuModel: string) : TargetCore =
        { core with CpuModel = cpuModel }

/// Constructors for limits.
module Limit =

    /// A named limit.
    let create (name: string) (value: int64) : Limit =
        { Name = name; Value = value }

    /// The verifier's stack ceiling, named as the obligations cite it.
    [<Literal>]
    let StackBytes = "stack-bytes"

    /// The verifier's instruction budget.
    [<Literal>]
    let InstructionBudget = "instruction-budget"

    /// The instruction-set level (BPF v1..v4).
    [<Literal>]
    let IsaLevel = "isa-level"
