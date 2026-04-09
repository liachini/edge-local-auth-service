# LocalAuthService — Spike Report

**Obiettivo:** Servizio OAuth2/OpenID Connect locale per ambienti industriali, con sync opzionale verso Keycloak.  
**Stato:** Spike completato — tutti i flussi funzionanti.

---

## 1. I 3 Scenari di Deployment

### Scenario A: Macchina Offline

La macchina opera **senza connessione di rete**. Il servizio gestisce autenticazione e autorizzazione in autonomia completa.

```
  ┌────────────────────────────────────────────────────┐
  │                    MACCHINA                        │
  │                                                    │
  │   ┌──────────┐     ┌──────────────────────┐        │
  │   │   HMI    │────>│  LocalAuthService    │        │
  │   │   MES    │<────│                      │        │
  │   │   ERP    │     │  - OAuth2 Server     │        │
  │   └──────────┘     │  - SQLite DB locale  │        │
  │                    │  - JWKS locale       │        │
  │                    │  - Vault Key         │        │
  │                    └──────────────────────┘        │
  │                                                    │
  │              ╳ NESSUNA RETE ╳                      │
  └────────────────────────────────────────────────────┘
```

**Come funziona:**
- Utenti e client sono gestiti nel DB SQLite locale
- Token JWT firmati con chiave locale (JWKS)
- Password hashate con BCrypt
- Credenziali legacy cifrate con chiave vault locale
- L'`OperatingModeDetector` rileva automaticamente l'assenza di rete

**Caso d'uso:** Fabbrica isolata, ambiente senza connettività.

---

### Scenario B: Macchina Online (Keycloak dal giorno 1)

La macchina nasce **connessa** a un'infrastruttura Keycloak centrale.

```
  ┌──────────────────────────┐           ┌──────────────────────┐
  │        MACCHINA          │           │     KEYCLOAK         │
  │                          │           │     (centrale)       │
  │  ┌──────────┐            │           │                      │
  │  │   HMI    │   ┌────────┴───────────|  - Realm dedicato    │
  │  │   MES    │──>│ LocalAuthService   │  - Utenti/Ruoli      │
  │  │   ERP    │<──│                    │  - Single Sign-On    │
  │  └──────────┘   │  OAuth2 Server     │  - Audit centrale    │
  │                 │  - SQLite DB locale│                      │
  │                 │  - JWKS locale     │                      │
  │                 │  - Vault Key       │                      │
  │                 │                    │                      │
  │                 │  ──── sync ──────> │                      │
  │                 │  <──── sync ────── │                      │
  │                 └────────┬───────────┘                      │
  │                          │           │                      │
  └──────────────────────────┘           └──────────────────────┘
```

**Come funziona:**
- All'avvio, il `SyncBackgroundService` sincronizza con Keycloak:
  - Crea il realm se non esiste
  - Sincronizza ruoli, utenti e client bidirezionalmente
- L'autenticazione può essere validata sia localmente che su Keycloak
- Se Keycloak ha utenti aggiuntivi, vengono importati localmente
- Le password impostate localmente vengono propagate a Keycloak

**Caso d'uso:** Fabbrica connessa, ambiente enterprise con SSO centralizzato.

---

### Scenario C: Offline-first, poi connessa (Transizione)

La macchina in prima battuta non riesce a contattare l'ipd remoto e successivamente viene collegata.

```
  FASE 1: Offline                         FASE 2: Rete disponibile
  ┌─────────────────────────┐            ┌─────────────────────────┐
  │       MACCHINA          │            │       MACCHINA          │
  │                         │            │                         │
  │  LocalAuthService       │            │  LocalAuthService       │
  │  ┌──────────────────┐   │            │  ┌──────────────────┐   │
  │  │ DB: 5 utenti     │   │            │  │ DB: 5 utenti     │───────> Keycloak
  │  │ 3 client OAuth   │   │    rete    │  │ 3 client OAuth   │<─────── (sync)
  │  │ ruoli assegnati  │   │  ======>   │  │ ruoli assegnati  │   │
  │  │ JWKS locale      │   │            │  │ JWKS locale      │   │
  │  │ Vault Key        │   │            │  │ Vault Key        │   │
  │  └──────────────────┘   │            │  └──────────────────┘   │
  │                         │            │                         │
  │  Badge: ● OFFLINE       │            │  Badge: ● ONLINE        │
  └─────────────────────────┘            └─────────────────────────┘
                                                     │
                                                     ▼
                                          Sync automatico:
                                          ✓ Realm creato
                                          ✓ Utenti sincronizzati
                                          ✓ Ruoli propagati
                                          ✓ Password migrate
```

