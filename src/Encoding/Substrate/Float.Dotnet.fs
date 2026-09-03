namespace BAREWire.Encoding

#nowarn "9"

open Microsoft.FSharp.NativeInterop

/// IEEE-754 bit casts on .NET, by pointer reinterpretation rather than
/// `System.BitConverter` (docs/99 §3.2). Selected by `BAREWire.fsproj`.
module Float =

    let f32ToBits (v: float32) : int32 =
        let mutable x = v
        NativePtr.read (NativePtr.ofNativeInt<int32> (NativePtr.toNativeInt &&x))

    let bitsToF32 (bits: int32) : float32 =
        let mutable x = bits
        NativePtr.read (NativePtr.ofNativeInt<float32> (NativePtr.toNativeInt &&x))

    let f64ToBits (v: float) : int64 =
        let mutable x = v
        NativePtr.read (NativePtr.ofNativeInt<int64> (NativePtr.toNativeInt &&x))

    let bitsToF64 (bits: int64) : float =
        let mutable x = bits
        NativePtr.read (NativePtr.ofNativeInt<float> (NativePtr.toNativeInt &&x))
