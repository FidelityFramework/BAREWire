namespace BAREWire.Platform

/// The closed and open vocabularies of the platform description (docs/11).
///
/// Every tag is a string alias with a `[<RequireQualifiedAccess>]` constant
/// module rather than a union: these types cross into Fidelity.Platform, and
/// the cross-assembly union layout path does not import (docs/12 §1,
/// `string-tags`; the idiom is `Fidelity.Platform/Contracts/PlatformContracts.clef`).
/// Tags are compared with `=`. Each module has an `isValid`; an open
/// vocabulary also has an `Unknown` hatch that admits a spelling the library
/// does not name yet. `Check.run` reports any tag `isValid` rejects, so an
/// `Unknown` tag is visible in the findings rather than silently accepted.

/// The kind of a memory space: the DMM enumeration sort (docs/11), extending
/// the hardware region kinds of docs/08 with the process and FPGA spaces.
type MemoryKind = string

[<RequireQualifiedAccess>]
module MemoryKind =
    [<Literal>]
    let Stack: MemoryKind = "stack"
    [<Literal>]
    let Arena: MemoryKind = "arena"
    [<Literal>]
    let Heap: MemoryKind = "heap"
    [<Literal>]
    let Rodata: MemoryKind = "rodata"
    [<Literal>]
    let Text: MemoryKind = "text"
    [<Literal>]
    let Data: MemoryKind = "data"
    [<Literal>]
    let Bss: MemoryKind = "bss"
    [<Literal>]
    let BlockRam: MemoryKind = "block-ram"
    [<Literal>]
    let DistributedRam: MemoryKind = "distributed-ram"
    [<Literal>]
    let Registers: MemoryKind = "registers"
    [<Literal>]
    let ViewBacked: MemoryKind = "view-backed"
    [<Literal>]
    let PersistentStore: MemoryKind = "persistent-store"
    [<Literal>]
    let NeuronState: MemoryKind = "neuron-state"
    [<Literal>]
    let Flash: MemoryKind = "flash"
    [<Literal>]
    let Sram: MemoryKind = "sram"
    [<Literal>]
    let Peripheral: MemoryKind = "peripheral"
    [<Literal>]
    let Dma: MemoryKind = "dma"
    /// A kernel map: fixed-layout key and value records shared between a
    /// verified kernel program and user space (docs/11, "The kernel as a
    /// described platform").
    [<Literal>]
    let Map: MemoryKind = "map"
    /// A ring of fixed slots with producer and consumer indices: a BPF ring
    /// buffer, an AF_XDP UMEM ring, an io_uring submission or completion ring.
    [<Literal>]
    let Ring: MemoryKind = "ring"

    /// A memory kind the library does not name; `Check.run` reports it.
    let Unknown (value: string) : MemoryKind = value

    /// True when the tag is one the library names.
    let isValid (k: MemoryKind) : bool =
        k = Stack || k = Arena || k = Heap || k = Rodata || k = Text || k = Data || k = Bss
        || k = BlockRam || k = DistributedRam || k = Registers || k = ViewBacked
        || k = PersistentStore || k = NeuronState || k = Flash || k = Sram || k = Peripheral || k = Dma
        || k = Map || k = Ring

/// The kind of a kernel map, for a memory space of kind Map.
///
/// `MemoryKind` above deliberately repeats the ARM memory-map kinds of
/// `Hardware.MemoryRegionKind` (flash, sram, peripheral, dma) as members of a
/// wider vocabulary: a platform description names every space a program can
/// occupy, and a peripheral descriptor names the region a register bank is
/// in. The spellings agree so a value moves between the two by name.
type MapKind = string

