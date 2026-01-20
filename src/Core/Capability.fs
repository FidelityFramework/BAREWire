namespace BAREWire.Core

// NOTE: Capability types simplified for FNCS/NTU.
// - Arena is an FNCS intrinsic (not defined here)
// - Lifetime/access/region tracking via measure parameters removed
// - FNCS provides native pointer safety via nativeptr<'T>
//
// The BCL-era capability scaffolding is obsolete. FNCS provides:
// - Arena<'lifetime> as intrinsic
// - nativeptr<'T> for typed pointers
// - Compile-time lifetime tracking via the type system

/// Simple capability for memory regions (no measure parameters)
/// For cases where you need to track pointer + length together
[<Struct>]
type MemorySpan = {
    /// The base address
    Pointer: nativeint

    /// The length in elements
    Length: int
}

/// Operations on memory spans
module MemorySpan =
    /// Creates a span from pointer and length
    let inline create (ptr: nativeint) (len: int) : MemorySpan =
        { Pointer = ptr; Length = len }

    /// Gets the length
    let inline length (span: MemorySpan) = span.Length

    /// Checks if empty
    let inline isEmpty (span: MemorySpan) = span.Length = 0
