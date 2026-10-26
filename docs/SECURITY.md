# LaptopCtl Security Architecture & Threat Model

## 1. Security Principles

LaptopCtl is architected around the following core security principles:
- **Zero Inbound Attack Surface**: Target machines never listen on open network ports. All connections are outbound TLS over WebSocket.
- **Defense in Depth**: Secrets are layered behind Windows DPAPI, strict NTFS ACLs, and BCrypt salted hashing.
- **Least Privilege**: File operations and administrative actions are scoped and validated.
- **Non-Repudiation & Auditability**: Every administrative action, session, file transfer, and enrollment is recorded in an immutable audit log.

---

## 2. Threat Analysis & Mitigations

| Threat | Attack Vector | LaptopCtl Mitigation |
| :--- | :--- | :--- |
| **Port Scanning & Inbound Exploits** | Attacker probes open network ports on target laptop | **No listening ports.** Agent only initiates outbound WSS tunnels to the central backend. |
| **Credential Theft on Target PC** | Standard local user attempts to read agent token | **Windows DPAPI + Strict NTFS ACLs.** `$env:ProgramData\LaptopCtl` strips standard user permissions; keys encrypted with LocalMachine DPAPI. |
| **Database Compromise** | Attacker accesses PostgreSQL database | **No plaintext tokens.** Device tokens and operator passwords are stored exclusively as salted BCrypt hashes (work factor 12). |
| **Path Traversal Attacks** | Attacker supplies `../../Windows/System32` in file commands | **Strict Path Canonicalization & Root Whitelisting.** `Path.GetFullPath()` resolves path and verifies it strictly resides within allowed user directories. |
| **Replay Attacks** | Eavesdropper captures and resends intercepted command messages | **Message Expiry & Unique Request IDs.** Every WebSocket message includes `issuedAt` and `expiresAt` (2 min lifetime). Stale messages are rejected. |
| **Man-in-the-Middle (MitM)** | Intermediary intercepts network traffic | **Strict TLS 1.2/1.3 Enforcement.** All communications require TLS. |
| **Unauthorized Device Control** | Operator attempts to send commands to unowned devices | **Tenant Isolation & Ownership Verification.** `AgentRelayService` validates device ownership before forwarding messages. |
| **Brute-Force & Denial of Service** | Rapid automated requests to auth and enrollment | **IP Rate Limiting.** Configured per-endpoint limits (`AspNetCoreRateLimit`) on login and enrollment endpoints. |

---

## 3. Cryptographic Primitives

- **Asymmetric Device Identity**: RSA 2048-bit (DER SubjectPublicKeyInfo format).
- **Symmetric Secret Storage**: Windows Data Protection API (DPAPI) `DataProtectionScope.LocalMachine`.
- **Password & Token Hashing**: BCrypt with work factor 12.
- **File Integrity**: SHA-256 computed on stream before and after file transfer.
- **Transport Encryption**: TLS 1.2 and TLS 1.3 with forward secrecy ciphers.
