module ProviderIntentAdmission

open TmForumTr292CommonCore
open TR290A
open TR292D
open TR292I

(* Provider-specific refinement of the executable TR292 common-core projection. *)

type profile =
  | LiveBroadcastSilver
  | LiveBroadcastGold
  | CriticalCareAssured
  | CriticalCareStandard
  | UnsupportedProfile

type admission_token (p:profile) = {
  profile:profile;
  admitted_intent_name:string;
  max_primary_devices:nat;
  max_auxiliary_endpoints:nat;
  min_admitted_latency_ms:nat
}

let profile_supported (p:profile) : bool =
  match p with
  | UnsupportedProfile -> false
  | _ -> true

let resolve_profile (i:raw_tm_intent) : profile =
  let target = target_descriptor_of i in
  match i.scenario_family, target.descriptor_name with
  | BroadcastFamily, Some "Detroit Stadium" -> LiveBroadcastGold
  | BroadcastFamily, Some "Metro Arena" -> LiveBroadcastSilver
  | CriticalServiceFamily, Some "Mayo Clinic" -> CriticalCareAssured
  | CriticalServiceFamily, Some "City General Hospital" -> CriticalCareStandard
  | _ -> UnsupportedProfile

let profile_matches (p:profile) (i:raw_tm_intent) : bool =
  profile_supported p &&
  resolve_profile i = p

let window_ok (i:raw_tm_intent) : bool =
  let window = delivery_window_of i in
  match window.window_start_hour, window.window_end_hour with
  | Some s, Some e -> s < e
  | _ -> false

let max_primary_devices (p:profile) : nat =
  match p with
  | LiveBroadcastSilver -> 100
  | LiveBroadcastGold -> 250
  | CriticalCareAssured -> 80
  | CriticalCareStandard -> 60
  | UnsupportedProfile -> 0

let max_auxiliary_endpoints (p:profile) : nat =
  match p with
  | LiveBroadcastSilver -> 0
  | LiveBroadcastGold -> 0
  | CriticalCareAssured -> 220
  | CriticalCareStandard -> 120
  | UnsupportedProfile -> 0

let min_latency_bound (p:profile) : nat =
  match p with
  | LiveBroadcastSilver -> 40
  | LiveBroadcastGold -> 20
  | CriticalCareAssured -> 10
  | CriticalCareStandard -> 15
  | UnsupportedProfile -> 0

let reporting_minimum (p:profile) : nat =
  match p with
  | LiveBroadcastSilver -> 15
  | LiveBroadcastGold -> 15
  | CriticalCareAssured -> 5
  | CriticalCareStandard -> 10
  | UnsupportedProfile -> 0

let provider_window_ok (p:profile) (i:raw_tm_intent) : bool =
  let window = delivery_window_of i in
  match p, window.window_start_hour, window.window_end_hour with
  | LiveBroadcastSilver, Some s, Some e -> 6 <= s && e <= 23
  | LiveBroadcastGold, Some s, Some e -> 6 <= s && e <= 23
  | CriticalCareAssured, Some s, Some e -> s < 24 && e <= 23
  | CriticalCareStandard, Some s, Some e -> s < 24 && e <= 23
  | _, _, _ -> false

let reporting_ok (p:profile) (i:raw_tm_intent) : bool =
  let reporting = reporting_interval_quantity_of i in
  match reporting.quantity_value with
  | Some minutes -> reporting_minimum p <= minutes
  | None -> false

let capacity_ok (p:profile) (i:raw_tm_intent) : bool =
  let primary_quantity = primary_device_quantity_of i in
  let auxiliary_quantity = auxiliary_endpoint_quantity_of i in
  provider_window_ok p i &&
  reporting_ok p i &&
  (match primary_quantity.quantity_value with
   | Some value -> value <= max_primary_devices p
   | None -> false) &&
  (match p, auxiliary_quantity.quantity_value with
   | LiveBroadcastSilver, _ -> true
   | LiveBroadcastGold, _ -> true
   | _, Some value -> value <= max_auxiliary_endpoints p
   | _, None -> false)

let latency_ok (p:profile) (i:raw_tm_intent) : bool =
  let latency_quantity = max_latency_quantity_of i in
  match latency_quantity.quantity_value with
  | Some latency -> min_latency_bound p <= latency
  | None -> false

let policy_ok (i:raw_tm_intent) : bool =
  let reporting = reporting_expectation_of i in
  let security = security_posture_of i in
  reporting.requires_immediate_alerts &&
  posture_declared security &&
  protected_traffic_preserved security &&
  not (requests_preemption security)

type window_checked_intent (i:raw_tm_intent) =
  v:raw_tm_intent{ v == i /\ common_core_valid v /\ window_ok v }

let mk_window_checked
  (i:raw_tm_intent{ common_core_valid i /\ window_ok i })
  : window_checked_intent i =
  i

type profiled_intent (p:profile) (i:raw_tm_intent) =
  v:raw_tm_intent{ v == i /\ common_core_valid v /\ window_ok v /\ profile_matches p v }

let mk_profiled
  (p:profile)
  (i:raw_tm_intent{ common_core_valid i /\ window_ok i /\ profile_matches p i })
  : profiled_intent p i =
  i

type capacity_checked_intent (p:profile) (i:raw_tm_intent) =
  v:raw_tm_intent{ v == i /\ common_core_valid v /\ window_ok v /\ profile_matches p v /\ capacity_ok p v }

let mk_capacity_checked
  (p:profile)
  (i:raw_tm_intent{ common_core_valid i /\ window_ok i /\ profile_matches p i /\ capacity_ok p i })
  : capacity_checked_intent p i =
  i

type latency_checked_intent (p:profile) (i:raw_tm_intent) =
  v:raw_tm_intent{ v == i /\ common_core_valid v /\ window_ok v /\ profile_matches p v /\ capacity_ok p v /\ latency_ok p v }

let mk_latency_checked
  (p:profile)
  (i:raw_tm_intent{ common_core_valid i /\ window_ok i /\ profile_matches p i /\ capacity_ok p i /\ latency_ok p i })
  : latency_checked_intent p i =
  i

type policy_checked_intent (p:profile) (i:raw_tm_intent) =
  v:raw_tm_intent{ v == i /\ common_core_valid v /\ window_ok v /\ profile_matches p v /\ capacity_ok p v /\ latency_ok p v /\ policy_ok v }

let mk_policy_checked
  (p:profile)
  (i:raw_tm_intent{ common_core_valid i /\ window_ok i /\ profile_matches p i /\ capacity_ok p i /\ latency_ok p i /\ policy_ok i })
  : policy_checked_intent p i =
  i

type provider_checked_intent (p:profile) (i:raw_tm_intent) =
  v:raw_tm_intent{ v == i /\ common_core_valid v /\ window_ok v /\ profile_matches p v /\ capacity_ok p v /\ latency_ok p v /\ policy_ok v }

let mk_provider_checked
  (p:profile)
  (i:raw_tm_intent{ common_core_valid i /\ window_ok i /\ profile_matches p i /\ capacity_ok p i /\ latency_ok p i /\ policy_ok i })
  : provider_checked_intent p i =
  i

let issue_admission_token
  (p:profile)
  (i:raw_tm_intent)
  (_checked:provider_checked_intent p i)
  : admission_token p =
  { profile = p;
    admitted_intent_name = i.intent_name;
    max_primary_devices = max_primary_devices p;
    max_auxiliary_endpoints = max_auxiliary_endpoints p;
    min_admitted_latency_ms = min_latency_bound p }
