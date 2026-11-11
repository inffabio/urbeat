# Terraform OCI Application Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adotar Terraform como fonte declarativa, não destrutiva e auditável para a infraestrutura OCI existente e para a configuração estável da topologia Urbeat, sem recriar a VM ARM64, sem assumir releases da aplicação e sem modificar secrets.

**Architecture:** Um root único em `terraform/oci/` usará módulos pequenos para rede, compute, DNS, referências do Vault e configuração da aplicação. O inventário não destrutivo fornecerá os IDs reais para imports; o state ficará em bucket privado versionado do OCI Object Storage, com locking validado antes de qualquer escrita. Cloud-init e templates distribuirão apenas configuração estável e idempotente; o pipeline PowerShell continuará sendo a autoridade para imagens, migrations, rollout, health checks funcionais e rollback da aplicação.

**Tech Stack:** Terraform fixado em versão suportada pelo repositório e pelo backend S3 compatível OCI; provider `oracle/oci` fixado; OCI CLI; PowerShell 7+/Pester 5; Docker Compose; Nginx; cloud-init; Prometheus; Grafana; `terraform fmt`, `validate`, `plan`, `show`; `tflint` (ou equivalente disponível); ferramentas locais de parsing YAML/Nginx/Compose.

## Global Constraints

- Preservar os OCIDs existentes e não criar outra VM, trocar AArch64, redesenhar a rede, migrar para OKE ou substituir Docker Compose.
- O primeiro passo operacional é inventário UTC, seguido de `terraform import`, `plan -refresh-only` e `plan`; nenhum `terraform apply` ocorre sem inventário completo, revisão humana do plan e aprovação explícita.
- `terraform destroy`, `terraform apply -auto-approve`, `terraform force-unlock`, update/delete de Vault, rotação de secrets, deploy, migration e rollback de release são proibidos no fluxo normal.
- Não versionar OCIDs operacionais reais em exemplos públicos, `.env`, credenciais, private keys, certificados privados, valores de secrets ou state Terraform.
- Terraform gerencia OCI, diretórios/base Docker estáveis e configuração estável; o pipeline `scripts/criarDeployOracleCloud/` continua gerenciando build/push AArch64, commit/tag, migrations, atualização de containers, smoke tests funcionais e rollback por manifest/imagem.
- Não usar data source que leia conteúdo de secret, `secret_content`, fallback hardcoded, `remote-exec` para release/migration/rollback ou `cloud-init` como mecanismo de release.
- Aplicar `prevent_destroy = true` a VM, VNIC/IP, volumes, DNS crítico, Vault/key e demais recursos cuja destruição cause indisponibilidade ou perda; usar `ignore_changes` somente com justificativa documentada.
- PostgreSQL e Redis permanecem com volumes existentes; nenhum `docker compose down -v`, recriação automática de volume ou gerenciamento de dados via Terraform.
- A topologia deve manter duas APIs em binds locais `127.0.0.1:5001` e `127.0.0.1:5002`, workers separados, PostgreSQL, Redis, Prometheus, Grafana e Nginx com upstream `least_conn`.
- State remoto é informação confidencial: bucket privado dedicado, versionamento, retenção/backup, logs quando disponíveis, acesso mínimo e uma única escrita autorizada por workspace/ambiente.
- SSL distribui somente paths/permissões/configuração; fingerprint, subject, SAN e expiração podem ser inventariados, mas chave e conteúdo do certificado nunca entram em Git, state, plan, output ou logs.
- Validações locais não devem exigir tenancy OCI real; testes contra OCI, import, lock concorrente, backup/restore e smoke test real são gates operacionais separados.
- Toda alteração futura deve preservar o commit de referência `3c475c5`, os paths reais descobertos no inventário e a ordem do pipeline existente `prerequisites -> vault -> docker -> environment -> application -> nginx -> ssl -> verify`.

---

## Mapa de arquivos

### Arquivos a criar

