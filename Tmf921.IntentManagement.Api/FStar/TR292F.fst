module TR292F

let specification_id = "TR292F"
let specification_title = "Set Operators"
let specification_version = "3.6.0"

type target_collection_shape =
  | IndividualTargets
  | ChosenFromSet
  | IntersectedTargets

let default_target_collection_shape = IndividualTargets
