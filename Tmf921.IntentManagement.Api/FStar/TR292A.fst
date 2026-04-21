module TR292A

let specification_id = "TR292A"
let specification_title = "Intent Management Elements"
let specification_version = "3.6.0"

type tio_model_classification =
  | BaseOntologyModel
  | IntentCommonModel
  | IntentExtensionModel

type intent_manager_role =
  | Owner
  | Handler

type associated_value_type =
  | BooleanAssociatedValue
  | QuantityAssociatedValue
  | ContainerAssociatedValue

type associated_value_combination =
  | LogicalConjunction
  | QuantitySum
  | ContainerUnion

let default_associated_value_type = BooleanAssociatedValue
let default_associated_value_combination = LogicalConjunction
