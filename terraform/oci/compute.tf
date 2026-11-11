module "compute" {
  source = "./modules/compute"
  instance_id = var.instance_ocid
  architecture = var.architecture
}
