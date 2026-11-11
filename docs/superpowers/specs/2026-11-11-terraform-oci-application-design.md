# Especificação técnica: Terraform para infraestrutura OCI e configuração da aplicação

**Data:** 2026-11-11  
**Status:** aprovado para detalhamento/implementação futura  
**Escopo:** infraestrutura OCI existente e topologia/configuração operacional da aplicação Urbeat  
**Commit de referência da aplicação:** `3c475c5`

## 1. Objetivo

Adotar Terraform como fonte declarativa para a infraestrutura OCI existente e para a configuração estável da topologia do Urbeat na VM ARM64 Ubuntu, sem recriar a VM nem interromper o ambiente atual. A primeira execução deve importar os recursos reais, produzir um `plan` sem mudanças destrutivas e somente depois assumir a gestão dos recursos explicitamente aprovados.

O desenho também define como Terraform poderá distribuir templates e configurar Docker Compose, Nginx, SSL, Prometheus e Grafana por cloud-init/templates, mantendo o pipeline PowerShell de aplicação como responsável pelo ciclo de release.

### Fora de escopo

- Migrar para Kubernetes/OKE, criar um cluster ou substituir Docker Compose.
- Criar outra VM, trocar a arquitetura AArch64 ou redesenhar a topologia OCI.
- Alterar, rotacionar, excluir ou recriar secrets existentes no OCI Vault.
- Versionar valores de secrets, certificados privados, tokens, senhas, `.env` ou estado Terraform.
- Fazer build de imagens, publicar imagens, executar migrations, promover uma versão de aplicação ou decidir rollback de uma release.
- Substituir os scripts existentes de deploy, alterar código de produção ou executar deploy durante a migração.
- Gerenciar dados de PostgreSQL/Redis como recursos Terraform ou recriar volumes com dados.

## 2. Estado atual e arquitetura alvo

### 2.1 Estado confirmado

- Uma VM OCI Ubuntu AArch64 existente hospeda o Urbeat.
- A aplicação é operada com Docker Compose.
- Há duas réplicas da API, workers separados, Nginx, SSL, PostgreSQL, Redis, Prometheus e Grafana.
- A infraestrutura inclui compartment, VCN/subnet, VNIC/IP público, volumes, regras de segurança, DNS, OCI Vault e referências aos secrets.
- A automação de aplicação está em `scripts/criarDeployOracleCloud/`; a revisão publicada de referência é `3c475c5`.
- O fluxo atual usa PowerShell/OCI CLI para pré-requisitos, ambiente, aplicação, Nginx, SSL, verificação e operações relacionadas ao Vault.

O inventário inicial deverá confirmar os OCIDs e os detalhes efetivamente presentes. Nomes, IPs, domínios, shape, políticas, mounts, imagens e paths que não estejam documentados não serão inferidos nem hardcoded na configuração Terraform.

### 2.2 Arquitetura alvo

`terraform/oci/` será o ponto de entrada de uma stack que:

1. referencia/importa recursos OCI existentes;
2. gerencia somente atributos intencionalmente declarados e compatíveis com o ambiente atual;
3. gera configuração de host e arquivos da topologia por templates/cloud-init;
4. deixa o pipeline de aplicação publicar artefatos e executar o rollout;
5. detecta drift por `plan` periódico e mantém histórico do state remoto.

Fluxo lógico da aplicação:

```text
Internet -> DNS -> IP público/VNIC -> Nginx :80/:443
                                      |
                                      +-> API :5001 (replica 1)
                                      +-> API :5002 (replica 2)
                                      +-> frontend/rotas estáticas, quando aplicável
                                      +-> endpoints protegidos de observabilidade

Docker network -> workers -> Redis/PostgreSQL
              -> Prometheus -> exporters/serviços
              -> Grafana -> Prometheus
```

O Nginx usará upstreams explícitos nas portas `5001` e `5002`, com `least_conn`, timeouts e headers/WebSocket necessários ao contrato atual. A configuração efetiva deve ser confirmada contra o ambiente antes de qualquer `apply`.

