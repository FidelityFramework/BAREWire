namespace BAREWire.Schema

open BAREWire.Encoding

/// The BARE schema language as text: the interchange artifact of the
/// schema-shared contract mode (Readiness Audit §1, §4 step 14). Emission
/// is deterministic: declarations in declaration order, fields in wire
/// order, explicit enum values, union tags spelled only where they break
/// the implicit sequence. `data<n>` and `list<T>[n]` are the fixed-length
/// spellings (docs/03).
module Emit =

    let private newline () : string =
        let b : byte array = Array.zeroCreate 1
        Array.set b 0 10uy
        Text.ofUtf8 b

    let private push (lines: string array) (line: string) : string array =
        let n = Array.length lines
        let out : string array = Array.zeroCreate (n + 1)
        let mutable i = 0
        while i < n do
            Array.set out i (Array.get lines i)
            i <- i + 1
        Array.set out n line
        out

    /// `NAME = value` for one enum value.
    let private enumValueText (v: EnumValue) : string =
        Text.append (Text.append v.Name " = ") (Fmt.ofUInt64 v.Value)

    /// The text of a type as it appears inline: after a field name, inside
    /// `list<...>`, or as a union case.
    let rec typeText (schema: SchemaDefinition) (t: SchemaType) : string =
        match t with
        | Prim k -> k
        | FixedData n -> Text.append (Text.append "data<" (Fmt.ofInt n)) ">"
        | Enum spec -> enumText spec
        | Optional inner -> Text.append (Text.append "optional<" (typeText schema inner)) ">"
        | List inner -> Text.append (Text.append "list<" (typeText schema inner)) ">"
        | FixedList spec ->
            Text.append (Text.append (Text.append (Text.append "list<" (typeText schema spec.Element)) ">[") (Fmt.ofInt spec.Length)) "]"
        | Map spec ->
            Text.append (Text.append (Text.append (Text.append "map<" (typeText schema spec.Key)) "><") (typeText schema spec.Value)) ">"
        | Union cases -> Text.append (Text.append "union { " (casesText schema cases)) " }"
        | Struct fields -> Text.append (Text.append "struct { " (fieldsText schema fields)) " }"
        | TypeRef name -> name

    and private enumText (spec: EnumSpec) : string =
        let n = Array.length spec.Values
        let parts : string array = Array.zeroCreate n
        let mutable i = 0
        while i < n do
            Array.set parts i (enumValueText (Array.get spec.Values i))
            i <- i + 1
        Text.append (Text.append "enum { " (Fmt.join " " parts)) " }"

    /// Cases joined by ` | `; a tag is written only where it is not the
    /// previous tag plus one (BARE's implicit numbering).
    and private casesText (schema: SchemaDefinition) (cases: UnionCase array) : string =
        let n = Array.length cases
        let parts : string array = Array.zeroCreate n
        let mutable expected = 0
        let mutable i = 0
        while i < n do
            let c = Array.get cases i
            let body = typeText schema c.Type
            let part = if c.Tag = expected then body else Text.append (Text.append body " = ") (Fmt.ofInt c.Tag)
            Array.set parts i part
            expected <- c.Tag + 1
            i <- i + 1
        Fmt.join " | " parts

    and private fieldText (schema: SchemaDefinition) (f: StructField) : string =
        Text.append (Text.append f.Name ": ") (typeText schema f.Type)

    and private fieldsText (schema: SchemaDefinition) (fields: StructField array) : string =
        let n = Array.length fields
        let parts : string array = Array.zeroCreate n
        let mutable i = 0
        while i < n do
            Array.set parts i (fieldText schema (Array.get fields i))
            i <- i + 1
        Fmt.join " " parts

    /// The lines declaring one named type: a struct opens a block with one
    /// field per line; anything else, an enum included, is a single line.
    let private declarationLines (schema: SchemaDefinition) (nt: NamedType) (lines: string array) : string array =
        let head = Text.append "type " nt.Name
        match nt.Type with
        | Struct fields ->
            let mutable acc = push lines (Text.append head " struct {")
            let n = Array.length fields
            let mutable i = 0
            while i < n do
                acc <- push acc (Text.append "  " (fieldText schema (Array.get fields i)))
                i <- i + 1
            push acc "}"
        | _ -> push lines (Text.append (Text.append head " ") (typeText schema nt.Type))

    /// The schema as lines of BARE schema text, in declaration order.
    let lines (schema: SchemaDefinition) : string array =
        let n = Array.length schema.Types
        let mutable acc : string array = Array.zeroCreate 0
        let mutable i = 0
        while i < n do
            acc <- declarationLines schema (Array.get schema.Types i) acc
            i <- i + 1
        acc

    /// The schema as one BARE schema text, lines joined by newlines.
    let schema (definition: SchemaDefinition) : string =
        Fmt.join (newline ()) (lines definition)
