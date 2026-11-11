module "dns" {
  source = "./modules/dns"
  domain_names = var.domain_names
}
