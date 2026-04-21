module TR292G

let specification_id = "TR292G"
let specification_title = "Metrics and Observations"
let specification_version = "3.6.0"

type metric_requirements = {
  metric_primary_device_count:option nat;
  metric_auxiliary_endpoint_count:option nat;
  metric_latency_bound_ms:option nat;
  metric_reporting_interval_minutes:option nat
}

let metric_present (value:option nat) : bool =
  match value with
  | Some _ -> true
  | None -> false

let required_metrics_present (requires_auxiliary_metric:bool) (metrics:metric_requirements) : bool =
  metric_present metrics.metric_primary_device_count &&
  metric_present metrics.metric_latency_bound_ms &&
  metric_present metrics.metric_reporting_interval_minutes &&
  (if requires_auxiliary_metric then metric_present metrics.metric_auxiliary_endpoint_count else true)
