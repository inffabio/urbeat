locals {
  common_tags = merge(var.tags, { "config-hash-source" = "terraform-oci-templates" })
  critical_ownership = { instance = "existing", network = "existing", volumes = "existing", dns = "existing", vault = "reference-only" }
  config_hash = sha256(join("", [for f in fileset(path.module, "templates/*.tftpl") : filesha256("${path.module}/${f}")]))
}
