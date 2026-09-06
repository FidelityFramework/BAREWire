namespace BAREWire.Platform

open BAREWire.Encoding

/// The decidable shape of an obligation. `Kind` names the form; `A` and `B`
/// are its constants; `Names` and `Values` carry the ranges of a `Disjoint`
/// form as (base, length) pairs in `Values`, one name per pair in `Names`.
type ObligationForm = {
    Kind: string
    A: int64
    B: int64
    Names: string array
    Values: int64 array
}

/// The forms an obligation can take.
[<RequireQualifiedAccess>]
module FormKind =
    /// A <= B.
    [<Literal>]
    let Leq: string = "leq"
    /// A > 0.
    [<Literal>]
    let Positive: string = "positive"
    /// A is a power of two (and not zero), decided over 64-bit vectors.
    [<Literal>]
    let PowerOfTwo: string = "power-of-two"
    /// For all r, 1 <= r <= A implies r - 1 <= B.
    [<Literal>]
    let RangeMinusOneLeq: string = "range-minus-one-leq"
    /// The (base, length) ranges in `Values` are pairwise disjoint.
    [<Literal>]
    let Disjoint: string = "disjoint"

/// One proof obligation stated against a declaration: its id, its family,
/// the logic it is decided in, the sentence it asserts, the declaration it
/// cites (`<description id>:<declaration name>`), the weakness ids it guards
/// against, and its decidable form.
type Obligation = {
    Id: string
    Kind: string
    Logic: Logic
    Statement: string
    Source: string
    Refs: string array
    Form: ObligationForm
}

/// The obligation families this observer states.
[<RequireQualifiedAccess>]
module ObligationKind =
    [<Literal>]
    let BufferCapacity: string = "buffer-capacity"
    [<Literal>]
    let InputBufferBound: string = "input-buffer-bound"
    [<Literal>]
    let InputCopyBound: string = "input-copy-bound"
    [<Literal>]
    let SpaceAlignment: string = "space-alignment"
    [<Literal>]
    let MemoryMapDisjointness: string = "memory-map-disjointness"
    /// A buffer carried by a transport fits the transport's largest unit.
    [<Literal>]
    let TransportUnit: string = "transport-unit"
    /// A space of kind Stack fits the host's declared stack-bytes limit.
    [<Literal>]
    let SpaceLimit: string = "space-limit"