- `terraform/oci/versions.tf` — versões mínimas/fixadas de Terraform, provider OCI e requisitos de lockfile.
- `terraform/oci/providers.tf` — provider OCI, profile/instance principal aprovado e região sem credenciais no Git.
- `terraform/oci/backend.tf` — backend S3 compatível com OCI Object Storage, sem valores reais.
- `terraform/oci/variables.tf` — inputs tipados e validações de tenancy, rede, host, portas, domínios e flags.
- `terraform/oci/locals.tf` — tags, nomes derivados, mapa de ownership e limites permitidos.
- `terraform/oci/data.tf` — consultas somente leitura de tenancy/compartment e dados necessários à validação.
- `terraform/oci/network.tf` — recursos/data sources de VCN, subnet, rotas, gateways, DHCP, security lists/NSGs e IPs importados.
- `terraform/oci/compute.tf` — instância AArch64, boot/block volumes e attachments importados.
- `terraform/oci/dns.tf` — zona e records A/CNAME/TXT aprovados.
- `terraform/oci/vault.tf` — referências de Vault, key, secret metadata e IAM mínimo, sem conteúdo.
- `terraform/oci/iam.tf` — grupos dinâmicos/policies explicitamente aprovados, sem ampliar permissões.
- `terraform/oci/application-config.tf` — módulo de configuração, `templatefile`, cloud-init, marcador de versão/hash e paths.
- `terraform/oci/outputs.tf` — OCIDs, IPs/FQDNs, paths, portas e observabilidade não secreta.
- `terraform/oci/templates/cloud-init.yaml.tftpl` — bootstrap/reconciliação idempotente, sem release.
- `terraform/oci/templates/docker-compose.yml.tftpl` — duas APIs, workers, bancos, cache e observabilidade com referências runtime.
- `terraform/oci/templates/nginx.conf.tftpl` — upstream, rotas, headers, WebSocket, TLS paths e limites confirmados.
- `terraform/oci/templates/prometheus.yml.tftpl` — scrape targets e retenção sem credenciais.
- `terraform/oci/templates/grafana-datasources.yml.tftpl` — datasource Prometheus sem senha.
- `terraform/oci/modules/{network,compute,dns,vault-references,application-config}/` — interfaces de módulos com responsabilidade única.
- `terraform/oci/environments/production/backend.hcl.example` — configuração de backend com `<STATE_BUCKET>` e endpoint genérico, sem OCID real.
- `terraform/oci/environments/production/terraform.tfvars.example` — exemplo seguro com `<COMPARTMENT_OCID>`, `<INSTANCE_OCID>` e demais entradas não secretas.
- `terraform/oci/inventory/README.md` — procedimento, escopo, timestamp UTC, artefatos redigidos e mapa recurso real -> endereço Terraform.
- `terraform/oci/inventory/schema.json` — contrato do inventário sanitizado e classificação de ownership.
- `terraform/oci/inventory/import-map.example.json` — endereços e IDs de exemplo sem valores operacionais reais.
- `terraform/oci/runbooks/import-plan.md`, `config-rollback.md`, `vault-references.md`, `backup-restore.md`, `drift.md` — procedimentos seguros e auditáveis.
- `terraform/oci/README.md` — pré-requisitos, autenticação, layout, fronteiras, comandos e gates.
- `terraform/oci/tests/terraform-structure.Tests.ps1` — invariantes de arquivos, `prevent_destroy`, portas e ausência de secrets.
- `terraform/oci/tests/template-render.Tests.ps1` — renderização, Compose, cloud-init, Nginx e redaction.
- `terraform/oci/tests/inventory.Tests.ps1` — schema, completude e proibição de IDs reais em fixtures versionadas.
- `terraform/oci/tests/fixtures/` — inputs sintéticos, inventory sanitizado e outputs renderizados efêmeros.
- `scripts/criarDeployOracleCloud/terraform-preflight.ps1` — gate somente leitura que valida commit/configuração Terraform antes do pipeline, sem apply.
- `scripts/criarDeployOracleCloud/tests/terraform-preflight.Tests.ps1` — Pester dos invariantes do gate e da fronteira Terraform/pipeline.

### Arquivos a modificar na implementação futura

- `scripts/criarDeployOracleCloud/deploy-all.ps1` — adicionar preflight explícito antes do fluxo de aplicação, sem transferir build, migration, rollout ou rollback para Terraform.
- `scripts/criarDeployOracleCloud/deploy-manifest.ps1` — registrar hash da configuração Terraform e versão da aplicação sem escrever state nem secret.
- `scripts/criarDeployOracleCloud/07-verify-deployment.ps1` — manter health checks funcionais e verificar compatibilidade do marcador de configuração.
- `scripts/criarDeployOracleCloud/tests/deploy-hardening.Tests.ps1`, `api-replicas.Tests.ps1` e `nginx-preflight.Tests.ps1` — ampliar invariantes de portas locais, upstream e ausência de `apply` implícito.

### Arquivos existentes usados somente como referência, sem alteração nesta tarefa de plano

- `scripts/criarDeployOracleCloud/00-prerequisites-check.ps1` até `08-rebuild-database.ps1`, `oci-context.ps1`, `oci-ssh.ps1`, `secret-input.ps1`, `configs/secrets-map.json`.
- `docker/docker-compose.yml`, `docker/nginx/nginx.conf`, `docker/nginx/conf.d/00-shared.conf`, `10-http.conf`, `20-https.conf`, `docker/prometheus/prometheus.yml` e `docker/grafana-urbeat-dashboard.json`.

## Tarefas de implementação

### Task 1: Congelar referência e produzir inventário não destrutivo

**Files:**
- Create: `terraform/oci/inventory/README.md`, `terraform/oci/inventory/schema.json`, `terraform/oci/inventory/import-map.example.json`
- Create: `terraform/oci/runbooks/import-plan.md`
- Test: `terraform/oci/tests/inventory.Tests.ps1`
- Reference only: `scripts/criarDeployOracleCloud/oci-context.ps1`, `configs/oci.local.json.example`, `configs/secrets-map.json`

**Interfaces:**
- Produces `inventory.json` local, não versionado, conforme `schema.json`, com `capturedAtUtc`, `referenceCommit`, `profileName`, `region`, `compartment`, `resources[]` e `ownership`.
- Produces `import-map.json` local com `{ "terraformAddress": string, "resourceType": string, "importId": string, "source": string, "approved": boolean }`.
- Consumes apenas OCI CLI/console em modo leitura e inspeção de arquivos/Compose; não executa import, apply, deploy ou atualização de Vault.

