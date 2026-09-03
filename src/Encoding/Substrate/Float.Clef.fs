namespace BAREWire.Encoding

/// IEEE-754 bit casts on the Clef substrate: the `Bits` intrinsics.
/// Selected by `BAREWire.fidproj`.
///
/// The intrinsics are typed over the platform word for the 32-bit casts
/// (`float32 -> int`, `int -> float32`) and over `int64` for the 64-bit ones;
/// the conversions at the edge keep this module's surface at the wire widths.
module Float =

    let f32ToBits (v: float32) : int32 = int32 (Bits.float32ToInt32Bits v)
    let bitsToF32 (bits: int32) : float32 = Bits.int32BitsToFloat32 (int bits)
    let f64ToBits (v: float) : int64 = Bits.float64ToInt64Bits v
    let bitsToF64 (bits: int64) : float = Bits.int64BitsToFloat64 bits
