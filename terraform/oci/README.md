# Terraform OCI do urbeat (render/scaffold-only)

Primeira regra: inventarie/importa a VM existente antes de qualquer apply. Nesta primeira
fase a stack é **scaffold/guard/render foundation**: não cria nem importa VM, VCN, DNS,
Vault ou volumes, não protege recursos existentes e não promove releases. Os
`terraform_data` são apenas marcadores de inventário; não dão falsa proteção e não
recebem `prevent_destroy`. Um import-map revisado e revisão do plan são pré-condições
para recursos OCI reais.
OCIDs, `.tfvars`, backend e state são locais/ignorados. Use `auth = "ApiKey"` com um
profile OCI local, ou `"InstancePrincipal"`/`"ResourcePrincipal"`; nesses dois últimos
modos profile e tenancy ficam omitidos. Nunca inclua chaves.

## Validação

Há duas fronteiras de validação deliberadas: `scripts/criarDeployOracleCloud/validate-pipeline.ps1`
valida apenas o pipeline OCI PowerShell legado e não depende do Terraform; já
`scripts/criarDeployOracleCloud/terraform-preflight.ps1` possui modos explícitos `Render`,
`Validate` e `OperationalGate`. Render/Validate são locais e não fazem chamadas OCI;
o pipeline legado continua funcional sem Terraform.

```powershell
terraform -chdir=terraform/oci fmt -check
terraform -chdir=terraform/oci init -backend=false
terraform -chdir=terraform/oci validate
pwsh -NoProfile -Command "Invoke-Pester terraform/oci/tests -Output Detailed"
```

O backend Object Storage é um exemplo configurável em `environments/production` e não
é ativado automaticamente. Locking/concurrency do backend S3 OCI não é considerado
garantido: valide-o em bucket de teste antes de uso. Se não houver locking nativo
confiável, o runbook exige lock externo e execução/revisão serializada; bloqueie
qualquer escrita concorrente.
Use `plan -refresh-only` antes do plan normal e revise todo diff. Nenhum artefato Terraform
é aplicado automaticamente nesta fase; `OperationalGate` exige plano JSON, inventory/import-map
aprovados, marker humano e marker de lock validado. Backend configurável não afirma locking
automático. `terraform_data` não fornece `prevent_destroy`; recursos OCI reais ainda precisam
de `lifecycle.prevent_destroy` antes do gate.

## Fronteira com o pipeline

Terraform/cloud-init **renderiza** diretórios, configuração estável, marcador e hash;
nesta fase o Terraform não entrega nada remotamente ao host. O artefato local deve ser
consumido explicitamente pelo pipeline antes de qualquer execução. Não faz pull/restart,
build, migrations, rollout, health funcional ou rollback. O pipeline
`scripts/criarDeployOracleCloud` continua responsável por isso na ordem existente:
`prerequisites -> vault -> docker -> environment -> application -> nginx -> ssl -> verify`.
Integração futura deve consumir o hash/marker sem substituir essas etapas.

### Fronteiras de runtime

- Nginx não é serviço Compose: `05-configure-nginx.ps1` instala/configura Nginx no
  host via systemd. Os templates separados `urbeat-frontend.conf` e `urbeat-api.conf`
  são uma fonte futura para `sites-available`, mantendo upstream
  `127.0.0.1:5001/5002` e `least_conn`. O Compose não publica 80/443.
- O frontend é entregue pelo build/serviço atual do pipeline em
  `/opt/urbeat/frontend-dist` por padrão (`host_paths.frontend` parametriza o path),
  não em `/opt/urbeat/frontend/dist`.
- Prometheus é container e, por isso, usa `webapi:8080` e
  `webapi_replica_2:8080` na rede Docker. Isso é deliberadamente diferente do
  upstream do Nginx host.
- `runtime_env_file` é parametrizado (por padrão `${host_paths.docker}/.env`). O
  segredo protegido é criado pelo estágio Vault/environment antes do Compose. O
  cloud-init apenas cria diretórios, renderiza arquivos e valida presença sem ler ou
  imprimir conteúdo; não inicia serviços e não cria um env fictício.

O script `tests/render-templates.ps1` é o mecanismo local explícito de renderização
para `tests/fixtures/rendered` (ou outro diretório de artefatos ignorado). Isso não é
deploy remoto; o pipeline ainda precisa consumir/copiar os artefatos. O root expõe
`application_cloud_init` e `application_template_hashes` para esse consumo; esses
outputs não significam que a VM foi alterada.
