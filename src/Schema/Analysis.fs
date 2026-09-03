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

    /// The size of a type. `path` holds the declaration names being
    /// resolved; a reference back into it is a legal recursion through an
    /// indirection, whose size has no bound.
    let rec private sizeOf (schema: SchemaDefinition) (path: string array) (t: SchemaType) : Size =
        match t with
        | Prim k -> primSize k
        | FixedData n ->
            let width = if n < 0 then 0 else n
            fixedWidth width
        | Enum spec -> primSize spec.Base
        | Optional inner ->
            let s = sizeOf schema path inner
            { Min = 1; Max = 1 + s.Max; IsBounded = s.IsBounded; IsFixed = false }
        | List _ -> unbounded 1
        | FixedList spec ->
            let s = sizeOf schema path spec.Element
            let n = if spec.Length < 0 then 0 else spec.Length
            { Min = s.Min * n; Max = s.Max * n; IsBounded = s.IsBounded; IsFixed = s.IsFixed }
        | Map _ -> unbounded 1
        | Union cases -> unionSize schema path cases
        | Struct fields -> structSize schema path fields
        | TypeRef name -> refSize schema path name

    /// A union is its tag (the ULEB128 width of each case's tag, which is a
    /// known constant) plus the case: the smallest case for `Min`, the
    /// largest for `Max`. Fixed only when it has one case and that case is
    /// fixed.
    and private unionSize (schema: SchemaDefinition) (path: string array) (cases: UnionCase array) : Size =
        let n = Array.length cases
        let mutable lo = 0
        let mutable hi = 0
        let mutable bounded = true
        let mutable i = 0
        while i < n do
            let c = Array.get cases i
            let tag = if c.Tag < 0 then 0 else c.Tag
            let tagSize = varintSize (uint64 tag)
            let s = sizeOf schema path c.Type
            let cLo = tagSize + s.Min
            let cHi = tagSize + s.Max
            lo <- (if i = 0 || cLo < lo then cLo else lo)
            hi <- (if cHi > hi then cHi else hi)
            bounded <- bounded && s.IsBounded
            i <- i + 1
        { Min = lo; Max = (if bounded then hi else 0); IsBounded = bounded; IsFixed = bounded && lo = hi }

    /// A struct is the sum of its fields, packed with no padding.
    and private structSize (schema: SchemaDefinition) (path: string array) (fields: StructField array) : Size =
        let n = Array.length fields
        let mutable lo = 0
        let mutable hi = 0
        let mutable bounded = true
        let mutable isFixed = true
        let mutable i = 0
        while i < n do
            let f = Array.get fields i
            let s = sizeOf schema path f.Type
            lo <- lo + s.Min
            hi <- hi + s.Max
            bounded <- bounded && s.IsBounded
            isFixed <- isFixed && s.IsFixed
            i <- i + 1
        { Min = lo; Max = (if bounded then hi else 0); IsBounded = bounded; IsFixed = isFixed }

    and private refSize (schema: SchemaDefinition) (path: string array) (name: string) : Size =
        let target = Schema.tryFindType name schema
        match target with
        | None -> unbounded 0
        | Some tt -> (if onPath name path then unbounded 0 else sizeOf schema (pushName path name) tt)

    /// The encoded size of a type within a schema. An undefined reference
    /// is unbounded; validate first for a meaningful answer.
    let wireSize (schema: SchemaDefinition) (t: SchemaType) : Size =
        sizeOf schema (Array.zeroCreate 0) t

    /// True when every value of the type encodes to the same width.
    let isFixedWidth (schema: SchemaDefinition) (t: SchemaType) : bool =
        (wireSize schema t).IsFixed

    /// The byte offset and width of each field of a struct whose fields are
    /// all fixed width. BARE structs are packed: no alignment padding, each
    /// field starts where the previous one ends. Empty when any field is not
    /// fixed width, since no static offsets exist past it.
    let packedOffsets (schema: SchemaDefinition) (fields: StructField array) : FieldExtent array =
        let n = Array.length fields
        let mutable allFixed = true
        let mutable i = 0
        while i < n do
            let f = Array.get fields i
            allFixed <- allFixed && isFixedWidth schema f.Type
            i <- i + 1
        let count = if allFixed then n else 0
        let out : FieldExtent array = Array.zeroCreate count
        let mutable offset = 0
        let mutable j = 0
        while j < count do
            let f = Array.get fields j
            let s = wireSize schema f.Type
            // SUBSET(record-inference): preferred spelling is the unqualified record literal in place.
            let extent : FieldExtent = { FieldExtent.Name = f.Name; FieldExtent.Offset = offset; FieldExtent.Size = s.Min }
            Array.set out j extent
            offset <- offset + s.Min
            j <- j + 1
        out

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
