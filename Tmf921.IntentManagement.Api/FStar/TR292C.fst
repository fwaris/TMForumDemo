module TR292C

let specification_id = "TR292C"
let specification_title = "Function Definition Ontology"
let specification_version = "3.6.0"

type function_application_shape =
  | UnaryFunction
  | BinaryFunction
  | NaryFunction

let all_arguments_present2 (left:bool) (right:bool) : bool =
  left && right

let all_arguments_present3 (first:bool) (second:bool) (third:bool) : bool =
  first && second && third

let all_arguments_present4 (first:bool) (second:bool) (third:bool) (fourth:bool) : bool =
  first && second && third && fourth
