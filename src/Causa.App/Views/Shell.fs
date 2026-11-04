// Owner: ui. The Limen view vocabulary: scalars and flat lists of scalar records (no nested lists).
namespace Causa.App.Content

type ViewPrimitive =
    | Text of string
    | Flag of bool
    | Number of int

type ViewValue =
    | Scalar of ViewPrimitive
    | Items of (string * ViewPrimitive) list list

[<RequireQualifiedAccess>]
module View =
    let text key (value: string) = key, Scalar(Text value)
    let flag key value = key, Scalar(Flag value)
    let number key value = key, Scalar(Number value)
    let items key (rows: (string * ViewPrimitive) list list) = key, Items rows
