# Arena: Simple Deterministic Allocator

## Purpose

Arena is the "just enough" memory allocator for non-actor Fidelity applications. It solves the stack lifetime problem without requiring full actor infrastructure.

## The Problem It Solves

Functions that allocate on the stack cannot return pointers to that memory - it's freed on return. Example: `Console.readln()` allocates a buffer, reads into it, creates a string pointing to it, returns - but the buffer is now invalid.

## The Solution

Arena provides deterministic bump allocation with caller-controlled lifetime:

```fsharp
// Caller creates arena on their stack
let mem = NativePtr.stackalloc<byte> 4096
let mutable arena = Arena.fromPointer (NativePtr.toNativeInt mem) 4096

// Allocations from arena survive callee returns
let name = Console.readlnFrom &arena  // String data lives in arena
Console.writeln $"Hello, {name}!"     // Still valid!
// Arena freed when this scope exits
```

## Key Design Points

1. **Bump allocation** - O(1), no fragmentation, no individual free
2. **Lifetime via type parameter** - `Arena<'lifetime>` tracks scope
3. **Caller controls scope** - Explicit, predictable, no hidden allocation
4. **Grade step to actors** - Same pattern as actor arenas, simpler scope

## Location

- Type: `BAREWire.Core.Capability.Arena<'lifetime>`
- Module: `BAREWire.Core.Capability.Arena`
- Docs: `BAREWire/docs/Arena_Design.md`

## Operations

```fsharp
Arena.fromPointer : nativeint -> int -> Arena<'lifetime>  // Create from backing memory
Arena.alloc : Arena byref -> int -> nativeint             // Bump allocate bytes
Arena.allocAligned : Arena byref -> int -> int -> nativeint  // With alignment
Arena.remaining : Arena -> int                            // Free space
Arena.reset : Arena byref -> unit                         // Reset (invalidates all)
```

## Relationship to Full Memory Model

| Mechanism | Lifetime | Complexity |
|-----------|----------|------------|
| Stack | Function | Automatic |
| **Arena** | Scope | Simple, explicit |
| Actor Arena | Actor | Full infrastructure |
| Heap | Manual | Explicit free |

Arena is for applications that need memory outliving function calls but don't need actor machinery. When actor infrastructure is built, the pattern transfers directly.

## Usage Note

Arena does NOT allocate backing memory itself - caller must provide via `NativePtr.stackalloc` or future heap intrinsics. This keeps allocation explicit and controllable.
