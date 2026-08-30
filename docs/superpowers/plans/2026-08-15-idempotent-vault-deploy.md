# Idempotent Vault Deploy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Allow deploys to reuse a complete OCI Vault without requiring a local secrets file.

**Architecture:** A small PowerShell resolver determines whether the Vault is complete from the tracked secret-name map and active secret names. The Vault setup script only loads the ignored local file when at least one mapped secret is missing.

**Tech Stack:** PowerShell 7+, OCI CLI, Pester 3.

## Global Constraints

- Never print, persist, rotate, delete, or replace secret values.
- Require `SecretsFile` only when a mapped Vault secret is missing.
- Preserve the existing deployment order and SSH parameters.

---

### Task 1: Add secret input resolution

**Files:**
- Create: `scripts/criarDeployOracleCloud/secret-input.ps1`
- Modify: `scripts/criarDeployOracleCloud/01-setup-vault-secrets.ps1`
- Test: `scripts/criarDeployOracleCloud/tests/secret-input.Tests.ps1`

- [x] Add a resolver that returns reuse mode when all mapped secrets exist.
- [x] Throw a clear error naming missing secret names when no local file is available.
- [x] Keep local file parsing and creation behavior unchanged when the file is supplied.
- [x] Verify the Pester regression tests pass.

### Task 2: Validate deployment behavior

**Files:**
- Verify: `scripts/criarDeployOracleCloud/validate-pipeline.ps1`
- Verify: `scripts/criarDeployOracleCloud/deploy-all.ps1`

- [x] Run local pipeline validation.
- [x] Run the deployment with process-scoped OCI variables derived from read-only Vault metadata.
- [x] Confirm the Vault step reports reuse and the deployment completed through verification.
