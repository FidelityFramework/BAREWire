# Native callback descriptors

`src/Descriptors/Bindings.fs` supplies two complementary declarations for typed native callbacks. Both are quoted module-level data read by Clef Compiler Services; neither occupies a runtime callback slot.

`CallbackDescriptor` names a listener `Record` and `Field` and declares its full native `FunctionDescriptor` signature. The listener field is a typed `FnPtr`. Instance and userdata arguments, scalar widths, pointer dimensions and the C return convention belong to this complete native signature.

`ClosedCallbackDescriptor` adds a binding-owned adapter for the implemented closed-handler subset:

```fsharp
type ClosedCallbackDescriptor = {
    Binding: string
    Adapter: string
    Record: string
    Field: string
}
```

`Binding` names an immutable module factory taking one ordinary Clef function and returning the native one-field listener record. Its body must be exactly `NativeDefault.zeroed ()`, allowing type annotations; effectful factory bodies are rejected. `Adapter` names an immutable module function taking that same handler first, followed by all native entry arguments. Both names are fully qualified literal strings. `Record` and `Field` select the existing `CallbackDescriptor`; the closed declaration does not replace or weaken its ABI contract.

CCS accepts a known closed application module handler, specializes the adapter for that identity, and supplies the normal typed `FnPtr.ofFunction` listener entry. Bindings can thus own instance/userdata handling and string copying and release while exposing only Clef payload values to applications. A saturated inline public wrapper makes the closed handler identity available at the factory call. Local/captured handlers and mutable template declarations are rejected.

This static closed-entry specialization needs no retained closure environment. It is not the future general closure-to-native trampoline mechanism: captured callbacks still need a typed environment and its allocation, lifetime and release contract. Binding payload ownership remains explicit in either case.

The compiler implementation and its restrictions are documented in [Clef's closed adapter contract](https://github.com/FidelityFramework/clef/blob/main/docs/fidelity/Closed_Native_Callback_Adapters.md).
