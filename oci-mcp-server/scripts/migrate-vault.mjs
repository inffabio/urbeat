import "dotenv/config";
import * as common from "oci-common";
import * as secrets from "oci-secrets";
import * as vault from "oci-vault";

const OLD_VAULT_ID = "ocid1.vault.oc1.sa-saopaulo-1.ffvctmavaacuu.abtxeljr3tmk6kjoscuokdg55geqsetz3dcfsrxwcggsqek4finxodazyrsa";
const NEW_VAULT_ID = "ocid1.vault.oc1.sa-saopaulo-1.ffvhl5cyaaalc.abtxeljrx7cy5idk3cn2hctzkxvj7omcrj7zolwtw3pahtotr3lmatpifdyq";
const COMPARTMENT_ID = process.env.OCI_COMPARTMENT_ID;
const KEY_ID = "ocid1.key.oc1.sa-saopaulo-1.ffvhl5cyaaalc.abtxeljrlwd6rwal3vt7bd4lwmkwjjo3qckgyuzcfq5bcpg5fbakkuc72xuq";

const configFile = process.env.OCI_CONFIG_FILE || "C:\\Users\\intfa\\.oci\\config";
const profile = process.env.OCI_CONFIG_PROFILE || "DEFAULT";

const provider = new common.ConfigFileAuthenticationDetailsProvider(configFile, profile);

const secretsClient = new secrets.SecretsClient({ authenticationDetailsProvider: provider });
const vaultClient = new vault.VaultsClient({ authenticationDetailsProvider: provider });

async function main() {
  console.log("=== 1. Listando secrets do vault antigo (happee_vault) ===\n");

  const listResp = await vaultClient.listSecrets({
    compartmentId: COMPARTMENT_ID,
    vaultId: OLD_VAULT_ID,
  });

  console.log(`Total secrets encontrados: ${listResp.items.length}\n`);

  const secretsList = [];
  for (const item of listResp.items) {
    const name = item.secretName;
    const id = item.id;
    console.log(`  ${name} (${item.lifecycleState})`);

    try {
      const bundle = await secretsClient.getSecretBundle({ secretId: id });
      const content = bundle.secretBundleContent;
      let value = "";
      if (content && content.contentType === "BASE64" && content.content) {
        value = Buffer.from(content.content, "base64").toString("utf-8");
      }
      secretsList.push({ name, id, value, lifecycle: item.lifecycleState });
    } catch (err) {
      console.log(`    ERRO ao obter valor: ${err.message}`);
      secretsList.push({ name, id, value: "", lifecycle: item.lifecycleState, error: true });
    }
  }

  // SMTP overrides
  const smtpOverrides = {
    SMTP_HOST: "smtp.email.sa-saopaulo-1.oci.oraclecloud.com",
    SMTP_PORT: "587",
    SMTP_USER: "ocid1.user.oc1..aaaaaaaagqfd556njfqlkcwrpzmarkppjzlciwv7wiw4nqusz27pcxndcosq@ocid1.tenancy.oc1..aaaaaaaah2m3lpf3efb7ulylcs4t3iurlzhjidsgwdp4tjiov2gvxzfdbv2q.60.com",
    SMTP_PASSWORD: "MO6Y33d7ExgnrBm2Dn(K",
    SMTP_FROM: "nao-responda@urbeat.com.br",
    SMTP_SSL: "true",
    FRONTEND_URL: "https://urbeat.com.br",
    API_URL: "https://api.urbeat.com.br",
  };

  console.log("\n=== 2. Mapeando secrets para novo vault (urbeat_vault) ===\n");

  const newSecrets = [];
  for (const s of secretsList) {
    let newName = s.name;
    let newValue = s.value;

    if (s.name.startsWith("HAPPEE_")) {
      const suffix = s.name.replace("HAPPEE_", "");
      newName = `URBEAT_${suffix}`;

      const overrideKey = suffix;
      if (smtpOverrides[overrideKey]) {
        newValue = smtpOverrides[overrideKey];
        console.log(`  ${s.name} → ${newName} = ${newValue.substring(0, 20)}... (SMTP override)`);
      } else {
        console.log(`  ${s.name} → ${newName} = ${newValue.substring(0, 40)}...`);
      }
    } else {
      console.log(`  ${s.name} → ${newName} (mantido)`);
    }

    newSecrets.push({ name: newName, value: newValue });
  }

  console.log(`\n=== 3. Criando ${newSecrets.length} secrets no novo vault ===\n`);

  for (const s of newSecrets) {
    const base64Content = Buffer.from(s.value).toString("base64");
    try {
      const createResp = await vaultClient.createSecret({
        createSecretDetails: {
          compartmentId: COMPARTMENT_ID,
          secretName: s.name,
          vaultId: NEW_VAULT_ID,
          keyId: KEY_ID,
          secretContent: {
            contentType: "BASE64",
            content: base64Content,
          },
          description: `Migrado do happee_vault em ${new Date().toISOString()}`,
        },
      });
      console.log(`  ✅ ${s.name} criado (${createResp.secret.lifecycleState})`);
    } catch (err) {
      console.log(`  ❌ ${s.name} ERRO: ${err.message}`);
    }
  }

  console.log("\n=== Transferencia concluida ===");
}

main().catch(err => {
  console.error("FATAL:", err);
  process.exit(1);
});
