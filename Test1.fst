module Test1
open FStar.Mul
//open FStar.Prelude

type vec (a:Type) : nat -> Type =
  | Nil : vec a 0
  | Cons : #n:nat -> hd:a -> tl:vec a n -> vec a (n + 1)

let rec append #a #n #m (v1:vec a n) (v2:vec a m)
  : vec a (n + m)
  = match v1 with
    | Nil -> v2
    | Cons hd tl -> Cons hd (append tl v2)

let rec reverse #a #n (v:vec a n)
  : vec a n
  = match v with
    | Nil -> Nil
    | Cons hd tl -> append (reverse tl) (Cons hd Nil)

let incr2 (x:int) : nat = x - x


let incr (x:int) : int = x + 1

val max (x:int) (y:int) : int
//let max (x:int) (y:int) = if x <= y then x else y
let max = admit()
//let max = admit() //remove the admit and write a definition

open FStar.Mul
val factorial (n:nat) : nat //replace this `val` with some others
let rec factorial n
  = if n = 0
    then 1
    else n * factorial (n - 1)

val fibonacci (n:nat) : nat //replace this `val` with some others
let rec fibonacci n
  = if n <= 1
    then 1
    else fibonacci (n - 1) + fibonacci (n - 2)

val fibonacci_greater_than_arg (n:nat{n >= 2})
  : Lemma (fibonacci n >= n)

let rec fibonacci_greater_than_arg (n:nat{n >= 2})
  : Lemma (fibonacci n >= n)
  = 
  if n <= 3 then ()
  else (
    fibonacci_greater_than_arg( n - 1);
    fibonacci_greater_than_arg(n - 2)
  )

let rec factorial_is_pos (x:int)
  : Lemma (requires x >= 0)
          (ensures factorial x > 0)
  = if x = 0 then ()
    else factorial_is_pos (x - 1)


val factorial_is_greater_than_arg (x:int)
  : Lemma (requires x > 2)
          (ensures factorial x > x)

let rec factorial_is_greater_than_arg (x:int)
  : Lemma (requires x > 2)
          (ensures factorial x > x)
  =
    if x = 3 then ()
    else factorial_is_greater_than_arg(x - 1)
