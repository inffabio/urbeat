import * as core from "oci-core";
import { z } from "zod";
import type { OCIClients } from "../oci-client.js";

// ── Tipo REAL confirmado pelo código fonte ─────────────
//
// instanceActionRequest = {
//   instanceId:    string   ← obrigatório (path)
//   action:        string   ← obrigatório (query)
//   opcRetryToken: string   ← opcional   (header)
//   ifMatch:       string   ← opcional   (header)
// }
//
// O SDK NÃO usa enum — usa string pura no queryParam!

interface InstanceActionRequest {
  instanceId:     string;
  action:         string;
  opcRetryToken?: string;
  ifMatch?:       string;
}

// ── Ações válidas documentadas pela OCI API ────────────
// Ref: docs.oracle.com/iaas/api/#/en/iaas/Instance/InstanceAction
const ACTION = {
  START:                        "START",
  STOP:                         "STOP",
  SOFTSTOP:                     "SOFTSTOP",
  RESET:                        "RESET",
  SOFTRESET:                    "SOFTRESET",
  SENDDIAGNOSTICINTERRUPT:      "SENDDIAGNOSTICINTERRUPT",
  DIAGNOSTICREBOOT:             "DIAGNOSTICREBOOT",
  REBOOTMIGRATE:                "REBOOTMIGRATE",
  SUSPEND:                      "SUSPEND",
  RESUME:                       "RESUME",
} as const;

type ActionValue =
  typeof ACTION[keyof typeof ACTION];

// ── Helper: formatar instância ─────────────────────────
function formatInstance(
  i: core.models.Instance
): Record<string, unknown> {
  return {
    id:                 i.id,
    name:               i.displayName,
    shape:              i.shape,
    state:              i.lifecycleState,
    region:             i.region,
    availabilityDomain: i.availabilityDomain,
    faultDomain:        i.faultDomain,
    ocpus:              i.shapeConfig?.ocpus,
    memoryGb:           i.shapeConfig?.memoryInGBs,
    networkGbps:
      i.shapeConfig?.networkingBandwidthInGbps,
    timeCreated: i.timeCreated,
  };
}

// ── Helper: executar action com log ───────────────────
async function executeAction(
  compute:    core.ComputeClient,
  instanceId: string,
  action:     ActionValue,
  status:     string,
  extra?:     Record<string, unknown>
): Promise<string> {
  const request: InstanceActionRequest = {
    instanceId,
    action,
  };

  // ✅ Cast necessário pois o tipo interno do SDK
  //    não exporta InstanceActionRequest publicamente
  //    mas aceita exatamente este shape
  await compute.instanceAction(
    request as Parameters<
      typeof compute.instanceAction
    >[0]
  );

  return JSON.stringify(
    {
      status,
      instanceId,
      action,
      ...extra,
      tip: "Use oci_get_instance para verificar estado",
    },
    null,
    2
  );
}

