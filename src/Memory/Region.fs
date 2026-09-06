namespace BAREWire.Memory

open BAREWire.Encoding

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

    /// The declared region lies inside its backing extent. Records arriving
    /// at this boundary need this premise established before an access.
    let isValid (region: Region) : bool =
        Cursor.fits region.Data region.Offset region.Length

    /// True when `length` bytes at relative `offset` lie inside the region.
    let contains (region: Region) (offset: int) (length: int) : bool =
        isValid region && offset >= 0 && length >= 0 && offset <= region.Length
        && length <= region.Length - offset

    /// A sub-region of `length` bytes at relative `offset`, when it lies inside this one.
    let slice (region: Region) (offset: int) (length: int) : Region option =
        if contains region offset length then
            Some { Data = region.Data; Offset = region.Offset + offset; Length = length }
        else None

    /// The absolute offset, or Cursor.Fault for an invalid region or position.
    let absolute (region: Region) (offset: int) : int =
        if contains region offset 0 then region.Offset + offset else Cursor.Fault

    /// A copy of a valid region's bytes; None for an invalid declaration.
    /// An invalid length must not silently become an empty payload.
    let tryToArray (region: Region) : byte array option =
        if isValid region then Some (Array.sub region.Data region.Offset region.Length)
        else None