- [ ] **Step 1: Escrever o teste que falha para o schema e invariantes**

```powershell
Describe 'OCI inventory contract' {
    It 'requires UTC reference commit and sanitized resource ownership' {
        $inventory = Get-Content $TestDrive/inventory.json -Raw | ConvertFrom-Json
        $inventory.referenceCommit | Should -Be '3c475c5'
        ([DateTimeOffset]$inventory.capturedAtUtc).Offset | Should -Be ([TimeSpan]::Zero)
        $inventory.resources | Should -Not -BeNullOrEmpty
        ($inventory | ConvertTo-Json -Depth 20) | Should -Not -Match '(?i)(secret_content|private.?key|password|token|connection.?string)'
    }
}
```

- [ ] **Step 2: Executar o teste para confirmar a falha**

Run: `pwsh -NoProfile -Command "Invoke-Pester terraform/oci/tests/inventory.Tests.ps1 -Output Detailed"`

Expected: `Failed` porque os artefatos de contrato ainda não existem.

- [ ] **Step 3: Especificar o procedimento de coleta somente leitura**

Documentar comandos `oci iam region-subscription list`, `oci network vcn get`, `oci network subnet list`, `oci compute instance get`, `oci dns record list`, `oci vault vault get` e `oci vault secret get` somente para OCIDs/metadados não secretos, além da inspeção de `docker compose config`, `ss -lntup`, mounts, certificados por `openssl x509 -fingerprint -subject -issuer -dates` e regras de firewall. Registrar horário UTC, profile, região, compartment e commit, redigindo respostas e nunca imprimindo conteúdo de secret.

- [ ] **Step 4: Implementar schema, mapa e gate de completude**

Exigir para cada recurso existente `resourceType`, `displayName`, `ocid` redigido ou referência local, `terraformAddress`, `source`, `owner`, `importStatus`; marcar `out-of-scope` recursos sem ownership comprovada, sem criar substitutos. O mapa deve cobrir compartment/tenancy, VCN/subnets/rotas/gateways/DHCP, listas/NSGs, VNIC/IP, compute, volumes/attachments/backups, DNS, Vault/key/secret metadata, dynamic groups e policies.

- [ ] **Step 5: Executar os testes e validar ausência de placeholders indevidos**

Run: `pwsh -NoProfile -Command "Invoke-Pester terraform/oci/tests/inventory.Tests.ps1 -Output Detailed"`

Expected: `Passed`; `<COMPARTMENT_OCID>`, `<INSTANCE_OCID>` e `<STATE_BUCKET>` aparecem apenas nos exemplos/runbooks, enquanto fixtures têm IDs sintéticos.

- [ ] **Step 6: Commitar o inventário contratual**

```powershell
git add terraform/oci/inventory terraform/oci/runbooks/import-plan.md terraform/oci/tests/inventory.Tests.ps1
git commit -m "docs: define non-destructive OCI inventory contract"
```

### Task 2: Fixar Terraform, provider, backend remoto e variáveis seguras

**Files:**
- Create: `terraform/oci/versions.tf`, `providers.tf`, `backend.tf`, `variables.tf`, `locals.tf`, `data.tf`
- Create: `terraform/oci/environments/production/backend.hcl.example`, `terraform.tfvars.example`
- Test: `terraform/oci/tests/terraform-structure.Tests.ps1`

**Interfaces:**
- Root inputs incluem `tenancy_ocid`, `compartment_ocid`, `region`, `profile`, `instance_ocid`, `architecture`, `domain_names`, `api_ports`, `host_paths`, `enabled_services`, `vault_references` e `tags`; nenhum input aceita valor de secret.
- Outputs e módulo usam mapas tipados; `api_ports` é exatamente `{ api_1 = 5001, api_2 = 5002 }` após validação.

- [ ] **Step 1: Escrever teste de estrutura e segurança**

```powershell
Describe 'Terraform foundation' {
    It 'pins Terraform and OCI provider and uses the S3-compatible backend' {
        (Get-Content terraform/oci/versions.tf -Raw) | Should -Match 'required_version'
        (Get-Content terraform/oci/versions.tf -Raw) | Should -Match 'oracle/oci'
        (Get-Content terraform/oci/backend.tf -Raw) | Should -Match 'backend\s+"s3"'
    }
    It 'does not contain credentials or secret values' {
        Get-ChildItem terraform/oci -Recurse -File | ForEach-Object {
            (Get-Content $_.FullName -Raw) | Should -Not -Match '(?i)(private_key|secret_content|password\s*=|token\s*=)'
        }
    }
}
```

- [ ] **Step 2: Rodar e confirmar falha**

Run: `pwsh -NoProfile -Command "Invoke-Pester terraform/oci/tests/terraform-structure.Tests.ps1 -Output Detailed"`

Expected: `Failed` porque a fundação Terraform não existe.

- [ ] **Step 3: Implementar versões e backend sem valores reais**

Fixar versões após verificar compatibilidade entre Terraform, provider OCI e backend S3. Manter `backend "s3" {}` sem credenciais no código; documentar `-backend-config=backend.hcl` e endpoint/namespace/bucket apenas no `.example`. Exigir lockfile `.terraform.lock.hcl` gerado e revisado, sem aceitar provider não verificado.

