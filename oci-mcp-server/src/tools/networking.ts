import * as core from "oci-core";
import { z } from "zod";
import type { OCIClients } from "../oci-client.js";

export function networkingTools(clients: OCIClients) {
  const { virtualNetwork, compartmentId } = clients;

  return [

    // ── Listar VCNs ──────────────────────────────────
    {
      name: "oci_list_vcns",
      description:
        "Lista todas as VCNs (redes virtuais) " +
        "do compartment com CIDR e estado.",
      schema: z.object({
        compartmentId: z
          .string()
          .optional()
          .describe("OCID do compartment"),
        sortOrder: z
          .enum(["ASC", "DESC"])
          .optional()
          .default("ASC")
          .describe("Ordenação ASC ou DESC"),
      }),

      handler: async (args: {
        compartmentId?: string;
        sortOrder?:     "ASC" | "DESC";
      }) => {
        const response =
          await virtualNetwork.listVcns({
            compartmentId:
              args.compartmentId || compartmentId,
            // ✅ Usando enum real do SDK
            sortOrder: args.sortOrder === "DESC"
              ? core.requests.ListVcnsRequest
                  .SortOrder.Desc
              : core.requests.ListVcnsRequest
                  .SortOrder.Asc,
          });

        return JSON.stringify(
          {
            total: response.items.length,
            vcns: response.items.map(v => ({
              id:          v.id,
              name:        v.displayName,
              cidr:        v.cidrBlock,
              cidrBlocks:  v.cidrBlocks,
              state:       v.lifecycleState,
              dnsLabel:    v.dnsLabel,
              timeCreated: v.timeCreated,
            })),
          },
          null,
          2
        );
      },
    },

    // ── Listar IPs Públicos ──────────────────────────
    {
      name: "oci_list_public_ips",
      description:
        "Lista IPs públicos do compartment. " +
        "lifetime=RESERVED → IPs fixos permanentes. " +
        "lifetime=EPHEMERAL → IPs temporários.",
      schema: z.object({
        compartmentId: z
          .string()
          .optional()
          .describe("OCID do compartment"),
        lifetime: z
          .enum(["RESERVED", "EPHEMERAL"])
          .optional()
          .default("RESERVED")
          .describe(
            "RESERVED = IPs fixos | " +
            "EPHEMERAL = IPs temporários"
          ),
      }),

      handler: async (args: {
        compartmentId?: string;
        lifetime:       "RESERVED" | "EPHEMERAL";
      }) => {
        // ✅ Enums confirmados pelo inspect-enums.mjs:
        // ListPublicIpsRequest.Scope.Region    = "REGION"
        // ListPublicIpsRequest.Lifetime.Reserved  = "RESERVED"
        // ListPublicIpsRequest.Lifetime.Ephemeral = "EPHEMERAL"

        const { Scope, Lifetime } =
          core.requests.ListPublicIpsRequest;

        const response =
          await virtualNetwork.listPublicIps({
            compartmentId:
              args.compartmentId || compartmentId,
            scope:    Scope.Region,
            lifetime: args.lifetime === "RESERVED"
              ? Lifetime.Reserved
              : Lifetime.Ephemeral,
          });

        return JSON.stringify(
          {
            total: response.items.length,
            publicIps: response.items.map(ip => ({
              id:               ip.id,
              name:             ip.displayName,
              ipAddress:        ip.ipAddress,
              state:            ip.lifecycleState,
              lifetime:         ip.lifetime,
              scope:            ip.scope,
              assignedEntityId: ip.assignedEntityId,
              timeCreated:      ip.timeCreated,
            })),
          },
          null,
          2
        );
      },
    },

    // ── Listar Subnets ───────────────────────────────
    {
      name: "oci_list_subnets",
      description:
        "Lista subnets de um compartment ou VCN. " +
        "Mostra CIDR, estado e availability domain.",
      schema: z.object({
        vcnId: z
          .string()
          .optional()
          .describe("OCID da VCN (opcional)"),
        compartmentId: z
          .string()
          .optional()
          .describe("OCID do compartment"),
        sortOrder: z
          .enum(["ASC", "DESC"])
          .optional()
          .default("ASC"),
      }),

      handler: async (args: {
        vcnId?:         string;
        compartmentId?: string;
        sortOrder?:     "ASC" | "DESC";
      }) => {
        const { SortOrder } =
          core.requests.ListSubnetsRequest;

        const response =
          await virtualNetwork.listSubnets({
            compartmentId:
              args.compartmentId || compartmentId,
            vcnId: args.vcnId,
            sortOrder: args.sortOrder === "DESC"
              ? SortOrder.Desc
              : SortOrder.Asc,
          });

        return JSON.stringify(
          {
            total: response.items.length,
            subnets: response.items.map(s => ({
              id:                 s.id,
              name:               s.displayName,
              cidr:               s.cidrBlock,
              state:              s.lifecycleState,
              availabilityDomain: s.availabilityDomain,
              dnsLabel:           s.dnsLabel,
              vcnId:              s.vcnId,
              prohibitPublicIp:
                s.prohibitPublicIpOnVnic,
              timeCreated:        s.timeCreated,
            })),
          },
          null,
          2
        );
      },
    },

    // ── Listar Security Lists ────────────────────────
    {
      name: "oci_list_security_lists",
      description:
        "Lista regras de firewall (Security Lists) " +
        "de uma VCN com regras de entrada e saída.",
      schema: z.object({
        vcnId: z
          .string()
          .describe("OCID da VCN"),
        compartmentId: z
          .string()
          .optional()
          .describe("OCID do compartment"),
        sortOrder: z
          .enum(["ASC", "DESC"])
          .optional()
          .default("ASC"),
      }),

      handler: async (args: {
        vcnId:          string;
        compartmentId?: string;
        sortOrder?:     "ASC" | "DESC";
      }) => {
        const { SortOrder } =
          core.requests.ListSecurityListsRequest;

        const response =
          await virtualNetwork.listSecurityLists({
            compartmentId:
              args.compartmentId || compartmentId,
            vcnId: args.vcnId,
            sortOrder: args.sortOrder === "DESC"
              ? SortOrder.Desc
              : SortOrder.Asc,
          });

        return JSON.stringify(
          {
            total: response.items.length,
            securityLists: response.items.map(sl => ({
              id:    sl.id,
              name:  sl.displayName,
              state: sl.lifecycleState,
              ingressRulesCount:
                sl.ingressSecurityRules?.length ?? 0,
              egressRulesCount:
                sl.egressSecurityRules?.length ?? 0,
              ingressRules:
                sl.ingressSecurityRules?.map(r => ({
                  protocol:    r.protocol,
                  source:      r.source,
                  sourceType:  r.sourceType,
                  isStateless: r.isStateless,
                  tcpPortMin:
                    r.tcpOptions
                      ?.destinationPortRange?.min,
                  tcpPortMax:
                    r.tcpOptions
                      ?.destinationPortRange?.max,
                })),
            })),
          },
          null,
          2
        );
      },
    },

    // ── Listar Internet Gateways ─────────────────────
    {
      name: "oci_list_internet_gateways",
      description:
        "Lista Internet Gateways de uma VCN. " +
        "Necessário para acesso público à internet.",
      schema: z.object({
        vcnId: z
          .string()
          .describe("OCID da VCN"),
        compartmentId: z
          .string()
          .optional()
          .describe("OCID do compartment"),
        sortOrder: z
          .enum(["ASC", "DESC"])
          .optional()
          .default("ASC"),
      }),

      handler: async (args: {
        vcnId:          string;
        compartmentId?: string;
        sortOrder?:     "ASC" | "DESC";
      }) => {
        const { SortOrder } =
          core.requests.ListInternetGatewaysRequest;

        const response =
          await virtualNetwork.listInternetGateways({
            compartmentId:
              args.compartmentId || compartmentId,
            vcnId: args.vcnId,
            sortOrder: args.sortOrder === "DESC"
              ? SortOrder.Desc
              : SortOrder.Asc,
          });

        return JSON.stringify(
          {
            total: response.items.length,
            internetGateways: response.items.map(
              ig => ({
                id:          ig.id,
                name:        ig.displayName,
                state:       ig.lifecycleState,
                isEnabled:   ig.isEnabled,
                timeCreated: ig.timeCreated,
              })
            ),
          },
          null,
          2
        );
      },
    },

    // ── Listar NAT Gateways ──────────────────────────
    {
      name: "oci_list_nat_gateways",
      description:
        "Lista NAT Gateways de uma VCN. " +
        "Permite saída à internet sem IP público.",
      schema: z.object({
        vcnId: z
          .string()
          .optional()
          .describe("OCID da VCN (opcional)"),
        compartmentId: z
          .string()
          .optional()
          .describe("OCID do compartment"),
      }),

      handler: async (args: {
        vcnId?:         string;
        compartmentId?: string;
      }) => {
        const response =
          await virtualNetwork.listNatGateways({
            compartmentId:
              args.compartmentId || compartmentId,
            vcnId: args.vcnId,
          });

        return JSON.stringify(
          {
            total: response.items.length,
            natGateways: response.items.map(ng => ({
              id:          ng.id,
              name:        ng.displayName,
              state:       ng.lifecycleState,
              natIp:       ng.natIp,
              blockTraffic: ng.blockTraffic,
              timeCreated: ng.timeCreated,
            })),
          },
          null,
          2
        );
      },
    },

    // ── Listar Route Tables ──────────────────────────
    {
      name: "oci_list_route_tables",
      description:
        "Lista tabelas de roteamento de uma VCN.",
      schema: z.object({
        vcnId: z
          .string()
          .describe("OCID da VCN"),
        compartmentId: z
          .string()
          .optional()
          .describe("OCID do compartment"),
      }),

      handler: async (args: {
        vcnId:          string;
        compartmentId?: string;
      }) => {
        const response =
          await virtualNetwork.listRouteTables({
            compartmentId:
              args.compartmentId || compartmentId,
            vcnId: args.vcnId,
          });

        return JSON.stringify(
          {
            total: response.items.length,
            routeTables: response.items.map(rt => ({
              id:    rt.id,
              name:  rt.displayName,
              state: rt.lifecycleState,
              routeRulesCount:
                rt.routeRules?.length ?? 0,
              routeRules: rt.routeRules?.map(r => ({
                destination:     r.destination,
                destinationType: r.destinationType,
                networkEntityId: r.networkEntityId,
                description:     r.description,
              })),
            })),
          },
          null,
          2
        );
      },
    },

    // ── Obter VNIC ───────────────────────────────────
    {
      name: "oci_get_vnic",
      description:
        "Obtém detalhes de uma VNIC (interface de rede) " +
        "incluindo IPs público e privado.",
      schema: z.object({
        vnicId: z
          .string()
          .describe("OCID da VNIC"),
      }),

      handler: async (args: { vnicId: string }) => {
        const response =
          await virtualNetwork.getVnic({
            vnicId: args.vnicId,
          });

        const v = response.vnic;

        return JSON.stringify(
          {
            id:            v.id,
            name:          v.displayName,
            state:         v.lifecycleState,
            privateIp:     v.privateIp,
            publicIp:      v.publicIp,
            macAddress:    v.macAddress,
            subnetId:      v.subnetId,
            isPrimary:     v.isPrimary,
            hostnameLabel: v.hostnameLabel,
            timeCreated:   v.timeCreated,
          },
          null,
          2
        );
      },
    },

    // ── Listar Network Security Groups ───────────────
    {
      name: "oci_list_network_security_groups",
      description:
        "Lista Network Security Groups (NSGs) " +
        "de uma VCN. NSGs são firewalls por VNIC.",
      schema: z.object({
        vcnId: z
          .string()
          .optional()
          .describe("OCID da VCN (opcional)"),
        compartmentId: z
          .string()
          .optional()
          .describe("OCID do compartment"),
      }),

      handler: async (args: {
        vcnId?:         string;
        compartmentId?: string;
      }) => {
        const response =
          await virtualNetwork.listNetworkSecurityGroups(
            {
              compartmentId:
                args.compartmentId || compartmentId,
              vcnId: args.vcnId,
            }
          );

        return JSON.stringify(
          {
            total: response.items.length,
            nsgs: response.items.map(nsg => ({
              id:          nsg.id,
              name:        nsg.displayName,
              state:       nsg.lifecycleState,
              vcnId:       nsg.vcnId,
              timeCreated: nsg.timeCreated,
            })),
          },
          null,
          2
        );
      },
    },

    // ── Listar NSG Security Rules ────────────────────
    {
      name: "oci_list_nsg_rules",
      description:
        "Lista regras de segurança de um NSG. " +
        "direction=INGRESS → entrada | " +
        "direction=EGRESS → saída.",
      schema: z.object({
        networkSecurityGroupId: z
          .string()
          .describe("OCID do NSG"),
        direction: z
          .enum(["INGRESS", "EGRESS"])
          .optional()
          .describe(
            "INGRESS = entrada | EGRESS = saída"
          ),
      }),

      handler: async (args: {
        networkSecurityGroupId: string;
        direction?: "INGRESS" | "EGRESS";
      }) => {
        // ✅ Enum confirmado:
        // ListNetworkSecurityGroupSecurityRulesRequest
        //   .Direction.Ingress = "INGRESS"
        //   .Direction.Egress  = "EGRESS"
        const { Direction } =
          core.requests
            .ListNetworkSecurityGroupSecurityRulesRequest;

        const response =
          await virtualNetwork
            .listNetworkSecurityGroupSecurityRules({
              networkSecurityGroupId:
                args.networkSecurityGroupId,
              direction: args.direction
                ? args.direction === "INGRESS"
                  ? Direction.Ingress
                  : Direction.Egress
                : undefined,
            });

        return JSON.stringify(
          {
            total: response.items.length,
            rules: response.items.map(r => ({
              id:          r.id,
              direction:   r.direction,
              protocol:    r.protocol,
              isStateless: r.isStateless,
              source:      r.source,
              destination: r.destination,
              tcpPortMin:
                r.tcpOptions
                  ?.destinationPortRange?.min,
              tcpPortMax:
                r.tcpOptions
                  ?.destinationPortRange?.max,
              description: r.description,
            })),
          },
          null,
          2
        );
      },
    },

  ];
}