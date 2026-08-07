import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import * as dotenv from "dotenv";
import { createOCIClients } from "./oci-client.js";
import { computeTools }      from "./tools/compute.js";
import { networkingTools }   from "./tools/networking.js";
import { monitoringTools }   from "./tools/monitoring.js";
import { storageTools }      from "./tools/storage.js";
import { blockstorageTools } from "./tools/blockstorage.js";
import { vaultTools }      from "./tools/vault.js";

dotenv.config();

console.error("🔌 Inicializando OCI MCP Server...");

const clients = createOCIClients();

// ── Registrar todas as ferramentas ────────────────────
const allTools = [
  ...computeTools(clients),
  ...blockstorageTools(clients),
  ...networkingTools(clients),
  ...monitoringTools(clients),
  ...storageTools(clients),
  ...vaultTools(clients),
];

console.error(
  `✅ ${allTools.length} ferramentas registradas:\n` +
  allTools.map(t => `   • ${t.name}`).join("\n")
);

// ── MCP Server ────────────────────────────────────────
const server = new Server(
  {
    name:    "oci-mcp-server",
    version: "1.0.0",
  },
  {
    capabilities: { tools: {} },
  }
);

// ── Listar ferramentas ────────────────────────────────
server.setRequestHandler(
  ListToolsRequestSchema,
  async () => ({
    tools: allTools.map(tool => ({
      name:        tool.name,
      description: tool.description,
      inputSchema: {
        type: "object" as const,
        properties: Object.fromEntries(
          Object.entries(tool.schema.shape).map(
            ([key, val]) => {
              const v = val as {
                description?: string;
                _def?: { typeName?: string };
              };
              return [
                key,
                {
                  type: "string",
                  description:
                    v.description || key,
                },
              ];
            }
          )
        ),
      },
    })),
  })
);

// ── Executar ferramenta ───────────────────────────────
server.setRequestHandler(
  CallToolRequestSchema,
  async request => {
    const tool = allTools.find(
      t => t.name === request.params.name
    );

    if (!tool) {
      return {
        content: [{
          type: "text" as const,
          text: `❌ Ferramenta não encontrada: ${request.params.name}`,
        }],
        isError: true,
      };
    }

    try {
      console.error(
        `🔧 Executando: ${tool.name}`,
        JSON.stringify(request.params.arguments)
      );

      const input = tool.schema.parse(
        request.params.arguments ?? {}
      );

      const result = await tool.handler(
        input as never
      );

      return {
        content: [{
          type: "text" as const,
          text: typeof result === "string"
            ? result
            : JSON.stringify(result, null, 2),
        }],
      };

    } catch (error) {
      const msg = error instanceof Error
        ? `${error.message}\n${error.stack}`
        : String(error);

      console.error(
        `❌ Erro em ${tool.name}:`, msg
      );

      return {
        content: [{
          type: "text" as const,
          text: `❌ Erro: ${msg}`,
        }],
        isError: true,
      };
    }
  }
);

// ── Iniciar servidor ──────────────────────────────────
const transport = new StdioServerTransport();
await server.connect(transport);

console.error(
  "🚀 OCI MCP Server pronto — aguardando requisições..."
);