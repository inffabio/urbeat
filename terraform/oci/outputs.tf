output "instance_id" { value = module.compute.instance_id }
output "api_ports" { value = var.api_ports }
output "config_hash" { value = local.config_hash }
output "config_marker" { value = "/var/lib/urbeat/terraform-config.json" }
output "vault_reference_names" { value = module.vault_references.names }
output "application_cloud_init" { value = module.application_config.cloud_init }
output "application_template_hashes" { value = module.application_config.template_hashes }
