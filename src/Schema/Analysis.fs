namespace BAREWire.Schema

open BAREWire.Encoding

/// The encoded size of a type in bytes. `Max` is meaningful only when
/// `IsBounded`; `IsFixed` means every value encodes to exactly `Min` bytes.
type Size = {
    Min: int
    Max: int
    IsBounded: bool
    IsFixed: bool
}

/// The place of one field in a packed struct: its byte offset from the
/// start of the struct and its fixed width.
type FieldExtent = {
    Name: string
    Offset: int
    Size: int
}

/// How two versions of a schema relate (docs/03, "Schema Versioning").
type Compatibility = string

[<RequireQualifiedAccess>]
module Compatibility =
    /// The same structure: either side reads the other's data.
    [<Literal>]
    let Full: Compatibility = "full"
    /// The new schema reads old data.
    [<Literal>]
    let Backward: Compatibility = "backward"
    /// The old schema reads new data.
    [<Literal>]
    let Forward: Compatibility = "forward"
    /// Neither reads the other's data.
    [<Literal>]
    let Incompatible: Compatibility = "incompatible"

/// Static analysis of schemas: wire sizes, packed offsets, structural
/// equality, and version compatibility (docs/03, "Schema Analysis").
module Analysis =

    /// The largest ULEB128 encoding of a 64-bit value.
    [<Literal>]
    let MaxVarintSize = 10

    let private fixedWidth (n: int) : Size =
        { Min = n; Max = n; IsBounded = true; IsFixed = true }

    let private ranged (lo: int) (hi: int) : Size =
        { Min = lo; Max = hi; IsBounded = true; IsFixed = false }

    let private unbounded (lo: int) : Size =
        { Min = lo; Max = 0; IsBounded = false; IsFixed = false }

    /// The number of bytes ULEB128 uses for a value: one per seven bits.
    let varintSize (v: uint64) : int =
        let mutable x = v
        let mutable n = 1
        while x >= 128UL do
            x <- x >>> 7
            n <- n + 1
        n

    /// The size of a primitive kind. `uint` and `int` are one to ten bytes;
    /// `str` and `data` carry at least a one-byte length and no bound.
    let private primSize (k: PrimKind) : Size =
        if k = PrimKind.UInt || k = PrimKind.Int then ranged 1 MaxVarintSize
        elif k = PrimKind.String || k = PrimKind.Data then unbounded 1
        elif k = PrimKind.Void then fixedWidth 0
        else fixedWidth (PrimKind.fixedSize k)

    let private onPath (name: string) (path: string array) : bool =
        let n = Array.length path
        let mutable i = 0
        let mutable found = false
        while not found && i < n do
            found <- Array.get path i = name
            i <- i + 1
        found

    let private pushName (path: string array) (name: string) : string array =
        let n = Array.length path
        let out : string array = Array.zeroCreate (n + 1)
        let mutable i = 0
        while i < n do
            Array.set out i (Array.get path i)
            i <- i + 1
        Array.set out n name
        out

    /// Narrow an exact hosted extent only when its value is preserved. This
    /// descriptor limit is not a source-level Clef numeric width.
    let private extent (value: int64) : int option =
        let narrowed = int value
        if value >= 0L && int64 narrowed = value then Some narrowed else None

    let private failure (kind: string) (location: string) : ValidationError array =
        [| { Kind = kind; Location = location } |]

    let private sized (location: string) (lo: int64) (hi: int64) (bounded: bool) (isFixed: bool) : Result<Size, ValidationError array> =
        let upper = if bounded then extent hi else Some 0
        match extent lo, upper with
        | Some minimum, Some maximum ->
            Ok { Min = minimum; Max = maximum; IsBounded = bounded; IsFixed = isFixed }
        | _ -> Error (failure "extent-overflow" location)

    /// Size a validated type. No invalid or unresolved fact is a zero-sized
    /// or unbounded type; those are meaningful claims about valid encodings.
    let rec private sizeOf (schema: SchemaDefinition) (path: string array) (location: string) (t: SchemaType) : Result<Size, ValidationError array> =
        match t with
        | Prim k -> Ok (primSize k)
        | FixedData n -> Ok (fixedWidth n)
        | Enum _ -> Ok (primSize PrimKind.UInt)
        | Optional inner ->
            match sizeOf schema path location inner with
            | Error errors -> Error errors
            | Ok s -> sized location 1L (1L + int64 s.Max) s.IsBounded false
        | List _ | Map _ -> Ok (unbounded 1)
        | FixedList spec ->
            match sizeOf schema path location spec.Element with
            | Error errors -> Error errors
            | Ok s -> sized location (int64 s.Min * int64 spec.Length) (int64 s.Max * int64 spec.Length) s.IsBounded s.IsFixed
        | Union cases -> unionSize schema path location cases
        | Struct fields -> structSize schema path location fields
        | TypeRef name ->
            match Schema.tryFindType name schema with
            | None -> Error (failure ValidationErrorKind.UndefinedType location)
            | Some target ->
                if onPath name path then Error (failure ValidationErrorKind.CyclicTypeReference location)
                else sizeOf schema (pushName path name) name target

    and private unionSize (schema: SchemaDefinition) (path: string array) (location: string) (cases: UnionCase array) : Result<Size, ValidationError array> =
        let mutable result = Ok (fixedWidth 0)
        let mutable i = 0
        while i < Array.length cases do
            let c = Array.get cases i
            match result, sizeOf schema path location c.Type with
            | Error errors, _ | _, Error errors -> result <- Error errors
            | Ok total, Ok s ->
                let tagSize = int64 (varintSize (uint64 c.Tag))
                match sized location (tagSize + int64 s.Min) (tagSize + int64 s.Max) s.IsBounded s.IsFixed with
                | Error errors -> result <- Error errors
                | Ok caseSize ->
                    let lo = if i = 0 || caseSize.Min < total.Min then caseSize.Min else total.Min
                    let hi = if caseSize.Max > total.Max then caseSize.Max else total.Max
                    let bounded = total.IsBounded && caseSize.IsBounded
                    result <- Ok { Min = lo; Max = (if bounded then hi else 0); IsBounded = bounded; IsFixed = bounded && lo = hi }
            i <- i + 1
        result

    and private structSize (schema: SchemaDefinition) (path: string array) (location: string) (fields: StructField array) : Result<Size, ValidationError array> =
        let mutable result = Ok (fixedWidth 0)
        let mutable i = 0
        while i < Array.length fields do
            let f = Array.get fields i
            let fieldLocation = Text.append (Text.append location ".") f.Name
            match result, sizeOf schema path fieldLocation f.Type with
            | Error errors, _ | _, Error errors -> result <- Error errors
            | Ok total, Ok s ->
                result <- sized fieldLocation (int64 total.Min + int64 s.Min) (int64 total.Max + int64 s.Max)
                              (total.IsBounded && s.IsBounded) (total.IsFixed && s.IsFixed)
            i <- i + 1
        result

    /// The exact size bounds of a well-formed type, or findings. Dynamic size
    /// is a successful unbounded range; invalid schemas and lost arithmetic
    /// are errors that cannot supply allocation or proof premises.
    let wireSize (schema: SchemaDefinition) (t: SchemaType) : Result<Size, ValidationError array> =
        let errors = Validation.validateType schema t
        if Array.length errors > 0 then Error errors
        else sizeOf schema (Array.zeroCreate 0) schema.Root t

    /// Fixed-width status is available only after successful analysis.
    let isFixedWidth (schema: SchemaDefinition) (t: SchemaType) : Result<bool, ValidationError array> =
        match wireSize schema t with
        | Error errors -> Error errors
        | Ok size -> Ok size.IsFixed

    /// Static packed offsets when all fields have fixed sizes. Ok None means
    /// valid data-dependent offsets; Error means an invalid or unrepresentable
    /// extent. Neither case supplies a plausible empty layout.
    let packedOffsets (schema: SchemaDefinition) (fields: StructField array) : Result<FieldExtent array option, ValidationError array> =
        match wireSize schema (Struct fields) with
        | Error errors -> Error errors
        | Ok size when not size.IsFixed -> Ok None
        | Ok _ ->
            let out : FieldExtent array = Array.zeroCreate (Array.length fields)
            let mutable offset = 0
            let mutable errors : ValidationError array = Array.zeroCreate 0
            let mutable i = 0
            while i < Array.length fields && Array.length errors = 0 do
                let f = Array.get fields i
                match sizeOf schema (Array.zeroCreate 0) f.Name f.Type with
                | Error found -> errors <- found
                | Ok s ->
                    match extent (int64 offset + int64 s.Min) with
                    | None -> errors <- failure "extent-overflow" f.Name
                    | Some endpoint ->
                        Array.set out i { Name = f.Name; Offset = offset; Size = s.Min }
                        offset <- endpoint
                i <- i + 1
            if Array.length errors > 0 then Error errors else Ok (Some out)

    /// The name a reference carries, or "" for any other type.
    let private refName (t: SchemaType) : string =
        match t with
        | TypeRef name -> name
        | _ -> ""

    /// Structural equality of two types, `a` read in `sa` and `b` in `sb`.
    /// References resolve in their own schema; a pair of names already
    /// being compared is taken as equal, so legal recursive types compare
    /// without looping.
    let rec private equalIn (sa: SchemaDefinition) (sb: SchemaDefinition) (path: string array) (a: SchemaType) (b: SchemaType) : bool =
        let ra = refName a
        let rb = refName b
        if ra <> "" || rb <> "" then equalRefs sa sb path ra rb a b
        else
            match a with
            | Prim ka -> equalPrim ka b
            | FixedData na -> equalFixedData na b
            | Enum ea -> equalEnum ea b
            | Optional ia -> equalOptional sa sb path ia b
            | List ia -> equalList sa sb path ia b
            | FixedList fa -> equalFixedList sa sb path fa b
            | Map ma -> equalMap sa sb path ma b
            | Union ca -> equalUnion sa sb path ca b
            | Struct fa -> equalStruct sa sb path fa b
            | TypeRef _ -> false

    and private equalRefs (sa: SchemaDefinition) (sb: SchemaDefinition) (path: string array) (ra: string) (rb: string) (a: SchemaType) (b: SchemaType) : bool =
        let key = Text.append (Text.append ra "|") rb
        if onPath key path then true
        else
            let ta = if ra <> "" then Schema.tryFindType ra sa else Some a
            let tb = if rb <> "" then Schema.tryFindType rb sb else Some b
            match ta with
            | None -> false
            | Some xa -> equalResolved sa sb (pushName path key) xa tb

    and private equalResolved (sa: SchemaDefinition) (sb: SchemaDefinition) (path: string array) (xa: SchemaType) (tb: SchemaType option) : bool =
        match tb with
        | None -> false
        | Some xb -> equalIn sa sb path xa xb

    and private equalPrim (ka: PrimKind) (b: SchemaType) : bool =
        match b with
        | Prim kb -> ka = kb
        | _ -> false

    and private equalFixedData (na: int) (b: SchemaType) : bool =
        match b with
        | FixedData nb -> na = nb
        | _ -> false

    and private equalEnum (ea: EnumSpec) (b: SchemaType) : bool =
        match b with
        | Enum eb -> equalEnumSpecs ea eb
        | _ -> false

    and private equalEnumSpecs (ea: EnumSpec) (eb: EnumSpec) : bool =
        let n = Array.length ea.Values
        let mutable same = ea.Base = eb.Base && n = Array.length eb.Values
        let mutable i = 0
        while same && i < n do
            let va = Array.get ea.Values i
            let vb = Array.get eb.Values i
            same <- va.Name = vb.Name && va.Value = vb.Value
            i <- i + 1
        same

    and private equalOptional (sa: SchemaDefinition) (sb: SchemaDefinition) (path: string array) (ia: SchemaType) (b: SchemaType) : bool =
        match b with
        | Optional ib -> equalIn sa sb path ia ib
        | _ -> false

    and private equalList (sa: SchemaDefinition) (sb: SchemaDefinition) (path: string array) (ia: SchemaType) (b: SchemaType) : bool =
        match b with
        | List ib -> equalIn sa sb path ia ib
        | _ -> false

    and private equalFixedList (sa: SchemaDefinition) (sb: SchemaDefinition) (path: string array) (fa: FixedListSpec) (b: SchemaType) : bool =
        match b with
        | FixedList fb -> fa.Length = fb.Length && equalIn sa sb path fa.Element fb.Element
        | _ -> false

    and private equalMap (sa: SchemaDefinition) (sb: SchemaDefinition) (path: string array) (ma: MapSpec) (b: SchemaType) : bool =
        match b with
        | Map mb -> equalIn sa sb path ma.Key mb.Key && equalIn sa sb path ma.Value mb.Value
        | _ -> false

    and private equalUnion (sa: SchemaDefinition) (sb: SchemaDefinition) (path: string array) (ca: UnionCase array) (b: SchemaType) : bool =
        match b with
        | Union cb -> equalCases sa sb path ca cb
        | _ -> false

    and private equalCases (sa: SchemaDefinition) (sb: SchemaDefinition) (path: string array) (ca: UnionCase array) (cb: UnionCase array) : bool =
        let n = Array.length ca
        let mutable same = n = Array.length cb
        let mutable i = 0
        while same && i < n do
            let x = Array.get ca i
            let y = Array.get cb i
            same <- x.Tag = y.Tag && equalIn sa sb path x.Type y.Type
            i <- i + 1
        same

    and private equalStruct (sa: SchemaDefinition) (sb: SchemaDefinition) (path: string array) (fa: StructField array) (b: SchemaType) : bool =
        match b with
        | Struct fb -> equalFields sa sb path fa fb
        | _ -> false

    and private equalFields (sa: SchemaDefinition) (sb: SchemaDefinition) (path: string array) (fa: StructField array) (fb: StructField array) : bool =
        let n = Array.length fa
        let mutable same = n = Array.length fb
        let mutable i = 0
        while same && i < n do
            let x = Array.get fa i
            let y = Array.get fb i
            same <- x.Name = y.Name && equalIn sa sb path x.Type y.Type
            i <- i + 1
        same

    /// Structural equality of two types, each read in its own schema: names
    /// resolve to their declarations, so a type is equal to an identical
    /// declaration under another name. Pass the same schema twice to compare
    /// two types of one schema.
    let typesEqual (sa: SchemaDefinition) (sb: SchemaDefinition) (a: SchemaType) (b: SchemaType) : bool =
        equalIn sa sb (Array.zeroCreate 0) a b

    /// Follow references until a type that is not a reference, or give up.
    let private deref (schema: SchemaDefinition) (t: SchemaType) : SchemaType =
        let mutable cur = t
        let mutable hops = 0
        let mutable fin = false
        while not fin do
            let name = refName cur
            let i = if name <> "" && hops < 64 then Schema.indexOf name schema else -1
            let found = i >= 0
            if found then
                cur <- (Array.get schema.Types i).Type
            if found then
                hops <- hops + 1
            fin <- not found
        cur

    /// True when every case of `xs` has a case in `ys` with the same tag
    /// and an equal type.
    let private casesCovered (sx: SchemaDefinition) (sy: SchemaDefinition) (xs: UnionCase array) (ys: UnionCase array) : bool =
        let n = Array.length xs
        let m = Array.length ys
        let mutable covered = true
        let mutable i = 0
        while covered && i < n do
            let x = Array.get xs i
            let mutable found = false
            let mutable j = 0
            while not found && j < m do
                let y = Array.get ys j
                found <- x.Tag = y.Tag && equalIn sx sy (Array.zeroCreate 0) x.Type y.Type
                j <- j + 1
            covered <- found
            i <- i + 1
        covered

    /// True when the first `Array.length xs` fields of `ys` are `xs`, in
    /// order, with equal types.
    let private fieldsPrefix (sx: SchemaDefinition) (sy: SchemaDefinition) (xs: StructField array) (ys: StructField array) : bool =
        let n = Array.length xs
        let mutable prefix = n <= Array.length ys
        let mutable i = 0
        while prefix && i < n do
            let x = Array.get xs i
            let y = Array.get ys i
            prefix <- x.Name = y.Name && equalIn sx sy (Array.zeroCreate 0) x.Type y.Type
            i <- i + 1
        prefix

    let private unionCompatibility (oldSchema: SchemaDefinition) (newSchema: SchemaDefinition) (oldCases: UnionCase array) (newCases: UnionCase array) : Compatibility =
        let oldCovered = casesCovered oldSchema newSchema oldCases newCases
        let newCovered = casesCovered newSchema oldSchema newCases oldCases
        if oldCovered && newCovered then Compatibility.Full
        elif oldCovered then Compatibility.Backward
        elif newCovered then Compatibility.Forward
        else Compatibility.Incompatible

    let private structCompatibility (oldSchema: SchemaDefinition) (newSchema: SchemaDefinition) (oldFields: StructField array) (newFields: StructField array) : Compatibility =
        let oldPrefix = fieldsPrefix oldSchema newSchema oldFields newFields
        let newPrefix = fieldsPrefix newSchema oldSchema newFields oldFields
        if oldPrefix && newPrefix then Compatibility.Full
        elif oldPrefix then Compatibility.Backward
        elif newPrefix then Compatibility.Forward
        else Compatibility.Incompatible

    let private rootCompatibility (oldSchema: SchemaDefinition) (newSchema: SchemaDefinition) (oldRoot: SchemaType) (newRoot: SchemaType) : Compatibility =
        let o = deref oldSchema oldRoot
        let n = deref newSchema newRoot
        match o with
        | Union oc ->
            (match n with
             | Union nc -> unionCompatibility oldSchema newSchema oc nc
             | _ -> Compatibility.Incompatible)
        | Struct ofs ->
            (match n with
             | Struct nfs -> structCompatibility oldSchema newSchema ofs nfs
             | _ -> Compatibility.Incompatible)
        | _ -> (if typesEqual oldSchema newSchema o n then Compatibility.Full else Compatibility.Incompatible)

    /// The compatibility of two schema versions, judged at their roots
    /// (docs/03, "Schema Versioning"). Unions: the new schema reads old data
    /// when every old case survives with its tag and type (backward), the
    /// old schema reads new data when every new case was already there
    /// (forward), both when the case sets agree (full). Structs: the old
    /// fields must be a prefix of the new ones in order (backward), or the
    /// new a prefix of the old (forward), or the same (full). Any other
    /// root must be structurally equal. A missing root is incompatible.
    let compatibility (oldSchema: SchemaDefinition) (newSchema: SchemaDefinition) : Compatibility =
        let oldRoot = Schema.tryFindType oldSchema.Root oldSchema
        let newRoot = Schema.tryFindType newSchema.Root newSchema
        match oldRoot with
        | None -> Compatibility.Incompatible
        | Some o ->
            (match newRoot with
             | None -> Compatibility.Incompatible
             | Some n -> rootCompatibility oldSchema newSchema o n)
