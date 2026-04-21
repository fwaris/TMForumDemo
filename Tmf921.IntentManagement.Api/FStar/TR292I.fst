module TR292I

let specification_id = "TR292I"
let specification_title = "Security Ontology"
let specification_version = "3.8.0"
let aligned_tio_release = "3.6.0"

type security_posture = {
  declared_safety_policy:bool;
  preserves_emergency_traffic:bool;
  requests_public_safety_preemption:bool
}

let posture_declared (posture:security_posture) : bool =
  posture.declared_safety_policy

let protected_traffic_preserved (posture:security_posture) : bool =
  posture.preserves_emergency_traffic

let requests_preemption (posture:security_posture) : bool =
  posture.requests_public_safety_preemption
