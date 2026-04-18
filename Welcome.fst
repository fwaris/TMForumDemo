module Welcome
open FStar.Mul

let divides (d n:nat) : prop =
  exists (k:nat) . n == d * k
  
let prime (p:nat) =
  p >= 2 /\
  (forall (d:nat) . divides d p ==> d == 1 \/ d == p)


let infinitely_many_primes  =
  forall (n:nat) . exists(p:nat) . prime p /\ p >= n


type d = {a:int; b:string}
type three =
  | One_of_three
  | Two_of_three
  | Three_of_three of d
  
let only_two_as_int (x:three { not (Three_of_three? x) })
  : int
  = match x with
    | One_of_three -> 1
    | Two_of_three -> 2    

let rec length #a (l:list a)
  : nat
  = match l with
    | [] -> 0
    | _ :: tl -> 1 + length tl

let rec append #a (l1 l2: list a) : l:list a {length l = length l1 + length l2} 
  = match l1 with
    | [] -> l2
    | hd :: tl -> hd :: append tl l2


let rec app #a (l1 l2:list a)
  : list a
  = match l1 with
    | [] -> l2
    | hd :: tl -> hd :: app tl l2

val app_length (#a:Type) (l1 l2:list a)
  : Lemma (length (app l1 l2) = length l1 + length l2)
  
let rec app_length (#a:Type) (l1 l2:list a)
  = match l1 with 
    | [] -> ()
    | _::tl -> app_length tl l2

  