**Come funziona:**
1. La macchina parte e lavora normalmente in modalità offline
2. Quando la rete viene attivata, l'`OperatingModeDetector` rileva Keycloak (~5 secondi)
3. Il `SyncBackgroundService` esegue la sincronizzazione completa automaticamente
4. Da quel momento in poi, il sistema opera in modalità online
5. **Nessun dato perso** — tutto ciò che è stato creato offline viene sincronizzato

**Caso d'uso:** Macchina installata in cantiere, commissioning senza rete, poi collegamento alla rete aziendale.

---

### Componenti crittografici (presenti in tutti gli scenari)

| Componente | Cosa fa | File su disco |
|---|---|---|
| **JWKS** (RSA-2048) | Firma i token JWT — chi riceve un token puo' verificare che lo ha emesso questa macchina | `jwks.json` |
| **Vault Key** (AES-256) | Cifra le password legacy salvate nel DB — senza questa chiave sono illeggibili | `vault.key` |

Entrambi vengono generati automaticamente al primo avvio e sono **unici per macchina**. Non dipendono dalla rete ne' da Keycloak.

---

## 2. Punti di Forza — Sicurezza

| Aspetto | Implementazione | Stato |
|---|---|---|
| **Standard OAuth2/OIDC** | OpenIddict — protocollo standard, non proprietario | Fatto |
| **Password hashing** | BCrypt con salt automatico | Fatto |
| **Token JWT firmati** | Chiave JWKS generata localmente, rotazione supportata | Fatto |
| **Vault Key** | Chiave di cifratura da file locale (non derivata da hostname) | Fatto |
| **Credenziali legacy cifrate** | AES encryption con vault key, accesso role-based | Fatto |
| **Security headers** | X-Content-Type-Options, X-Frame-Options, Referrer-Policy, HSTS | Fatto |
| **HTTPS enforcement** | Redirect automatico, HSTS in produzione | Fatto |
| **Autorizzazione role-based** | `[Authorize(Roles = "admin")]` su tutti gli endpoint sensibili | Fatto |
| **Secrets fuori dal codice** | Config file + environment variables, nessun secret in sorgente | Fatto |
| **Consent esplicito** | Schermata di consenso per client di terze parti | Fatto |

---

## 3. Punti di Forza — Scalabilità e Architettura

| Aspetto | Dettaglio |
|---|---|
| **Offline-first** | Funziona al 100% senza rete — nessuna dipendenza esterna |
| **Sync bidirezionale** | Utenti, ruoli e client sincronizzati con Keycloak in entrambe le direzioni |
| **Detection automatica** | Il servizio rileva automaticamente se Keycloak e' raggiungibile |
| **Transizione trasparente** | Passaggio offline→online senza intervento manuale |
| **Multi-client** | Supporta client pubblici (HMI), confidential (MES), e M2M (service account) |
| **Multi-grant** | Password Grant, Authorization Code + PKCE, Client Credentials, Refresh Token |
| **Cross-platform** | Windows Service, Linux, macOS, Docker |
| **Portable** | SQLite locale — nessun database server necessario |
| **Legacy bridge** | Gestione credenziali legacy cifrate per sistemi che non supportano OAuth2 |

---

## 4. Flussi OAuth2 Supportati

