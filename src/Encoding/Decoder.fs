namespace BAREWire.Encoding

/// BARE decoding primitives over a bounded byte extent.
///
/// Every function takes `(data, offset)` and returns `(value, nextOffset)`.
/// A read that would leave the extent, a malformed encoding (a bool byte other
/// than 0 or 1, an optional tag other than 0 or 1, a varint longer than ten
/// bytes), or a fault offset in, yields the type's zero value and
/// `Cursor.Fault`. Decoding untrusted bytes therefore never reads out of
/// bounds and never raises; the caller checks the final offset once.
module Decoder =

    /// Read one byte.
    let readU8 (data: byte array) (offset: int) : byte * int =
        let ok = Cursor.fits data offset 1
        let v = if ok then Array.get data offset else 0uy
        (v, (if ok then offset + 1 else Cursor.Fault))

    /// Read `count` raw bytes with no prefix.
    let readBytesRaw (data: byte array) (offset: int) (count: int) : byte array * int =
        let ok = Cursor.fits data offset count
        let bytes = if ok then Array.sub data offset count else Array.zeroCreate 0
        (bytes, (if ok then offset + count else Cursor.Fault))

    /// Read a uint16, little-endian.
    let readU16 (data: byte array) (offset: int) : uint16 * int =
        let ok = Cursor.fits data offset 2
        let b0 = if ok then uint16 (Array.get data offset) else 0us
        let b1 = if ok then uint16 (Array.get data (offset + 1)) else 0us
        (b0 ||| (b1 <<< 8), (if ok then offset + 2 else Cursor.Fault))

    /// Read a uint32, little-endian.
    let readU32 (data: byte array) (offset: int) : uint32 * int =
        let ok = Cursor.fits data offset 4
        let b0 = if ok then uint32 (Array.get data offset) else uint32 0
        let b1 = if ok then uint32 (Array.get data (offset + 1)) else uint32 0
        let b2 = if ok then uint32 (Array.get data (offset + 2)) else uint32 0
        let b3 = if ok then uint32 (Array.get data (offset + 3)) else uint32 0
        (b0 ||| (b1 <<< 8) ||| (b2 <<< 16) ||| (b3 <<< 24), (if ok then offset + 4 else Cursor.Fault))

    /// Read a uint64, little-endian.
    let readU64 (data: byte array) (offset: int) : uint64 * int =
        let lo, o1 = readU32 data offset
        let hi, o2 = readU32 data o1
        ((uint64 lo) ||| ((uint64 hi) <<< 32), o2)

    /// Read an int8.
    let readI8 (data: byte array) (offset: int) : sbyte * int =
        let v, next = readU8 data offset
        (sbyte v, next)

    /// Read an int16, little-endian two's complement.
    let readI16 (data: byte array) (offset: int) : int16 * int =
        let v, next = readU16 data offset
        (int16 v, next)

    /// Read an int32, little-endian two's complement.
    let readI32 (data: byte array) (offset: int) : int32 * int =
        let v, next = readU32 data offset
        (int32 v, next)

    /// Read an int64, little-endian two's complement.
    let readI64 (data: byte array) (offset: int) : int64 * int =
        let v, next = readU64 data offset
        (int64 v, next)

    /// Read an IEEE-754 binary32, little-endian.
    let readF32 (data: byte array) (offset: int) : float32 * int =
        let bits, next = readI32 data offset
        (Float.bitsToF32 bits, next)

    /// Read an IEEE-754 binary64, little-endian.
    let readF64 (data: byte array) (offset: int) : float * int =
        let bits, next = readI64 data offset
        (Float.bitsToF64 bits, next)

    /// Read an unsigned varint (ULEB128). More than ten bytes is a fault.
    let readUInt (data: byte array) (offset: int) : uint64 * int =
        let mutable result = 0UL
        let mutable shift = 0
        let mutable pos = offset
        let mutable fin = Cursor.isFault offset
        while not fin do
            let ok = Cursor.fits data pos 1
            let b = if ok then Array.get data pos else 0uy
            let more = ok && (b &&& 128uy) <> 0uy
            // The tenth byte may carry only bit 0: a continuation bit or any
            // of bits 1..6 encodes a value beyond 64 bits.
            let overflow = shift >= 63 && (more || (b &&& 126uy) <> 0uy)
            result <- result ||| ((uint64 (b &&& 127uy)) <<< shift)
            shift <- shift + 7
            pos <- (if ok && not overflow then pos + 1 else Cursor.Fault)
            fin <- (not more) || overflow
        (result, pos)

    /// Read a signed varint: ULEB128, then inverse zigzag.
    let readInt (data: byte array) (offset: int) : int64 * int =
        let u, next = readUInt data offset
        ((int64 (u >>> 1)) ^^^ (0L - (int64 (u &&& 1UL))), next)

    /// Read a bool. A byte other than 0 or 1 is a fault.
    let readBool (data: byte array) (offset: int) : bool * int =
        let b, next = readU8 data offset
        let valid = b = 0uy || b = 1uy
        (b = 1uy, (if valid then next else Cursor.Fault))

    /// Read length-prefixed bytes.
    let readData (data: byte array) (offset: int) : byte array * int =
        let len64, next = readUInt data offset
        let ok = Cursor.isOk next && len64 <= uint64 (Cursor.remaining data next)
        if ok then readBytesRaw data next (int len64) else (Array.zeroCreate 0, Cursor.Fault)

    /// Read fixed-length bytes with no prefix.
    let readFixedData (data: byte array) (offset: int) (length: int) : byte array * int =
        readBytesRaw data offset length

    /// Read a length-prefixed UTF-8 string.
    let readString (data: byte array) (offset: int) : string * int =
        let bytes, next = readData data offset
        (Text.ofUtf8 bytes, next)

    /// Read a union case tag. A tag that does not fit an int is a fault.
    let readTag (data: byte array) (offset: int) : int * int =
        let u, next = readUInt data offset
        let ok = Cursor.isOk next && u <= 2147483647UL
        ((if ok then int u else 0), (if ok then next else Cursor.Fault))

    /// Read an optional value. A tag byte other than 0 or 1 is a fault.
    let readOption (data: byte array) (offset: int) (read: byte array -> int -> 'a * int) : 'a option * int =
        let tag, next = readU8 data offset
        if Cursor.isFault next then (None, Cursor.Fault)
        elif tag = 0uy then (None, next)
        elif tag = 1uy then
            let v, after = read data next
            ((if Cursor.isOk after then Some v else None), after)
        else (None, Cursor.Fault)

    /// Read a fixed-length list: `length` values, no count.
    let readFixedList (data: byte array) (offset: int) (length: int) (read: byte array -> int -> 'a * int) : 'a array * int =
        let n = if length < 0 then 0 else length
        let items : 'a array = Array.zeroCreate n
        let mutable pos = if length < 0 then Cursor.Fault else offset
        let mutable i = 0
        while i < n && Cursor.isOk pos do
            let v, next = read data pos
            if Cursor.isOk next then
                Array.set items i v
            pos <- next
            i <- i + 1
        (items, pos)

    /// Read a list: uint count, then values. A count larger than the
    /// remaining bytes is a fault before anything is allocated.
    let readList (data: byte array) (offset: int) (read: byte array -> int -> 'a * int) : 'a array * int =
        let count64, next = readUInt data offset
        let ok = Cursor.isOk next && count64 <= uint64 (Cursor.remaining data next)
        if ok then readFixedList data next (int count64) read else (Array.zeroCreate 0, Cursor.Fault)