- [ ] **Step 4: Implementar variáveis e validações**

Validar `architecture == "aarch64"`, portas fechadas `5001/5002`, CIDRs válidos, portas 1–65535, FQDNs, modo SSL e lista fechada de serviços. Marcar como `sensitive` somente entradas inevitáveis, sem tratar isso como permissão para armazenar secret. Tags obrigatórias: `environment`, `system`, `owner`, `managed-by`, `schema-version`.

- [ ] **Step 5: Passar validação local**

Run: `terraform -chdir=terraform/oci fmt -check`; `terraform -chdir=terraform/oci init -backend=false`; `terraform -chdir=terraform/oci validate`; `pwsh -NoProfile -Command "Invoke-Pester terraform/oci/tests/terraform-structure.Tests.ps1 -Output Detailed"`

Expected: `fmt -check` sem arquivos alterados, `Success! The configuration is valid`, Pester `Passed`; init não contata OCI Object Storage.

- [ ] **Step 6: Commitar fundação**

```powershell
git add terraform/oci/versions.tf terraform/oci/providers.tf terraform/oci/backend.tf terraform/oci/variables.tf terraform/oci/locals.tf terraform/oci/data.tf terraform/oci/environments/production terraform/oci/tests/terraform-structure.Tests.ps1 terraform/oci/.terraform.lock.hcl
git commit -m "feat: add safe Terraform OCI foundation"
```

### Task 3: Criar módulos e recursos OCI importáveis com proteção

**Files:**
- Create: `terraform/oci/modules/network/*`, `modules/compute/*`, `modules/dns/*`, `modules/vault-references/*`
- Create: `terraform/oci/network.tf`, `compute.tf`, `dns.tf`, `vault.tf`, `iam.tf`, `outputs.tf`
- Modify: `terraform/oci/tests/terraform-structure.Tests.ps1`

**Interfaces:**
- `module.network` recebe IDs/import metadata, CIDRs e flags aprovadas; expõe `vcn_id`, `subnet_ids`, `security_list_ids`, `nsg_ids`, `public_ip`.
- `module.compute` recebe `instance_id`, `boot_volume_id`, `block_volume_ids`, `architecture`, paths e `prevent_destroy`; expõe `instance_id`, `private_ip`, `public_ip`.
- `module.dns` recebe `zone_id`, records aprovados e `prevent_destroy`; expõe `zone_id`, `record_names`.
- `module.vault_references` recebe `vault_id`, `key_id`, `secret_refs`, `runtime_principal`; expõe apenas IDs/nomes e policy IDs, nunca conteúdo.

- [ ] **Step 1: Criar testes de invariantes antes dos módulos**

Adicionar asserts que todos os recursos críticos contenham `prevent_destroy = true`, que nenhum resource block contenha `secret_content`, que security rules não contenham 5001/5002 como source público e que Vault data sources usem apenas metadata/OCIDs.

- [ ] **Step 2: Confirmar falha**

Run: `pwsh -NoProfile -Command "Invoke-Pester terraform/oci/tests/terraform-structure.Tests.ps1 -Output Detailed"`

Expected: `Failed` pela ausência de módulos/resources protegidos.

- [ ] **Step 3: Implementar interfaces e blocks mínimos**

Modelar recursos existentes com atributos mínimos compatíveis com o provider; importar em ordem compartment/rede, segurança, VNIC/IP, volumes/compute, DNS, Vault/IAM. Usar `moved` blocks para reorganização de endereços e `ignore_changes` somente em campos controlados pelo pipeline, documentando cada exceção no módulo e runbook. Recursos ausentes ou sem ownership ficam fora do state.

- [ ] **Step 4: Implementar Vault sem leitura de valor**

Representar OCID do vault/key/secrets e vínculo lógico variável -> referência. Conceder somente `read secret-bundle` nos OCIDs necessários à identidade de runtime. Não usar `oci_vault_secret` como conteúdo, não atualizar versões/descrições e não reutilizar `01-setup-vault-secrets.ps1`.

- [ ] **Step 5: Executar validações sem OCI real**

Run: `terraform -chdir=terraform/oci fmt -check`; `terraform -chdir=terraform/oci init -backend=false`; `terraform -chdir=terraform/oci validate`; `terraform -chdir=terraform/oci plan -refresh-only -input=false -lock=false -out=plan-refresh-local.tfplan` com fixtures sintéticas.

Expected: formatação/validação passam; fixtures produzem configuração testável sem chamadas de tenancy real; nenhum destroy/replace aparece no plan sintético.

- [ ] **Step 6: Commitar módulos protegidos**

```powershell
git add terraform/oci/modules terraform/oci/network.tf terraform/oci/compute.tf terraform/oci/dns.tf terraform/oci/vault.tf terraform/oci/iam.tf terraform/oci/outputs.tf terraform/oci/tests/terraform-structure.Tests.ps1
git commit -m "feat: model imported OCI resources safely"
```

### Task 4: Implementar templates e cloud-init idempotente

