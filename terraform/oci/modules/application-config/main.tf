variable "api_ports" { type = map(number) }
variable "api_image" { type = string }
variable "host_paths" { type = map(string) }
variable "domains" { type = list(string) }
variable "tls_paths" { type = object({ certificate = string, key = string }) }
variable "template_hash" { type = string }
variable "runtime_env_file" { type = string }
variable "compose_root" { type = string }
locals {
  config_path = lookup(var.host_paths, "config", "/opt/urbeat/docker/generated")
  frontend_path = lookup(var.host_paths, "frontend", "/opt/urbeat/frontend-dist")
  uploads_path = lookup(var.host_paths, "uploads", "/opt/urbeat/uploads")
  tls_path = lookup(var.host_paths, "tls", "/var/lib/urbeat/letsencrypt")
  server_names = length(var.domains) > 0 ? join(" ", var.domains) : "_"
  compose = templatefile("${path.module}/../../templates/docker-compose.yml.tftpl", { api_image = var.api_image, api_port_1 = var.api_ports.api_1, api_port_2 = var.api_ports.api_2, runtime_env_file = var.runtime_env_file, compose_root = var.compose_root, config_path = local.config_path })
  nginx_frontend = templatefile("${path.module}/../../templates/urbeat-frontend.conf.tftpl", { frontend_server_names = local.server_names, frontend_path = local.frontend_path, downloads_path = lookup(var.host_paths, "downloads", "/opt/urbeat/downloads"), client_max_body_size = "50m" })
  nginx_api = templatefile("${path.module}/../../templates/urbeat-api.conf.tftpl", { api_port_1 = var.api_ports.api_1, api_port_2 = var.api_ports.api_2, api_server_name = "api.${length(var.domains) > 0 ? var.domains[0] : "example.invalid"}", client_max_body_size = "50m" })
  nginx_monitoring = file("${path.module}/../../templates/urbeat-monitoring.conf.tftpl")
  prometheus = templatefile("${path.module}/../../templates/prometheus.yml.tftpl", { scrape_interval = "15s" })
  grafana = templatefile("${path.module}/../../templates/grafana-datasources.yml.tftpl", {})
  cloud_init = templatefile("${path.module}/../../templates/cloud-init.yaml.tftpl", { config_path = local.config_path, uploads_path = local.uploads_path, runtime_env_file = var.runtime_env_file, compose = local.compose, nginx_frontend = local.nginx_frontend, nginx_api = local.nginx_api, nginx_monitoring = local.nginx_monitoring, prometheus = local.prometheus, grafana = local.grafana, template_hash = var.template_hash, api_image = var.api_image })
}
output "cloud_init" { value = local.cloud_init }
output "files" { value = { compose = local.compose, nginx_frontend = local.nginx_frontend, nginx_api = local.nginx_api, nginx_monitoring = local.nginx_monitoring, prometheus = local.prometheus, grafana = local.grafana } }
output "template_hashes" { value = { compose = sha256(local.compose), nginx_frontend = sha256(local.nginx_frontend), nginx_api = sha256(local.nginx_api), nginx_monitoring = sha256(local.nginx_monitoring), prometheus = sha256(local.prometheus), grafana = sha256(local.grafana), cloud_init = sha256(local.cloud_init) } }
output "marker_path" { value = "/var/lib/urbeat/terraform-config.json" }