## 3. Recursos OCI existentes: inventário, identificação e importação

### 3.1 Recursos a importar

O inventário e o mapa Terraform devem cobrir, conforme existência no tenancy/compartment:

- compartment e tenancy/region de operação;
- VCN, subnets, route tables, internet/NAT/service gateways e DHCP options;
- security lists e/ou NSGs, incluindo regras de entrada/saída usadas por SSH, HTTP, HTTPS, aplicação, métricas e administração;
- VNIC primária, attachment, private IP e public reserved/ephemeral IP;
- instância de computação, shape AArch64, imagem/metadata, availability domain e lifecycle;
- boot volume, block volumes, attachments e políticas de backup;
- DNS zone, records A/CNAME/TXT necessários ao Urbeat;
- Vault, encryption key, secret objects e políticas/compartments relacionados, apenas como referências e metadados não secretos;
- grupos dinâmicos, policies e demais identidades OCI exigidas pelo consumo de secrets/observabilidade, sem ampliar permissões automaticamente.

Recursos que não existam ou cuja ownership não esteja comprovada ficam fora do state até decisão explícita. Não criar substitutos para “completar” o inventário.

### 3.2 Estratégia não destrutiva

1. Congelar o estado de referência e registrar commit `3c475c5`, conta/profile OCI, região, compartment e horário UTC do inventário.
2. Exportar somente metadados não sensíveis via OCI CLI/console e produzir um mapa `recurso real -> endereço Terraform`.
3. Criar os blocos de recurso/data source com `lifecycle { prevent_destroy = true }` nos recursos críticos e com atributos mínimos, evitando declarar valores que o provider normaliza de outra forma.
4. Executar `terraform import` recurso a recurso, começando por compartment/rede e terminando por compute, DNS e Vault.
5. Executar `terraform plan -refresh-only` e depois `terraform plan` sem aprovação de mudança. O resultado esperado da fase de import é vazio ou limitado a diferenças documentadas e não destrutivas.
6. Usar `moved` blocks quando um endereço Terraform for reorganizado; nunca remover/recriar um recurso apenas para ajustar o nome lógico.
7. Qualquer `-/+`, destroy, substituição de IP/VNIC/volume/VM, alteração de DNS, regra de rede ou secret interrompe a migração até a causa ser resolvida.

IDs de importação devem ser obtidos do inventário atual e fornecidos em arquivos locais não versionados, variáveis de ambiente ou comandos manuais. OCIDs reais não devem ser copiados para exemplos públicos se isso expuser tenancy, endpoints internos ou dados operacionais desnecessários.

## 4. Organização Terraform proposta

### 4.1 Estrutura provável

```text
terraform/oci/
├── versions.tf                 # Terraform e providers fixados
├── providers.tf                # OCI e backend/configuração de região
├── backend.tf                  # state remoto OCI Object Storage/S3 compatível
├── variables.tf                # entradas tipadas, sem valores secretos
├── locals.tf                   # nomes, tags e mapas derivados
├── data.tf                     # tenancy/compartment e consultas somente leitura
├── network.tf                  # VCN, subnet, VNIC/IP, rotas e regras importadas
├── compute.tf                  # instância, boot/block volumes e attachments
├── dns.tf                      # zona e records importados
├── vault.tf                    # OCIDs, metadados e referências, sem conteúdo
├── iam.tf                      # policies/dynamic groups mínimos, se aprovados
├── application-config.tf       # cloud-init/templates e entrega de configuração
├── outputs.tf                  # OCIDs, endpoints não secretos e status úteis
├── templates/
│   ├── cloud-init.yaml.tftpl
│   ├── docker-compose.yml.tftpl
│   ├── nginx.conf.tftpl
│   ├── prometheus.yml.tftpl
│   └── grafana-datasources.yml.tftpl
├── modules/
│   ├── network/
│   ├── compute/
│   ├── dns/
│   ├── vault-references/
│   └── application-config/
└── environments/
    └── production/
        ├── backend.hcl.example
        └── terraform.tfvars.example
```

