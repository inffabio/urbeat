import * as core from "oci-core";
import { z } from "zod";
import type { OCIClients } from "../oci-client.js";

export function blockstorageTools(clients: OCIClients) {
  const { blockstorage, compartmentId } = clients;

  return [

    // ── Listar Volumes ───────────────────────────────
    {
      name: "oci_list_volumes",
      description:
        "Lista volumes de bloco (discos) do compartment",
      schema: z.object({
        compartmentId: z.string().optional(),
        lifecycleState: z
          .enum([
            "PROVISIONING",
            "RESTORING",
            "AVAILABLE",
            "TERMINATING",
            "TERMINATED",
            "FAULTY",
          ])
          .optional(),
      }),

      handler: async (args: {
        compartmentId?: string;
        lifecycleState?: string;
      }) => {
        // ✅ core.requests.ListVolumesRequest
        const request: core.requests.ListVolumesRequest =
          {
            compartmentId:
              args.compartmentId || compartmentId,
            lifecycleState: args.lifecycleState as
              | core.models.Volume.LifecycleState
              | undefined,
          };

        const response =
          await blockstorage.listVolumes(request);

        return JSON.stringify(
          {
            total: response.items.length,
            volumes: response.items.map(v => ({
              id:            v.id,
              name:          v.displayName,
              state:         v.lifecycleState,
              sizeGb:        v.sizeInGBs,
              sizeMb:        v.sizeInMBs,
              vpusPerGb:     v.vpusPerGB,
              isAutoTuned:   v.isAutoTuneEnabled,
              timeCreated:   v.timeCreated,
              availabilityDomain:
                v.availabilityDomain,
            })),
          },
          null,
          2
        );
      },
    },

    // ── Detalhes de Volume ───────────────────────────
    {
      name: "oci_get_volume",
      description:
        "Obtém detalhes de um volume de bloco específico",
      schema: z.object({
        volumeId: z
          .string()
          .describe("OCID do volume"),
      }),

      handler: async (args: { volumeId: string }) => {
        const request: core.requests.GetVolumeRequest =
          {
            volumeId: args.volumeId,
          };

        const response =
          await blockstorage.getVolume(request);

        const v = response.volume;

        return JSON.stringify(
          {
            id:          v.id,
            name:        v.displayName,
            state:       v.lifecycleState,
            sizeGb:      v.sizeInGBs,
            vpusPerGb:   v.vpusPerGB,
            isBootVolume: false,
            timeCreated: v.timeCreated,
            availabilityDomain:
              v.availabilityDomain,
          },
          null,
          2
        );
      },
    },

    // ── Listar Boot Volumes ──────────────────────────
    {
      name: "oci_list_boot_volumes",
      description:
        "Lista volumes de boot (disco do SO) do compartment",
      schema: z.object({
        availabilityDomain: z
          .string()
          .optional()
          .describe(
            "AD ex: bHBf:SA-SAOPAULO-1-AD-1"
          ),
        compartmentId: z.string().optional(),
      }),

      handler: async (args: {
        availabilityDomain?: string;
        compartmentId?: string;
      }) => {
        const request: core.requests.ListBootVolumesRequest =
          {
            compartmentId:
              args.compartmentId || compartmentId,
            availabilityDomain:
              args.availabilityDomain,
          };

        const response =
          await blockstorage.listBootVolumes(request);

        return JSON.stringify(
          {
            total: response.items.length,
            bootVolumes: response.items.map(bv => ({
              id:          bv.id,
              name:        bv.displayName,
              state:       bv.lifecycleState,
              sizeGb:      bv.sizeInGBs,
              vpusPerGb:   bv.vpusPerGB,
              imageId:     bv.imageId,
              timeCreated: bv.timeCreated,
              availabilityDomain:
                bv.availabilityDomain,
            })),
          },
          null,
          2
        );
      },
    },

    // ── Listar Backups de Volume ─────────────────────
    {
      name: "oci_list_volume_backups",
      description:
        "Lista backups de volumes de bloco",
      schema: z.object({
        volumeId: z
          .string()
          .optional()
          .describe(
            "Filtrar por volume específico (opcional)"
          ),
        compartmentId: z.string().optional(),
      }),

      handler: async (args: {
        volumeId?: string;
        compartmentId?: string;
      }) => {
        const request: core.requests.ListVolumeBackupsRequest =
          {
            compartmentId:
              args.compartmentId || compartmentId,
            volumeId: args.volumeId,
          };

        const response =
          await blockstorage.listVolumeBackups(request);

        return JSON.stringify(
          {
            total: response.items.length,
            backups: response.items.map(b => ({
              id:          b.id,
              name:        b.displayName,
              state:       b.lifecycleState,
              type:        b.type,
              sizeGb:      b.sizeInGBs,
              uniqueSizeGb: b.uniqueSizeInGBs,
              timeCreated: b.timeCreated,
              expirationTime: b.expirationTime,
            })),
          },
          null,
          2
        );
      },
    },

  ];
}