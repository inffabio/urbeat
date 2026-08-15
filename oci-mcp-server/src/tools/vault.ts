import { z } from "zod";
import { VaultsClient } from "oci-vault";
import type { OCIClients } from "../oci-client.js";

const URBEAT_VAULT_ID = "ocid1.vault.oc1.sa-saopaulo-1.ffvhl5cyaaalc.abtxeljrx7cy5idk3cn2hctzkxvj7omcrj7zolwtw3pahtotr3lmatpifdyq";

export function vaultTools(clients: OCIClients) {
  const { compartmentId, authProvider } = clients;

  const vaultClient: any = new VaultsClient({
    authenticationDetailsProvider: authProvider,
  });

  return [
    {
      name: "oci_list_secrets",
      description: "Lista todos os secrets do vault 'urbeat-vault' com seus nomes e status",
      schema: z.object({}),
      handler: async () => {
        const resp = await vaultClient.listSecrets({
          compartmentId,
          vaultId: URBEAT_VAULT_ID,
        });

        const secrets = resp.items.map((s: any) => ({
          name: s.secretName,
          id: s.id,
          lifecycle: s.lifecycleState,
          timeCreated: s.timeCreated,
        }));

        const nameCount = new Map<string, number>();
        for (const s of secrets) {
          nameCount.set(s.name, (nameCount.get(s.name) || 0) + 1);
        }

        const duplicates = [...nameCount.entries()]
          .filter(([, count]) => count > 1)
          .map(([name, count]) => ({ name, count }));

        return JSON.stringify(
          {
            total: secrets.length,
            secrets,
            duplicates: duplicates.length > 0 ? duplicates : "No duplicates.",
          },
          null,
          2
        );
      },
    },

    {
      name: "oci_delete_duplicate_secrets",
      description: "Encontra e deleta secrets duplicados, mantendo apenas o mais recente de cada nome",
      schema: z.object({
        dryRun: z.boolean().optional().default(true)
          .describe("Se true, apenas lista sem deletar"),
      }),
      handler: async (args: { dryRun?: boolean }) => {
        const isDryRun = args.dryRun !== false;

        const resp = await vaultClient.listSecrets({
          compartmentId,
          vaultId: URBEAT_VAULT_ID,
        });

        const groups = new Map<string, { name: string; id: string; timeCreated: string }[]>();
        for (const s of resp.items) {
          const name: string = s.secretName!;
          if (!groups.has(name)) groups.set(name, []);
          groups.get(name)!.push({
            name,
            id: s.id!,
            timeCreated: s.timeCreated || "",
          });
        }

        const toDelete: { name: string; id: string; timeCreated: string }[] = [];
        for (const [, items] of groups) {
          if (items.length > 1) {
            items.sort((a, b) => b.timeCreated.localeCompare(a.timeCreated));
            toDelete.push(...items.slice(1));
          }
        }

        if (!isDryRun) {
          for (const item of toDelete) {
            await vaultClient.scheduleSecretDeletion({
              secretId: item.id,
              scheduleSecretDeletionDetails: {},
            });
            console.error(`Deleted: ${item.name} (${item.id})`);
          }
        }

        return JSON.stringify(
          {
            action: isDryRun ? "DRY RUN" : "DELETED",
            totalSecrets: resp.items.length,
            duplicateNames: groups.size,
            duplicatesFound: toDelete.length,
            ...(toDelete.length > 0 ? { deleted: toDelete } : {}),
          },
          null,
          2
        );
      },
    },
  ];
}