Os módulos são uma separação de responsabilidade, não autorização para transformar uma importação em recurso novo. O root de produção deve ser o único entrypoint inicialmente; módulos só serão extraídos quando reduzirem risco ou duplicação.

### 4.2 Providers e variáveis

- Fixar versão do Terraform e do provider OCI em `versions.tf`; revisar changelog antes de upgrades.
- Usar autenticação OCI por profile/configuração local, instance principal ou workload identity aprovado; nunca incluir chave privada no repositório.
- Variáveis devem ser estritamente tipadas e separadas em: tenancy/region/compartment, IDs importados, nomes, parâmetros de rede, paths de host, portas, imagem/tag de configuração, domínios e flags de operação.
- Valores sensíveis devem ser `sensitive = true`, mas isso não torna seguro armazená-los no state; por isso secrets não são entradas Terraform.
- Validar CIDRs, portas, nomes DNS, arquitetura `aarch64`, modo de SSL e lista fechada de serviços com `validation` blocks.
- Tags devem incluir ambiente, sistema, owner, managed-by e versão do schema; não incluir tokens ou dados pessoais.

### 4.3 Outputs

Outputs permitidos: OCIDs de recursos, região, compartment, private/public IP (se operacionalmente necessário), FQDNs, paths de configuração, portas e identificadores de observabilidade. Não expor conteúdo de secrets, private keys, connection strings ou material de certificado. Outputs sensíveis, quando inevitáveis, devem ser marcados `sensitive` e ainda assim não devem ser usados para transportar valores secretos.

### 4.4 State remoto e locking

O state deve residir em OCI Object Storage, em bucket dedicado, privado, com versionamento, retenção/backup e acesso mínimo. A implementação deve usar o backend S3 compatível do OCI apenas após validar a compatibilidade com a versão do Terraform escolhida.

Locking é obrigatório: usar o mecanismo de lockfile suportado pela versão fixada (ou um mecanismo remoto equivalente validado antes do rollout). Se a combinação Terraform/backend não oferecer locking confiável, não prosseguir com escrita concorrente; escolher um backend com locking comprovado em vez de improvisar lock local.

O bucket deve ter política de retenção, logs de acesso quando disponíveis, lifecycle controlado e nenhuma exposição pública. O state será tratado como informação confidencial mesmo sem secret intencionalmente modelado. CI e operadores devem usar identidades separadas, com uma única escrita autorizada por workspace/ambiente.

## 5. OCI Vault e consumo de secrets

Terraform gerenciará somente:

- OCID do vault e endpoint de consumo;
- OCID da encryption key, quando necessário para referência;
- OCIDs/nomes das secrets e suas versões/metadados não secretos;
- permissões IAM mínimas para a identidade de runtime ler secrets;
- mapeamento lógico nome da variável -> referência OCI, sem valor.

Terraform não deverá usar data sources que leiam o conteúdo de uma secret para renderizar Compose, nem atribuir conteúdo a `secret_content`, atualizar versões, recriar secrets, alterar descrição com dados sensíveis ou imprimir respostas da OCI CLI. Nenhuma secret existente pode ser rotacionada/alterada por esta iniciativa.

Fluxo de consumo:

1. A identidade do host/serviço recebe somente `read secret-bundle` nos OCIDs necessários, no compartment correto.
2. Na inicialização controlada do host ou do serviço auxiliar já aprovado, o valor é obtido do Vault em runtime e entregue por arquivo/mecanismo de ambiente com permissões restritas.
3. O Compose referencia nomes/arquivos de runtime; templates versionados contêm apenas chaves, paths, defaults não secretos e comentários operacionais.
4. Logs, `terraform plan`, manifests, backups de configuração e diagnósticos devem redigir valores.
5. Falha na leitura deve impedir o serviço dependente de iniciar de forma segura e ser visível no health check, sem fallback para segredo hardcoded.

Os nomes atuais e os vínculos com `configs/secrets-map.json` deverão ser reconciliados no inventário. O script histórico `01-setup-vault-secrets.ps1` não deve ser reutilizado para atualizar secrets durante a migração.

