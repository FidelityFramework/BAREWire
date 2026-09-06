namespace BAREWire.Encoding

/// Portable number formatting for the text residuals (schema text, the
/// memory-map manifest, obligation statements). Written over bytes and the
/// substrate `Text` shim so it needs no `sprintf` and no platform library.
module Fmt =

    let private digitsOf (magnitude: uint64) (negative: bool) : string =
        let buf : byte array = Array.zeroCreate 21
        let mutable pos = 20
        let mutable v = magnitude
        let mutable fin = false
        while not fin do
            let digit = v % 10UL
            Array.set buf pos (byte (48UL + digit))
            pos <- pos - 1
            v <- v / 10UL
            fin <- v = 0UL
        if negative then
            Array.set buf pos 45uy
            pos <- pos - 1
        let start = pos + 1
        Text.ofUtf8 (Array.sub buf start (21 - start))

    /// Decimal text of an unsigned 64-bit value.
    let ofUInt64 (v: uint64) : string =
        digitsOf v false

    // -(v + 1) is representable even for the least signed value; the final
    // unit is added in the unsigned magnitude domain. Never negate MinValue.
    let private magnitude (v: int64) : uint64 =
        if v < 0L then uint64 (0L - (v + 1L)) + 1UL else uint64 v

    /// Absolute decimal text, including the magnitude of the least signed value.
    let magnitudeText (v: int64) : string =
        digitsOf (magnitude v) false

    /// Decimal text of a signed 64-bit value.
    let ofInt64 (v: int64) : string =
        digitsOf (magnitude v) (v < 0L)

    /// Decimal text of a platform-width int.
    let ofInt (v: int) : string =
        ofInt64 (int64 v)

    /// Lower-case hexadecimal text of a 64-bit value with a `0x` prefix and
    /// no leading zeros (`0x0` for zero).
    let hex64 (v: uint64) : string =
        let buf : byte array = Array.zeroCreate 18
        let mutable pos = 17
        let mutable x = v
        let mutable fin = false
        while not fin do
            let d = int (x &&& 15UL)
            let c = if d < 10 then 48 + d else 87 + d
            Array.set buf pos (byte c)
            pos <- pos - 1
            x <- x >>> 4
            fin <- x = 0UL
        Array.set buf pos 120uy
        Array.set buf (pos - 1) 48uy
        let start = pos - 1
        Text.ofUtf8 (Array.sub buf start (18 - start))

    /// Join parts with a separator.
    let join (separator: string) (parts: string array) : string =
        let mutable acc = ""
        let n = Array.length parts
        let mutable i = 0
        while i < n do
            let part = Array.get parts i
            acc <- (if i = 0 then part else Text.append (Text.append acc separator) part)
            i <- i + 1
        acc
