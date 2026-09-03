namespace BAREWire.Memory

open BAREWire.Encoding
open BAREWire.Hardware

/// A typed view of a region through a declared layout (docs/04 "Memory
/// Views"). Fields are addressed by name; a read or write goes to
/// `Region.absolute view.Region field.Offset`, little-endian, through the
/// Encoding tier, and is refused when the field is missing, its
/// representation disagrees with the accessor, or the bytes fall outside
/// the region.
type View = {
    Region: Region
    Layout: PeripheralLayout
}

/// View construction and field access.
module View =

    /// A view of a region through a layout, when the region is long enough for it.
    let create (region: Region) (layout: PeripheralLayout) : View option =
        if layout.Size >= 0 && layout.Size <= region.Length then
            Some { Region = region; Layout = layout }
        else None

    /// The field with the given name, when the layout declares one.
    let tryField (view: View) (name: string) : FieldDescriptor option =
        Layout.tryField view.Layout name

    /// The absolute offset of a named field whose representation is `repr`
    /// and whose `width` bytes lie inside the region and the backing array,
    /// or `Cursor.Fault`.
    let private locate (view: View) (name: string) (repr: Repr) (width: int) : int =
        let found = tryField view name
        match found with
        | None -> Cursor.Fault
        | Some f ->
            let at = Region.absolute view.Region f.Offset
            let ok = f.Repr = repr && Region.contains view.Region f.Offset width && Cursor.fits view.Region.Data at width
            if ok then at else Cursor.Fault

    /// Read a u8 field.
    let readU8 (view: View) (name: string) : byte option =
        let at = locate view name Repr.U8 1
        if Cursor.isFault at then None
        else
            let v, next = Decoder.readU8 view.Region.Data at
            if Cursor.isOk next then Some v else None

    /// Read an i8 field.
    let readI8 (view: View) (name: string) : sbyte option =
        let at = locate view name Repr.I8 1
        if Cursor.isFault at then None
        else
            let v, next = Decoder.readI8 view.Region.Data at
            if Cursor.isOk next then Some v else None

    /// Read an i16 field, little-endian.
    let readI16 (view: View) (name: string) : int16 option =
        let at = locate view name Repr.I16 2
        if Cursor.isFault at then None
        else
            let v, next = Decoder.readI16 view.Region.Data at
            if Cursor.isOk next then Some v else None

    /// Read a bool field: one byte, 0 or 1; another value is not a bool.
    let readBool (view: View) (name: string) : bool option =
        let at = locate view name Repr.Bool 1
        if Cursor.isFault at then None
        else
            let v, next = Decoder.readBool view.Region.Data at
            if Cursor.isOk next then Some v else None

    /// Read a u16 field, little-endian.
    let readU16 (view: View) (name: string) : uint16 option =
        let at = locate view name Repr.U16 2
        if Cursor.isFault at then None
        else
            let v, next = Decoder.readU16 view.Region.Data at
            if Cursor.isOk next then Some v else None

    /// Read a u32 field, little-endian.
    let readU32 (view: View) (name: string) : uint32 option =
        let at = locate view name Repr.U32 4
        if Cursor.isFault at then None
        else
            let v, next = Decoder.readU32 view.Region.Data at
            if Cursor.isOk next then Some v else None

    /// Read a u64 field, little-endian.
    let readU64 (view: View) (name: string) : uint64 option =
        let at = locate view name Repr.U64 8
        if Cursor.isFault at then None
        else
            let v, next = Decoder.readU64 view.Region.Data at
            if Cursor.isOk next then Some v else None

    /// Read an i32 field, little-endian two's complement.
    let readI32 (view: View) (name: string) : int32 option =
        let at = locate view name Repr.I32 4
        if Cursor.isFault at then None
        else
            let v, next = Decoder.readI32 view.Region.Data at
            if Cursor.isOk next then Some v else None

    /// Read an i64 field, little-endian two's complement.
    let readI64 (view: View) (name: string) : int64 option =
        let at = locate view name Repr.I64 8
        if Cursor.isFault at then None
        else
            let v, next = Decoder.readI64 view.Region.Data at
            if Cursor.isOk next then Some v else None

    /// Read an f32 field, IEEE-754 binary32 little-endian.
    let readF32 (view: View) (name: string) : float32 option =
        let at = locate view name Repr.F32 4
        if Cursor.isFault at then None
        else
            let v, next = Decoder.readF32 view.Region.Data at
            if Cursor.isOk next then Some v else None

    /// Read an f64 field, IEEE-754 binary64 little-endian.
    let readF64 (view: View) (name: string) : float option =
        let at = locate view name Repr.F64 8
        if Cursor.isFault at then None
        else
            let v, next = Decoder.readF64 view.Region.Data at
            if Cursor.isOk next then Some v else None

    /// Write a u8 field. False when the field is missing, disagrees, or does not fit.
    let writeU8 (view: View) (name: string) (v: byte) : bool =
        let at = locate view name Repr.U8 1
        if Cursor.isFault at then false
        else Cursor.isOk (Encoder.writeU8 view.Region.Data at v)

    /// Write a u16 field, little-endian.
    /// Write an i8 field.
    let writeI8 (view: View) (name: string) (v: sbyte) : bool =
        let at = locate view name Repr.I8 1
        Cursor.isOk at && Cursor.isOk (Encoder.writeI8 view.Region.Data at v)

    /// Write an i16 field, little-endian.
    let writeI16 (view: View) (name: string) (v: int16) : bool =
        let at = locate view name Repr.I16 2
        Cursor.isOk at && Cursor.isOk (Encoder.writeI16 view.Region.Data at v)

    /// Write a bool field as 1 or 0.
    let writeBool (view: View) (name: string) (v: bool) : bool =
        let at = locate view name Repr.Bool 1
        Cursor.isOk at && Cursor.isOk (Encoder.writeBool view.Region.Data at v)

    let writeU16 (view: View) (name: string) (v: uint16) : bool =
        let at = locate view name Repr.U16 2
        if Cursor.isFault at then false
        else Cursor.isOk (Encoder.writeU16 view.Region.Data at v)

    /// Write a u32 field, little-endian.
    let writeU32 (view: View) (name: string) (v: uint32) : bool =
        let at = locate view name Repr.U32 4
        if Cursor.isFault at then false
        else Cursor.isOk (Encoder.writeU32 view.Region.Data at v)

    /// Write a u64 field, little-endian.
    let writeU64 (view: View) (name: string) (v: uint64) : bool =
        let at = locate view name Repr.U64 8
        if Cursor.isFault at then false
        else Cursor.isOk (Encoder.writeU64 view.Region.Data at v)

    /// Write an i32 field, little-endian two's complement.
    let writeI32 (view: View) (name: string) (v: int32) : bool =
        let at = locate view name Repr.I32 4
        if Cursor.isFault at then false
        else Cursor.isOk (Encoder.writeI32 view.Region.Data at v)

    /// Write an i64 field, little-endian two's complement.
    let writeI64 (view: View) (name: string) (v: int64) : bool =
        let at = locate view name Repr.I64 8
        if Cursor.isFault at then false
        else Cursor.isOk (Encoder.writeI64 view.Region.Data at v)

    /// Write an f32 field, IEEE-754 binary32 little-endian.
    let writeF32 (view: View) (name: string) (v: float32) : bool =
        let at = locate view name Repr.F32 4
        if Cursor.isFault at then false
        else Cursor.isOk (Encoder.writeF32 view.Region.Data at v)

    /// Write an f64 field, IEEE-754 binary64 little-endian.
    let writeF64 (view: View) (name: string) (v: float) : bool =
        let at = locate view name Repr.F64 8
        if Cursor.isFault at then false
        else Cursor.isOk (Encoder.writeF64 view.Region.Data at v)