## 6. Configuração da aplicação e topologia operacional

Terraform gerenciará a forma e os parâmetros estáveis da topologia; o pipeline continuará gerenciando a versão executável da aplicação.

### 6.1 Docker Compose

O template deverá descrever, no mínimo, duas instâncias API com bind local nas portas `5001` e `5002`, workers separados, PostgreSQL, Redis, Prometheus e Grafana, além dos serviços estáticos necessários ao frontend. Nomes de serviço, volumes, networks, health checks, restart policy, limits e dependências devem ser compatíveis com o Compose atual antes de serem declarados.

- APIs não devem publicar diretamente em interfaces públicas; o bind externo deve ser limitado a loopback/host conforme o desenho vigente.
- APIs e workers devem compartilhar somente as networks/volumes necessários.
- Workers não devem ser tratados como réplicas HTTP; seu número e fila devem permanecer explícitos.
- Volumes de PostgreSQL e Redis devem apontar para paths existentes identificados no inventário, sem `docker compose down -v` ou recriação automática.
- Imagem/tag é parâmetro de release do pipeline; Terraform não deve puxar uma imagem nova apenas por refresh/apply de infraestrutura.
- `depends_on` não substitui readiness: health checks e o pipeline devem validar disponibilidade real.

### 6.2 Nginx e portas

O Nginx continuará terminando HTTP/HTTPS e encaminhando para o upstream API com:

```nginx
upstream urbeat_api {
    least_conn;
    server 127.0.0.1:5001;
    server 127.0.0.1:5002;
}
```

O bloco real deve preservar rotas, headers, CORS, SignalR/WebSocket, timeouts, uploads, limites e health endpoints já contratados. A porta 80 deve apenas aplicar o comportamento de redirect/challenge vigente; a porta 443 deve usar o certificado existente. A configuração não pode abrir `5001`/`5002` em security lists ou NSGs.

### 6.3 PostgreSQL, Redis e workers

PostgreSQL permanecerá no host/Compose atual, com pool de conexões definido por configuração não secreta e dimensionado para duas APIs mais workers, respeitando os limites da VM. O total de conexões deve ser documentado e testado antes do rollout; não aumentar pool por padrão.

Redis continuará sendo o broker/cache conforme o contrato atual, com persistência e autenticação consumidas do Vault quando aplicável. Workers devem ter health/heartbeat observável e política de reinício que não cause loop destrutivo.

### 6.4 SSL e certificados

Terraform poderá distribuir paths, permissões, configuração Nginx e agendamento/integração do mecanismo de renovação já aprovado, mas não armazenará nem renderizará chaves privadas. Certificados existentes serão inventariados por fingerprint, subject, SAN e expiração, sem incluir conteúdo no Git ou no state. Renovação e validação devem ocorrer fora de `terraform apply` quando dependerem de challenge, DNS ou material privado.

### 6.5 Prometheus e Grafana

Templates deverão manter scrape targets, retenção, volumes e datasource do Grafana sem credenciais em claro. Endpoints administrativos não devem ser publicados na Internet sem a proteção existente. Dashboards e regras podem ser provisionados somente se forem arquivos não secretos e compatíveis com o ambiente; dados históricos não podem ser apagados por troca de container/volume.

## 7. Fronteira Terraform versus pipeline PowerShell

| Responsabilidade | Terraform/cloud-init | Pipeline de aplicação (`scripts/criarDeployOracleCloud/`) |
|---|---|---|
| OCI, rede, VM, IP, volumes, DNS, IAM | Sim, após import e aprovação | Não |
| Pacotes/base Docker e diretórios estáveis do host | Sim, de modo idempotente | Não, salvo pré-requisito explicitamente mantido |
| Estrutura Compose/Nginx/observabilidade | Templates e configuração estável | Não alterar estrutura fora do contrato |
| Build/push de imagens AArch64 | Não | Sim |
| Escolha do commit/tag da release | Não | Sim; validar commit publicado |
| Migrations | Não | Sim |
| Atualização/rollout de containers | Não como efeito implícito de apply | Sim |
| Health checks funcionais e smoke tests | Checks de infraestrutura podem existir | Sim, obrigatórios após release |
| Rollback de aplicação | Não | Sim, usando artefato/manifest anterior |
| Alteração/rotação de secrets | Não | Não nesta iniciativa; fluxo Vault aprovado separado |

