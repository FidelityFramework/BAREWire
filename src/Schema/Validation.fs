namespace BAREWire.Schema

open BAREWire.Encoding

/// One schema defect: what kind, and where. `Location` is a dotted path
/// from the declaration name into the type (`Message.attachments.item`).
type ValidationError = {
    Kind: string
    Location: string
}

/// The vocabulary of schema defects (docs/03, "Schema Validation", plus
/// the duplicate checks a real schema needs).
[<RequireQualifiedAccess>]
module ValidationErrorKind =
    [<Literal>]
    let CyclicTypeReference = "cyclic-type-reference"
    [<Literal>]
    let UndefinedType = "undefined-type"
    [<Literal>]
    let InvalidVoidUsage = "invalid-void-usage"
    [<Literal>]
    let InvalidMapKeyType = "invalid-map-key-type"
    [<Literal>]
    let EmptyEnum = "empty-enum"
    [<Literal>]
    let EmptyUnion = "empty-union"
    [<Literal>]
    let EmptyStruct = "empty-struct"
    [<Literal>]
    let InvalidFixedLength = "invalid-fixed-length"
    [<Literal>]
    let DuplicateTag = "duplicate-tag"
    [<Literal>]
    let DuplicateField = "duplicate-field"
    /// A union case tag below zero.
    [<Literal>]
    let InvalidTag = "invalid-tag"
    /// Two union cases of the same type.
    [<Literal>]
    let DuplicateCaseType = "duplicate-case-type"
    /// An enum value name declared twice.
    [<Literal>]
    let DuplicateEnumName = "duplicate-enum-name"
    /// An enum value declared twice.
    [<Literal>]
    let DuplicateEnumValue = "duplicate-enum-value"
    [<Literal>]
    let DuplicateTypeName = "duplicate-type-name"
    [<Literal>]
    let InvalidEnumBase = "invalid-enum-base"
    [<Literal>]
    let InvalidPrimitive = "invalid-primitive"
    [<Literal>]
    let UndefinedRoot = "undefined-root"

