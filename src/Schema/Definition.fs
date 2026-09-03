namespace BAREWire.Schema

/// A BARE primitive kind, spelled as the BARE schema language spells it
/// (`uint`, `u8`, `str`, `data`, `void`, ...). A string alias with a
/// constant module rather than a union: the schema is a boundary contract
/// and crosses into other assemblies, so its tags clear the DU-import wall
/// the way `Fidelity.Platform` contracts do (docs/12 §1, `string-tags`).
///
/// The schema speaks BARE's own fixed vocabulary (docs/10, "Fixed
/// dimensions"), not the compiler's type universe; mapping Clef types onto
/// this vocabulary is the compiler's job.
type PrimKind = string

[<RequireQualifiedAccess>]
module PrimKind =
    [<Literal>]
    let UInt: PrimKind = "uint"
    [<Literal>]
    let Int: PrimKind = "int"
    [<Literal>]
    let U8: PrimKind = "u8"
    [<Literal>]
    let U16: PrimKind = "u16"
    [<Literal>]
    let U32: PrimKind = "u32"
    [<Literal>]
    let U64: PrimKind = "u64"
    [<Literal>]
    let I8: PrimKind = "i8"
    [<Literal>]
    let I16: PrimKind = "i16"
    [<Literal>]
    let I32: PrimKind = "i32"
    [<Literal>]
    let I64: PrimKind = "i64"
    [<Literal>]
    let F32: PrimKind = "f32"
    [<Literal>]
    let F64: PrimKind = "f64"
    [<Literal>]
    let Bool: PrimKind = "bool"
    [<Literal>]
    let String: PrimKind = "str"
    [<Literal>]
    let Data: PrimKind = "data"
    [<Literal>]
    let Void: PrimKind = "void"

    /// True when the kind is an unsigned integer: the variable-width `uint`
    /// or one of the fixed unsigned widths.
    let isUnsignedInteger (k: PrimKind) : bool =
        k = UInt || k = U8 || k = U16 || k = U32 || k = U64

    /// True when the kind is a signed integer: the variable-width `int` or
    /// one of the fixed signed widths.
    let isSignedInteger (k: PrimKind) : bool =
        k = Int || k = I8 || k = I16 || k = I32 || k = I64

    /// True when the kind is any integer kind, signed or unsigned.
    let isInteger (k: PrimKind) : bool =
        isUnsignedInteger k || isSignedInteger k

    /// True when the string names a BARE primitive kind.
    let isValid (k: PrimKind) : bool =
        isInteger k || k = F32 || k = F64 || k = Bool || k = String || k = Data || k = Void

    /// The encoded width in bytes of a fixed-width kind, or 0 when the kind
    /// is not fixed width (`uint`, `int`, `str`, `data`) or unknown. `void`
    /// is fixed at zero bytes and also answers 0; use `isFixedWidth` to tell
    /// the two apart.
    let fixedSize (k: PrimKind) : int =
        if k = U8 || k = I8 || k = Bool then 1
        elif k = U16 || k = I16 then 2
        elif k = U32 || k = I32 || k = F32 then 4
        elif k = U64 || k = I64 || k = F64 then 8
        else 0

    /// True when every value of the kind encodes to the same number of bytes.
    let isFixedWidth (k: PrimKind) : bool =
        fixedSize k > 0 || k = Void

/// One named value of an enum.
type EnumValue = {
    Name: string
    Value: uint64
}

/// An enum: named values over an unsigned base kind. BARE encodes enums as
/// `uint`; a fixed base records that the values are known to fit a width.
type EnumSpec = {
    Base: PrimKind
    Values: EnumValue array
}

/// A named field of a struct.
type StructField = {
    Name: string
    Type: SchemaType
}

/// One case of a union: its wire tag and the type it carries.
and UnionCase = {
    Tag: int
    Type: SchemaType
}

/// A fixed-length list: the element type and the element count.
and FixedListSpec = {
    Element: SchemaType
    Length: int
}

/// A map from a key type to a value type.
and MapSpec = {
    Key: SchemaType
    Value: SchemaType
}

/// A BARE type. Every case carries exactly one payload, a record, an array
/// of records, another type, or a name (docs/12 §1, `single-payload`).
/// `TypeRef` names a type declared in the enclosing schema.
and SchemaType =
    | Prim of PrimKind
    | FixedData of int
    | Enum of EnumSpec
    | Optional of SchemaType
    | List of SchemaType
    | FixedList of FixedListSpec
    | Map of MapSpec
    | Union of UnionCase array
    | Struct of StructField array
    | TypeRef of string

/// A type declaration: a name bound to a type.
type NamedType = {
    Name: string
    Type: SchemaType
}

/// A schema: its declarations in declaration order, and the name of the
/// root message type. Declaration order is the emission order (docs/03).
type SchemaDefinition = {
    Types: NamedType array
    Root: string
}