Cloud-init é adequado para bootstrap/reconciliação inicial e entrega idempotente de arquivos, mas não deve ser usado como mecanismo de release em cada `plan/apply`. `remote-exec` fica reservado a uma exceção documentada e temporária para descoberta ou bootstrap que não possa ser expressa por cloud-init; não será usado para comandos de rollout, migrations ou rollback.

Terraform deve gravar versão/hash dos templates e um marcador de configuração no host. O pipeline deve verificar compatibilidade antes de atualizar imagens e registrar no manifest a versão da aplicação e o estado de configuração, sem alterar o state Terraform.

## 8. Segurança, IAM, proteção, backup e recuperação

- Usar compartment e região explícitos; negar por padrão ações fora do escopo.
- Separar identidade de leitura/plan da identidade de apply; exigir aprovação humana para apply de produção.
- Conceder ao runtime apenas leitura dos secrets necessários e somente leitura de Object Storage/configuração quando aplicável.
- Restringir SSH por origem administrativa aprovada; não abrir Docker socket, PostgreSQL, Redis, Prometheus ou Grafana publicamente.
- Aplicar `prevent_destroy = true` a VM, VNIC/IP, volumes, DNS zone/records críticos, Vault/key e demais recursos cuja destruição possa causar indisponibilidade ou perda de dados. Destruição excepcional exige remoção deliberada, revisão e aprovação separadas.
- Usar `ignore_changes` somente para campos comprovadamente controlados por outro sistema (por exemplo, versão de imagem da aplicação); cada exceção deve ser documentada para não esconder drift relevante.
- Habilitar backups/snapshots OCI para boot/block volumes e verificar restauração, retenção e janela sem sobrescrever dados.
- Fazer backup consistente de PostgreSQL e validar restore; tratar Redis conforme seu papel (cache/broker ou dados persistentes) e política atual.
- O state remoto e seus objetos históricos precisam de backup/versionamento e controle de acesso.
- Registrar auditoria OCI, histórico de plans/applies, hash dos templates, commit da aplicação e resultados dos health checks.

Recuperação de infraestrutura significa restaurar state e importar/associar recursos existentes ou restaurar volumes/VM conforme runbook; não significa aplicar um state antigo cegamente sobre produção. Recuperação de aplicação permanece no pipeline, com manifest e imagem anterior.

## 9. Migração faseada

### Fase 0 — preparação e inventário

- Confirmar branch/commit `3c475c5`, janela de manutenção e responsáveis.
- Executar validação local não destrutiva disponível no repositório.
- Catalogar todos os recursos OCI, serviços Compose, portas, mounts, regras, DNS, certificados, secrets por OCID/nome e políticas.
- Comparar configuração em disco com os templates propostos sem substituí-la.

### Fase 1 — state e import

- Criar bucket/state remoto com versionamento e locking testados.
- Inicializar providers e módulos sem recursos novos involuntários.
- Importar em ordem de dependência e registrar cada comando/resultado sem valores sensíveis.
- Executar `terraform plan -refresh-only` e `terraform plan` sem aplicar.
- Corrigir endereços, atributos e `ignore_changes` até o plan representar o ambiente real.

### Fase 2 — plan sem mudanças

O gate é um plan vazio ou com mudanças explicitamente classificadas como configuração aprovada. Qualquer destroy, replace, alteração de IP/DNS/volume/Vault, mudança de regra de rede ou leitura de secret bloqueia a passagem.

### Fase 3 — configuração controlada

- Aplicar somente diretórios/templates e pré-requisitos idempotentes, em janela aprovada.
- Fazer backup dos arquivos atuais e manter uma cópia de rollback fora do state.
- Validar `docker compose config`, Nginx syntax, permissões, volumes, health checks, portas locais, certificates e targets Prometheus.
- Não reiniciar PostgreSQL/Redis ou trocar volumes sem plano operacional específico.

