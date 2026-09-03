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

    /// Walk the edges that embed a type in place (struct fields, fixed-list
    /// elements, plain references) and answer whether they lead back to the
    /// declaration at `start`. Optional, list, map, and union cases are
    /// indirections in BARE and are not followed: a cycle through them is
    /// legal. Declarations with an index below `start` are not followed
    /// either, so each cycle is reported once, at its first-declared member.
    let rec private reachesInPlace (schema: SchemaDefinition) (start: int) (path: int array) (t: SchemaType) : bool =
        match t with
        | Struct fields -> fieldsReachInPlace schema start path fields 0
        | FixedList spec -> reachesInPlace schema start path spec.Element
        | TypeRef name -> refReachesInPlace schema start path name
        | _ -> false

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
            acc <- (if dupName then push acc (error ValidationErrorKind.DuplicateField valueLocation) else acc)
            acc <- (if dupValue then push acc (error ValidationErrorKind.DuplicateTag valueLocation) else acc)
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
            while j < i do
                let d = Array.get cases j
                dup <- dup || d.Tag = c.Tag
                j <- j + 1
            let caseLocation = sub location (Text.append "case" (Fmt.ofInt c.Tag))
            acc <- (if dup then push acc (error ValidationErrorKind.DuplicateTag caseLocation) else acc)
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
    /// resolves, `void` appears only as a union case, map keys are keyable
    /// primitives, no enum, union, or struct is empty, fixed lengths are
    /// positive, no tag, field, or enum value repeats, and no type embeds
    /// itself in place. Errors come in declaration order.
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
            acc <- walk schema nt.Name false nt.Type acc
            i <- i + 1
        acc

    /// True when the schema has no defects.
    let isValid (schema: SchemaDefinition) : bool =
        Array.length (validate schema) = 0

    /// One line of plain text for an error: `<kind> at <location>`.
    let describe (e: ValidationError) : string =
        Text.append (Text.append e.Kind " at ") e.Location
