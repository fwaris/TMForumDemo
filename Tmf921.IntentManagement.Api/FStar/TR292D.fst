module TR292D

let specification_id = "TR292D"
let specification_title = "Quantity Ontology"
let specification_version = "3.6.0"

type quantity_requirement = {
  quantity_value:option nat;
  quantity_unit:option string
}

let quantity_present (quantity:quantity_requirement) : bool =
  match quantity.quantity_value with
  | Some _ -> true
  | None -> false

let positive_quantity (quantity:quantity_requirement) : bool =
  match quantity.quantity_value with
  | Some value -> value > 0
  | None -> false

let all_quantities_positive
  (requires_auxiliary_quantity:bool)
  (primary_quantity:quantity_requirement)
  (auxiliary_quantity:quantity_requirement)
  (latency_quantity:quantity_requirement)
  (reporting_quantity:quantity_requirement)
  : bool =
  positive_quantity primary_quantity &&
  positive_quantity latency_quantity &&
  positive_quantity reporting_quantity &&
  (if requires_auxiliary_quantity then positive_quantity auxiliary_quantity else true)