### Fase 4 — validação e cutover

- Executar smoke tests locais e externos, login/fluxos críticos, API em ambas as portas, workers, Redis/PostgreSQL, SignalR, SSL, métricas e dashboards.
- Executar o pipeline da aplicação separadamente apenas quando o ambiente de configuração estiver aprovado.
- Comparar latência/erros e confirmar que `least_conn` distribui requests entre `5001` e `5002`.

### Fase 5 — operação e rollback

- Fazer plans periódicos em modo refresh e alertar drift.
- Para falha de configuração, restaurar o backup de templates/configuração e reverter o commit Terraform correspondente, após inspecionar o plan.
- Para falha de release, usar rollback do pipeline para o manifest/imagem anterior; não usar `terraform apply` como rollback de aplicação.
- Se o state remoto estiver indisponível, parar writes, preservar o ambiente e recuperar o backend/lock conforme runbook antes de qualquer ação.

## 10. Testes e critérios de aceite

### Testes

- `terraform fmt -check`, `terraform validate` e validação de providers/backend em ambiente de teste.
- Plan de import com refresh, sem mudanças destrutivas, repetido duas vezes para verificar idempotência.
- Teste de permissões IAM: runtime lê apenas secrets autorizados e não consegue listar/alterar conteúdo indevido.
- Teste de renderização sem secrets: templates não contêm valores sensíveis e `docker compose config` funciona com referências de runtime.
- Teste de rede: somente portas públicas esperadas; `5001`/`5002` locais; PostgreSQL/Redis/observabilidade sem exposição indevida.
- Teste de Nginx: sintaxe, redirect/SSL, WebSocket/SignalR, upstream `least_conn`, headers e health routes.
- Teste de serviços: duas APIs saudáveis, workers processando, pool PostgreSQL dentro do limite, Redis acessível, Prometheus coletando e Grafana conectado.
- Teste de backup/restore de volumes e PostgreSQL em ambiente controlado.
- Teste de drift: alteração controlada e reversível aparece no plan, sem aplicar automaticamente.
- Teste do pipeline: build AArch64, migrations, rollout, health checks e rollback continuam funcionando sem depender de `terraform apply`.

### Critérios de aceite

1. A VM e os recursos existentes permanecem os mesmos OCIDs após import; nenhuma recriação ocorre.
2. O state é remoto, versionado, protegido e bloqueado contra escrita concorrente.
3. O primeiro plan aprovado não contém destroy/replace não autorizado nem alteração de secrets.
4. Terraform consegue reproduzir a configuração estável sem depender de `remote-exec` para releases.
5. Compose mantém duas APIs em `5001`/`5002`, workers separados, Nginx com `least_conn`, PostgreSQL, Redis e observabilidade.
6. SSL, DNS, volumes e dados existentes são preservados e têm procedimento de recuperação testado.
7. O pipeline PowerShell continua único responsável por imagens, migrations, rollout, health checks funcionais e rollback.
8. Nenhum secret ou chave privada aparece em Git, plan, state intencionalmente modelado, log, output ou template.
9. Drift de recursos fora do state é detectado no inventário/plan e não é corrigido por criação destrutiva.
10. A documentação operacional permite repetir plan/import/rollback com placeholders seguros, sem valores reais.