/// Schema validation: every rule of docs/03 "Schema Validation", the
/// duplicate checks, and BARE's cycle rule. A schema with no errors is
/// well-formed and every other module may assume so.
module Validation =

    /// How many reference hops a lookup follows before giving up; bounds
    /// the walk over a schema whose reference chain is itself cyclic.
    [<Literal>]
    let private MaxRefDepth = 64

    let private error (kind: string) (location: string) : ValidationError =
        { Kind = kind; Location = location }

    let private push (errors: ValidationError array) (e: ValidationError) : ValidationError array =
        let n = Array.length errors
        let out : ValidationError array = Array.zeroCreate (n + 1)
        let mutable i = 0
        while i < n do
            Array.set out i (Array.get errors i)
            i <- i + 1
        Array.set out n e
        out

    let private sub (location: string) (part: string) : string =
        Text.append (Text.append location ".") part

    let private onPath (index: int) (path: int array) : bool =
        let n = Array.length path
        let mutable i = 0
        let mutable found = false
        while not found && i < n do
            found <- Array.get path i = index
            i <- i + 1
        found

    let private pushIndex (path: int array) (index: int) : int array =
        let n = Array.length path
        let out : int array = Array.zeroCreate (n + 1)
        let mutable i = 0
        while i < n do
            Array.set out i (Array.get path i)
            i <- i + 1
        Array.set out n index
        out

    /// True when a primitive kind may key a map: any primitive but the
    /// floats, `data`, and `void` (BARE, "map").
    let private isMapKeyKind (k: PrimKind) : bool =
        PrimKind.isValid k && not (k = PrimKind.F32 || k = PrimKind.F64 || k = PrimKind.Data || k = PrimKind.Void)

    /// True when the type may key a map, following references.
    let rec private isValidMapKey (schema: SchemaDefinition) (t: SchemaType) (depth: int) : bool =
        match t with
        | Prim k -> isMapKeyKind k
        | Enum _ -> true
        | TypeRef name -> depth < MaxRefDepth && isValidMapKeyRef schema name depth
        | _ -> false

    and private isValidMapKeyRef (schema: SchemaDefinition) (name: string) (depth: int) : bool =
        let target = Schema.tryFindType name schema
        match target with
        | Some tt -> isValidMapKey schema tt (depth + 1)
        | None -> false

    /// True when the type is `void`, following references.
    let rec private isVoid (schema: SchemaDefinition) (t: SchemaType) (depth: int) : bool =
        match t with
        | Prim k -> k = PrimKind.Void
        | TypeRef name -> depth < MaxRefDepth && isVoidRef schema name depth
        | _ -> false

    and private isVoidRef (schema: SchemaDefinition) (name: string) (depth: int) : bool =
        let target = Schema.tryFindType name schema
        match target with
        | Some tt -> isVoid schema tt (depth + 1)
        | None -> false

    /// Walk every reference edge of a type (struct fields, list and fixed-list
    /// elements, optional payloads, map keys and values, union cases, plain
    /// references) and answer whether they lead back to the declaration at
    /// `start`. BARE forbids a type defined in terms of itself by any route
    /// (docs/03, "Schema Validation"), so no edge is exempt. Declarations
    /// with an index below `start` are not followed, so each cycle is
    /// reported once, at its first-declared member.
    let rec private reachesInPlace (schema: SchemaDefinition) (start: int) (path: int array) (t: SchemaType) : bool =
        match t with
        | Struct fields -> fieldsReachInPlace schema start path fields 0
        | FixedList spec -> reachesInPlace schema start path spec.Element
        | List inner -> reachesInPlace schema start path inner
        | Optional inner -> reachesInPlace schema start path inner
        | Map spec -> reachesInPlace schema start path spec.Key || reachesInPlace schema start path spec.Value
        | Union cases -> casesReachInPlace schema start path cases 0
        | TypeRef name -> refReachesInPlace schema start path name
        | _ -> false

    and private casesReachInPlace (schema: SchemaDefinition) (start: int) (path: int array) (cases: UnionCase array) (from: int) : bool =
        let n = Array.length cases
        let mutable i = from
        let mutable found = false
        while not found && i < n do
            let c = Array.get cases i
            found <- reachesInPlace schema start path c.Type
            i <- i + 1
        found

    and private fieldsReachInPlace (schema: SchemaDefinition) (start: int) (path: int array) (fields: StructField array) (from: int) : bool =
        let n = Array.length fields
        let mutable i = from
        let mutable found = false
        while not found && i < n do
            let f = Array.get fields i
            found <- reachesInPlace schema start path f.Type
            i <- i + 1
        found

    and private refReachesInPlace (schema: SchemaDefinition) (start: int) (path: int array) (name: string) : bool =
        let index = Schema.indexOf name schema
        if index < 0 then false
        elif index = start then true
        elif index < start || onPath index path then false
        else reachesInPlace schema start (pushIndex path index) (Array.get schema.Types index).Type

    /// Structural equality of two types as written (references compare by
    /// name), enough to refuse a union with two cases of one type.
    let rec private sameType (a: SchemaType) (b: SchemaType) : bool =
        match a with
        | Prim ka ->
            let r = match b with | Prim kb -> ka = kb | _ -> false
            r
        | FixedData na ->
            let r = match b with | FixedData nb -> na = nb | _ -> false
            r
        | Enum sa ->
            let r = match b with | Enum sb -> sa.Base = sb.Base && Array.length sa.Values = Array.length sb.Values | _ -> false
            r
        | Optional ia ->
            let r = match b with | Optional ib -> sameType ia ib | _ -> false
            r
        | List ia ->
            let r = match b with | List ib -> sameType ia ib | _ -> false
            r
        | FixedList fa ->
            let r = match b with | FixedList fb -> fa.Length = fb.Length && sameType fa.Element fb.Element | _ -> false
            r
        | Map ma ->
            let r = match b with | Map mb -> sameType ma.Key mb.Key && sameType ma.Value mb.Value | _ -> false
            r
        | Union ca ->
            let r = match b with | Union cb -> Array.length ca = Array.length cb && casesSame ca cb 0 | _ -> false
            r
        | Struct fa ->
            let r = match b with | Struct fb -> Array.length fa = Array.length fb && fieldsSame fa fb 0 | _ -> false
            r
        | TypeRef na ->
            let r = match b with | TypeRef nb -> na = nb | _ -> false
            r

    and private casesSame (xs: UnionCase array) (ys: UnionCase array) (from: int) : bool =
        let mutable i = from
        let mutable same = true
        while same && i < Array.length xs do
            let x = Array.get xs i
            let y = Array.get ys i
            same <- x.Tag = y.Tag && sameType x.Type y.Type
            i <- i + 1
        same

    and private fieldsSame (xs: StructField array) (ys: StructField array) (from: int) : bool =
        let mutable i = from
        let mutable same = true
        while same && i < Array.length xs do
            let x = Array.get xs i
            let y = Array.get ys i
            same <- x.Name = y.Name && sameType x.Type y.Type
            i <- i + 1
        same

    /// Walk a type, appending every defect found under `location`.
    /// `inUnion` is true when the type is directly a union case, the one
    /// place `void` may appear.
    let rec private walk (schema: SchemaDefinition) (location: string) (inUnion: bool) (t: SchemaType) (errors: ValidationError array) : ValidationError array =
        match t with
        | Prim k -> walkPrim location inUnion k errors
        | FixedData n ->
            if n <= 0 then push errors (error ValidationErrorKind.InvalidFixedLength location) else errors
        | Enum spec -> walkEnum location spec errors
        | Optional inner -> walk schema (sub location "value") false inner errors
        | List inner -> walk schema (sub location "item") false inner errors
        | FixedList spec ->
            let e1 = if spec.Length <= 0 then push errors (error ValidationErrorKind.InvalidFixedLength location) else errors
            walk schema (sub location "item") false spec.Element e1
        | Map spec ->
            let keyLocation = sub location "key"
            let e1 = if isValidMapKey schema spec.Key 0 then errors else push errors (error ValidationErrorKind.InvalidMapKeyType keyLocation)
            let e2 = walk schema keyLocation false spec.Key e1
            walk schema (sub location "value") false spec.Value e2
        | Union cases -> walkUnion schema location cases errors
        | Struct fields -> walkStruct schema location fields errors
        | TypeRef name -> walkRef schema location inUnion name errors

    and private walkPrim (location: string) (inUnion: bool) (k: PrimKind) (errors: ValidationError array) : ValidationError array =
        let e1 = if PrimKind.isValid k then errors else push errors (error ValidationErrorKind.InvalidPrimitive location)
        if k = PrimKind.Void && not inUnion then push e1 (error ValidationErrorKind.InvalidVoidUsage location) else e1

    and private walkEnum (location: string) (spec: EnumSpec) (errors: ValidationError array) : ValidationError array =
        let n = Array.length spec.Values
        let e1 = if n = 0 then push errors (error ValidationErrorKind.EmptyEnum location) else errors
        let e2 = if PrimKind.isUnsignedInteger spec.Base then e1 else push e1 (error ValidationErrorKind.InvalidEnumBase location)
        let mutable acc = e2
        let mutable i = 0
        while i < n do
            let v = Array.get spec.Values i
            let mutable j = 0
            let mutable dupName = false
            let mutable dupValue = false
            while j < i do
                let w = Array.get spec.Values j
                dupName <- dupName || w.Name = v.Name
                dupValue <- dupValue || w.Value = v.Value
                j <- j + 1
            let valueLocation = sub location v.Name
            acc <- (if dupName then push acc (error ValidationErrorKind.DuplicateEnumName valueLocation) else acc)
            acc <- (if dupValue then push acc (error ValidationErrorKind.DuplicateEnumValue valueLocation) else acc)
            i <- i + 1
        acc

    and private walkUnion (schema: SchemaDefinition) (location: string) (cases: UnionCase array) (errors: ValidationError array) : ValidationError array =
        let n = Array.length cases
        let mutable acc = if n = 0 then push errors (error ValidationErrorKind.EmptyUnion location) else errors
        let mutable i = 0
        while i < n do
            let c = Array.get cases i
            let mutable j = 0
            let mutable dup = false
            let mutable dupType = false
            while j < i do
                let d = Array.get cases j
                dup <- dup || d.Tag = c.Tag
                dupType <- dupType || sameType d.Type c.Type
                j <- j + 1
            let caseLocation = sub location (Text.append "case" (Fmt.ofInt c.Tag))
            acc <- (if c.Tag < 0 then push acc (error ValidationErrorKind.InvalidTag caseLocation) else acc)
            acc <- (if dup then push acc (error ValidationErrorKind.DuplicateTag caseLocation) else acc)
            acc <- (if dupType then push acc (error ValidationErrorKind.DuplicateCaseType caseLocation) else acc)
            acc <- walk schema caseLocation true c.Type acc
            i <- i + 1
        acc

    and private walkStruct (schema: SchemaDefinition) (location: string) (fields: StructField array) (errors: ValidationError array) : ValidationError array =
        let n = Array.length fields
        let mutable acc = if n = 0 then push errors (error ValidationErrorKind.EmptyStruct location) else errors
        let mutable i = 0
        while i < n do
            let f = Array.get fields i
            let mutable j = 0
            let mutable dup = false
            while j < i do
                let g = Array.get fields j
                dup <- dup || g.Name = f.Name
                j <- j + 1
            let fieldLocation = sub location f.Name
            acc <- (if dup then push acc (error ValidationErrorKind.DuplicateField fieldLocation) else acc)
            acc <- walk schema fieldLocation false f.Type acc
            i <- i + 1
        acc

    and private walkRef (schema: SchemaDefinition) (location: string) (inUnion: bool) (name: string) (errors: ValidationError array) : ValidationError array =
        let defined = Schema.hasType name schema
        let e1 = if defined then errors else push errors (error ValidationErrorKind.UndefinedType location)
        let voidMisuse = defined && not inUnion && isVoidRef schema name 0
        if voidMisuse then push e1 (error ValidationErrorKind.InvalidVoidUsage location) else e1

    /// Validate a schema. An empty result means the schema is well-formed:
    /// the root is declared, no name is declared twice, every reference
    /// resolves, `void` appears only as a union case (a bare `void`
    /// declaration is allowed for that purpose), map keys are keyable
    /// primitives, no enum, union, or struct is empty, fixed lengths are
    /// positive, tags are non-negative, no tag, case type, field, or enum
    /// name or value repeats, and no type is defined in terms of itself by
    /// any route. Errors come in declaration order.
    let validate (schema: SchemaDefinition) : ValidationError array =
        let n = Array.length schema.Types
        let mutable acc : ValidationError array = Array.zeroCreate 0
        acc <- (if Schema.hasType schema.Root schema then acc else push acc (error ValidationErrorKind.UndefinedRoot schema.Root))
        let mutable i = 0
        while i < n do
            let nt = Array.get schema.Types i
            let firstIndex = Schema.indexOf nt.Name schema
            acc <- (if firstIndex < i then push acc (error ValidationErrorKind.DuplicateTypeName nt.Name) else acc)
            let cyclic = firstIndex = i && reachesInPlace schema i (Array.zeroCreate 0) nt.Type
            acc <- (if cyclic then push acc (error ValidationErrorKind.CyclicTypeReference nt.Name) else acc)
            // A bare `void` declaration is legal: it exists to be a union case
            // by reference, and every non-union use of the reference is refused
            // where it occurs.
            let bareVoid =
                match nt.Type with
                | Prim k -> k = PrimKind.Void
                | _ -> false
            acc <- walk schema nt.Name bareVoid nt.Type acc
            i <- i + 1
        acc

    /// True when the schema has no defects.
    let isValid (schema: SchemaDefinition) : bool =
        Array.length (validate schema) = 0

    /// One line of plain text for an error: `<kind> at <location>`.
    let describe (e: ValidationError) : string =
        Text.append (Text.append e.Kind " at ") e.Location
