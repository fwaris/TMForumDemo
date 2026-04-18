module Poly
open FStar.Mul

let id (a:Type) (x:a) : a = x


let _ : bool = id bool true
let _ : bool = id bool false
let _ : int = id int (-1)
let _ : nat = id nat 17
let _ : string = id string "hello"
let _ : int -> int = id (int -> int) (id int)

val apply (a b:Type) (f:a -> b) : a -> b
val compose (a b c:Type) (f: b -> c) (g : a -> b) : a -> c

let apply a b f = fun x -> f x
let compose a b c f g = fun x -> f (g x)

val twice (a:Type) (f: a -> a) (x:a) : a
let twice a f x = compose a a a f f x

let sqr_is_pos (x:nat{x > 0}) : unit = assert(x * x > 0)