[<RequireQualifiedAccess>]
module MapKind =
    [<Literal>]
    let Hash: MapKind = "hash"
    [<Literal>]
    let Array: MapKind = "array"
    [<Literal>]
    let PerCpuHash: MapKind = "per-cpu-hash"
    [<Literal>]
    let PerCpuArray: MapKind = "per-cpu-array"
    [<Literal>]
    let LruHash: MapKind = "lru-hash"
    [<Literal>]
    let RingBuffer: MapKind = "ring-buffer"

    /// An open vocabulary: kernels add map kinds.
    let Unknown (value: string) : MapKind = value

    /// True when the tag is one the library names.
    let isValid (k: MapKind) : bool =
        k = Hash || k = Array || k = PerCpuHash || k = PerCpuArray || k = LruHash || k = RingBuffer

/// How a space is consumed: a stack grows down, an arena or heap grows up,
/// a section or register bank has a fixed extent.
type Growth = string

[<RequireQualifiedAccess>]
module Growth =
    [<Literal>]
    let Up: Growth = "up"
    [<Literal>]
    let Down: Growth = "down"
    [<Literal>]
    let Fixed: Growth = "fixed"

    /// True when the tag is one the library names.
    let isValid (g: Growth) : bool =
        g = Up || g = Down || g = Fixed

/// Page or region permissions, spelled as the linker spells them.
type Access = string

[<RequireQualifiedAccess>]
module Access =
    [<Literal>]
    let ReadOnly: Access = "r"
    [<Literal>]
    let ReadWrite: Access = "rw"
    [<Literal>]
    let ReadExecute: Access = "rx"
    [<Literal>]
    let WriteOnly: Access = "w"
    [<Literal>]
    let NoAccess: Access = "-"

    /// True when the tag is one the library names.
    let isValid (a: Access) : bool =
        a = ReadOnly || a = ReadWrite || a = ReadExecute || a = WriteOnly || a = NoAccess

/// How a buffer's content is bounded: a fixed extent, a length prefix, a
/// delimiter byte, or a capacity cap with no in-band marker.
type Framing = string

[<RequireQualifiedAccess>]
module Framing =
    [<Literal>]
    let Fixed: Framing = "fixed"
    [<Literal>]
    let LengthPrefixed: Framing = "length-prefixed"
    [<Literal>]
    let Delimited: Framing = "delimited"
    [<Literal>]
    let Capped: Framing = "capped"
    /// Fixed slots consumed through producer and consumer indices.
    [<Literal>]
    let Ring: Framing = "ring"

    /// True when the tag is one the library names.
    let isValid (f: Framing) : bool =
        f = Fixed || f = LengthPrefixed || f = Delimited || f = Capped || f = Ring

    /// True when the framing carries a delimiter byte.
    let hasDelimiter (f: Framing) : bool =
        f = Delimited

/// Where an endpoint is: a package pin, a syscall number, a global path, a
/// port, or a linked symbol.
type EndpointKind = string

[<RequireQualifiedAccess>]
module EndpointKind =
    [<Literal>]
    let PackagePin: EndpointKind = "package-pin"
    [<Literal>]
    let SyscallNumber: EndpointKind = "syscall-number"
    [<Literal>]
    let GlobalPath: EndpointKind = "global-path"
    [<Literal>]
    let Port: EndpointKind = "port"
    [<Literal>]
    let Symbol: EndpointKind = "symbol"
    /// A numbered host helper (a BPF helper id), the address being the number.
    [<Literal>]
    let HelperNumber: EndpointKind = "helper-number"

    /// An endpoint location the library does not name; `Check.run` reports it.
    let Unknown (value: string) : EndpointKind = value

    /// True when the tag is one the library names.
    let isValid (k: EndpointKind) : bool =
        k = PackagePin || k = SyscallNumber || k = GlobalPath || k = Port || k = Symbol || k = HelperNumber

/// The kind of boundary a surface is: the syscall table, a foreign function
/// interface, a host API, a pin map, or an IPC channel.
type SurfaceKind = string