**Files:**
- Create: `terraform/oci/templates/cloud-init.yaml.tftpl`, `docker-compose.yml.tftpl`, `nginx.conf.tftpl`, `prometheus.yml.tftpl`, `grafana-datasources.yml.tftpl`
- Create: `terraform/oci/application-config.tf`, `modules/application-config/*`
- Create: `terraform/oci/tests/template-render.Tests.ps1`, `terraform/oci/tests/fixtures/*`

**Interfaces:**
- `module.application-config` recebe `host_paths`, `api_ports`, `image_tags`, `domains`, `vault_refs`, `tls_paths`, `service_flags`, `template_hash`; gera `cloud_init`, arquivos de configuração estável e marker `/var/lib/urbeat/terraform-config.json`.
- `image_tags` é parâmetro informativo do pipeline e não dispara pull/restart por refresh/apply.
- Runtime secrets são referências a paths/names; templates nunca recebem secret values.

- [ ] **Step 1: Escrever testes de renderização**

```powershell
Describe 'Rendered application configuration' {
    BeforeAll {
        $rendered = Get-Content "$TestDrive/rendered" -Raw
        $compose = Get-Content "$TestDrive/rendered/docker-compose.yml" -Raw
        $nginx = Get-Content "$TestDrive/rendered/nginx.conf" -Raw
        $cloudInit = Get-Content "$TestDrive/rendered/cloud-init.yaml" -Raw
    }
    It 'uses local API bindings' {
        $compose | Should -Match '127\.0\.0\.1:5001'
        $compose | Should -Match '127\.0\.0\.1:5002'
        $compose | Should -Not -Match '0\.0\.0\.0:500[12]'
    }
    It 'configures least_conn and WebSocket headers' {
        $nginx | Should -Match 'upstream urbeat_api'
        $nginx | Should -Match 'least_conn'
        $nginx | Should -Match 'proxy_http_version 1\.1'
        $nginx | Should -Match 'Upgrade'
        $nginx | Should -Match 'Connection'
    }
    It 'contains references but no secret values' {
        $rendered | Should -Match 'vault|secret|runtime'
        $rendered | Should -Not -Match '(?i)(password\s*[:=]|private.?key|BEGIN .* PRIVATE KEY|jwt|connection.?string)'
    }
    It 'writes an idempotent cloud-init marker' {
        $cloudInit | Should -Match '/var/lib/urbeat/terraform-config\.json'
        $cloudInit | Should -Match 'sha256|template.?hash'
        $cloudInit | Should -Match 'permissions|owner|mkdir'
    }
}
```

Os asserts devem exigir `127.0.0.1:5001`, `127.0.0.1:5002`, `least_conn`, `proxy_http_version 1.1`, Upgrade/Connection, paths TLS existentes, health checks e ausência de padrões `password=`, JWT, PEM ou connection string.

- [ ] **Step 2: Confirmar falha**

Run: `pwsh -NoProfile -Command "Invoke-Pester terraform/oci/tests/template-render.Tests.ps1 -Output Detailed"`

Expected: `Failed` porque templates/render harness ainda não existem.

- [ ] **Step 3: Implementar Compose estável**

Descrever duas APIs, workers separados, PostgreSQL, Redis, Prometheus, Grafana e frontend estático quando aplicável. Usar volumes/paths do inventário, networks mínimas, restart policies, limits, health checks e `depends_on` apenas como ordenação. Não publicar bancos, Redis, métricas ou Grafana indevidamente; não usar `down -v`; não alterar volumes por default. Compose não deve puxar nova imagem nem escolher release.

- [ ] **Step 4: Implementar Nginx, observabilidade e cloud-init**

Preservar rotas, CORS, uploads, timeouts, health, SSL/challenge, SignalR/WebSocket e headers confirmados contra configuração atual. Usar exatamente o upstream `urbeat_api` com `least_conn` e servidores locais 5001/5002. Prometheus mantém targets/retenção; Grafana aponta para Prometheus sem credencial; cloud-init instala/reconcilia apenas base idempotente, permissões, arquivos e marker, sem restart automático de PostgreSQL/Redis e sem rollout.

- [ ] **Step 5: Rodar parsers e testes locais**

Run: `pwsh -NoProfile -Command "Invoke-Pester terraform/oci/tests/template-render.Tests.ps1 -Output Detailed"`; `docker compose -f terraform/oci/tests/fixtures/rendered/docker-compose.yml config`; `nginx -t -c terraform/oci/tests/fixtures/rendered/nginx.conf -p terraform/oci/tests/fixtures/rendered`; `cloud-init schema --config-file terraform/oci/tests/fixtures/rendered/cloud-init.yaml`.

Expected: Pester `Passed`; Compose imprime configuração normalizada sem secrets; Nginx retorna `syntax is ok` e `test is successful`; cloud-init retorna schema válido. Se uma ferramenta não estiver instalada, o teste reporta dependência ausente e a validação equivalente documentada, sem falsificar sucesso.

- [ ] **Step 6: Commitar configuração estável**

```powershell
git add terraform/oci/application-config.tf terraform/oci/modules/application-config terraform/oci/templates terraform/oci/tests/template-render.Tests.ps1 terraform/oci/tests/fixtures
git commit -m "feat: define idempotent application configuration templates"
```

### Task 5: Documentar state OCI Object Storage, locking, Vault e permissões

