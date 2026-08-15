import "dotenv/config";
import * as common from "oci-common";
import * as vault from "oci-vault";
import { randomBytes } from "crypto";

const NEW_VAULT_ID = "ocid1.vault.oc1.sa-saopaulo-1.ffvhl5cyaaalc.abtxeljrx7cy5idk3cn2hctzkxvj7omcrj7zolwtw3pahtotr3lmatpifdyq";
const KEY_ID = "ocid1.key.oc1.sa-saopaulo-1.ffvhl5cyaaalc.abtxeljrlwd6rwal3vt7bd4lwmkwjjo3qckgyuzcfq5bcpg5fbakkuc72xuq";
const COMPARTMENT_ID = process.env.OCI_COMPARTMENT_ID;

const provider = new common.ConfigFileAuthenticationDetailsProvider(
  process.env.OCI_CONFIG_FILE || "C:\\Users\\intfa\\.oci\\config",
  process.env.OCI_CONFIG_PROFILE || "DEFAULT"
);

const vaultClient = new vault.VaultsClient({ authenticationDetailsProvider: provider });

const genHex = (len) => randomBytes(len).toString("hex");

const secrets = [
  // Cloudinary (mantidos como estao)
  { name: "CLOUDINARY_API_KEY", value: "MIGRATE_ME_CLOUDINARY_API_KEY" },
  { name: "CLOUDINARY_API_SECRET", value: "MIGRATE_ME_CLOUDINARY_API_SECRET" },
  { name: "CLOUDINARY_CLOUD_NAME", value: "MIGRATE_ME_CLOUDINARY_CLOUD_NAME" },

  // SMTP (valores novos fornecidos pelo usuario)
  { name: "URBEAT_SMTP_HOST", value: "smtp.email.sa-saopaulo-1.oci.oraclecloud.com" },
  { name: "URBEAT_SMTP_PORT", value: "587" },
  { name: "URBEAT_SMTP_USER", value: "ocid1.user.oc1..aaaaaaaagqfd556njfqlkcwrpzmarkppjzlciwv7wiw4nqusz27pcxndcosq@ocid1.tenancy.oc1..aaaaaaaah2m3lpf3efb7ulylcs4t3iurlzhjidsgwdp4tjiov2gvxzfdbv2q.60.com" },
  { name: "URBEAT_SMTP_PASSWORD", value: "MO6Y33d7ExgnrBm2Dn(K" },
  { name: "URBEAT_SMTP_FROM", value: "nao-responda@urbeat.com.br" },
  { name: "URBEAT_SMTP_SSL", value: "true" },

  // Frontend / API URLs
  { name: "URBEAT_FRONTEND_URL", value: "https://urbeat.com.br" },
  { name: "URBEAT_API_URL", value: "https://api.urbeat.com.br" },

  // CORS
  { name: "URBEAT_CORS_ORIGINS", value: "https://urbeat.com.br,https://api.urbeat.com.br" },

  // JWT (novos valores gerados)
  { name: "URBEAT_JWT_SECRET", value: genHex(32) },
  { name: "URBEAT_JWT_EXPIRY_HOURS", value: "720" },
  { name: "URBEAT_JWT_ISSUER", value: "urbeat" },
  { name: "URBEAT_JWT_AUDIENCE", value: "urbeat" },

  // Database
  { name: "URBEAT_DB_HOST", value: "urbeat_db" },
  { name: "URBEAT_DB_PORT", value: "5432" },
  { name: "URBEAT_DB_USER", value: "postgres" },
  { name: "URBEAT_DB_PASSWORD", value: genHex(16) },
  { name: "URBEAT_DB_NAME", value: "UrbeatDb" },
  { name: "URBEAT_DB_CONNECTION", value: "Host=urbeat_db;Database=UrbeatDb;Username=postgres;Password=MIGRATE_ME_DB_PASS" },

  // Grafana
  { name: "URBEAT_GRAFANA_USER", value: "admin" },
  { name: "URBEAT_GRAFANA_PASSWORD", value: genHex(8) },

  // Prometheus
  { name: "URBEAT_PROMETHEUS_URL", value: "http://urbeat_prometheus:9090" },

  // Postgres (mantidos)
  { name: "POSTGRES_PASSWORD", value: genHex(16) },
  { name: "POSTGRES_DB", value: "UrbeatDb" },
  { name: "POSTGRES_USER", value: "postgres" },
];

async function main() {
  console.log(`Criando ${secrets.length} secrets no vault urbeat...\n`);

  for (const s of secrets) {
    const base64Content = Buffer.from(s.value).toString("base64");
    try {
      const resp = await vaultClient.createSecret({
        createSecretDetails: {
          compartmentId: COMPARTMENT_ID,
          secretName: s.name,
          vaultId: NEW_VAULT_ID,
          keyId: KEY_ID,
          secretContent: { contentType: "BASE64", content: base64Content },
          description: `Migrado para urbeat-vault em ${new Date().toISOString()}`,
        },
      });
      console.log(`  ✅ ${s.name} (${resp.secret.lifecycleState})`);
    } catch (err) {
      console.log(`  ❌ ${s.name}: ${err.message}`);
    }
  }

  console.log("\n=== Concluido ===");
  console.log("Secrets com prefixo MIGRATE_ME precisam ser atualizados com valores reais.");
  console.log("Secrets com valores gerados aleatoriamente (JWT, DB_PASS, etc.) foram criados.");
}

main().catch(err => { console.error("FATAL:", err); process.exit(1); });
