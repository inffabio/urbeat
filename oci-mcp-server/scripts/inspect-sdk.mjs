// scripts/inspect-sdk.mjs
// Roda com: node scripts/inspect-sdk.mjs

import * as core from "oci-core";

console.log("\n╔══════════════════════════════════════╗");
console.log("║     INSPECIONANDO oci-core SDK       ║");
console.log("╚══════════════════════════════════════╝\n");

// ── Top Level ─────────────────────────────────────────
console.log("📦 TOP LEVEL EXPORTS:");
console.log(Object.keys(core).join(" | "));

// ── Models relacionados a Instance ───────────────────
console.log("\n🖥️  MODELS com 'Instance':");
Object.keys(core.models)
  .filter(k => k.toLowerCase().includes("instance"))
  .forEach(k => console.log(`  → ${k}`));

// ── Models relacionados a Action ──────────────────────
console.log("\n⚡ MODELS com 'Action':");
Object.keys(core.models)
  .filter(k => k.toLowerCase().includes("action"))
  .forEach(k => {
    console.log(`  → ${k}`);
    const model = core.models[k];
    if (model && typeof model === "object") {
      console.log(
        "     valores:",
        JSON.stringify(model)
      );
    }
  });

// ── Lifecycle States ──────────────────────────────────
console.log("\n🔄 MODELS com 'LifecycleState':");
Object.keys(core.models)
  .filter(k => k.toLowerCase().includes("lifecycle"))
  .forEach(k => {
    const model = core.models[k];
    console.log(`  → ${k}:`, JSON.stringify(model));
  });

// ── Requests de Compute ───────────────────────────────
console.log("\n📤 REQUESTS com 'Instance':");
Object.keys(core.requests)
  .filter(k => k.toLowerCase().includes("instance"))
  .forEach(k => console.log(`  → ${k}`));

// ── Verificar InstanceAction especificamente ──────────
console.log("\n🎯 VERIFICANDO InstanceAction:");
if (core.models.InstanceAction) {
  console.log(
    "  core.models.InstanceAction =",
    JSON.stringify(core.models.InstanceAction, null, 4)
  );
} else {
  console.log("  ❌ core.models.InstanceAction NÃO existe");
}

// ── Verificar request de instanceAction ───────────────
console.log("\n🎯 VERIFICANDO InstanceActionRequest:");
if (core.requests.InstanceActionRequest) {
  console.log(
    "  Campos:",
    Object.keys(
      core.requests.InstanceActionRequest
    ).join(", ")
  );
} else {
  console.log(
    "  ❌ core.requests.InstanceActionRequest NÃO existe"
  );
}

// ── ComputeClient métodos ─────────────────────────────
console.log("\n🔧 MÉTODOS do ComputeClient:");
const proto = core.ComputeClient.prototype;
Object.getOwnPropertyNames(proto)
  .filter(m => m !== "constructor")
  .filter(m => m.toLowerCase().includes("instance"))
  .forEach(m => console.log(`  → ${m}()`));

// ── VirtualNetworkClient métodos ──────────────────────
console.log("\n🌐 MÉTODOS do VirtualNetworkClient:");
const vnetProto = core.VirtualNetworkClient.prototype;
Object.getOwnPropertyNames(vnetProto)
  .filter(m => m !== "constructor")
  .filter(m =>
    m.toLowerCase().includes("ip") ||
    m.toLowerCase().includes("vcn") ||
    m.toLowerCase().includes("subnet")
  )
  .forEach(m => console.log(`  → ${m}()`));

// ── BlockstorageClient métodos ────────────────────────
console.log("\n💾 MÉTODOS do BlockstorageClient:");
const bsProto = core.BlockstorageClient.prototype;
Object.getOwnPropertyNames(bsProto)
  .filter(m => m !== "constructor")
  .filter(m =>
    m.toLowerCase().includes("volume") ||
    m.toLowerCase().includes("boot")
  )
  .forEach(m => console.log(`  → ${m}()`));