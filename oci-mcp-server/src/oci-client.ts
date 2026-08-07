import * as common from "oci-common";
import * as core from "oci-core";
import * as identity from "oci-identity";
import * as monitoring from "oci-monitoring";
import * as objectStorage from "oci-objectstorage";
import * as dotenv from "dotenv";
import * as path from "path";
import * as os from "os";

dotenv.config();

export interface OCIClients {
  compute:        core.ComputeClient;
  blockstorage:   core.BlockstorageClient;
  virtualNetwork: core.VirtualNetworkClient;
  identity:       identity.IdentityClient;
  monitoring:     monitoring.MonitoringClient;
  objectStorage:  objectStorage.ObjectStorageClient;
  compartmentId:  string;
  region:         string;
  authProvider:   common.AuthenticationDetailsProvider;
}

export function createOCIClients(): OCIClients {
  const configFile = path.join(
    os.homedir(), ".oci", "config"
  );

  const profile =
    process.env.OCI_CONFIG_PROFILE || "DEFAULT";

  console.error(`📁 OCI Config: ${configFile}`);
  console.error(`👤 Profile:    ${profile}`);
  console.error(
    `🌎 Region:     ${process.env.OCI_REGION}`
  );

  let provider: common.AuthenticationDetailsProvider;

  try {
    provider =
      new common.ConfigFileAuthenticationDetailsProvider(
        configFile,
        profile
      );
  } catch (error) {
    console.error("❌ Erro config OCI:", error);
    process.exit(1);
  }

  const clientConfig = {
    authenticationDetailsProvider: provider,
  };

  const compartmentId =
    process.env.OCI_COMPARTMENT_ID || "";

  if (!compartmentId) {
    console.error(
      "⚠️  OCI_COMPARTMENT_ID não definido no .env"
    );
  }

  return {
    compute:
      new core.ComputeClient(clientConfig),

    // ✅ BlockstorageClient corretamente instanciado
    blockstorage:
      new core.BlockstorageClient(clientConfig),

    virtualNetwork:
      new core.VirtualNetworkClient(clientConfig),

    identity:
      new identity.IdentityClient(clientConfig),

    monitoring:
      new monitoring.MonitoringClient(clientConfig),

    objectStorage:
      new objectStorage.ObjectStorageClient(clientConfig),

    compartmentId,
    region: process.env.OCI_REGION || "sa-saopaulo-1",
    authProvider: provider,
  };
}