**Files:**
- Modify: `terraform/oci/README.md`, `terraform/oci/environments/production/backend.hcl.example`
- Create: `terraform/oci/runbooks/vault-references.md`, `backup-restore.md`, `drift.md`
- Create: `terraform/oci/tests/backend-runbook.Tests.ps1`

**Interfaces:**
- Runbook define `init`, `refresh-only`, `plan`, import e restore usando IDs do inventário local; exemplos usam `<STATE_BUCKET>` e `<COMPARTMENT_OCID>` sem valores reais.
- State bucket exige privado, versionamento, retenção/backup, access logs disponíveis, lifecycle controlado e identidades separadas de read/plan/apply.
- Locking deve ser demonstrado com a combinação Terraform/backend escolhida; sem lock confiável, o gate falha e nenhum write é permitido.

- [ ] **Step 1: Escrever testes de runbook**

Assertar que o README inclui `fmt -check`, `validate`, `plan -refresh-only`, `plan`, revisão humana, `terraform import`, proibição de apply automático, restore não cego e procedimento autorizado para lock abandonado; garantir ausência de `apply -auto-approve`, `destroy`, `force-unlock` e comandos Vault update/delete.

- [ ] **Step 2: Confirmar falha**

Run: `pwsh -NoProfile -Command "Invoke-Pester terraform/oci/tests/backend-runbook.Tests.ps1 -Output Detailed"`

Expected: `Failed` até o README/runbooks possuírem os gates completos.

- [ ] **Step 3: Documentar PoC de backend sem exigir OCI real**

Descrever teste manual controlado com bucket de teste: `init`, leitura de state versionado, duas sessões concorrentes tentando lock, falha da segunda escrita, recuperação de versão anterior e interrupção segura. O plano de implementação não executa esse teste em produção; o resultado deve ser anexado ao registro operacional antes de habilitar apply.

- [ ] **Step 4: Documentar Vault e backup/restore**

Registrar somente referências por OCID/nome, IAM mínimo de `read secret-bundle`, redaction e falha segura de serviço dependente. Definir backup de state/volumes/PostgreSQL, restore em ambiente controlado, fingerprint/expiração de certificados e regra de nunca restaurar state antigo cegamente sobre produção.

- [ ] **Step 5: Passar testes**

Run: `pwsh -NoProfile -Command "Invoke-Pester terraform/oci/tests/backend-runbook.Tests.ps1 -Output Detailed"`; `terraform -chdir=terraform/oci fmt -check`.

Expected: Pester `Passed` e formato limpo.

- [ ] **Step 6: Commitar documentação operacional**

```powershell
git add terraform/oci/README.md terraform/oci/environments/production/backend.hcl.example terraform/oci/runbooks terraform/oci/tests/backend-runbook.Tests.ps1
git commit -m "docs: define OCI state locking and recovery runbooks"
```

### Task 6: Definir importação segura, plan gates e proteção contra drift

**Files:**
- Modify: `terraform/oci/runbooks/import-plan.md`, `terraform/oci/README.md`
- Create: `terraform/oci/tests/plan-safety.Tests.ps1`, `terraform/oci/scripts/assert-safe-plan.ps1`

**Interfaces:**
- `assert-safe-plan.ps1 -PlanJsonPath <path> -InventoryPath <path> -RequireInventoryCommit <sha>` retorna exit code 0 somente sem destroy/replace não aprovado, secret read, IP/DNS/volume/Vault change ou recurso fora do mapa.
- Import order: compartment/rede -> segurança -> VNIC/IP -> volumes/attachments/compute -> DNS -> Vault/IAM.

- [ ] **Step 1: Escrever fixtures de plan e testes**

Criar plans JSON sintéticos para: vazio, mudança tag aprovada, destroy, replace de VM/IP/volume, alteração DNS, security rule pública 5001, leitura de secret e recurso fora do inventário. Assertar aprovação apenas do primeiro/segundo caso.

- [ ] **Step 2: Confirmar falha**

Run: `pwsh -NoProfile -Command "Invoke-Pester terraform/oci/tests/plan-safety.Tests.ps1 -Output Detailed"`

Expected: `Failed` até o validador existir.

- [ ] **Step 3: Implementar o gate**

Consumir apenas `terraform show -json`; comparar `resource_changes[].address`, `change.actions`, `before/after` e `inventory.referenceCommit`. Bloquear `delete`, `replace`, alterações de OCID/IP/DNS/volume/VM/Vault, qualquer secret content e alteração de rede fora da allowlist. Exigir arquivo de aprovação separado, revisão humana e inventário correspondente.

- [ ] **Step 4: Documentar refresh/drift e import manual**

Executar `terraform plan -refresh-only -out=plan-refresh.tfplan`, revisar `show -no-color`, depois plan normal. Repetir duas vezes para idempotência. Registrar cada comando de import com ID vindo do arquivo local não versionado; nunca incluir OCIDs reais no runbook público. `moved` blocks corrigem endereços sem recriar recursos.

- [ ] **Step 5: Passar fixtures e TFLint**