```
┌─────────────────────────────────────────────────────────────┐
│                     LocalAuthService                        │
│                                                             │
│   /connect/token ─────────── Password Grant (HMI)           │
│   /connect/authorize ─────── Authorization Code (MES)       │
│   /connect/token ─────────── Client Credentials (M2M)       │
│   /connect/token ─────────── Refresh Token                  │
│   /connect/userinfo ──────── UserInfo endpoint              │
│                                                             │
│   /api/me ────────────────── Profilo utente (protetto)      │
│   /api/clients ───────────── Lista client registrati        │
│   /api/legacy/* ──────────── Credenziali legacy (role-based)│
└─────────────────────────────────────────────────────────────┘
```

---

## 5. Legacy Password Management

Molti sistemi usano ancora credenziali username/password tradizionali e non supportano OAuth2. LocalAuthService risolve il problema facendo da **vault centralizzato** per queste credenziali.

### Come funziona

```
  Salvataggio                             Lettura
  (admin/manager)                         (servizio autorizzato)

  POST /api/legacy/credentials            POST /api/legacy/get-password
  ┌──────────────────┐                    ┌──────────────────┐
  │ serviceId: "erp" │                    │ serviceId: "erp" │
  │ username: "sa"   │                    └────────┬─────────┘
  │ password: "P@ss" │                             │
  └────────┬─────────┘                             │
           │                                       │
           ▼                                       ▼
  ┌──────────────────────────────────────────────────────────┐
  │                   LocalAuthService                       │
  │                                                          │
  │  1. Verifica ruolo chiamante                             │
  │     - Salvataggio: "admin" o "legacy-credentials-manager"│
  │     - Lettura: "legacy-password-reader"                  │
  │                                                          │
  │  2. Verifica client autorizzato                          │
  │     - Ogni credenziale ha una lista AllowedClientIds     │
  │     - Solo i client in lista possono accedere            │
  │                                                          │
  │  3. Encryption AES-256-CBC                               │
  │     - Chiave dal VaultKey locale (file)                  │
  │     - IV random per ogni cifratura                       │
  │     - Password MAI in chiaro nel DB                      │
  │                                                          │
  │  4. Audit trail                                          │
  │     - Chi ha letto, quando, da quale client              │
  │     - Log di ogni accesso negato                         │
  └──────────────────────────────────────────────────────────┘
```

### Sicurezza multilivello

```
  Richiesta lettura password
         │
         ▼
  ┌─ Bearer token valido? ──── NO ──> 401 Unauthorized
  │      │
  │     SI'
  │      ▼
  ├─ Ruolo "legacy-password-reader"? ── NO ──> 403 Forbidden
  │      │
  │     SI'
  │      ▼
  ├─ Client in AllowedClientIds? ── NO ──> 403 Forbidden
  │      │
  │     SI'
  │      ▼
  ├─ Credenziale esiste e attiva? ── NO ──> 404 Not Found
  │      │
  │     SI'
  │      ▼
  └─ Decripta + Audit log + Risposta
```

### Perche' e' importante

| Problema | Soluzione |
|---|---|
| Password salvate in chiaro nei config file | Cifrate con AES-256, chiave in vault locale |
| Chiunque puo' leggere le password | Accesso solo con token + ruolo + client autorizzato |
| Non si sa chi ha letto cosa | Audit trail: utente, timestamp, client |
| Password condivise via email/chat | API sicura con controllo accessi |
| Revoca difficile | `DELETE /api/legacy/credentials/{id}` — disattiva e cancella |

---

## 6. Client Registrati (Demo)

| Client | Tipo | Grant | Uso |
|---|---|---|---|
| `hmi-local` | Public | Password | Operatore HMI — login diretto |
| `mes-fornitore` | Confidential | Authorization Code | MES esterno — login con consent |
| `office-api` | Confidential | Client Credentials | API M2M — nessun utente |
| `cli-simulator` | Confidential | Client Credentials | Accesso legacy credentials |
| `erp-simulator` | Confidential | Client Credentials | Lettura credenziali legacy |

---

## 7. Scalabilita' Multi-Macchina