export function computeTools(clients: OCIClients) {
  const { compute, compartmentId } = clients;

  return [

    // ── Listar Instâncias ────────────────────────────
    {
      name: "oci_list_instances",
      description:
        "Lista todas as instâncias Compute. " +
        "Mostra nome, shape, estado, CPU, RAM e região.",
      schema: z.object({
        compartmentId: z
          .string()
          .optional()
          .describe("OCID do compartment"),
        lifecycleState: z
          .enum([
            "MOVING",
            "PROVISIONING",
            "RUNNING",
            "STARTING",
            "STOPPING",
            "STOPPED",
            "CREATING_IMAGE",
            "TERMINATING",
            "TERMINATED",
          ])
          .optional()
          .describe(
            "Filtrar por estado. " +
            "Omitir para listar todas."
          ),
      }),

      handler: async (args: {
        compartmentId?: string;
        lifecycleState?: string;
      }) => {
        const response = await compute.listInstances({
          compartmentId:
            args.compartmentId || compartmentId,
          lifecycleState:
            args.lifecycleState as
              Parameters<
                typeof compute.listInstances
              >[0]["lifecycleState"],
        });

        return JSON.stringify(
          {
            total: response.items.length,
            instances:
              response.items.map(formatInstance),
          },
          null,
          2
        );
      },
    },

    // ── Detalhes de Instância ────────────────────────
    {
      name: "oci_get_instance",
      description:
        "Obtém todos os detalhes de uma instância " +
        "incluindo estado atual, CPU, RAM e rede.",
      schema: z.object({
        instanceId: z
          .string()
          .describe("OCID da instância"),
      }),

      handler: async (args: {
        instanceId: string;
      }) => {
        const response = await compute.getInstance({
          instanceId: args.instanceId,
        });

        return JSON.stringify(
          formatInstance(response.instance),
          null,
          2
        );
      },
    },

    // ── Iniciar ──────────────────────────────────────
    {
      name: "oci_start_instance",
      description:
        "Inicia uma instância parada. " +
        "Estado: STOPPED → STARTING → RUNNING.",
      schema: z.object({
        instanceId: z
          .string()
          .describe("OCID da instância"),
      }),

      handler: async (args: {
        instanceId: string;
      }) =>
        executeAction(
          compute,
          args.instanceId,
          ACTION.START,
          "iniciando"
        ),
    },

    // ── Parar (gracioso) ─────────────────────────────
    {
      name: "oci_stop_instance",
      description:
        "Para uma instância em execução. " +
        "force=false → SOFTSTOP: envia sinal de " +
        "desligamento ao SO (recomendado). " +
        "force=true  → STOP: corta energia imediatamente " +
        "(risco de corrupção de dados).",
      schema: z.object({
        instanceId: z
          .string()
          .describe("OCID da instância"),
        force: z
          .boolean()
          .optional()
          .default(false)
          .describe(
            "false = gracioso (SOFTSTOP) | " +
            "true = forçado (STOP)"
          ),
      }),

      handler: async (args: {
        instanceId: string;
        force:      boolean;
      }) => {
        const action: ActionValue = args.force
          ? ACTION.STOP
          : ACTION.SOFTSTOP;

        return executeAction(
          compute,
          args.instanceId,
          action,
          "parando",
          { force: args.force }
        );
      },
    },

    // ── Reiniciar ────────────────────────────────────
    {
      name: "oci_reboot_instance",
      description:
        "Reinicia uma instância. " +
        "force=false → SOFTRESET: reboot gracioso " +
        "(recomendado para produção). " +
        "force=true  → RESET: reinício imediato " +
        "(equivale a reset de hardware).",
      schema: z.object({
        instanceId: z
          .string()
          .describe("OCID da instância"),
        force: z
          .boolean()
          .optional()
          .default(false)
          .describe(
            "false = gracioso (SOFTRESET) | " +
            "true = forçado (RESET)"
          ),
      }),

      handler: async (args: {
        instanceId: string;
        force:      boolean;
      }) => {
        const action: ActionValue = args.force
          ? ACTION.RESET
          : ACTION.SOFTRESET;

        return executeAction(
          compute,
          args.instanceId,
          action,
          "reiniciando",
          { force: args.force }
        );
      },
    },

    // ── Suspend / Resume (para Burstable) ────────────
    {
      name: "oci_suspend_instance",
      description:
        "Suspende uma instância burstable. " +
        "Mantém o estado em memória (baixo custo).",
      schema: z.object({
        instanceId: z
          .string()
          .describe("OCID da instância burstable"),
      }),

      handler: async (args: {
        instanceId: string;
      }) =>
        executeAction(
          compute,
          args.instanceId,
          ACTION.SUSPEND,
          "suspendendo"
        ),
    },

    {
      name: "oci_resume_instance",
      description:
        "Retoma uma instância burstable suspensa.",
      schema: z.object({
        instanceId: z
          .string()
          .describe("OCID da instância"),
      }),

      handler: async (args: {
        instanceId: string;
      }) =>
        executeAction(
          compute,
          args.instanceId,
          ACTION.RESUME,
          "retomando"
        ),
    },

    // ── Diagnostic Reboot ────────────────────────────
    {
      name: "oci_diagnostic_reboot",
      description:
        "Reboot de diagnóstico. Útil quando a " +
        "instância não responde a SOFTRESET. " +
        "Coleta informações de diagnóstico antes " +
        "de reiniciar.",
      schema: z.object({
        instanceId: z
          .string()
          .describe("OCID da instância"),
      }),

      handler: async (args: {
        instanceId: string;
      }) =>
        executeAction(
          compute,
          args.instanceId,
          ACTION.DIAGNOSTICREBOOT,
          "diagnostic-reboot",
          {
            warning:
              "Operação de diagnóstico — " +
              "use apenas quando necessário",
          }
        ),
    },

    // ── Listar Shapes ────────────────────────────────
    {
      name: "oci_list_shapes",
      description:
        "Lista shapes disponíveis no compartment. " +
        "Inclui ARM64 Ampere A1 (Always Free elegível: " +
        "até 4 OCPUs e 24 GB RAM).",
      schema: z.object({
        compartmentId: z.string().optional(),
      }),

      handler: async (args: {
        compartmentId?: string;
      }) => {
        const response = await compute.listShapes({
          compartmentId:
            args.compartmentId || compartmentId,
        });

        const all = response.items.map(s => ({
          name:        s.shape,
          ocpus:       s.ocpus,
          memoryGb:    s.memoryInGBs,
          isFlexible:  s.isFlexible,
          processor:   s.processorDescription,
          networkGbps: s.networkingBandwidthInGbps,
        }));

        return JSON.stringify(
          {
            arm64Ampere: all.filter(s =>
              s.name?.includes("A1")
            ),
            x86:         all.filter(s =>
              !s.name?.includes("A1")
            ),
            total:       all.length,
          },
          null,
          2
        );
      },
    },

    // ── Listar VNICs ─────────────────────────────────
    {
      name: "oci_list_vnic_attachments",
      description:
        "Lista interfaces de rede (VNICs) " +
        "de uma instância.",
      schema: z.object({
        instanceId: z
          .string()
          .describe("OCID da instância"),
        compartmentId: z.string().optional(),
      }),

      handler: async (args: {
        instanceId:    string;
        compartmentId?: string;
      }) => {
        const response =
          await compute.listVnicAttachments({
            compartmentId:
              args.compartmentId || compartmentId,
            instanceId: args.instanceId,
          });

        return JSON.stringify(
          {
            total: response.items.length,
            vnics: response.items.map(v => ({
              id:       v.id,
              vnicId:   v.vnicId,
              state:    v.lifecycleState,
              subnetId: v.subnetId,
              nicIndex: v.nicIndex,
            })),
          },
          null,
          2
        );
      },
    },

    // ── Listar Volume Attachments ────────────────────
    {
      name: "oci_list_volume_attachments",
      description:
        "Lista volumes de bloco anexados " +
        "a uma instância.",
      schema: z.object({
        instanceId: z
          .string()
          .describe("OCID da instância"),
        compartmentId: z.string().optional(),
      }),

      handler: async (args: {
        instanceId:    string;
        compartmentId?: string;
      }) => {
        const response =
          await compute.listVolumeAttachments({
            compartmentId:
              args.compartmentId || compartmentId,
            instanceId: args.instanceId,
          });

        return JSON.stringify(
          response.items.map(v => ({
            id:             v.id,
            volumeId:       v.volumeId,
            state:          v.lifecycleState,
            displayName:    v.displayName,
            attachmentType: v.attachmentType,
            isReadOnly:     v.isReadOnly,
          })),
          null,
          2
        );
      },
    },

  ];
}