/// Building and querying schema definitions.
module Schema =

    /// A schema with no declarations and the given root type name.
    let create (root: string) : SchemaDefinition =
        { Types = Array.zeroCreate 0; Root = root }

    /// Add a type declaration at the end of the schema. A repeated name is
    /// kept and reported by `Validation.validate`.
    let addType (name: string) (t: SchemaType) (schema: SchemaDefinition) : SchemaDefinition =
        let n = Array.length schema.Types
        let types : NamedType array = Array.zeroCreate (n + 1)
        let mutable i = 0
        while i < n do
            Array.set types i (Array.get schema.Types i)
            i <- i + 1
        // SUBSET(record-inference): preferred spelling is `{ Name = name; Type = t }` in place.
        let declaration : NamedType = { NamedType.Name = name; NamedType.Type = t }
        Array.set types n declaration
        { Types = types; Root = schema.Root }

    /// The index of a declaration by name, or `-1` when absent. The first
    /// declaration wins when a name is repeated.
    let indexOf (name: string) (schema: SchemaDefinition) : int =
        let n = Array.length schema.Types
        let mutable i = 0
        let mutable found = -1
        while found < 0 && i < n do
            let nt = Array.get schema.Types i
            found <- (if nt.Name = name then i else -1)
            i <- i + 1
        found

    /// The type declared under a name, when there is one.
    let tryFindType (name: string) (schema: SchemaDefinition) : SchemaType option =
        let i = indexOf name schema
        if i < 0 then None else Some (Array.get schema.Types i).Type

    /// True when the schema declares the name.
    let hasType (name: string) (schema: SchemaDefinition) : bool =
        indexOf name schema >= 0

    /// The declared names in declaration order.
    let typeNames (schema: SchemaDefinition) : string array =
        let n = Array.length schema.Types
        let names : string array = Array.zeroCreate n
        let mutable i = 0
        while i < n do
            Array.set names i (Array.get schema.Types i).Name
            i <- i + 1
        names

/// Constructors for BARE types, one per construct in the schema language.
/// Qualified access keeps `Bare.int` and `Bare.string` from shadowing the
/// conversion functions of the same names.
[<RequireQualifiedAccess>]
module Bare =

    /// Variable-width unsigned integer (ULEB128).
    let uint : SchemaType = Prim PrimKind.UInt
    /// Variable-width signed integer (zigzag ULEB128).
    let int : SchemaType = Prim PrimKind.Int
    /// Unsigned 8-bit integer.
    let u8 : SchemaType = Prim PrimKind.U8
    /// Unsigned 16-bit integer.
    let u16 : SchemaType = Prim PrimKind.U16
    /// Unsigned 32-bit integer.
    let u32 : SchemaType = Prim PrimKind.U32
    /// Unsigned 64-bit integer.
    let u64 : SchemaType = Prim PrimKind.U64
    /// Signed 8-bit integer.
    let i8 : SchemaType = Prim PrimKind.I8
    /// Signed 16-bit integer.
    let i16 : SchemaType = Prim PrimKind.I16
    /// Signed 32-bit integer.
    let i32 : SchemaType = Prim PrimKind.I32
    /// Signed 64-bit integer.
    let i64 : SchemaType = Prim PrimKind.I64
    /// IEEE-754 binary32.
    let f32 : SchemaType = Prim PrimKind.F32
    /// IEEE-754 binary64.
    let f64 : SchemaType = Prim PrimKind.F64
    /// Boolean, one byte.
    let bool : SchemaType = Prim PrimKind.Bool
    /// UTF-8 string with a `uint` length prefix.
    let string : SchemaType = Prim PrimKind.String
    /// Bytes with a `uint` length prefix.
    let data : SchemaType = Prim PrimKind.Data
    /// Zero-length type, legal only as a union case. Spelled `void'` because
    /// `void` is a reserved word in F#.
    let void' : SchemaType = Prim PrimKind.Void

    /// Exactly `n` bytes with no prefix.
    let fixedData (n: int) : SchemaType = FixedData n

    /// An enum over a base kind with the given values.
    let enum' (baseKind: PrimKind) (values: EnumValue array) : SchemaType =
        let spec : EnumSpec = { Base = baseKind; Values = values }
        Enum spec

    /// A value that may be absent.
    let optional (t: SchemaType) : SchemaType = Optional t

    /// A variable-length list.
    let list (t: SchemaType) : SchemaType = List t

    /// A list of exactly `n` elements.
    let fixedList (t: SchemaType) (n: int) : SchemaType =
        let spec : FixedListSpec = { Element = t; Length = n }
        FixedList spec

    /// A map from keys to values.
    let map (k: SchemaType) (v: SchemaType) : SchemaType =
        // SUBSET(record-inference): preferred spelling is `{ Key = k; Value = v }`.
        let spec : MapSpec = { MapSpec.Key = k; MapSpec.Value = v }
        Map spec

    /// A tagged union of cases.
    let union (cases: UnionCase array) : SchemaType = Union cases

    /// A struct with fields in wire order.
    let struct' (fields: StructField array) : SchemaType = Struct fields

    /// A reference to a type declared in the schema.
    let typeRef (name: string) : SchemaType = TypeRef name

    /// A struct field.
    let field (name: string) (t: SchemaType) : StructField =
        // SUBSET(record-inference): preferred spelling is `{ Name = name; Type = t }` as the body.
        let f : StructField = { StructField.Name = name; StructField.Type = t }
        f

    /// A union case with an explicit tag.
    let case (tag: int) (t: SchemaType) : UnionCase =
        // SUBSET(record-inference): preferred spelling is `{ Tag = tag; Type = t }` as the body.
        let c : UnionCase = { UnionCase.Tag = tag; UnionCase.Type = t }
        c

    /// An enum value.
    let enumValue (name: string) (v: uint64) : EnumValue =
        // SUBSET(record-inference): preferred spelling is `{ Name = name; Value = v }` as the body.
        let e : EnumValue = { EnumValue.Name = name; EnumValue.Value = v }
        e
