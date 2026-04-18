module IntentOntology

type ontology_profile = {
  name:string;
  version:string;
  enabled_modules:list string
}

type source_metadata = {
  classification:string;
  source_text:option string;
  source_iri:option string
}

type target_ref = {
  id:string;
  target_type:option string;
  name:option string
}

type quantity = {
  value:string;
  unit:option string
}

type function_application = {
  name:string;
  arguments:list string
}

type condition = {
  kind:string;
  subject:option string;
  operator:option string;
  value:option string;
  children:list condition
}

type expectation = {
  kind:string;
  subject:string;
  description:option string;
  condition:option condition;
  quantity:option quantity;
  function_application:option function_application
}

type canonical_intent_ir = {
  intent_name:string;
  description:option string;
  targets:list target_ref;
  expectations:list expectation;
  context:option string;
  priority:option string;
  profile_name:string;
  profile_version:string;
  enabled_modules:list string;
  source_classification:string;
  source_text:option string;
  source_iri:option string;
  raw_expression_type:option string
}

type raw_intent_ir = canonical_intent_ir

let string_non_empty (value:string) : bool =
  value <> ""

let rec list_non_empty (#a:Type) (values:list a) : bool =
  match values with
  | [] -> false
  | _ -> true

let rec condition_well_formed (value:condition) : bool =
  string_non_empty value.kind &&
  children_well_formed value.children

and children_well_formed (values:list condition) : bool =
  match values with
  | [] -> true
  | hd::tl -> condition_well_formed hd && children_well_formed tl

let target_well_formed (value:target_ref) : bool =
  string_non_empty value.id

let rec targets_well_formed (values:list target_ref) : bool =
  match values with
  | [] -> false
  | hd::tl -> target_well_formed hd && targets_tail_well_formed tl

and targets_tail_well_formed (values:list target_ref) : bool =
  match values with
  | [] -> true
  | hd::tl -> target_well_formed hd && targets_tail_well_formed tl

let expectation_well_formed (value:expectation) : bool =
  string_non_empty value.kind &&
  string_non_empty value.subject &&
  (match value.condition with
   | None -> true
   | Some condition -> condition_well_formed condition)

let rec expectations_well_formed (values:list expectation) : bool =
  match values with
  | [] -> false
  | hd::tl -> expectation_well_formed hd && expectations_tail_well_formed tl

and expectations_tail_well_formed (values:list expectation) : bool =
  match values with
  | [] -> true
  | hd::tl -> expectation_well_formed hd && expectations_tail_well_formed tl

let ontology_well_formed (value:raw_intent_ir) : bool =
  string_non_empty value.intent_name &&
  string_non_empty value.profile_name &&
  string_non_empty value.profile_version &&
  targets_well_formed value.targets &&
  expectations_well_formed value.expectations

let profile_conformant (profile:ontology_profile) (value:raw_intent_ir) : bool =
  profile.name = value.profile_name &&
  profile.version = value.profile_version &&
  value.source_classification <> "Ambiguous"

type ontology_well_formed_intent (raw:raw_intent_ir) =
  v:raw_intent_ir{ v == raw /\ ontology_well_formed v }

type profile_conformant_intent (profile:ontology_profile) (raw:raw_intent_ir) =
  v:raw_intent_ir{ v == raw /\ ontology_well_formed v /\ profile_conformant profile v }

type checked_intent (profile:ontology_profile) (raw:raw_intent_ir) =
  v:canonical_intent_ir{ v == raw /\ ontology_well_formed v /\ profile_conformant profile v }

let mk_checked_intent
  (profile:ontology_profile)
  (raw:raw_intent_ir{ ontology_well_formed raw /\ profile_conformant profile raw })
  : checked_intent profile raw =
  raw
