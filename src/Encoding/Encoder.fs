namespace BAREWire.Encoding

/// BARE encoding primitives over a bounded byte extent.
///
/// Every function takes `(data, offset, value)` and returns the next offset,
/// or `Cursor.Fault` when the value does not fit. A fault offset in propagates
/// as a fault offset out, so a codec is written straight through and checked
/// once. Nothing here allocates except `writeString`, which asks the
/// substrate's `Text` shim for the UTF-8 bytes (an identity view on Clef).
///
/// Wire format (BARE, https://baremessages.org):
///   uint/int  ULEB128, int zigzag-mapped first
///   u8..u64, i8..i64, f32, f64  fixed width, little-endian
///   bool      one byte, 0 or 1
///   string/data  uint length prefix, then bytes
///   data[n]   n bytes, no prefix
///   optional  one byte tag (0 absent, 1 present), then the value
///   list      uint count, then values;  list[n]  n values, no count
///   union     uint case tag, then the case value
module Encoder =

    /// Write one byte.
    let writeU8 (data: byte array) (offset: int) (v: byte) : int =
        let ok = Cursor.fits data offset 1
        if ok then
            Array.set data offset v
        if ok then offset + 1 else Cursor.Fault

    /// Write raw bytes with no prefix.
    let writeBytesRaw (data: byte array) (offset: int) (bytes: byte array) : int =
        let n = Array.length bytes
        let ok = Cursor.fits data offset n
        let mutable i = 0
        while ok && i < n do
            Array.set data (offset + i) (Array.get bytes i)
            i <- i + 1
        if ok then offset + n else Cursor.Fault

    /// Write a uint16, little-endian.
    let writeU16 (data: byte array) (offset: int) (v: uint16) : int =
        let ok = Cursor.fits data offset 2
        if ok then
            Array.set data offset (byte (v &&& 0xFFus))
            Array.set data (offset + 1) (byte ((v >>> 8) &&& 0xFFus))
        if ok then offset + 2 else Cursor.Fault

    /// Write a uint32, little-endian.
    let writeU32 (data: byte array) (offset: int) (v: uint32) : int =
        let ok = Cursor.fits data offset 4
        if ok then
            Array.set data offset (byte (v &&& uint32 255))
            Array.set data (offset + 1) (byte ((v >>> 8) &&& uint32 255))
            Array.set data (offset + 2) (byte ((v >>> 16) &&& uint32 255))
            Array.set data (offset + 3) (byte ((v >>> 24) &&& uint32 255))
        if ok then offset + 4 else Cursor.Fault

    /// Write a uint64, little-endian.
    let writeU64 (data: byte array) (offset: int) (v: uint64) : int =
        let lo = writeU32 data offset (uint32 (v &&& 0xFFFFFFFFUL))
        writeU32 data lo (uint32 (v >>> 32))

    /// Write an int8 as its two's-complement byte.
    let writeI8 (data: byte array) (offset: int) (v: sbyte) : int =
        writeU8 data offset (byte v)

    /// Write an int16, little-endian two's complement.
    let writeI16 (data: byte array) (offset: int) (v: int16) : int =
        writeU16 data offset (uint16 v)

    /// Write an int32, little-endian two's complement.
    let writeI32 (data: byte array) (offset: int) (v: int32) : int =
        writeU32 data offset (uint32 v)

    /// Write an int64, little-endian two's complement.
    let writeI64 (data: byte array) (offset: int) (v: int64) : int =
        writeU64 data offset (uint64 v)

    /// Write an IEEE-754 binary32, little-endian.
    let writeF32 (data: byte array) (offset: int) (v: float32) : int =
        writeI32 data offset (Float.f32ToBits v)

    /// Write an IEEE-754 binary64, little-endian.
    let writeF64 (data: byte array) (offset: int) (v: float) : int =
        writeI64 data offset (Float.f64ToBits v)

    /// Write an unsigned varint (ULEB128), at most ten bytes.
    let writeUInt (data: byte array) (offset: int) (value: uint64) : int =
        let mutable v = value
        let mutable pos = offset
        while v >= 128UL && Cursor.isOk pos do
            pos <- writeU8 data pos (byte ((v &&& 127UL) ||| 128UL))
            v <- v >>> 7
        writeU8 data pos (byte v)

    /// Write a signed varint: zigzag map, then ULEB128.
    let writeInt (data: byte array) (offset: int) (value: int64) : int =
        let zz = (uint64 (value <<< 1)) ^^^ (uint64 (value >>> 63))
        writeUInt data offset zz

    /// Write a bool as 1 or 0.
    let writeBool (data: byte array) (offset: int) (v: bool) : int =
        writeU8 data offset (if v then 1uy else 0uy)

    /// Write length-prefixed bytes.
    let writeData (data: byte array) (offset: int) (bytes: byte array) : int =
        let next = writeUInt data offset (uint64 (Array.length bytes))
        writeBytesRaw data next bytes

    /// Write fixed-length bytes with no prefix. Faults if the length disagrees.
    let writeFixedData (data: byte array) (offset: int) (bytes: byte array) (length: int) : int =
        if Array.length bytes = length then writeBytesRaw data offset bytes else Cursor.Fault

    /// Write a length-prefixed UTF-8 string.
    let writeString (data: byte array) (offset: int) (s: string) : int =
        writeData data offset (Text.toUtf8 s)

    /// Write a union case tag (the case index) as a uint.
    let writeTag (data: byte array) (offset: int) (tag: int) : int =
        if tag < 0 then Cursor.Fault else writeUInt data offset (uint64 tag)

    /// Write an optional value: tag byte, then the value when present.
    let writeOption (data: byte array) (offset: int) (write: byte array -> int -> 'a -> int) (v: 'a option) : int =
        match v with
        | None -> writeU8 data offset 0uy
        | Some x ->
            let next = writeU8 data offset 1uy
            write data next x

    /// Write a list: uint count, then each value.
    let writeList (data: byte array) (offset: int) (write: byte array -> int -> 'a -> int) (items: 'a array) : int =
        let n = Array.length items
        let mutable pos = writeUInt data offset (uint64 n)
        let mutable i = 0
        while i < n && Cursor.isOk pos do
            pos <- write data pos (Array.get items i)
            i <- i + 1
        pos

    /// Write a fixed-length list: each value, no count. Faults if the length disagrees.
    let writeFixedList (data: byte array) (offset: int) (write: byte array -> int -> 'a -> int) (items: 'a array) (length: int) : int =
        let n = Array.length items
        let mutable pos = if n = length then offset else Cursor.Fault
        let mutable i = 0
        while i < n && Cursor.isOk pos do
            pos <- write data pos (Array.get items i)
            i <- i + 1
        pos
