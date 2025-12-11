namespace BAREWire.Memory

open FSharp.UMX
open BAREWire.Core

/// <summary>
/// Memory mapping types and abstractions.
/// Actual platform implementations are provided by Alex bindings via extern primitives.
/// </summary>
module Mapping =
    /// <summary>
    /// Defines the type of memory mapping
    /// </summary>
    type MappingType =
        /// <summary>Copy-on-write private mapping visible only to the current process</summary>
        | PrivateMapping
        /// <summary>Shared mapping visible to other processes</summary>
        | SharedMapping

    /// <summary>
    /// Defines the access permissions for memory mappings
    /// </summary>
    type AccessType =
        /// <summary>Read-only access to memory</summary>
        | ReadOnly
        /// <summary>Read and write access to memory</summary>
        | ReadWrite

    /// <summary>
    /// Handle for a memory mapping
    /// </summary>
    type MappingHandle = {
        /// <summary>Native handle to the mapping</summary>
        Handle: nativeint

        /// <summary>Base address of the mapped memory</summary>
        Address: nativeint

        /// <summary>Size of the mapped region in bytes</summary>
        Size: int<bytes>

        /// <summary>Type of mapping (private or shared)</summary>
        Type: MappingType

        /// <summary>Access permissions (read-only or read-write)</summary>
        Access: AccessType
    }