## 11. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| Recursos OCI fora do state | Inventário por tenancy/compartment, mapa de ownership, plan e revisão antes de criar qualquer recurso |
| Drift causado por OCI CLI, console ou scripts | Plans refresh-only periódicos, ownership explícito e `ignore_changes` mínimo |
| Dependência/limitação do backend S3 compatível do OCI | PoC de init/lock/concurrency/restore antes da produção; não operar sem lock comprovado |
| Secret vazando no state/plan | Não ler conteúdo no Terraform, referências por OCID, redaction, IAM mínimo e revisão de logs |
| Cloud-init não reaplicado ou falha parcial | Scripts idempotentes, marcador de versão, logs persistentes, validação pós-boot e runbook de reexecução segura |
| Cloud-init reiniciar serviços críticos | Separar bootstrap de rollout, preservar volumes e tornar restart uma ação explícita |
| Certificado expirado/renovação quebrada | Inventário de expiração/fingerprint, alerta, validação Nginx e rollback de configuração sem expor chave |
| Estado Docker divergente do Compose | `docker compose config`, snapshot de arquivos, não usar `down -v`, health checks e reconciliação controlada |
| Alteração acidental de IP/DNS/VNIC/volume | `prevent_destroy`, plan obrigatório, revisão humana e backups |
| Pool PostgreSQL insuficiente ou excessivo | Dimensionamento para APIs/workers, métricas de conexões e teste de carga controlado |
| `least_conn` mascarar falha de uma réplica | Health checks, upstream failover explícito, métricas por réplica e smoke test em ambas as portas |
| Terraform assumir release de aplicação | Fronteira de responsabilidades, tags de imagem fora do apply e pipeline/manifest como autoridade |
| Estado remoto indisponível ou lock abandonado | Monitorar bucket, procedimento de unlock autorizado e proibição de force-unlock sem evidência |

## 12. Documentação operacional e comandos seguros

Devem ser criados junto da implementação futura, sem valores reais:

- `terraform/oci/README.md`: pré-requisitos, autenticação por profile/instance principal, layout, ownership e matriz de permissões;
- runbook de inventário/import com placeholders como `<COMPARTMENT_OCID>`, `<INSTANCE_OCID>` e `<STATE_BUCKET>`;
- runbook de plan/apply/refresh/drift e aprovação;
- runbook de cloud-init/configuração, validação e rollback;
- runbook de Vault por OCID/nome, sem conteúdo ou comandos de update;
- runbook de backup/restore de volumes, PostgreSQL, state e certificados;
- matriz Terraform versus pipeline e critérios de handoff;
- registro de incidentes, mudanças de provider e hashes de templates.

Exemplos de comandos seguros, sempre executados no diretório correto e revisados antes de qualquer escrita:

```powershell
terraform -chdir=terraform/oci fmt -check
terraform -chdir=terraform/oci init -backend-config=backend.hcl
terraform -chdir=terraform/oci validate
terraform -chdir=terraform/oci plan -refresh-only -out=plan-refresh.tfplan
terraform -chdir=terraform/oci show -no-color plan-refresh.tfplan
terraform -chdir=terraform/oci plan -out=plan.tfplan
# Somente após revisão/aprovação explícita:
terraform -chdir=terraform/oci apply plan.tfplan
```

Comandos de importação devem usar IDs fornecidos pelo inventário local e ser registrados no runbook, por exemplo `terraform import <endereco> <ocid>`, sem colocar OCIDs reais em documentação pública. `terraform destroy`, `terraform apply -auto-approve`, `terraform force-unlock` e qualquer update/delete de Vault ficam proibidos no fluxo normal. O pipeline de aplicação continua sendo invocado pelos comandos PowerShell existentes, com commit esperado e sem imprimir entradas de secrets.

## Revisão final da especificação

- **Ambiguidades resolvidas:** “gerenciar aplicação” significa gerenciar topologia/configuração estável, não releases; “importar Vault” significa importar referências/metadados, nunca valores; `5001` e `5002` são binds locais das duas APIs; rollback de infraestrutura e rollback de aplicação são fluxos distintos.
- **Placeholders e pendências:** não há marcador de decisão pendente. Placeholders com `<...>` aparecem somente em exemplos de comandos/documentação para impedir o uso acidental de valores reais.
- **Contradições verificadas:** a preservação da VM/volumes é compatível com `prevent_destroy`; cloud-init distribui configuração sem transformar Terraform em pipeline de release; state remoto não contém secrets por desenho; DNS/SSL/Vault são importados sem rotação.
- **Limite de escopo:** não há alteração de código existente, script de produção, secrets, deployment OCI ou migração para OKE nesta especificação.
