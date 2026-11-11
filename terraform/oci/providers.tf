provider "oci" {
  # Official OCI provider enum values are ApiKey, InstancePrincipal and
  # ResourcePrincipal. API-key material is read from the local profile.
  tenancy_ocid        = var.auth == "ApiKey" ? var.tenancy_ocid : null
  region              = var.region
  config_file_profile = var.auth == "ApiKey" ? var.profile : null
  auth                = var.auth
}
