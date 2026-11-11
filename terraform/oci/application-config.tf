module "application_config" {
  source = "./modules/application-config"
  api_ports = var.api_ports
  api_image = lookup(var.image_tags, "api", "urbeat/webapi:stable")
  host_paths = var.host_paths
  domains = var.domain_names
  tls_paths = var.tls_paths
  template_hash = local.config_hash
  runtime_env_file = "${lookup(var.host_paths, "docker", "/opt/urbeat/docker")}/.env"
  compose_root = lookup(var.host_paths, "docker", "/opt/urbeat/docker")
}
