# WRENStack IPC Patterns

## Context

This memory documents specific IPC communication patterns for WRENStack applications using BAREWire over WebSocket.

## Message Protocol Design

### Request-Response Pattern

```fsharp
// Shared/Protocol.fs
type RequestId = int64

type Request =
    | FetchData of RequestId * path: string
    | SaveData of RequestId * path: string * data: byte[]
    | GetStatus of RequestId

type Response =
    | DataFetched of RequestId * data: byte[] option * error: string option
    | DataSaved of RequestId * success: bool * error: string option
    | StatusResult of RequestId * status: SystemStatus
```

**Key pattern**: Every request carries a `RequestId` that the response echoes back, enabling correlation on the frontend.

### Event Push Pattern

```fsharp
type Event =
    | FileChanged of path: string
    | ConnectionLost
    | ProgressUpdate of operation: string * percent: int
```

Events are server-initiated, no request ID needed.

### Combined Protocol

```fsharp
type Message =
    | Req of Request
    | Res of Response
    | Evt of Event
```

## WebSocket Frame Encoding

BAREWire messages are wrapped in WebSocket binary frames:

```
┌─────────────────────────────────────────────┐
│ WebSocket Binary Frame                      │
├─────────────────────────────────────────────┤
│ [1 byte]  Message type tag (0=Req,1=Res,2=Evt) │
│ [N bytes] BAREWire-encoded payload          │
└─────────────────────────────────────────────┘
```

## Base64 for Text Frames (Alternative)

When binary frames aren't supported (some WebView bridges):

```fsharp
// Encode to Base64 string for text frame
let sendMessage (msg: Message) =
    let bytes = MessageCodec.encode msg
    let base64 = Convert.ToBase64String bytes
    WebSocket.sendText base64

// Decode from Base64
let receiveMessage (text: string) =
    let bytes = Convert.FromBase64String text
    MessageCodec.decode bytes
```

## Codec Generation Pattern

Each message type needs encode/decode functions:

```fsharp
module RequestCodec =
    let encode (buf: Buffer byref) (req: Request) =
        match req with
        | FetchData (id, path) ->
            Buffer.writeU8 &buf 0uy  // Tag
            Buffer.writeI64 &buf id
            StringCodec.encode &buf path
        | SaveData (id, path, data) ->
            Buffer.writeU8 &buf 1uy
            Buffer.writeI64 &buf id
            StringCodec.encode &buf path
            BytesCodec.encode &buf data
        | GetStatus id ->
            Buffer.writeU8 &buf 2uy
            Buffer.writeI64 &buf id

    let decode (bytes: byte[]) (offset: byref<int>) : Request =
        let tag = bytes.[offset]
        offset <- offset + 1
        match tag with
        | 0uy ->
            let id = Int64Codec.decode bytes &offset
            let path = StringCodec.decode bytes &offset
            FetchData (id, path)
        | 1uy ->
            let id = Int64Codec.decode bytes &offset
            let path = StringCodec.decode bytes &offset
            let data = BytesCodec.decode bytes &offset
            SaveData (id, path, data)
        | 2uy ->
            let id = Int64Codec.decode bytes &offset
            GetStatus id
        | _ -> failwith "Unknown Request tag"
```

## Error Handling

Errors are returned in responses, not thrown:

```fsharp
type Response =
    | Success of RequestId * data: 'T
    | Failure of RequestId * errorCode: int * message: string
```

This ensures errors are serializable and the protocol remains robust.

## Related

- `wren_stack_integration` memory
- `/home/hhh/repos/WrenHello/src/Shared/Protocol.fs`
- `/home/hhh/repos/Firefly/docs/WRENStack_Roadmap.md`
