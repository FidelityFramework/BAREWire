namespace BAREWire.Hardware

open BAREWire.Encoding

/// One BTF type as the reader reports it: enough to check an emitted blob
/// against the descriptors it came from.
type BtfType = {
    /// 1-based type id; 0 is void.
    Id: int
    /// The BTF kind number (1 int, 2 pointer, 3 array, 4 struct, 16 float).
    Kind: int
    /// The name, or "" for anonymous types.
    Name: string
    /// Size in bytes for int, struct, and float; the pointee or element type id otherwise.
    SizeOrType: int
    /// Member names for a struct, in order.
    MemberNames: string array
    /// Member bit offsets for a struct, in order.
    MemberBitOffsets: int array
    /// Member type ids for a struct, in order.
    MemberTypes: int array
}

/// A parsed BTF blob: the types in id order, or `Ok = false` when the header
/// or a type entry was malformed.
type BtfImage = {
    Ok: bool
    Types: BtfType array
}

/// BTF, the BPF Type Format, as a second schema serialization beside the
/// `.bare` text of `Schema.Emit` (docs/03, docs/11 "The kernel as a
/// described platform"). BTF describes C natural layout: struct members at
/// bit offsets, integer encodings, arrays, pointers. That is exactly what a
/// `StructDescriptor` declares and `Validator.derive` computes under an ABI,
/// so the emitter is a serialization of descriptors, not a new model.
///
/// The emitter writes the type table and the string table of a raw `.BTF`
/// section (header, `btf_type` entries, NUL-separated strings); the reader
/// parses one back so the tests can check the round trip and `bpftool btf
/// dump file` can confirm the blob independently. Placing the section in an
/// ELF object is the compiler's job.
///
/// Format reference: Linux `Documentation/bpf/btf.rst`. Header: magic
/// 0xeB9F (u16), version 1 (u8), flags (u8), hdr_len (u32) = 24, then
/// type_off, type_len, str_off, str_len (u32 each), offsets relative to the
/// end of the header. Each type: name_off (u32), info (u32: vlen in bits
/// 0..15, kind in bits 24..28, kind_flag in bit 31), size_or_type (u32).
/// INT carries one extra u32 (encoding in bits 24..27, offset 16..23,
/// nr_bits 0..7); ARRAY carries type, index_type, nelems; STRUCT carries
/// vlen members of name_off, type, offset (in bits).
module Btf =

    [<Literal>]
    let Magic = 0xeB9F

    [<Literal>]
    let HeaderSize = 24

    [<Literal>]
    let KindInt = 1
    [<Literal>]
    let KindPointer = 2
    [<Literal>]
    let KindArray = 3
    [<Literal>]
    let KindStruct = 4
    [<Literal>]
    let KindFloat = 16

    [<Literal>]
    let IntSigned = 1
    [<Literal>]
    let IntBool = 4

    /// The name of the array index type BTF requires.
    [<Literal>]
    let ArrayIndexTypeName = "__ARRAY_SIZE_TYPE__"

    // ---- the string table -------------------------------------------------

    /// Offset of a name in the string table, adding it when absent. The table
    /// is a byte array with a leading NUL; `used` is its filled length.
    let private internName (table: byte array) (used: int) (name: string) : int * int =
        let bytes = Text.toUtf8 name
        let n = Array.length bytes
        // search for an existing NUL-terminated copy
        let mutable pos = 1
        let mutable found = -1
        while pos < used && found < 0 do
            let mutable k = 0
            let mutable same = true
            while same && k < n do
                same <- pos + k < used && Array.get table (pos + k) = Array.get bytes k
                k <- k + 1
            let terminated = same && pos + n < used && Array.get table (pos + n) = 0uy
            if terminated then
                found <- pos
            // advance to the byte after the next NUL
            let mutable q = pos
            while q < used && Array.get table q <> 0uy do
                q <- q + 1
            pos <- q + 1
        if found >= 0 then (found, used)
        elif n = 0 then (0, used)
        else
            let mutable i = 0
            while i < n do
                Array.set table (used + i) (Array.get bytes i)
                i <- i + 1
            Array.set table (used + n) 0uy
            (used, used + n + 1)

    // ---- primitive type ids ------------------------------------------------

    /// The primitive BTF types the reprs need, in a fixed order so ids are
    /// stable: u8 u16 u32 u64 i8 i16 i32 i64 bool f32 f64, then the array
    /// index type, then the void pointer. Ids are 1-based.
    let private primitiveReprs : Repr array =
        [| Repr.U8; Repr.U16; Repr.U32; Repr.U64; Repr.I8; Repr.I16; Repr.I32; Repr.I64; Repr.Bool; Repr.F32; Repr.F64 |]

    [<Literal>]
    let private IndexTypeId = 12

    [<Literal>]
    let private PointerTypeId = 13

    [<Literal>]
    let private FirstArrayId = 14

    /// The type id of a scalar repr, or 0 for an unknown one.
    let private primitiveId (repr: Repr) : int =
        let mutable i = 0
        let mutable id = 0
        while i < Array.length primitiveReprs do
            if Array.get primitiveReprs i = repr then
                id <- i + 1
            i <- i + 1
        if repr = Repr.Pointer then PointerTypeId else id

    let private isSigned (repr: Repr) : bool =
        repr = Repr.I8 || repr = Repr.I16 || repr = Repr.I32 || repr = Repr.I64

    let private isFloat (repr: Repr) : bool =
        repr = Repr.F32 || repr = Repr.F64

    let private info (kind: int) (vlen: int) : uint32 =
        (uint32 kind <<< 24) ||| uint32 vlen

    // ---- emission ---------------------------------------------------------

    /// The byte size of the type table for the given descriptors under the ABI:
    /// primitives (INT 16 bytes each, FLOAT 12, the index INT 16, PTR 12), one
    /// ARRAY (24 bytes) per field with Count > 1, and one STRUCT (12 + 12 per
    /// member) per descriptor.
    let private typeTableSize (descriptors: StructDescriptor array) : int =
        let mutable size = 0
        let mutable i = 0
        while i < Array.length primitiveReprs do
            size <- size + (if isFloat (Array.get primitiveReprs i) then 12 else 16)
            i <- i + 1
        size <- size + 16 + 12
        let mutable d = 0
        while d < Array.length descriptors do
            let fields = (Array.get descriptors d).Layout.Fields
            let mutable f = 0
            while f < Array.length fields do
                if (Array.get fields f).Count > 1 then
                    size <- size + 24
                f <- f + 1
            size <- size + 12 + 12 * Array.length fields
            d <- d + 1
        size

    /// Emit a raw BTF blob describing the structs under the ABI profile.
    /// Member offsets are the descriptors' declared byte offsets times eight;
    /// struct sizes are the declared sizes. Validate the descriptors against
    /// the ABI first (`Validator.validate`); the emitter serializes what it is
    /// given.
    let emit (abi: AbiProfile) (descriptors: StructDescriptor array) : byte array =
        let typesLen = typeTableSize descriptors
        // string table upper bound: the leading NUL, every primitive name, the
        // index type name, then every struct and field name, each plus a NUL
        let mutable strBound = 1 + 1 + Array.length (Text.toUtf8 ArrayIndexTypeName)
        let mutable r = 0
        while r < Array.length primitiveReprs do
            strBound <- strBound + Array.length (Text.toUtf8 (Array.get primitiveReprs r)) + 1
            r <- r + 1
        let mutable d = 0
        while d < Array.length descriptors do
            let desc = Array.get descriptors d
            strBound <- strBound + Array.length (Text.toUtf8 desc.Name) + 1
            let fields = desc.Layout.Fields
            let mutable f = 0
            while f < Array.length fields do
                strBound <- strBound + Array.length (Text.toUtf8 (Array.get fields f).Name) + 1
                f <- f + 1
            d <- d + 1
        let strings : byte array = Array.zeroCreate strBound
        let mutable used = 1
        let types : byte array = Array.zeroCreate typesLen
        let mutable pos = 0
        // primitives
        let mutable p = 0
        while p < Array.length primitiveReprs do
            let repr = Array.get primitiveReprs p
            let size = Abi.reprSize abi repr
            let nameOff, used2 = internName strings used repr
            used <- used2
            if isFloat repr then
                pos <- Encoder.writeU32 types pos (uint32 nameOff)
                pos <- Encoder.writeU32 types pos (info KindFloat 0)
                pos <- Encoder.writeU32 types pos (uint32 size)
            else
                let encoding = if repr = Repr.Bool then IntBool elif isSigned repr then IntSigned else 0
                pos <- Encoder.writeU32 types pos (uint32 nameOff)
                pos <- Encoder.writeU32 types pos (info KindInt 0)
                pos <- Encoder.writeU32 types pos (uint32 size)
                pos <- Encoder.writeU32 types pos ((uint32 encoding <<< 24) ||| uint32 (size * 8))
            p <- p + 1
        // array index type (id 12): an unsigned int of pointer width
        let idxOff, used3 = internName strings used ArrayIndexTypeName
        used <- used3
        pos <- Encoder.writeU32 types pos (uint32 idxOff)
        pos <- Encoder.writeU32 types pos (info KindInt 0)
        pos <- Encoder.writeU32 types pos (uint32 abi.PointerSize)
        pos <- Encoder.writeU32 types pos (uint32 (abi.PointerSize * 8))
        // void pointer (id 13)
        pos <- Encoder.writeU32 types pos (uint32 0)
        pos <- Encoder.writeU32 types pos (info KindPointer 0)
        pos <- Encoder.writeU32 types pos (uint32 0)
        // arrays, one per counted field, ids from 14 upward, in descriptor order
        let mutable nextId = FirstArrayId
        d <- 0
        while d < Array.length descriptors do
            let fields = (Array.get descriptors d).Layout.Fields
            let mutable f = 0
            while f < Array.length fields do
                let field = Array.get fields f
                if field.Count > 1 then
                    pos <- Encoder.writeU32 types pos (uint32 0)
                    pos <- Encoder.writeU32 types pos (info KindArray 0)
                    pos <- Encoder.writeU32 types pos (uint32 0)
                    pos <- Encoder.writeU32 types pos (uint32 (primitiveId field.Repr))
                    pos <- Encoder.writeU32 types pos (uint32 IndexTypeId)
                    pos <- Encoder.writeU32 types pos (uint32 field.Count)
                    nextId <- nextId + 1
                f <- f + 1
            d <- d + 1
        // structs: member types refer to the primitives or to the arrays in the same order
        let mutable arrayId = FirstArrayId
        d <- 0
        while d < Array.length descriptors do
            let desc = Array.get descriptors d
            let fields = desc.Layout.Fields
            let nameOff, used4 = internName strings used desc.Name
            used <- used4
            pos <- Encoder.writeU32 types pos (uint32 nameOff)
            pos <- Encoder.writeU32 types pos (info KindStruct (Array.length fields))
            pos <- Encoder.writeU32 types pos (uint32 desc.Layout.Size)
            let mutable f = 0
            while f < Array.length fields do
                let field = Array.get fields f
                let memberOff, used5 = internName strings used field.Name
                used <- used5
                let typeId = if field.Count > 1 then arrayId else primitiveId field.Repr
                if field.Count > 1 then
                    arrayId <- arrayId + 1
                pos <- Encoder.writeU32 types pos (uint32 memberOff)
                pos <- Encoder.writeU32 types pos (uint32 typeId)
                pos <- Encoder.writeU32 types pos (uint32 (field.Offset * 8))
                f <- f + 1
            d <- d + 1
        // assemble: header, types, strings
        let total = HeaderSize + typesLen + used
        let blob : byte array = Array.zeroCreate total
        let mutable o = Encoder.writeU16 blob 0 (uint16 Magic)
        o <- Encoder.writeU8 blob o 1uy
        o <- Encoder.writeU8 blob o 0uy
        o <- Encoder.writeU32 blob o (uint32 HeaderSize)
        o <- Encoder.writeU32 blob o (uint32 0)
        o <- Encoder.writeU32 blob o (uint32 typesLen)
        o <- Encoder.writeU32 blob o (uint32 typesLen)
        o <- Encoder.writeU32 blob o (uint32 used)
        o <- Encoder.writeBytesRaw blob o types
        let _ = Encoder.writeBytesRaw blob o (Array.sub strings 0 used)
        blob

    // ---- reading ----------------------------------------------------------

    /// The NUL-terminated string at an offset in the string table.
    let private stringAt (strings: byte array) (offset: int) : string =
        let n = Array.length strings
        let mutable e = offset
        while e < n && Array.get strings e <> 0uy do
            e <- e + 1
        if offset < 0 || offset >= n then "" else Text.ofUtf8 (Array.sub strings offset (e - offset))

    let private emptyType : BtfType =
        { Id = 0; Kind = 0; Name = ""; SizeOrType = 0; MemberNames = Array.zeroCreate 0; MemberBitOffsets = Array.zeroCreate 0; MemberTypes = Array.zeroCreate 0 }

    /// Parse a raw BTF blob into its types. Kinds this emitter does not write
    /// are skipped by their documented sizes where known; an unknown kind or a
    /// truncated entry ends the parse with `Ok = false`.
    let read (blob: byte array) : BtfImage =
        let magic, o1 = Decoder.readU16 blob 0
        let version, o2 = Decoder.readU8 blob o1
        let _flags, o3 = Decoder.readU8 blob o2
        let hdrLen, o4 = Decoder.readU32 blob o3
        let typeOff, o5 = Decoder.readU32 blob o4
        let typeLen, o6 = Decoder.readU32 blob o5
        let strOff, o7 = Decoder.readU32 blob o6
        let strLen, o8 = Decoder.readU32 blob o7
        let headerOk = Cursor.isOk o8 && int magic = Magic && version = 1uy && hdrLen = uint32 HeaderSize
        let typesStart = int hdrLen + int typeOff
        let typesEnd = typesStart + int typeLen
        let strStart = int hdrLen + int strOff
        let boundsOk = headerOk && Cursor.fits blob typesStart (int typeLen) && Cursor.fits blob strStart (int strLen)
        if not boundsOk then { Ok = false; Types = Array.zeroCreate 0 }
        else
            let strings = Array.sub blob strStart (int strLen)
            // count entries first
            let mutable count = 0
            let mutable pos = typesStart
            let mutable ok = true
            while ok && pos < typesEnd do
                let _nameOff, p1 = Decoder.readU32 blob pos
                let infoV, p2 = Decoder.readU32 blob p1
                let _sz, p3 = Decoder.readU32 blob p2
                let kind = int ((infoV >>> 24) &&& uint32 31)
                let vlen = int (infoV &&& uint32 65535)
                let extra =
                    if kind = KindInt then 4
                    elif kind = KindArray then 12
                    elif kind = KindStruct then 12 * vlen
                    elif kind = KindPointer || kind = KindFloat then 0
                    else -1
                ok <- Cursor.isOk p3 && extra >= 0 && Cursor.fits blob p3 extra
                if ok then
                    count <- count + 1
                    pos <- p3 + extra
            if not ok then { Ok = false; Types = Array.zeroCreate 0 }
            else
                let types : BtfType array = Array.zeroCreate count
                let mutable id = 0
                pos <- typesStart
                while id < count do
                    let nameOff, p1 = Decoder.readU32 blob pos
                    let infoV, p2 = Decoder.readU32 blob p1
                    let sizeOrType, p3 = Decoder.readU32 blob p2
                    let kind = int ((infoV >>> 24) &&& uint32 31)
                    let vlen = int (infoV &&& uint32 65535)
                    let memberCount = if kind = KindStruct then vlen else 0
                    let names : string array = Array.zeroCreate memberCount
                    let offsets : int array = Array.zeroCreate memberCount
                    let mtypes : int array = Array.zeroCreate memberCount
                    let mutable m = 0
                    let mutable q = p3
                    while m < memberCount do
                        let mn, q1 = Decoder.readU32 blob q
                        let mt, q2 = Decoder.readU32 blob q1
                        let mo, q3 = Decoder.readU32 blob q2
                        Array.set names m (stringAt strings (int mn))
                        Array.set mtypes m (int mt)
                        Array.set offsets m (int mo)
                        q <- q3
                        m <- m + 1
                    let extra = if kind = KindInt then 4 elif kind = KindArray then 12 else 12 * memberCount
                    Array.set types id { emptyType with Id = id + 1; Kind = kind; Name = stringAt strings (int nameOff); SizeOrType = int sizeOrType; MemberNames = names; MemberBitOffsets = offsets; MemberTypes = mtypes }
                    pos <- p3 + extra
                    id <- id + 1
                { Ok = true; Types = types }

    /// The struct type with the given name in a parsed image, when present.
    let tryStruct (image: BtfImage) (name: string) : BtfType option =
        let n = Array.length image.Types
        let mutable i = 0
        let mutable found = -1
        while i < n && found < 0 do
            let t = Array.get image.Types i
            if t.Kind = KindStruct && t.Name = name then
                found <- i
            i <- i + 1
        if found < 0 then None else Some (Array.get image.Types found)
