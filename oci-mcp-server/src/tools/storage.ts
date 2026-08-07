import { z } from "zod";
import type { OCIClients } from "../oci-client.js";

export function storageTools(clients: OCIClients) {
  const { objectStorage, compartmentId } = clients;

  return [
    // ── Listar Buckets ─────────────────────────────────
    {
      name: "oci_list_buckets",
      description: "Lista todos os buckets do Object Storage",
      schema: z.object({
        namespace: z
          .string()
          .optional()
          .describe(
            "Namespace do Object Storage (auto-detectado se omitido)"
          ),
        compartmentId: z.string().optional()
      }),
      handler: async (args: {
        namespace?: string;
        compartmentId?: string;
      }) => {
        // Obter namespace automaticamente se não fornecido
        let ns = args.namespace;
        if (!ns) {
          const nsResp =
            await objectStorage.getNamespace({});
          ns = nsResp.value;
        }

        const response = await objectStorage.listBuckets({
          namespaceName: ns,
          compartmentId: args.compartmentId || compartmentId
        });

        return JSON.stringify(
          {
            namespace: ns,
            buckets: response.items.map(b => ({
              name: b.name,
              compartmentId: b.compartmentId,
              timeCreated: b.timeCreated,
              etag: b.etag
            }))
          },
          null,
          2
        );
      }
    }
  ];
}