# LocalAuthService — Architettura del Sistema

## Obiettivo

Sistema di autenticazione OAuth2 **offline-first** per macchine industriali. Funziona sempre anche senza rete, si sincronizza con Keycloak quando disponibile.

---

## Visione d'insieme

```
┌─────────────────────────────────────────────────────────┐
│                   LocalAuthService                      │
│                                                         │
│  ┌─────────────┐  ┌──────────────┐  ┌───────────────┐   │
│  │   SQLite    │  │  OpenIddict  │  │ JWKS Manager  │   │
│  │  (locale)   │  │  OAuth2 Srv  │  │  (RSA 2048)   │   │
│  └─────────────┘  └──────────────┘  └───────────────┘   │
│                                                         │
│  ┌─────────────────────────────────────────────────┐    │
│  │              Background Services                │    │
│  │  OperatingModeDetector  │  SyncBackgroundService│    │
│  └─────────────────────────────────────────────────┘    │
└──────────────────────────┬──────────────────────────────┘
                           │ (quando online)
                           ▼
              ┌────────────────────────┐
              │   Keycloak (remoto)    │
              │   realm configurabile  │
              └────────────────────────┘
```

---

## Componenti Principali

### 1. Database SQLite

**Posizione:**
- Windows: `%LOCALAPPDATA%\LocalAuthService\auth.db`
- Linux: `~/.local/share/LocalAuthService/auth.db`

**Tabelle custom:**

| Tabella | Scopo |
|---|---|
| `Users` | Utenti locali + metadati Keycloak |
| `OAuthClients` | Definizione client OAuth2 (non usata direttamente da OpenIddict) |
| `UserConsents` | Consent persistenti per Authorization Code flow |
| `MachineConfig` | Configurazione macchina (JWKS, sync) |

**Tabelle OpenIddict** (gestite automaticamente):
- `OpenIddictApplications` — client OAuth2
- `OpenIddictAuthorizations` — autorizzazioni emesse
- `OpenIddictScopes` — scopes registrati
- `OpenIddictTokens` — token attivi

### 2. Modello Utente

```
User
├── Id                    GUID univoco locale
├── Username              Username univoco
├── PasswordHash          BCrypt hash della password locale
├── HasLocalPassword      true = password locale impostata
├── Email, FirstName, LastName, Roles (JSON), Enabled
├── CreatedLocally        true = creato su questa macchina
├── KeycloakUserId        ID Keycloak (null = non ancora sincronizzato)
├── LastSyncFromKeycloak  ultima volta importato da Keycloak
└── LastSyncToKeycloak    ultima volta esportato a Keycloak
```

### 3. JWKS Manager

Genera e mantiene una coppia di chiavi RSA 2048-bit per firmare i token JWT localmente.

- File: `%LOCALAPPDATA%\LocalAuthService\jwks.json`
- KeyId: `{MachineName}-{yyyyMMdd}`
- Endpoint pubblico: `GET /.well-known/jwks.json`

La firma locale garantisce **autonomia completa**: i token sono validi anche senza Keycloak.

### 4. OpenIddict 6.0

Implementa il server OAuth2/OIDC. Gestisce:
- Emissione e validazione token JWT
- Authorization code flow con PKCE
- Refresh token con rotation automatica
- Validazione client credentials

### 5. OperatingModeDetector

Pinga l'endpoint `/health/ready` di Keycloak ogni ciclo di sync. Espone `IsOnline` usato da controller e background service.

### 6. SyncBackgroundService

Gira ogni N minuti (default: 15, dev: 5). Quando Keycloak è online esegue in sequenza:

```
1. EnsureRealmExistsAsync        → crea il realm se non esiste
2. EnsureLoginClientExistsAsync  → abilita Direct Access Grants su client 'account'
3. SyncRolesToKeycloakAsync      → crea realm roles su Keycloak
4. SyncClientsToKeycloakAsync    → porta client locali su Keycloak (crea o aggiorna)
5. SyncClientsFromKeycloakAsync  → importa client NUOVI da Keycloak (skip se già esistenti)
6. SyncFromKeycloakAsync         → importa utenti da Keycloak in locale
7. SyncToKeycloakAsync           → porta utenti locali su Keycloak + temp password + ruoli
```

