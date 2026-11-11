variable "references" { type = map(string); default = {} }
resource "terraform_data" "vault_reference_guard" {
  input = var.references
}
output "names" { value = keys(var.references) }
# Deliberately metadata-only: no secret payload data source or assignment, and
# no protection of Vault resources. A reviewed import-map is still required.