/// The third observer of the description (docs/11, "Three observers, one
/// truth"): proof obligations stated against the declared facts, so a
/// buffer's capacity or a space's base is cited by name rather than restated
/// as a constant in witness code. `ofDescription` is total and deterministic;
/// `smtLib` renders the refutation form HelloProof checks (`unsat` means the
/// obligation holds); `ledgerLine` is the one-line record for the ledger.
/// This observer and its external ledger are verification scaffolding for the
/// proof-carrying PSG's joint constraint mechanism, not its canonical proof
/// state. Retain the scaffold until cross-layer agreement demonstrates that
/// the graph carries these obligations and their evidence without loss.
module Obligations =

    let private push (acc: Obligation array) (o: Obligation) : Obligation array =
        let n = Array.length acc
        let m = n + 1
        let out : Obligation array = Array.zeroCreate m
        let mutable i = 0
        while i < n do
            Array.set out i (Array.get acc i)
            i <- i + 1
        Array.set out n o
        out

    /// The slug of a name: lower case, every run of characters outside
    /// `a-z0-9` collapsed to one underscore.
    let slug (s: string) : string =
        let bytes = Text.toUtf8 s
        let n = Array.length bytes
        let out : byte array = Array.zeroCreate n
        let mutable o = 0
        let mutable i = 0
        let mutable lastUnderscore = false
        while i < n do
            let b = Array.get bytes i
            let isLower = b >= 97uy && b <= 122uy
            let isUpper = b >= 65uy && b <= 90uy
            let isDigit = b >= 48uy && b <= 57uy
            let keep = isLower || isUpper || isDigit
            let lowered = byte (int b + 32)
            let c = if isUpper then lowered else b
            let underscore = (not keep) && (not lastUnderscore)
            if keep then
                Array.set out o c
                o <- o + 1
            if underscore then
                Array.set out o 95uy
                o <- o + 1
            lastUnderscore <- not keep
            i <- i + 1
        Text.ofUtf8 (Array.sub out 0 o)

    let private source (desc: PlatformDescription) (declaration: string) : string =
        Text.append (Text.append desc.Id ":") declaration

    let private scalar (kind: string) (a: int64) (b: int64) : ObligationForm =
        { Kind = kind; A = a; B = b; Names = Array.zeroCreate 0; Values = Array.zeroCreate 0 }

    let private bufferObligations (acc: Obligation array) (desc: PlatformDescription) (b: BufferSchema) : Obligation array =
        let s = slug b.Name
        let cap = Fmt.ofInt64 b.Capacity
        let space = PlatformDescription.spaceOfBuffer desc b
        let spaceCapacity =
            match space with
            | Some sp -> sp.Capacity
            | None -> 0L
        let spaceText =
            match space with
            | Some sp -> Text.append (Text.append (Text.append "the capacity " (Fmt.ofInt64 sp.Capacity)) " of its space ") sp.Name
            | None -> Text.append (Text.append "the capacity of its space " b.Space) ", which is not declared (taken as 0)"
        let refsCapacity = [| "CWE-131"; "CWE-120" |]
        let positive =
            { Id = Text.append "capacity_positive_" s
              Kind = ObligationKind.BufferCapacity
              Logic = Logic.QfLia
              Statement = Text.append (Text.append (Text.append "buffer " b.Name) " declares a positive capacity (") (Text.append cap " > 0)")
              Source = source desc b.Name
              Refs = refsCapacity
              Form = scalar FormKind.Positive b.Capacity 0L }
        let fits =
            { Id = Text.append "capacity_" s
              Kind = ObligationKind.BufferCapacity
              Logic = Logic.QfLia
              Statement = Text.append (Text.append (Text.append (Text.append "buffer " b.Name) " declares capacity ") cap) (Text.append ", at most " spaceText)
              Source = source desc b.Name
              Refs = refsCapacity
              Form = scalar FormKind.Leq b.Capacity spaceCapacity }
        let acc1 = push (push acc positive) fits
        let trimmed = Framing.hasDelimiter b.Framing && b.TrimDelimiter
        if trimmed then
            let bound =
                { Id = Text.append "input_bound_" s
                  Kind = ObligationKind.InputBufferBound
                  Logic = Logic.QfLia
                  Statement = Text.append (Text.append (Text.append (Text.append "the count handed to the reader of " b.Name) " is its declared capacity (") cap) (Text.append (Text.append "), so the reader writes at most the allocation the same declaration sizes (" cap) ")")
                  Source = source desc b.Name
                  Refs = [| "CWE-120" |]
                  Form = scalar FormKind.Leq b.Capacity b.Capacity }
            let less = Fmt.ofInt64 (b.Capacity - 1L)
            let copy =
                { Id = Text.append "input_copy_bound_" s
                  Kind = ObligationKind.InputCopyBound
                  Logic = Logic.QfLia
                  Statement = Text.append (Text.append (Text.append (Text.append "for any successful read of r bytes into " b.Name) " (1 <= r <= ") cap) (Text.append (Text.append "), the trimmed copy of r - 1 bytes is within " less) " bytes")
                  Source = source desc b.Name
                  Refs = [| "CWE-120"; "CWE-787" |]
                  Form = scalar FormKind.RangeMinusOneLeq b.Capacity (b.Capacity - 1L) }
            push (push acc1 bound) copy
        else acc1

    let private hasBase (s: MemorySpace) : bool =
        let h =
            match s.Base with
            | Some _ -> true
            | None -> false
        h

    let private baseOf (s: MemorySpace) : int64 =
        let b =
            match s.Base with
            | Some v -> v
            | None -> 0L
        b

    let private alignObligation (desc: PlatformDescription) (s: MemorySpace) : Obligation =
        { Id = Text.append "align_" (slug s.Name)
          Kind = ObligationKind.SpaceAlignment
          Logic = Logic.QfBv
          Statement = Text.append (Text.append (Text.append "space " s.Name) " declares alignment ") (Text.append (Fmt.ofInt s.Alignment) ", a power of two")
          Source = source desc s.Name
          Refs = Array.zeroCreate 0
          Form = scalar FormKind.PowerOfTwo (int64 s.Alignment) 0L }

    let private disjointObligation (desc: PlatformDescription) (based: MemorySpace array) : Obligation =
        let n = Array.length based
        let names : string array = Array.zeroCreate n
        let values : int64 array = Array.zeroCreate (2 * n)
        let mutable i = 0
        while i < n do
            let s = Array.get based i
            Array.set names i s.Name
            Array.set values (2 * i) (baseOf s)
            Array.set values (2 * i + 1) s.Capacity
            i <- i + 1
        { Id = "spaces_disjoint"
          Kind = ObligationKind.MemoryMapDisjointness
          Logic = Logic.QfLia
          Statement = Text.append (Text.append (Text.append "the " (Fmt.ofInt n)) " spaces with declared bases (") (Text.append (Fmt.join ", " names) ") occupy pairwise-disjoint address ranges")
          Source = source desc "spaces"
          Refs = [| "CWE-787"; "CWE-125" |]
          Form = { Kind = FormKind.Disjoint; A = int64 n; B = 0L; Names = names; Values = values } }

    /// For a transport with a declared largest unit and a schema, every buffer
    /// of the same schema fits the unit: the frame a wire carries is a fact of
    /// its declaration, not a measurement (ThreeBody's open payload question,
    /// docs/11).
    let private transportObligations (acc: Obligation array) (desc: PlatformDescription) (t: Transport) : Obligation array =
        let mutable out = acc
        if t.MaxUnit > 0L && String.length t.Schema > 0 then
            let nb = Array.length desc.Buffers
            let mutable i = 0
            while i < nb do
                let b = Array.get desc.Buffers i
                if b.Schema = t.Schema then
                    let id = Text.append (Text.append (Text.append "fits_" (slug t.Name)) "_") (slug b.Name)
                    let statement =
                        Text.append (Text.append (Text.append (Text.append "buffer " b.Name) " (") (Fmt.ofInt64 b.Capacity))
                            (Text.append (Text.append (Text.append " bytes) fits one unit of transport " t.Name) " (") (Text.append (Fmt.ofInt64 t.MaxUnit) " bytes)"))
                    out <- push out { Id = id; Kind = ObligationKind.TransportUnit; Logic = Logic.QfLia; Statement = statement
                                      Source = source desc t.Name; Refs = [| "CWE-120" |]; Form = scalar FormKind.Leq b.Capacity t.MaxUnit }
                i <- i + 1
        out

    /// A space of kind Stack fits the host's declared `stack-bytes` limit
    /// when one is declared: the verifier's ceiling as a cited declaration.
    let private limitObligations (acc: Obligation array) (desc: PlatformDescription) : Obligation array =
        let mutable out = acc
        let limit = PlatformDescription.tryFindLimit desc Limit.StackBytes
        let ceiling =
            match limit with
            | Some l -> l.Value
            | None -> 0L
        if ceiling > 0L then
            let ns = Array.length desc.Spaces
            let mutable i = 0
            while i < ns do
                let s = Array.get desc.Spaces i
                if s.Kind = MemoryKind.Stack then
                    let id = Text.append "stack_within_limit_" (slug s.Name)
                    let statement =
                        Text.append (Text.append (Text.append "space " s.Name) " declares capacity ") (Text.append (Text.append (Fmt.ofInt64 s.Capacity) ", at most the stack-bytes limit ") (Fmt.ofInt64 ceiling))
                    out <- push out { Id = id; Kind = ObligationKind.SpaceLimit; Logic = Logic.QfLia; Statement = statement
                                      Source = source desc s.Name; Refs = [| "CWE-121" |]; Form = scalar FormKind.Leq s.Capacity ceiling }
                i <- i + 1
        out

    /// Two names can share a slug ("Console Readln" and "console-readln"); a
    /// repeated id gets `_2`, `_3`, ... so every anchor is unique.
    let private dedupeIds (obs: Obligation array) : Obligation array =
        let n = Array.length obs
        let out : Obligation array = Array.zeroCreate n
        let used (items: Obligation array) (upto: int) (id: string) : bool =
            let mutable j = 0
            let mutable found = false
            while j < upto && not found do
                found <- (Array.get items j).Id = id
                j <- j + 1
            found
        let mutable i = 0
        while i < n do
            let o = Array.get obs i
            let mutable id = o.Id
            if used out i id then
                let mutable suffix = 2
                id <- Text.append (Text.append o.Id "_") (Fmt.ofInt suffix)
                // Reserve original names too: a generated x_2 must not take
                // the id belonging to a declaration encountered later.
                while used out i id || used obs n id do
                    suffix <- suffix + 1
                    id <- Text.append (Text.append o.Id "_") (Fmt.ofInt suffix)
            Array.set out i { o with Id = id }
            i <- i + 1
        out

    /// Every obligation the description supports, in declaration order:
    /// buffers first (capacity, then the input bounds of a trimmed delimited
    /// buffer), then transports (each carried buffer fits the unit), then the
    /// stack limit, then the alignment of each space with a declared base,
    /// then the disjointness of all spaces with declared bases.
    let ofDescription (desc: PlatformDescription) : Obligation array =
        let start : Obligation array = Array.zeroCreate 0
        let nb = Array.length desc.Buffers
        let mutable acc = start
        let mutable i = 0
        while i < nb do
            acc <- bufferObligations acc desc (Array.get desc.Buffers i)
            i <- i + 1
        let nt = Array.length desc.Transports
        let mutable ti = 0
        while ti < nt do
            acc <- transportObligations acc desc (Array.get desc.Transports ti)
            ti <- ti + 1
        acc <- limitObligations acc desc
        let ns = Array.length desc.Spaces
        let mutable basedCount = 0
        let mutable j = 0
        while j < ns do
            let s = Array.get desc.Spaces j
            if hasBase s then
                acc <- push acc (alignObligation desc s)
                basedCount <- basedCount + 1
            j <- j + 1
        let based : MemorySpace array = Array.zeroCreate basedCount
        let mutable k = 0
        let mutable w = 0
        while k < ns do
            let s = Array.get desc.Spaces k
            if hasBase s then
                Array.set based w s
                w <- w + 1
            k <- k + 1
        let all = if basedCount > 0 then push acc (disjointObligation desc based) else acc
        dedupeIds all

    /// An SMT-LIB integer numeral; negatives are written `(- n)`.
    let private smtInt (v: int64) : string =
        if v < 0L then Text.append (Text.append "(- " (Fmt.magnitudeText v)) ")" else Fmt.ofInt64 v

    /// A 64-bit SMT-LIB vector literal, `#x` and sixteen hex digits.
    let private hex16 (v: uint64) : string =
        let buf : byte array = Array.zeroCreate 16
        let mutable x = v
        let mutable pos = 15
        while pos >= 0 do
            let d = int (x &&& 15UL)
            let c = if d < 10 then 48 + d else 87 + d
            Array.set buf pos (byte c)
            x <- x >>> 4
            pos <- pos - 1
        Text.append "#x" (Text.ofUtf8 buf)

    let private line (acc: string) (text: string) : string =
        Text.append (Text.append acc text) "\n"

    let private paren2 (op: string) (a: string) (b: string) : string =
        Text.append (Text.append (Text.append (Text.append (Text.append "(" op) " ") a) (Text.append " " b)) ")"

    let private assert' (term: string) : string =
        Text.append (Text.append "(assert " term) ")"

    /// The disjointness term for ranges i and j: one ends before the other begins.
    let private disjointPair (values: int64 array) (i: int) (j: int) : string =
        // Names remain in Form.Names in declaration order. Solver symbols use
        // that ordinal, so distinct declarations never alias through slugging.
        let bi = Text.append "b_" (Fmt.ofInt i)
        let bj = Text.append "b_" (Fmt.ofInt j)
        let li = smtInt (Array.get values (2 * i + 1))
        let lj = smtInt (Array.get values (2 * j + 1))
        paren2 "or" (paren2 "<=" (paren2 "+" bi li) bj) (paren2 "<=" (paren2 "+" bj lj) bi)

    /// The scaffold's SMT-LIB serialization of one obligation: the definition is
    /// asserted equal to a Boolean named by the id, its negation is asserted,
    /// and `unsat` means the obligation holds. Integer forms are stated in
    /// QF_LIA; `PowerOfTwo` in QF_BV over 64-bit vectors.
    let smtLib (o: Obligation) : string =
        let f = o.Form
        let h1 = line "" (Text.append (Text.append (Text.append "; " o.Id) ": ") o.Statement)
        let h2 = line h1 (Text.append "; origin: " o.Source)
        let h3 = line h2 (Text.append (Text.append "(set-logic " o.Logic) ")")
        let h4 = line h3 (Text.append (Text.append "(declare-const " o.Id) " Bool)")
        let body =
            if f.Kind = FormKind.Leq then
                line h4 (assert' (paren2 "=" o.Id (paren2 "<=" (smtInt f.A) (smtInt f.B))))
            elif f.Kind = FormKind.Positive then
                line h4 (assert' (paren2 "=" o.Id (paren2 ">" (smtInt f.A) "0")))
            elif f.Kind = FormKind.RangeMinusOneLeq then
                let d1 = line h4 "(declare-const r Int)"
                let d2 = line d1 "(assert (>= r 1))"
                let d3 = line d2 (assert' (paren2 "<=" "r" (smtInt f.A)))
                line d3 (assert' (paren2 "=" o.Id (paren2 "<=" "(- r 1)" (smtInt f.B))))
            elif f.Kind = FormKind.PowerOfTwo then
                let zero = hex16 (uint64 0)
                let one = hex16 (uint64 1)
                let d1 = line h4 "(declare-const a (_ BitVec 64))"
                let d2 = line d1 (assert' (paren2 "=" "a" (hex16 (uint64 f.A))))
                let nonzero = Text.append (Text.append "(not " (paren2 "=" "a" zero)) ")"
                let lowbit = paren2 "=" (paren2 "bvand" "a" (paren2 "bvsub" "a" one)) zero
                line d2 (assert' (paren2 "=" o.Id (paren2 "and" nonzero lowbit)))
            elif f.Kind = FormKind.Disjoint then
                let n = Array.length f.Names
                let mutable d = h4
                let mutable i = 0
                while i < n do
                    let name = Text.append "b_" (Fmt.ofInt i)
                    d <- line d (Text.append (Text.append "(declare-const " name) " Int)")
                    d <- line d (assert' (paren2 "=" name (smtInt (Array.get f.Values (2 * i)))))
                    i <- i + 1
                let pairCount = n * (n - 1) / 2
                let pairs : string array = Array.zeroCreate pairCount
                let mutable p = 0
                let mutable a = 0
                while a < n do
                    let mutable b = a + 1
                    while b < n do
                        Array.set pairs p (disjointPair f.Values a b)
                        p <- p + 1
                        b <- b + 1
                    a <- a + 1
                let term =
                    if pairCount = 0 then "true"
                    elif pairCount = 1 then Array.get pairs 0
                    else Text.append (Text.append "(and " (Fmt.join " " pairs)) ")"
                line d (assert' (paren2 "=" o.Id term))
            else
                line h4 (assert' (paren2 "=" o.Id "false"))
        let t1 = line body (Text.append (Text.append "(assert (not " o.Id) "))")
        let t2 = line t1 "(check-sat)"
        line t2 "(reset)"

    /// One line for the obligation ledger:
    /// `obligation <id> kind=<k> logic=<l> source=<s> refs=<a,b> : <statement>`.
    let ledgerLine (o: Obligation) : string =
        let refs = if Array.length o.Refs = 0 then "none" else Fmt.join "," o.Refs
        let l1 = Text.append (Text.append "obligation " o.Id) (Text.append " kind=" o.Kind)
        let l2 = Text.append l1 (Text.append " logic=" o.Logic)
        let l3 = Text.append l2 (Text.append " source=" o.Source)
        let l4 = Text.append l3 (Text.append " refs=" refs)
        Text.append l4 (Text.append " : " o.Statement)
