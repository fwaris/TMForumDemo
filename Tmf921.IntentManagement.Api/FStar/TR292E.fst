module TR292E

let specification_id = "TR292E"
let specification_title = "Conditions and Logical Operators"
let specification_version = "3.6.0"

let all_of (left:bool) (right:bool) : bool =
  left && right

let any_of (left:bool) (right:bool) : bool =
  left || right

let implies (premise:bool) (consequence:bool) : bool =
  not premise || consequence
