import * as monitoringSDK from "oci-monitoring";
import { z } from "zod";
import type { OCIClients } from "../oci-client.js";

export function monitoringTools(clients: OCIClients) {
  const { monitoring, compartmentId } = clients;

  return [
    // ── Consultar Métricas ─────────────────────────────
    {
      name: "oci_query_metrics",
      description:
        "Consulta métricas de CPU, memória, rede de instâncias OCI",
      schema: z.object({
        query: z
          .string()
          .describe(
            "Query MQL ex: CpuUtilization[1m].mean()"
          ),
        namespace: z
          .string()
          .optional()
          .default("oci_computeagent")
          .describe("Namespace da métrica"),
        compartmentId: z.string().optional(),
        startTime: z
          .string()
          .optional()
          .describe("ISO 8601 ex: 2024-01-01T00:00:00Z"),
        endTime: z
          .string()
          .optional()
          .describe("ISO 8601 ex: 2024-01-01T01:00:00Z")
      }),
      handler: async (args: {
        query: string;
        namespace: string;
        compartmentId?: string;
        startTime?: string;
        endTime?: string;
      }) => {
        const now = new Date();
        const oneHourAgo = new Date(
          now.getTime() - 60 * 60 * 1000
        );

        const response =
          await monitoring.summarizeMetricsData({
            compartmentId:
              args.compartmentId || compartmentId,
            summarizeMetricsDataDetails: {
              namespace: args.namespace,
              query: args.query,
              startTime: args.startTime
                ? new Date(args.startTime)
                : oneHourAgo,
              endTime: args.endTime
                ? new Date(args.endTime)
                : now
            }
          });

        return JSON.stringify(
          response.items.map(item => ({
            name: item.name,
            namespace: item.namespace,
            dimensions: item.dimensions,
            datapoints: item.aggregatedDatapoints?.map(
              dp => ({
                timestamp: dp.timestamp,
                value: dp.value
              })
            )
          })),
          null,
          2
        );
      }
    },

    // ── Métricas Rápidas de Instância ──────────────────
    {
      name: "oci_instance_metrics_summary",
      description:
        "Resumo rápido de CPU e memória de uma instância na última hora",
      schema: z.object({
        instanceId: z
          .string()
          .describe("OCID da instância"),
        compartmentId: z.string().optional()
      }),
      handler: async (args: {
        instanceId: string;
        compartmentId?: string;
      }) => {
        const compId =
          args.compartmentId || compartmentId;
        const now = new Date();
        const oneHourAgo = new Date(
          now.getTime() - 60 * 60 * 1000
        );

        const queries = [
          "CpuUtilization[5m].mean()",
          "MemoryUtilization[5m].mean()",
          "NetworkBytesIn[5m].sum()",
          "NetworkBytesOut[5m].sum()"
        ];

        const results = await Promise.all(
          queries.map(async q => {
            try {
              const r =
                await monitoring.summarizeMetricsData({
                  compartmentId: compId,
                  summarizeMetricsDataDetails: {
                    namespace: "oci_computeagent",
                    query: `${q}{resourceId = "${args.instanceId}"}`,
                    startTime: oneHourAgo,
                    endTime: now
                  }
                });
              return { query: q, data: r.items };
            } catch {
              return { query: q, data: [], error: true };
            }
          })
        );

        return JSON.stringify(
          { instanceId: args.instanceId, metrics: results },
          null,
          2
        );
      }
    }
  ];
}