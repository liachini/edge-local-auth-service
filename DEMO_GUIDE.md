# Demo Guide — LocalAuthService

Base URL: `http://localhost:5063`

---

## Prerequisiti

- .NET 10 SDK
- Docker (per Keycloak negli scenari B e C)
- Postman o curl *(oppure usa la pagina di test integrata — vedi sotto)*

---

## Pagina di test integrata

Con il servizio avviato, apri:

```
http://localhost:5063/test
```

La pagina copre tutti i flow in modo interattivo e mostra token decodificati e chiamate API in tempo reale. I comandi curl riportati sono l'equivalente testuale degli stessi flow.

---

## Reset tra scenari

Ogni scenario usa un DB separato. Per ripartire da zero:

**Windows (PowerShell)**
```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\LocalAuthService\ScenarioA"
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\LocalAuthService\ScenarioB"
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\LocalAuthService\ScenarioC"
```

**Linux**
```bash
rm -rf "$HOME/.local/share/LocalAuthService/ScenarioA"
rm -rf "$HOME/.local/share/LocalAuthService/ScenarioB"
rm -rf "$HOME/.local/share/LocalAuthService/ScenarioC"
```

**macOS**
```bash
rm -rf "$HOME/Library/Application Support/LocalAuthService/ScenarioA"
rm -rf "$HOME/Library/Application Support/LocalAuthService/ScenarioB"
rm -rf "$HOME/Library/Application Support/LocalAuthService/ScenarioC"
```

---

## Avvio

```bash
# Scenario A — macchina offline
dotnet run --project src/LocalAuthService --launch-profile ScenarioA-Offline

# Scenario B — online dal giorno 1
dotnet run --project src/LocalAuthService --launch-profile ScenarioB-Online

# Scenario C — transizione offline → online
dotnet run --project src/LocalAuthService --launch-profile ScenarioC-Transition
```

Keycloak (scenari B e C): assicurati che Keycloak sia avviato e raggiungibile su `http://localhost:8080`.

---

## Utenti disponibili (Scenari A e C)

| Username | Password | Ruolo |
|---|---|---|
| admin | admin123 | admin |
| marco.bianchi | Marco2024! | supervisor, operator |
| luigi.ferrari | Luigi2024! | operator |
| giuseppe.conti | Giuseppe2024! | operator |
| anna.ricci | Anna2024! | warehouse-manager |

---

## Scenario A — Macchina offline

Il servizio parte senza rete, crea il DB e semina automaticamente gli utenti.
Il badge mostra `● OFFLINE`.

---

### A1. Password Grant — Login operatore HMI

> **Pagina di test:** `http://localhost:5063/test` → Scenario 1 — Password Grant

```bash
curl -X POST http://localhost:5063/connect/token \
  -d "grant_type=password" \
  -d "client_id=hmi-local" \
  -d "username=luigi.ferrari" \
  -d "password=Luigi2024!" \
  -d "scope=openid profile"
```

