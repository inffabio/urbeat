variable "existing_ids" { type = map(string); default = {} }
resource "terraform_data" "existing_network_guard" {
  input = var.existing_ids
}
# Scaffold only; this does not import or protect a VCN, subnet, NSG or IP.
output "vcn_id" { value = lookup(var.existing_ids, "vcn", null) }
output "subnet_ids" { value = {} }
output "security_list_ids" { value = {} }
output "nsg_ids" { value = {} }
output "public_ip" { value = lookup(var.existing_ids, "public_ip", null) }