Run: `pwsh -NoProfile -Command "Invoke-Pester terraform/oci/tests/plan-safety.Tests.ps1 -Output Detailed"`; `terraform -chdir=terraform/oci show -json terraform/oci/tests/fixtures/plans/empty.tfplan | pwsh -File terraform/oci/scripts/assert-safe-plan.ps1 -PlanJsonPath - -InventoryPath terraform/oci/tests/fixtures/inventory.json -RequireInventoryCommit 3c475c5`; `tflint --chdir=terraform/oci`.

Expected: Pester `Passed`, gate retorna `0` para fixture vazia e `1` para cada fixture perigosa; TFLint sem erro bloqueador. Se TFLint não estiver disponível, executar equivalente configurado e registrar a ausência como falha de pré-requisito de implementação, não como aprovação.

- [ ] **Step 6: Commitar gates**

```powershell
git add terraform/oci/runbooks/import-plan.md terraform/oci/README.md terraform/oci/tests/plan-safety.Tests.ps1 terraform/oci/scripts/assert-safe-plan.ps1 terraform/oci/tests/fixtures/plans
git commit -m "feat: add non-destructive Terraform plan gates"
```

### Task 7: Integrar preflight Terraform ao pipeline sem assumir releases

**Files:**
- Create: `scripts/criarDeployOracleCloud/terraform-preflight.ps1`
- Create: `scripts/criarDeployOracleCloud/tests/terraform-preflight.Tests.ps1`
- Modify: `scripts/criarDeployOracleCloud/deploy-all.ps1`, `deploy-manifest.ps1`, `07-verify-deployment.ps1`, `tests/deploy-hardening.Tests.ps1`, `tests/api-replicas.Tests.ps1`, `tests/nginx-preflight.Tests.ps1`

**Interfaces:**
- `Invoke-TerraformPreflight -TerraformDirectory -InventoryPath -ExpectedApplicationCommit -RequirePlanReview` somente executa fmt/validate/plan gate e retorna um objeto `{ Passed, ConfigHash, InventoryCommit, PlanPath, Errors }`; nunca chama apply/import/deploy.
- `deploy-all.ps1` chama preflight antes do ciclo existente; continua chamando scripts na ordem atual e mantém `-ExpectedCommit`, `-AllowDirty` e port-knock.
- `deploy-manifest.ps1` registra `applicationCommit`, `terraformConfigHash`, `configMarker`, health-check result e UTC; não grava secret/state.

- [ ] **Step 1: Escrever testes Pester de fronteira**

Mockar `terraform` e assertar que preflight chama `fmt -check`, `validate`, `plan`, `show` e gate, mas nunca `apply`, `import`, `destroy`, `force-unlock`; assertar que `deploy-all.ps1` ainda possui as etapas e que release tag/imagem pertence ao pipeline.

- [ ] **Step 2: Confirmar falha**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/criarDeployOracleCloud/tests/terraform-preflight.Tests.ps1 -Output Detailed"`

Expected: `Failed` porque o preflight e a integração ainda não existem.

- [ ] **Step 3: Implementar preflight somente leitura**

Usar `System.Diagnostics.Process` ou helper existente para capturar exit code/stdout/stderr redigido. Exigir inventário, commit esperado, revisão do plan e ausência de ações proibidas; interromper antes de qualquer etapa de aplicação quando qualquer gate falhar.

- [ ] **Step 4: Integrar sem alterar ownership**

Adicionar chamada explícita antes do pipeline de aplicação. Não adicionar `terraform apply`, não puxar imagem, não executar migration, não reiniciar serviço e não decidir rollback. `07-verify-deployment.ps1` continua responsável por smoke/health funcional; rollback de release continua usando manifest anterior.

- [ ] **Step 5: Executar Pester e validação existente**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/criarDeployOracleCloud/tests/terraform-preflight.Tests.ps1,scripts/criarDeployOracleCloud/tests/deploy-hardening.Tests.ps1,scripts/criarDeployOracleCloud/tests/api-replicas.Tests.ps1,scripts/criarDeployOracleCloud/tests/nginx-preflight.Tests.ps1 -Output Detailed"`; `pwsh -NoProfile -File scripts/criarDeployOracleCloud/validate-pipeline.ps1`.

Expected: todos os testes `Passed`; validação local não destrutiva reporta sucesso; nenhum acesso OCI/deploy é realizado.

- [ ] **Step 6: Commitar integração**

```powershell
git add scripts/criarDeployOracleCloud/terraform-preflight.ps1 scripts/criarDeployOracleCloud/tests/terraform-preflight.Tests.ps1 scripts/criarDeployOracleCloud/deploy-all.ps1 scripts/criarDeployOracleCloud/deploy-manifest.ps1 scripts/criarDeployOracleCloud/07-verify-deployment.ps1 scripts/criarDeployOracleCloud/tests/deploy-hardening.Tests.ps1 scripts/criarDeployOracleCloud/tests/api-replicas.Tests.ps1 scripts/criarDeployOracleCloud/tests/nginx-preflight.Tests.ps1
git commit -m "feat: gate application pipeline with Terraform preflight"
```

### Task 8: Executar validação de migração, cutover e rollback em ambiente controlado

**Files:**
- Modify: `terraform/oci/runbooks/import-plan.md`, `config-rollback.md`, `backup-restore.md`, `drift.md`, `README.md`
- Modify: `terraform/oci/tests/plan-safety.Tests.ps1`, `scripts/criarDeployOracleCloud/tests/terraform-preflight.Tests.ps1`

