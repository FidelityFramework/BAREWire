namespace BAREWire.Platform

open BAREWire.Encoding

/// The memory-map manifest: the CPU's constraint residual, emitted from the
/// description as XDC is emitted from the FPGA's (docs/11, "declared,
/// directed, then confirmed"; Readiness Audit §4 step 13). One fact per line
/// in declaration order, `key=value` fields, so a line can be grepped and a
/// linker script or an ELF cross-check can be derived from it. The `MEMORY`
/// block at the end is linker-script shaped and lists every space with a
/// declared base.
module Manifest =

    let private kv (key: string) (value: string) : string =
        Text.append (Text.append key "=") value

    let private sp (a: string) (b: string) : string =
        Text.append (Text.append a " ") b

    /// `none` for an empty string, so every field has a value.
    let private orNone (s: string) : string =
        if String.length s = 0 then "none" else s

    let private boolText (b: bool) : string =
        if b then "true" else "false"

    let private refsText (refs: string array) : string =
        if Array.length refs = 0 then "none" else Fmt.join "," refs

    let private baseText (space: MemorySpace) : string =
        let t =
            match space.Base with
            | Some b -> Fmt.hex64 (uint64 b)
            | None -> "dynamic"
        t

    let private delimiterText (buffer: BufferSchema) : string =
        if Framing.hasDelimiter buffer.Framing && buffer.Delimiter >= 0 then Fmt.ofInt buffer.Delimiter else "none"

    /// The `core` line, or "" when the description has no core. The match
    /// lives here so the bound value is only read through field access.
    // SUBSET(option-argument): preferred spelling is `match desc.Core with
    // | Some c -> coreLine c | None -> ""` at the call site; the current
    // Composer snapshot types a `Some`-bound variable passed as a function
    // argument as the payload slot (`int64`), while field access on it types
    // correctly.
    let private coreLine (core: TargetCore option) : string =
        match core with
        | None -> ""
        | Some c ->
            let l1 = sp "core" (kv "os" (orNone c.Os))
            let l2 = sp l1 (kv "arch" (orNone c.Arch))
            let l3 = sp l2 (kv "word" (Fmt.ofInt c.WordSizeBits))
            let l4 = sp l3 (kv "endian" (orNone c.Endianness))
            let l5 = sp l4 (kv "runtime" (orNone c.Runtime))
            let l6 = sp l5 (kv "triple" (orNone c.Triple))
            sp l6 (kv "cpu" (orNone c.CpuModel))

    let private spaceLine (s: MemorySpace) : string =
        let l1 = sp (sp "space" s.Name) (kv "kind" s.Kind)
        let l2 = sp l1 (kv "capacity" (Fmt.ofInt64 s.Capacity))
        let l3 = sp l2 (kv "align" (Fmt.ofInt s.Alignment))
        let l4 = sp l3 (kv "granularity" (Fmt.ofInt s.Granularity))
        let l5 = sp l4 (kv "growth" s.Growth)
        let l6 = sp l5 (kv "access" s.Access)
        let l7 = sp l6 (kv "base" (baseText s))
        let l8 = if s.Kind = MemoryKind.Map then sp l7 (kv "mapkind" (orNone s.MapKind)) else l7
        sp (sp l8 (kv "since" (orNone s.Since))) (kv "until" (orNone s.Until))

    let private bufferLine (b: BufferSchema) : string =
        let l1 = sp (sp "buffer" b.Name) (kv "schema" (orNone b.Schema))
        let l2 = sp l1 (kv "capacity" (Fmt.ofInt64 b.Capacity))
        let l3 = sp l2 (kv "framing" b.Framing)
        let l4 = sp l3 (kv "delimiter" (delimiterText b))
        let l5 = sp l4 (kv "trim" (boolText b.TrimDelimiter))
        let l6 = sp l5 (kv "space" (orNone b.Space))
        let l7 = sp l6 (kv "lifetime" b.Lifetime)
        sp l7 (kv "access" b.Access)

    let private contractLine (indent: string) (c: Contract) : string =
        let l1 = sp (sp (Text.append indent "contract") c.Name) (kv "logic" c.Logic)
        let l2 = sp l1 (kv "refs" (refsText c.Refs))
        sp (sp l2 ":") c.Statement

    let private endpointLine (e: Endpoint) : string =
        let l1 = sp (sp "  endpoint" e.Name) (kv "location" e.Location)
        let l2 = sp l1 (kv "address" (orNone e.Address))
        sp (sp l2 (kv "since" (orNone e.Since))) (kv "until" (orNone e.Until))

    let private transportLine (t: Transport) : string =
        let l1 = sp (sp "transport" t.Name) (kv "kind" t.Kind)
        let l2 = sp l1 (kv "endpoints" (refsText t.Endpoints))
        let l3 = sp l2 (kv "rate" (Fmt.ofInt64 t.RateHz))
        let l4 = sp l3 (kv "maxunit" (Fmt.ofInt64 t.MaxUnit))
        let l5 = sp l4 (kv "ordered" (boolText t.Ordered))
        let l6 = sp l5 (kv "schema" (orNone t.Schema))
        sp (sp l6 (kv "since" (orNone t.Since))) (kv "until" (orNone t.Until))

    let private limitLine (l: Limit) : string =
        sp (sp "limit" l.Name) (kv "value" (Fmt.ofInt64 l.Value))

    let private lifecycleLine (l: LifecycleFacts) : string =
        let l1 = sp "lifecycle" (kv "entry" (orNone l.Entry))
        let l2 = sp l1 (kv "teardown" (orNone l.Teardown))
        sp l2 (kv "persistence" l.Persistence)

    let private clockLine (c: Clock) : string =
        sp (sp "  clock" c.Name) (kv "hz" (Fmt.ofInt64 c.FrequencyHz))

    let private resetLine (r: Reset) : string =
        let l1 = sp (sp "  reset" r.Name) (kv "external" (boolText r.External))
        sp l1 (kv "activehigh" (boolText r.ActiveHigh))

    /// `  <name> (<access>) : ORIGIN = <0x...>, LENGTH = <n>` for a space with a base.
    let private memoryLine (s: MemorySpace) : string =
        let l1 = sp (sp (Text.append "  " s.Name) (Text.append (Text.append "(" s.Access) ")")) ": ORIGIN ="
        let l2 = sp l1 (Text.append (baseText s) ",")
        sp l2 (Text.append "LENGTH = " (Fmt.ofInt64 s.Capacity))

    /// Append one line to the manifest text: a newline separator, then the line.
    let private line (acc: string) (text: string) : string =
        if String.length acc = 0 then text else Text.append (Text.append acc "\n") text

    let private contractLines (acc: string) (indent: string) (contracts: Contract array) : string =
        let n = Array.length contracts
        let mutable out = acc
        let mutable i = 0
        while i < n do
            out <- line out (contractLine indent (Array.get contracts i))
            i <- i + 1
        out

    let private surfaceLines (acc: string) (s: BoundarySurface) : string =
        let mutable out = line acc (sp (sp "surface" s.Name) (kv "kind" s.Kind))
        let nd = Array.length s.Defaults
        let mutable d = 0
        while d < nd do
            let def = Array.get s.Defaults d
            out <- line out (sp "  default" (kv def.Key def.Value))
            d <- d + 1
        let ne = Array.length s.Endpoints
        let mutable i = 0
        while i < ne do
            let e = Array.get s.Endpoints i
            out <- line out (endpointLine e)
            out <- contractLines out "    " e.Contracts
            i <- i + 1
        contractLines out "  " s.Contracts

    /// The manifest text for a description.
    let emit (desc: PlatformDescription) : string =
        let mutable out = Text.append "# BAREWire memory map: " desc.Id
        out <- line out (Text.append (Text.append (Text.append "# " desc.DisplayName) "; substrate ") (orNone desc.Substrate))
        let nn = Array.length desc.Notes
        let mutable q = 0
        while q < nn do
            out <- line out (Text.append "# note " (Array.get desc.Notes q))
            q <- q + 1
        let coreText = coreLine desc.Core
        if String.length coreText > 0 then
            out <- line out coreText
        let ns = Array.length desc.Spaces
        let mutable i = 0
        while i < ns do
            out <- line out (spaceLine (Array.get desc.Spaces i))
            i <- i + 1
        let nb = Array.length desc.Buffers
        let mutable j = 0
        while j < nb do
            out <- line out (bufferLine (Array.get desc.Buffers j))
            j <- j + 1
        let nsf = Array.length desc.Surfaces
        let mutable k = 0
        while k < nsf do
            out <- surfaceLines out (Array.get desc.Surfaces k)
            k <- k + 1
        let nt = Array.length desc.Transports
        let mutable m = 0
        while m < nt do
            out <- line out (transportLine (Array.get desc.Transports m))
            m <- m + 1
        let nlim = Array.length desc.Limits
        let mutable u = 0
        while u < nlim do
            out <- line out (limitLine (Array.get desc.Limits u))
            u <- u + 1
        out <- line out (lifecycleLine desc.Lifecycle)
        let nc = Array.length desc.Lifecycle.Clocks
        let mutable c = 0
        while c < nc do
            out <- line out (clockLine (Array.get desc.Lifecycle.Clocks c))
            c <- c + 1
        let nr = Array.length desc.Lifecycle.Resets
        let mutable r = 0
        while r < nr do
            out <- line out (resetLine (Array.get desc.Lifecycle.Resets r))
            r <- r + 1
        out <- line out "MEMORY"
        out <- line out "{"
        let mutable p = 0
        while p < ns do
            let s = Array.get desc.Spaces p
            let hasBase =
                match s.Base with
                | Some _ -> true
                | None -> false
            if hasBase then
                out <- line out (memoryLine s)
            p <- p + 1
        line out "}"
