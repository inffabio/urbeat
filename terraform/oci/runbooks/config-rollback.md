# Rollback de configuração

Faça backup dos arquivos fora do state. Em falha, restaure a cópia, reverta o commit
Terraform e execute novo `plan` antes de qualquer escrita autorizada. Rollback de
imagem/migration é responsabilidade do manifest do pipeline, não do Terraform.