Quando un cliente ha **piu' macchine** che devono condividere utenti, ruoli e client OAuth, ci sono diverse strategie a seconda dell'infrastruttura disponibile.

---

### Scenario 1: Multi-macchina con Keycloak (hub centrale)

La soluzione piu' completa. Keycloak fa da **hub centralizzato** e ogni macchina sincronizza con esso.

```
  ┌─────────────────────────────────────────────────────────────┐
  │                    INFRASTRUTTURA CLIENTE                   │
  │                                                             │
  │   Macchina 1 ──┐                                            │
  │   Macchina 2 ──┤         ┌───────────────────────┐          │
  │   Macchina 3 ──┼── sync ─┤    KEYCLOAK           │          │
  │   Macchina 4 ──┤         │    (hub centrale)     │          │
  │   Macchina 5 ──┘         │                       │          │
  │                          │  - Single Sign-On     │          │
  │   Ogni macchina ha:      │  - Audit centralizzato│          │
  │   - LocalAuthService     │  - Federation LDAP/AD │          │
  │   - DB SQLite locale     │  - UI di gestione     │          │
  │   - JWKS + Vault Key     └───────────────────────┘          │
  │   - Funziona anche                                          │
  │     se Keycloak e'                                          │
  │     irraggiungibile                                         │
  └─────────────────────────────────────────────────────────────┘
```

**Flusso di sincronizzazione:**

```
  Macchina 1 crea utente "mario"
       │
       ▼
  Sync Local → Keycloak (automatico)
       │
       ▼
  Keycloak ha "mario"
       │
       ▼
  Macchina 2..5: Sync Keycloak → Local (automatico)
       │
       ▼
  Tutte le macchine hanno "mario"
```

**Stato implementazione:** Completo — il `SyncBackgroundService` gestisce tutto automaticamente.

---

### Scenario 2: Multi-macchina SENZA Keycloak — Leader dinamico con fallback

Per clienti che **non hanno Keycloak** e **non hanno un server dedicato**. Le macchine si organizzano autonomamente: una diventa leader, le altre sincronizzano con lei. Se il leader si spegne, un'altra macchina prende il ruolo.

```
  SITUAZIONE INIZIALE                    IL LEADER SI SPEGNE

  ┌─────────────────────────┐           ┌─────────────────────────┐
  │   RETE LOCALE FABBRICA  │           │   RETE LOCALE FABBRICA  │
  │                         │           │                         │
  │   Macchina 1 - LEADER   │           │   Macchina 1  (spenta)  │
  │     ▲    ▲              │           │                         │
  │     │    │              │           │   Macchina 2 - LEADER   │
  │     ▼    ▼              │           │     ▲                   │
  │   Macch.2  Macch.3      │           │     ▼                   │
  │   (sync)   (sync)       │           │   Macchina 3            │
  │                         │           │   (sync)                │
  └─────────────────────────┘           └─────────────────────────┘

  ╳ NESSUN KEYCLOAK ╳                  Il leader e' caduto:
  ╳ NESSUN SERVER DEDICATO ╳           Macchina 2 diventa leader
                                        automaticamente
```

**Come funziona:**

```
  Avvio macchina
       │
       ▼
  Cerca altre macchine nella rete
  (broadcast/multicast o lista configurata)
       │
       ├─── Nessuno risponde ──► Divento LEADER
       │
       └─── Trovo un leader ──► Sincronizzo con lui
                                     │
                                     ▼
                              Polling periodico
                                     │
                              Leader non risponde?
                                     │
                                     ▼
                              Elezione nuovo leader
                              (priorita' per uptime
                               o ID macchina)
```

