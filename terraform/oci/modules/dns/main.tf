variable "domain_names" { type = list(string) }
resource "terraform_data" "existing_dns_guard" {
  input = var.domain_names
}
# Scaffold only; this does not import or protect DNS resources.
output "zone_id" { value = null }
output "record_names" { value = var.domain_names }
