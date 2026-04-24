module BroadcastProviderDemo

type venue =
  | DetroitStadium
  | MetroArena
  | OtherVenue : string -> venue

type profile =
  | LiveBroadcastSilver
  | LiveBroadcastGold
  | UnsupportedProfile

type tm_intent = {
  intent_name:string;
  venue:option venue;
  service_class:option string;
  event_month:option string;
  event_day:option nat;
  event_year:option nat;
  start_hour:option nat;
  end_hour:option nat;
  timezone:option string;
  device_count:option nat;
  max_uplink_latency_ms:option nat;
  reporting_interval_minutes:option nat;
  immediate_degradation_alerts:bool;
  preserve_emergency_traffic:bool;
  request_public_safety_preemption:bool
}

type admission_token (p:profile) = {
  profile:profile;
  admitted_intent_name:string;
  max_admitted_devices:nat;
  min_admitted_latency_ms:nat
}

let has_value (#a:Type) (v:option a) : bool =
  match v with
  | Some _ -> true
  | None -> false

let measurable (i:tm_intent) : bool =
  has_value i.venue &&
  has_value i.service_class &&
  has_value i.event_month &&
  has_value i.event_day &&
  has_value i.event_year &&
  has_value i.start_hour &&
  has_value i.end_hour &&
  has_value i.timezone &&
  has_value i.device_count &&
  has_value i.max_uplink_latency_ms &&
  has_value i.reporting_interval_minutes

let has_positive_nat (v:option nat) : bool =
  match v with
  | Some value -> value > 0
  | None -> false

let quantities_ok (i:tm_intent) : bool =
  has_positive_nat i.device_count &&
  has_positive_nat i.max_uplink_latency_ms &&
  has_positive_nat i.reporting_interval_minutes

let common_core_valid (i:tm_intent) : bool =
  measurable i &&
  quantities_ok i

let window_ok (i:tm_intent) : bool =
  match i.start_hour, i.end_hour with
  | Some s, Some e -> s < e
  | _ -> false

let profile_supported (p:profile) : bool =
  match p with
  | UnsupportedProfile -> false
  | _ -> true

let profile_for_venue (v:venue) : profile =
  match v with
  | DetroitStadium -> LiveBroadcastGold
  | MetroArena -> LiveBroadcastSilver
  | OtherVenue _ -> UnsupportedProfile

let profile_for_intent (i:tm_intent) : profile =
  match i.venue with
  | Some venue -> profile_for_venue venue
  | None -> UnsupportedProfile

let profile_matches (p:profile) (i:tm_intent) : bool =
  profile_supported p &&
  profile_for_intent i = p

let max_devices (p:profile) : nat =
  match p with
  | LiveBroadcastSilver -> 100
  | LiveBroadcastGold -> 250
  | UnsupportedProfile -> 0

let min_latency_bound (p:profile) : nat =
  match p with
  | LiveBroadcastSilver -> 40
  | LiveBroadcastGold -> 20
  | UnsupportedProfile -> 0

let provider_window_ok (i:tm_intent) : bool =
  match i.start_hour, i.end_hour with
  | Some s, Some e -> 6 <= s && e <= 23
  | _ -> false

let reporting_ok (i:tm_intent) : bool =
  match i.reporting_interval_minutes with
  | Some minutes -> 15 <= minutes
  | None -> false

let capacity_ok (p:profile) (i:tm_intent) : bool =
  provider_window_ok i &&
  reporting_ok i &&
  (match i.device_count with
   | Some devices -> devices <= max_devices p
   | None -> false)

let latency_ok (p:profile) (i:tm_intent) : bool =
  match i.max_uplink_latency_ms with
  | Some latency -> min_latency_bound p <= latency
  | None -> false

let policy_ok (i:tm_intent) : bool =
  i.immediate_degradation_alerts &&
  i.preserve_emergency_traffic &&
  not i.request_public_safety_preemption

type measurable_intent (i:tm_intent) =
  v:tm_intent{ v == i /\ measurable v }

let mk_measurable
  (i:tm_intent{ measurable i })
  : measurable_intent i =
  i

type quantity_checked_intent (i:tm_intent) =
  v:tm_intent{ v == i /\ measurable v /\ quantities_ok v }

let mk_quantity_checked
  (i:tm_intent{ common_core_valid i })
  : quantity_checked_intent i =
  i

type window_checked_intent (i:tm_intent) =
  v:tm_intent{ v == i /\ common_core_valid v /\ window_ok v }

let mk_window_checked
  (i:tm_intent{ common_core_valid i /\ window_ok i })
  : window_checked_intent i =
  i

type profiled_intent (p:profile) (i:tm_intent) =
  v:tm_intent{ v == i /\ common_core_valid v /\ window_ok v /\ profile_matches p v }

let mk_profiled
  (p:profile)
  (i:tm_intent{ common_core_valid i /\ window_ok i /\ profile_matches p i })
  : profiled_intent p i =
  i

type capacity_checked_intent (p:profile) (i:tm_intent) =
  v:tm_intent{ v == i /\ common_core_valid v /\ window_ok v /\ profile_matches p v /\ capacity_ok p v }

let mk_capacity_checked
  (p:profile)
  (i:tm_intent{ common_core_valid i /\ window_ok i /\ profile_matches p i /\ capacity_ok p i })
  : capacity_checked_intent p i =
  i

type latency_checked_intent (p:profile) (i:tm_intent) =
  v:tm_intent{ v == i /\ common_core_valid v /\ window_ok v /\ profile_matches p v /\ capacity_ok p v /\ latency_ok p v }

let mk_latency_checked
  (p:profile)
  (i:tm_intent{ common_core_valid i /\ window_ok i /\ profile_matches p i /\ capacity_ok p i /\ latency_ok p i })
  : latency_checked_intent p i =
  i

type policy_checked_intent (p:profile) (i:tm_intent) =
  v:tm_intent{ v == i /\ common_core_valid v /\ window_ok v /\ profile_matches p v /\ capacity_ok p v /\ latency_ok p v /\ policy_ok v }

let mk_policy_checked
  (p:profile)
  (i:tm_intent{ common_core_valid i /\ window_ok i /\ profile_matches p i /\ capacity_ok p i /\ latency_ok p i /\ policy_ok i })
  : policy_checked_intent p i =
  i

type provider_checked_intent (p:profile) (i:tm_intent) =
  v:tm_intent{ v == i /\ common_core_valid v /\ window_ok v /\ profile_matches p v /\ capacity_ok p v /\ latency_ok p v /\ policy_ok v }

let mk_provider_checked
  (p:profile)
  (i:tm_intent{ common_core_valid i /\ window_ok i /\ profile_matches p i /\ capacity_ok p i /\ latency_ok p i /\ policy_ok i })
  : provider_checked_intent p i =
  i

let issue_admission_token
  (p:profile)
  (i:tm_intent)
  (_checked:provider_checked_intent p i)
  : admission_token p =
  { profile = p;
    admitted_intent_name = i.intent_name;
    max_admitted_devices = max_devices p;
    min_admitted_latency_ms = min_latency_bound p }
