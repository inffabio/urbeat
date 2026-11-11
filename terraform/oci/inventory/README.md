# Inventário OCI não destrutivo

O arquivo local `inventory.json` não é versionado. Registre `capturedAtUtc` em UTC,
`referenceCommit` (`3c475c5`), profile, região, compartment e somente metadados.
Redija OCIDs se o artefato sair do ambiente operacional; nunca copie secret values,
private keys, `.env` ou respostas completas da OCI CLI.

Coleta somente leitura (não executar nesta tarefa): `oci iam region-subscription list`,
`oci network vcn get`, `oci network subnet list`, `oci compute instance get`,
`oci dns record list`, `oci vault vault get` e `oci vault secret get` apenas para
metadados/OCID. No host, registre `docker compose config`, `ss -lntup`, mounts e
fingerprint/subject/issuer/dates via `openssl x509`; não imprima conteúdo de secret.

Cada recurso deve conter `resourceType`, `displayName`, `terraformAddress`, `source`,
`owner` e `importStatus`. Recursos sem ownership ficam `out-of-scope`; não crie
substitutos. O `import-map.json` local usa IDs coletados e aprovação explícita.
