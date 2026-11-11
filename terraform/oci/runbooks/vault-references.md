# Referências do Vault

Terraform armazena apenas OCIDs/nomes e mapeamentos lógicos. O runtime recebe somente
`read secret-bundle` nos objetos aprovados. Não use data sources que leiam conteúdo,
`secret_content`, update/delete, rotação ou fallback hardcoded. Falha de leitura deve
impedir dependentes de iniciar com segurança.
