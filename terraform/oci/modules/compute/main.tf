variable "instance_id" { type = string }
variable "architecture" { type = string }
resource "terraform_data" "existing_instance_guard" {
  input = { instance_id = var.instance_id, architecture = var.architecture }
}
output "instance_id" { value = var.instance_id }
output "private_ip" { value = null }
output "public_ip" { value = null }
# This is metadata only: terraform_data does not import or protect the VM.
# A real OCI instance resource and lifecycle protection require a reviewed import-map.
