# Importação e plano seguro

1. Gere o inventário UTC e valide `referenceCommit`; a VM atual deve ser inventariada
   antes de qualquer escrita.
2. Inicialize somente após revisão do backend de teste: `terraform -chdir=terraform/oci init -backend-config=backend.hcl`.
3. Importe manualmente, usando IDs do inventário local, na ordem compartment/rede,
   segurança, VNIC/IP, volumes/attachments/compute, DNS e Vault/IAM.
4. Rode `terraform -chdir=terraform/oci plan -refresh-only -out=plan-refresh.tfplan`,
   revise o `show`, depois `plan` normal. Repita para confirmar idempotência.
5. Qualquer destroy/replace ou alteração de IP, DNS, volume, VM, regra ou Vault bloqueia.

O backend S3 OCI neste repositório é apenas configuração de exemplo: locking e
concorrência ainda precisam ser demonstrados em bucket de teste. Até essa validação,
use lock externo e uma janela serializada; escrita concorrente é proibida.

Nenhum comando deste runbook faz `apply`, release, migration ou rollback. `apply` só
é possível após inventário completo, lock demonstrado, diff revisado e aprovação humana.
