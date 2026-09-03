namespace BAREWire.Memory

/// A bounded extent of bytes: a backing array, a starting offset into it,
/// and a length (docs/04 "Memory Regions"). Every access through a region
/// is relative to `Offset` and checked against `Length`, so a slice cannot
/// reach outside the region it was cut from. The JavaScript realization is
/// this record over an `ArrayBuffer`-backed array; the native memref
/// realization is the compiler's.
type Region = {
    Data: byte array
    Offset: int
    Length: int
}

/// Region constructors and bounds arithmetic.
module Region =

    /// A region over a whole array.
    let ofArray (data: byte array) : Region =
        { Data = data; Offset = 0; Length = Array.length data }

    /// True when `length` bytes at relative `offset` lie inside the region.
    let contains (region: Region) (offset: int) (length: int) : bool =
        offset >= 0 && length >= 0 && length <= region.Length - offset

    /// A sub-region of `length` bytes at relative `offset`, when it lies inside this one.
    let slice (region: Region) (offset: int) (length: int) : Region option =
        if contains region offset length then
            Some { Data = region.Data; Offset = region.Offset + offset; Length = length }
        else None

    /// The offset into the backing array of a relative offset.
    let absolute (region: Region) (offset: int) : int =
        region.Offset + offset

    /// A copy of the region's bytes.
    let toArray (region: Region) : byte array =
        let n = if region.Length < 0 then 0 else region.Length
        let out : byte array = Array.zeroCreate n
        let mutable i = 0
        while i < n do
            Array.set out i (Array.get region.Data (region.Offset + i))
            i <- i + 1
        out