**Regole di elezione:**
1. Ogni macchina ha un **ID univoco** e un **peso** (configurabile, es. priorita' statica o uptime)
2. All'avvio, ogni macchina cerca i peer nella rete locale
3. Se nessuno risponde, si autoproclama leader
4. Se trova un leader attivo, sincronizza con lui
5. Se il leader non risponde per N secondi, le macchine rimaste **eleggono il nuovo leader** (quella con peso piu' alto)
6. Quando il vecchio leader torna online, sincronizza con il nuovo leader (non reclama il ruolo)

**Gestione conflitti:**
- Il leader e' la **fonte di verita'** — in caso di conflitto, i suoi dati vincono
- Ogni modifica ha un **timestamp** (`UpdatedAt`) — in caso di merge, vince la modifica piu' recente
- Utenti creati offline su macchine diverse vengono uniti per username

**Stato implementazione:** Da sviluppare. Richiede:
- Servizio di **peer discovery** (broadcast UDP o lista statica di IP)
- Logica di **leader election** (basata su priorita'/uptime)
- Adapter di **sync peer-to-peer** (riutilizza la logica di sync esistente)
- **Conflict resolution** basata su timestamp

---

### Scenario 3: Macchina singola — Nessuna sync

Lo scenario piu' semplice: una sola macchina, nessuna necessita' di sincronizzazione.

```
  ┌────────────────────────────────────────┐
  │              MACCHINA                  │
  │                                        │
  │   LocalAuthService                     │
  │   - DB SQLite locale                   │
  │   - JWKS + Vault Key                   │
  │   - Completamente autonomo             │
  │                                        │
  │   ╳ NESSUNA RETE ╳                     │
  │   ╳ NESSUN HUB ╳                       │
  │   ╳ NESSUN KEYCLOAK ╳                  │
  └────────────────────────────────────────┘
```

**Stato implementazione:** Completo — e' lo scenario base gia' funzionante.

---

### Confronto scenari

| | Keycloak Hub | Leader Dinamico | Singola Macchina |
|---|---|---|---|
| **Infrastruttura richiesta** | Server Keycloak (Java) | Nessuna (solo rete locale) | Nessuna |
| **Server dedicato** | Si' | No | No |
| **Gestione centralizzata** | Si' (Keycloak Admin) | Si' (leader corrente) | Locale |
| **Offline resilience** | Si' (ogni macchina autonoma) | Si' (ogni macchina autonoma) | Si' (nativa) |
| **Tolleranza a macchine spente** | Si' (Keycloak resta attivo) | Si' (elezione nuovo leader) | N/A |
| **UI gestione utenti** | Keycloak Admin Console | Da sviluppare | Da sviluppare |
| **Complessita' operativa** | Media-Alta | Bassa | Nessuna |
| **Complessita' sviluppo** | Gia' scritto | Media (discovery + election) | Gia' scritto |

---

## 8. Roadmap Sicurezza (Post-Spike)

Queste sono le azioni pianificate per portare il servizio in produzione:

### Priorita' Alta
- [ ] **Database Encryption** — SQLCipher per cifrare il DB SQLite at rest
- [ ] **Rate Limiting** — Protezione brute-force su login e token endpoint
- [ ] **Input Validation** — Sanitizzazione sistematica di tutti gli input
- [ ] **Audit Trail** — Log immutabili di ogni operazione di autenticazione

### Priorita' Media
- [ ] **Admin API + UI** — CRUD per utenti, client e ruoli via interfaccia
- [ ] **Key Rotation** — Rotazione automatica delle chiavi JWKS
- [ ] **Certificate Management** — Certificati TLS gestiti (non development)
- [ ] **Token Revocation** — Revoca token attivi

### Priorita' Bassa
- [ ] **Penetration Testing** — Test di sicurezza formale
- [ ] **Load Testing** — Verifica performance sotto carico
- [ ] **Monitoring & Alerting** — Dashboard operativa
- [ ] **Backup & Recovery** — Procedure di disaster recovery

---

## 9. Stack Tecnologico

| Componente | Tecnologia |
|---|---|
| Runtime | .NET 10 |
| OAuth2 Server | OpenIddict 6.0 |
| Database | SQLite (EF Core) |
| Password Hashing | BCrypt.Net |
| Token Format | JWT (RS256) |
| Encryption | AES (vault key) |
| Sync Target | Keycloak |
| Deployment | Windows Service / Linux / Docker |
