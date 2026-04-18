module BroadcastProviderDemo

type venue =
  | DetroitStadium
  | MetroArena

type profile =
  | LiveBroadcastSilver
  | LiveBroadcastGold

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

let has_value (#a:Type) (v:option a) : bool =
  match v with
  | Some _ -> true
  | None -> false

let tm_window_valid (i:tm_intent) : bool =
  match i.start_hour, i.end_hour with
  | Some s, Some e -> s < e
  | _ -> false

let tm_valid (i:tm_intent) : bool =
  has_value i.venue &&
  has_value i.service_class &&
  has_value i.event_month &&
  has_value i.event_day &&
  has_value i.event_year &&
  tm_window_valid i &&
  has_value i.timezone &&
  has_value i.device_count &&
  has_value i.max_uplink_latency_ms &&
  has_value i.reporting_interval_minutes &&
  i.immediate_degradation_alerts

type tm_checked_intent (i:tm_intent) =
  v:tm_intent{ v == i /\ tm_valid v }

let mk_tm_checked
  (i:tm_intent{ tm_valid i })
  : tm_checked_intent i =
  i

let profile_for_venue (v:venue) : profile =
  match v with
  | DetroitStadium -> LiveBroadcastGold
  | MetroArena -> LiveBroadcastSilver

let max_devices (p:profile) : nat =
  match p with
  | LiveBroadcastSilver -> 100
  | LiveBroadcastGold -> 250

let min_latency_bound (p:profile) : nat =
  match p with
  | LiveBroadcastSilver -> 40
  | LiveBroadcastGold -> 20

let provider_window_ok (i:tm_intent) : bool =
  match i.start_hour, i.end_hour with
  | Some s, Some e -> 6 <= s && e <= 23
  | _ -> false

let reporting_ok (i:tm_intent) : bool =
  match i.reporting_interval_minutes with
  | Some minutes -> 15 <= minutes
  | None -> false

let provider_valid (i:tm_intent) : bool =
  tm_valid i &&
  provider_window_ok i &&
  reporting_ok i &&
  i.preserve_emergency_traffic &&
  not i.request_public_safety_preemption &&
  (match i.venue, i.device_count, i.max_uplink_latency_ms with
   | Some venue, Some devices, Some latency ->
     let p = profile_for_venue venue in
     devices <= max_devices p &&
     min_latency_bound p <= latency
   | _ -> false)

type provider_checked_intent (i:tm_intent) =
  v:tm_intent{ v == i /\ provider_valid v }

let mk_provider_checked
  (i:tm_intent{ provider_valid i })
  : provider_checked_intent i =
  i