Risposta attesa: `200 OK` con `access_token` e `refresh_token`.
Decodifica il token su [jwt.io](https://jwt.io) — trovi `"role": "operator"` nel payload.

**Password errata → rifiuto:**

```bash
curl -X POST http://localhost:5063/connect/token \
  -d "grant_type=password" \
  -d "client_id=hmi-local" \
  -d "username=luigi.ferrari" \
  -d "password=sbagliata"
```

Risposta attesa: `400 Bad Request` con `"error": "invalid_grant"`.

---

### A2. M2M Client Credentials — Office API

Autenticazione machine-to-machine: nessun utente coinvolto, il servizio si autentica con `client_id` + `client_secret`.

> **Pagina di test:** `http://localhost:5063/test` → Scenario 3 — Client Credentials

```bash
curl -X POST http://localhost:5063/connect/token \
  -d "grant_type=client_credentials" \
  -d "client_id=office-api" \
  -d "client_secret=office-secret-456" \
  -d "scope=openid api.read"
```

Risposta attesa: `200 OK`. Nel payload il `sub` è `service-account-office-api` — nessun utente umano.

**Secret errato → rifiuto:**

```bash
curl -X POST http://localhost:5063/connect/token \
  -d "grant_type=client_credentials" \
  -d "client_id=office-api" \
  -d "client_secret=sbagliato" \
  -d "scope=openid api.read"
```

Risposta attesa: `400 Bad Request` con `"error": "invalid_client"`.

---

### A3. Consent — Authorization Code (MES Fornitore)

Flow interattivo: l'utente vede la schermata di consenso prima di autorizzare un'applicazione esterna.

> **Pagina di test:** `http://localhost:5063/test` → Scenario 2 — Authorization Code (gestisce il callback automaticamente)

**Manuale — apri nel browser:**

```
http://localhost:5063/connect/authorize?client_id=mes-fornitore&response_type=code&scope=openid%20profile&redirect_uri=http://localhost:5063/test/callback
```

1. Fai il login (es. `marco.bianchi` / `Marco2024!`).
2. Appare la schermata di consenso — approva.
3. Il browser viene reindirizzato a `/test/callback` con il `code`.
4. Il callback scambia automaticamente il code per un token.

Il client `mes-fornitore` è confidential — il secret non viaggia mai nel browser.

---

### A4. Legacy Credentials — Vault cifrato (ERP → DB gestionale)

Sistema non-OAuth2 che accede a credenziali cifrate tramite vault protetto da token.

> **Pagina di test:** `http://localhost:5063/test` → Scenario 4 — Legacy Credentials

**Passo 1** — Il manager salva la credenziale (una tantum):

```bash
TOKEN=$(curl -s -X POST http://localhost:5063/connect/token \
  -d "grant_type=client_credentials" \
  -d "client_id=legacy-credentials-manager" \
  -d "client_secret=legacy-manager-secret-456" \
  -d "scope=openid" | jq -r .access_token)

curl -X POST http://localhost:5063/api/legacy/credentials \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "serviceId": "db-gestionale",
    "username": "sa",
    "password": "DbPass2024!",
    "description": "DB SQL Server gestionale",
    "allowedClientIds": ["erp-simulator"]
  }'
```

**Passo 2** — L'ERP legge la password:

```bash
TOKEN=$(curl -s -X POST http://localhost:5063/connect/token \
  -d "grant_type=client_credentials" \
  -d "client_id=erp-simulator" \
  -d "client_secret=erp-simulator-secret-789" \
  -d "scope=openid" | jq -r .access_token)

curl -X POST http://localhost:5063/api/legacy/get-password \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"serviceId": "db-gestionale"}'
```

Risposta attesa: `200 OK` con `"password": "DbPass2024!"`.

**Passo 3** — Client non autorizzato → rifiuto:

```bash
TOKEN=$(curl -s -X POST http://localhost:5063/connect/token \
  -d "grant_type=client_credentials" \
  -d "client_id=unauthorized-test" \
  -d "client_secret=unauthorized-test-secret" \
  -d "scope=openid" | jq -r .access_token)

curl -X POST http://localhost:5063/api/legacy/get-password \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"serviceId": "db-gestionale"}'
```

Risposta attesa: `403 Forbidden` — token valido ma client senza ruolo `legacy-password-reader`.

---

## Scenario B — Online dal giorno 1 (Keycloak)

Avvia Keycloak prima del servizio. Al primo avvio il servizio:
1. Rileva Keycloak → badge `● ONLINE`
2. Crea il realm `falegnameria-rossi` se non esiste
3. Sincronizza client e ruoli

Gli utenti esistono su Keycloak.

---

### B1. Password Grant — Login utente Keycloak

> **Pagina di test:** `http://localhost:5063/test` → Scenario 1 — Password Grant

```bash
curl -X POST http://localhost:5063/connect/token \
  -d "grant_type=password" \
  -d "client_id=hmi-local" \
  -d "username=<utente-keycloak>" \
  -d "password=<password-keycloak>" \
  -d "scope=openid profile"
```

Risposta attesa: `200 OK`. Il token è firmato localmente ma l'autenticazione è avvenuta su Keycloak.

---

### B2. M2M Client Credentials — Office API

Identico allo Scenario A — il flow M2M è indipendente da Keycloak.

> **Pagina di test:** `http://localhost:5063/test` → Scenario 3 — Client Credentials

```bash
curl -X POST http://localhost:5063/connect/token \
  -d "grant_type=client_credentials" \
  -d "client_id=office-api" \
  -d "client_secret=office-secret-456" \
  -d "scope=openid api.read"
```

---

### B3. Consent — Authorization Code (MES Fornitore)

Identico allo Scenario A — il flow di consent è locale, Keycloak non è coinvolto.

> **Pagina di test:** `http://localhost:5063/test` → Scenario 2 — Authorization Code

```
http://localhost:5063/connect/authorize?client_id=mes-fornitore&response_type=code&scope=openid%20profile&redirect_uri=http://localhost:5063/test/callback
```

Questa volta il login viene validato su Keycloak, ma la schermata di consenso e il token sono gestiti localmente.

---

### B4. Legacy Credentials — Vault cifrato

Identico allo Scenario A — il vault è locale e indipendente da Keycloak.

> **Pagina di test:** `http://localhost:5063/test` → Scenario 4 — Legacy Credentials

Stessi comandi dello Scenario A4.

---

## Scenario C — Transizione offline → online

Dimostra che la transizione è **trasparente**: stessi token, stessi ruoli, nessuna interruzione di servizio.

---

### Fase 1: avvia offline (senza Keycloak)

Il badge mostra `● OFFLINE`. Tutti e 4 i flow funzionano in locale.

---

### C1. Password Grant — offline

> **Pagina di test:** `http://localhost:5063/test` → Scenario 1 — Password Grant

```bash
curl -X POST http://localhost:5063/connect/token \
  -d "grant_type=password" \
  -d "client_id=hmi-local" \
  -d "username=marco.bianchi" \
  -d "password=Marco2024!" \
  -d "scope=openid profile"
```

---

### C2. M2M Client Credentials — offline

Identico allo Scenario A2 — il flow M2M funziona senza rete.

> **Pagina di test:** `http://localhost:5063/test` → Scenario 3 — Client Credentials

---

### C3. Consent — Authorization Code — offline

Identico allo Scenario A3 — il flow di consent è completamente locale.

> **Pagina di test:** `http://localhost:5063/test` → Scenario 2 — Authorization Code

---

### C4. Legacy Credentials — offline

Identico allo Scenario A4 — il vault funziona senza rete.

> **Pagina di test:** `http://localhost:5063/test` → Scenario 4 — Legacy Credentials

---

### Fase 2: avvia Keycloak a runtime

Avvia Keycloak e assicurati che sia raggiungibile su `http://localhost:8080`.

Dopo ~5 secondi il badge diventa `● ONLINE` e nei log del servizio compare il sync automatico.

Apri la Keycloak Admin Console (`http://localhost:8080`) → realm `falegnameria-verdi` → gli utenti creati offline (marco.bianchi, luigi.ferrari, ecc.) sono arrivati.

---

### Fase 3: gli stessi flow ora girano online

Ripeti C1–C4. I comandi sono identici — la transizione è trasparente.

```bash
curl -X POST http://localhost:5063/connect/token \
  -d "grant_type=password" \
  -d "client_id=hmi-local" \
  -d "username=marco.bianchi" \
  -d "password=Marco2024!" \
  -d "scope=openid profile"
```

Stesso token, stessi ruoli. Il badge ora mostra `● ONLINE`.