[<RequireQualifiedAccess>]
module SurfaceKind =
    [<Literal>]
    let Syscall: SurfaceKind = "syscall"
    [<Literal>]
    let Ffi: SurfaceKind = "ffi"
    [<Literal>]
    let HostApi: SurfaceKind = "host-api"
    [<Literal>]
    let Pins: SurfaceKind = "pins"
    [<Literal>]
    let Ipc: SurfaceKind = "ipc"

    /// A surface kind the library does not name; `Check.run` reports it.
    let Unknown (value: string) : SurfaceKind = value

    /// True when the tag is one the library names.
    let isValid (k: SurfaceKind) : bool =
        k = Syscall || k = Ffi || k = HostApi || k = Pins || k = Ipc

/// The medium a transport runs over.
type TransportKind = string

[<RequireQualifiedAccess>]
module TransportKind =
    [<Literal>]
    let Uart: TransportKind = "uart"
    [<Literal>]
    let WebSocket: TransportKind = "websocket"
    [<Literal>]
    let ScriptMessage: TransportKind = "script-message"
    [<Literal>]
    let Wayland: TransportKind = "wayland"
    [<Literal>]
    let Ethernet: TransportKind = "ethernet"
    [<Literal>]
    let Pcie: TransportKind = "pcie"
    [<Literal>]
    let SharedMemory: TransportKind = "shared-memory"
    [<Literal>]
    let Pipe: TransportKind = "pipe"
    [<Literal>]
    let Stream: TransportKind = "stream"

    /// A transport kind the library does not name; `Check.run` reports it.
    let Unknown (value: string) : TransportKind = value

    /// True when the tag is one the library names.
    let isValid (k: TransportKind) : bool =
        k = Uart || k = WebSocket || k = ScriptMessage || k = Wayland || k = Ethernet
        || k = Pcie || k = SharedMemory || k = Pipe || k = Stream

/// How long a buffer lives: the program, a scope (an arena, docs/Arena_Design),
/// an actor, a session, one request, or across runs.
type Lifetime = string

[<RequireQualifiedAccess>]
module Lifetime =
    [<Literal>]
    let Program: Lifetime = "program"
    [<Literal>]
    let Scope: Lifetime = "scope"
    [<Literal>]
    let Actor: Lifetime = "actor"
    [<Literal>]
    let Session: Lifetime = "session"
    [<Literal>]
    let Request: Lifetime = "request"
    [<Literal>]
    let Persistent: Lifetime = "persistent"

    /// True when the tag is one the library names.
    let isValid (l: Lifetime) : bool =
        l = Program || l = Scope || l = Actor || l = Session || l = Request || l = Persistent

/// What happens to state when the platform stops: lost, kept, or kept while
/// the host hibernates the program (the V8 isolate case, Readiness Audit §4 step 12).
type Persistence = string

[<RequireQualifiedAccess>]
module Persistence =
    [<Literal>]
    let Volatile: Persistence = "volatile"
    [<Literal>]
    let Durable: Persistence = "durable"
    [<Literal>]
    let Hibernating: Persistence = "hibernating"

    /// True when the tag is one the library names.
    let isValid (p: Persistence) : bool =
        p = Volatile || p = Durable || p = Hibernating

/// The standing of a contract or obligation: an SMT logic it is decided in,
/// or `Assumed` for a trusted-base fact on the far side of a boundary
/// (docs/11, "The TCB, mechanized").
type Logic = string

[<RequireQualifiedAccess>]
module Logic =
    [<Literal>]
    let QfLia: Logic = "QF_LIA"
    [<Literal>]
    let QfBv: Logic = "QF_BV"
    [<Literal>]
    let Assumed: Logic = "assumed"

    /// A logic the library does not name; `Check.run` reports it.
    let Unknown (value: string) : Logic = value

    /// True when the tag is one the library names.
    let isValid (l: Logic) : bool =
        l = QfLia || l = QfBv || l = Assumed

    /// True when the standing is a trusted-base assumption rather than a proof.
    let isAssumed (l: Logic) : bool =
        l = Assumed