---

## Modalità Operative

### Offline Mode (sempre attiva)

La macchina non ha bisogno di rete. Usa esclusivamente SQLite locale e firma i token con JWKS locale.

**Utenti disponibili:** solo quelli nel DB locale (seed + sincronizzati in precedenza).

**Login:** BCrypt.Verify contro `PasswordHash` locale. Richiede `HasLocalPassword = true`.

### Online Mode (Keycloak raggiungibile)

Keycloak è **autoritativo** per l'autenticazione. Il login viene validato su Keycloak.

**Fallback intelligente:** se Keycloak rifiuta ma l'utente ha una password locale valida (es. password temporanea non ancora cambiata), il sistema usa la password locale e la sincronizza automaticamente su Keycloak.

---

## Ciclo di Vita della Password

```
Scenario A — Utente da Keycloak:
  Keycloak online → login con password Keycloak
    → password salvata localmente (HasLocalPassword = true)
    → login offline possibile d'ora in poi

Scenario B — Utente creato localmente:
  Keycloak offline → utente creato con password locale
  Keycloak torna online → utente sincronizzato su Keycloak con password TEMPORANEA
  Primo login online → Keycloak rifiuta (password temporanea)
    → fallback: password locale valida → login OK
    → password locale sincronizzata su Keycloak (non temporanea)
    → da quel momento: password allineata su entrambi i sistemi

Scenario C — Cambio password su Keycloak:
  Utente cambia password su Keycloak
  Login successivo → Keycloak accetta nuova password
    → nuova password salvata localmente
    → allineamento automatico
```

---

## I 3 Scenari OAuth2

### Scenario 1 — Password Grant (HMI)

**Caso d'uso:** Operatore si autentica nell'interfaccia HMI della macchina.

```
Client → POST /connect/token
         grant_type=password
         username=mario
         password=pass123
         client_id=hmi-local

Server → verifica credenziali (Keycloak se online, locale se offline)
       → emette access_token + refresh_token (se richiesto offline_access)
```

**Client:** `hmi-local` (public, nessun secret)

### Scenario 2 — Authorization Code + Consent (MES)

**Caso d'uso:** Applicazione MES terza parte richiede accesso ai dati con consenso esplicito dell'utente.

```
Browser → GET /connect/authorize?client_id=mes-fornitore&...
        → redirect login se non autenticato
        → consent screen (prima volta o scaduto)
        → redirect a redirect_uri?code=XXX

App MES → POST /connect/token
          grant_type=authorization_code
          code=XXX
          client_secret=mes-secret-123
        → access_token + id_token
```

**Client:** `mes-fornitore` (confidential, richiede consent)

**Consent persistente:** l'utente sceglie la durata (10s/1d/7d/30d/90d/mai). Alla scadenza viene riproposto.

### Scenario 3 — Client Credentials (M2M)

**Caso d'uso:** Servizio office chiama le API della macchina senza utente (machine-to-machine).

```
Office Service → POST /connect/token
                 grant_type=client_credentials
                 client_id=office-api
                 client_secret=office-secret-456
               → access_token (no refresh token)
```

**Client:** `office-api` (confidential, service account)

---

## Sincronizzazione Bidirezionale

### Keycloak → Locale

- Scarica tutti gli utenti del realm
- Crea utenti nuovi in locale (`CreatedLocally=false`)
- Aggiorna email, nome, cognome, enabled degli esistenti
- Non sovrascrive la password locale (`HasLocalPassword` rimane intatto)

### Locale → Keycloak

- Trova utenti con `CreatedLocally=true` e `KeycloakUserId=null`
- Crea l'utente su Keycloak con metadati (no password hash — non trasferibile)
- Imposta password **temporanea** su Keycloak (loggata una volta sola)
- Assegna i realm roles corrispondenti
- Al primo login online: password locale sincronizzata su Keycloak (non temporanea)

### Client Locale → Keycloak

- `hmi-local`, `mes-fornitore`, `office-api` vengono creati/aggiornati su Keycloak
- I secret locali NON vengono sovrascritti dal sync inverso
- `SyncClientsFromKeycloakAsync` importa solo client **nuovi** (non sovrascrive esistenti)

---