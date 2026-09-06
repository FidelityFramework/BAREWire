namespace BAREWire.Platform

open BAREWire.Encoding

/// Exact finite bounds in the description vocabulary: an optional sign,
/// decimal digits, and an optional fractional part. Comparing the digits
/// avoids a host-sized integer or floating-point approximation of a fact
/// that declaration, selection and proof must all read identically.
module DecimalText =

    type private Parts = {
        Negative: bool
        Start: int
        Point: int
        End: int
    }

    let private digit (c: char) : bool = c >= '0' && c <= '9'

    let private parse (s: string) : Parts option =
        let n = String.length s
        let mutable i = 0
        let mutable negative = false
        if i < n && (Text.charAt s i = '-' || Text.charAt s i = '+') then
            negative <- Text.charAt s i = '-'
            i <- i + 1
        let first = i
        let mutable nonzero = false
        while i < n && digit (Text.charAt s i) do
            if Text.charAt s i <> '0' then nonzero <- true
            i <- i + 1
        let point = i
        let mutable valid = point > first
        if i < n && Text.charAt s i = '.' then
            i <- i + 1
            let fraction = i
            while i < n && digit (Text.charAt s i) do
                if Text.charAt s i <> '0' then nonzero <- true
                i <- i + 1
            valid <- valid && i > fraction
        let mutable start = first
        while start < point && Text.charAt s start = '0' do
            start <- start + 1
        if valid && i = n then
            Some { Negative = negative && nonzero; Start = start; Point = point; End = n }
        else None

    let isInteger (s: string) : bool =
        match parse s with
        | Some p -> p.Point = p.End
        | None -> false

    let private magnitude (a: string) (pa: Parts) (b: string) (pb: Parts) : int =
        let na = pa.Point - pa.Start
        let nb = pb.Point - pb.Start
        let mutable order = if na < nb then -1 elif na > nb then 1 else 0
        let mutable i = 0
        while order = 0 && i < na do
            let ca = Text.charAt a (pa.Start + i)
            let cb = Text.charAt b (pb.Start + i)
            if ca < cb then order <- -1
            elif ca > cb then order <- 1
            i <- i + 1
        let mutable ia = pa.Point
        let mutable ib = pb.Point
        if ia < pa.End then ia <- ia + 1
        if ib < pb.End then ib <- ib + 1
        while order = 0 && (ia < pa.End || ib < pb.End) do
            let ca = if ia < pa.End then Text.charAt a ia else '0'
            let cb = if ib < pb.End then Text.charAt b ib else '0'
            if ca < cb then order <- -1
            elif ca > cb then order <- 1
            if ia < pa.End then ia <- ia + 1
            if ib < pb.End then ib <- ib + 1
        order

    /// None means malformed text; Some -1/0/1 is exact numeric ordering.
    let compare (a: string) (b: string) : int option =
        match parse a, parse b with
        | Some pa, Some pb ->
            if pa.Negative <> pb.Negative then Some (if pa.Negative then -1 else 1)
            else
                let order = magnitude a pa b pb
                Some (if pa.Negative then -order else order)
        | _ -> None