**Interfaces:**
- Gate de produção exige inventário completo, state remoto/lock demonstrados, `plan -refresh-only`, plan normal vazio ou mudanças explicitamente aprovadas e revisão humana assinada.
- Rollback de configuração restaura cópia fora do state e reverte commit Terraform após novo plan; rollback de aplicação usa manifest/imagem anterior no pipeline; state indisponível congela writes.

- [ ] **Step 1: Executar validações locais completas**

Run: `terraform -chdir=terraform/oci fmt -check`; `terraform -chdir=terraform/oci init -backend=false`; `terraform -chdir=terraform/oci validate`; `tflint --chdir=terraform/oci`; `pwsh -NoProfile -Command "Invoke-Pester terraform/oci/tests -Output Detailed"`; `pwsh -NoProfile -File scripts/criarDeployOracleCloud/validate-pipeline.ps1`.

Expected: todos os comandos passam sem tenancy OCI real; testes de fixtures confirmam Compose/YAML/cloud-init/Nginx, secrets ausentes, `prevent_destroy`, ports e ownership.

- [ ] **Step 2: Executar inventário e revisão operacional, sem apply**

Na janela aprovada, coletar inventário UTC e comparar configuração em disco com templates sem substituir nada. Validar IDs e ownership. Rodar `terraform import` apenas recurso a recurso com IDs locais, depois `plan -refresh-only` e `plan`; salvar outputs redigidos e revisar `terraform show -no-color`. Qualquer `-/+`, destroy, IP/DNS/volume/VM/Vault/rule change ou leitura de secret bloqueia a migração.

- [ ] **Step 3: Validar configuração em ambiente controlado**

Executar renderização, `docker compose config`, Nginx syntax, permissões, ports locais, certificados por fingerprint, targets Prometheus, duas APIs saudáveis, workers/heartbeat, pool PostgreSQL, Redis, Grafana, SignalR/WebSocket e smoke externo. Não reiniciar PostgreSQL/Redis nem trocar volumes sem plano operacional específico.

- [ ] **Step 4: Validar backup, restore e drift**

Testar restore de state versionado, boot/block volumes e backup consistente PostgreSQL em ambiente controlado; tratar Redis conforme política existente. Fazer alteração controlada/reversível fora do Terraform, confirmar que o refresh plan acusa drift sem corrigir automaticamente, e retornar o recurso ao estado anterior.

- [ ] **Step 5: Documentar cutover e rollback**

Registrar que Terraform não promove release. Só depois de configuração aprovada, o pipeline executa build AArch64, migration, rollout e smoke tests; em falha, pipeline retorna manifest/imagem anterior. Em falha de configuração, restaurar backup de arquivos, reverter commit e revisar plan. Em state/lock indisponível, parar writes e recuperar backend/lock com evidência; não usar force-unlock por conveniência.

- [ ] **Step 6: Commitar documentação final e revisão**

```powershell
git add terraform/oci/runbooks terraform/oci/README.md terraform/oci/tests scripts/criarDeployOracleCloud/tests/terraform-preflight.Tests.ps1
git commit -m "docs: document Terraform OCI migration and rollback gates"
```

## Self-review obrigatório antes da execução

- [ ] Confirmar cobertura da especificação: inventário/commit UTC, todos os recursos OCI importáveis, state Object Storage/versionamento/locking, módulos, cloud-init, Compose, Nginx/SSL, Prometheus/Grafana, Vault sem conteúdo, `prevent_destroy`, drift/plan, integração PowerShell, rollback e fronteira Terraform/pipeline.
- [ ] Pesquisar o plano por marcadores de indefinição, instruções genéricas sem ação verificável e reticências; remover ocorrências ou substituir por instruções concretas. Os únicos tokens de exemplo permitidos são `<COMPARTMENT_OCID>`, `<INSTANCE_OCID>` e `<STATE_BUCKET>`.
- [ ] Conferir consistência de interfaces: `Invoke-TerraformPreflight`, `assert-safe-plan`, entradas `api_ports`, marker `/var/lib/urbeat/terraform-config.json`, upstream `urbeat_api` e endereços de módulos devem ser idênticos em todas as tarefas.
- [ ] Confirmar que nenhum passo do plano executa `terraform apply`, `apply -auto-approve`, `destroy`, `force-unlock`, update/delete de Vault, import em tenancy real, deploy ou alteração OCI durante a implementação local.
- [ ] Confirmar que o gate de apply futuro exige inventário e revisão do plan, e que nenhuma mudança destrutiva pode ser aprovada apenas por refresh ou por pipeline automático.
- [ ] Confirmar que a validação final inclui `terraform fmt -check`, `validate`, `tflint`/equivalente, renderização de templates/cloud-init, parsing YAML/Compose/Nginx, Pester de invariantes, idempotência de dois plans e teste de fronteira do pipeline.

Plano completo salvo em `docs/superpowers/plans/2026-11-11-terraform-oci-application.md`. Após revisão, a implementação deve ser executada por tarefas com commits frequentes; este plano, por si só, não implementa Terraform nem modifica OCI.
