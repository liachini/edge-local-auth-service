# LocalAuthService - Context File per Claude in VS Code

**Ultimo aggiornamento:** 2026-04-08

Questo file contiene il contesto completo del progetto per permettere a Claude (in VS Code o altri IDE) di capire immediatamente lo stato attuale e i prossimi passi.

---

## 📋 Stato Attuale del Progetto

### ✅ COMPLETATO (Offline Mode - 100%)

**Auth Service OAuth2 offline-first completamente funzionante con:**

1. **Database SQLite locale**
   - Location: `%LOCALAPPDATA%\LocalAuthService\auth.db`
   - Tabelle: Users, Clients, UserConsents, MachineConfig, OpenIddict tables
   - Migrations: Code-first con EF Core 9.0

2. **JWKS Manager**
   - Chiave RSA 2048-bit generata localmente
   - File: `%LOCALAPPDATA%\LocalAuthService\jwks.json`
   - KeyId: `{MachineName}-{yyyyMMdd}`
   - Endpoint pubblico: `/.well-known/jwks.json`

3. **Tre scenari OAuth2 funzionanti:**
   - ✅ **Password Grant** (HMI user login)
   - ✅ **Authorization Code + Login page + Consent persistente** (MES terza parte)
   - ✅ **Client Credentials** (M2M Office API)

4. **Consent con scadenza scelta dall'utente**
   - Tabella custom `UserConsents` (campo `ExpiresAt` nullable)
   - L'utente sceglie la durata nella consent screen: 10s (test), 1d, 7d, 30d (default), 90d, never
   - Salvataggio in background con `IServiceScopeFactory` (scope DI separato per evitare `DbUpdateConcurrencyException`)
   - Skip consent se non scaduto; upsert se scaduto o revocato
   - Cookie login persistente (`IsPersistent = true`, scade dopo 8h)

5. **Controllers implementati:**
   - `TokenController`: gestisce `/connect/token` (password, client_credentials, authorization_code, refresh_token)
   - `AuthorizationController`: gestisce `/connect/authorize` e consent screen
   - `AccountController`: gestisce `/account/login` e `/account/logout` (cookie auth)
   - `TestController`: gestisce `/test` (playground), `/test/callback` (scenario 2), `/test/revoke-consent`
   - `ApiController`: gestisce `GET /api/me` (endpoint protetto da Bearer token, restituisce claims)

6. **Seed data:**
   - Users: `admin/admin123` (role: admin), `operator1/oper123` (role: operator)
   - Clients: `hmi-local` (public), `mes-fornitore` (confidential + consent), `office-api` (service account)

---

## 🏗️ Architettura Corrente

```
┌─────────────────────────────────────────┐
│  LocalAuthService (Offline-First)      │
│                                         │
│  ┌──────────────────────────────────┐  │
│  │ SQLite Database (Locale)         │  │
│  │ ├── Users                         │  │
│  │ ├── OAuthClients                 │  │
│  │ ├── UserConsents (custom!)       │  │
│  │ ├── MachineConfig                │  │
│  │ └── OpenIddict tables            │  │
│  └──────────────────────────────────┘  │
│                                         │
│  ┌──────────────────────────────────┐  │
│  │ JWKS Manager                     │  │
│  │ - Chiave RSA locale              │  │
│  │ - KeyId: MACHINE-20250401        │  │
│  └──────────────────────────────────┘  │
│                                         │
│  ┌──────────────────────────────────┐  │
│  │ OAuth2 Server (OpenIddict 6.0)   │  │
│  │                                   │  │
│  │ Endpoints:                        │  │
│  │ - /connect/token                 │  │
│  │ - /connect/authorize             │  │
│  │ - /connect/userinfo              │  │
│  │                                   │  │
│  │ Grant Types:                      │  │
│  │ - password                        │  │
│  │ - authorization_code             │  │
│  │ - client_credentials             │  │
│  │ - refresh_token                  │  │
│  └──────────────────────────────────┘  │
│                                         │
│  Controllers:                           │
│  - TokenController                      │
│  - AuthorizationController              │
│                                         │
│  Views:                                 │
│  - Consent.cshtml (consent screen)     │
└─────────────────────────────────────────┘
            │
            │ (ONLINE MODE ✅ FUNZIONANTE)
            ↓
┌─────────────────────────────────────────┐
│  Keycloak (Cloud/Office)                │
│  - Realm: falegnameria-rossi            │
│  - Auth: valida credenziali via ROPC    │
│  - Sync: utenti scaricati in locale     │
│  - Fallback: locale se Keycloak offline │
└─────────────────────────────────────────┘
```

---

## 🔧 Stack Tecnologico

### Framework & Runtime
- .NET 8.0+ (testato con .NET 10)
- ASP.NET Core Web API

### Dipendenze Principali

```xml
<PackageReference Include="OpenIddict.AspNetCore" Version="6.0.0" />
<PackageReference Include="OpenIddict.EntityFrameworkCore" Version="6.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.0" />
<PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
<PackageReference Include="Keycloak.Net" Version="4.0.0" />
<PackageReference Include="Swashbuckle.AspNetCore" />
```

### Database
- SQLite (file-based, locale)
- Entity Framework Core Code-First

---

---

## 📁 Struttura File Importanti

```
LocalAuthService/
├── Models/
│   ├── User.cs                      # ✅ Completo
│   ├── OAuthClient.cs               # ✅ Completo
│   ├── MachineConfig.cs             # ✅ Completo
│   └── UserConsent.cs               # ✅ Completo (tabella custom!)
│
├── Data/
│   └── AuthDbContext.cs             # ✅ Completo (include UserConsents)
│
├── Services/
│   └── JwksManager.cs               # ✅ Completo (genera RSA, salva/carica)
│
├── Controllers/
│   ├── TokenController.cs           # ✅ Completo (4 grant types incl. refresh_token)
│   ├── AuthorizationController.cs   # ✅ Completo (cookie auth + consent con scadenza)
│   ├── AccountController.cs         # ✅ Completo (login/logout, cookie persistente 8h)
│   ├── TestController.cs            # ✅ Completo (playground /test, callback, revoca)
│   └── ApiController.cs             # ✅ Completo (GET /api/me protetto da Bearer token)
│
├── Views/
│   ├── Account/
│   │   └── Login.cshtml             # ✅ Completo (form login)
│   ├── Authorization/
│   │   └── Consent.cshtml           # ✅ Completo (UI consent + selettore durata)
│   └── Test/
│       ├── Index.cshtml             # ✅ Completo (3 scenari + JWT decoder + tab Chiama API)
│       └── Callback.cshtml          # ✅ Completo (scenario 2: code → token → API validation)
│
├── Program.cs                       # ✅ Completo (OpenIddict config, seed)
└── appsettings.json                # ✅ Completo
```

---

## 🎯 Decisioni Tecniche Prese

### Perché OpenIddict 6.0?

- ✅ OAuth2/OIDC completo e certificato
- ✅ Supporto EF Core nativo
- ✅ Flessibile (offline + online)
- ✅ Open source, attivamente mantenuto
- ✅ Usato in produzione da molte aziende

### Perché SQLite?

- ✅ File-based (zero configurazione)
- ✅ Cross-platform (Windows/Linux)
- ✅ Embedded (no server esterno)
- ✅ Perfetto per offline-first
- ✅ Facile backup (copia file)

**Contro PostgreSQL/MSSQL:**
- ❌ Richiederebbero server sempre attivo
- ❌ Più complessi da configurare
- ❌ Contro filosofia offline-first

### Perché JWKS Locale?

- ✅ Macchina firma i propri token (autonomia)
- ✅ No dipendenze esterne per firmare
- ✅ Chiave unica per macchina
- ✅ Possibile rotazione chiavi in futuro

### Perché Tabella Custom per Consent?

- ✅ OpenIddict AuthorizationManager causa conflitti con SignIn
- ✅ Più semplice e diretto
- ✅ Pieno controllo sulla logica
- ✅ Facile da interrogare/revocare

---

## 📊 Flow Diagram - Authorization Code

```
┌─────────┐                                    ┌──────────────┐
│ Browser │                                    │ Auth Service │
└────┬────┘                                    └──────┬───────┘
     │                                                │
     │  1. GET /connect/authorize?client_id=...      │
     ├──────────────────────────────────────────────>│
     │                                                │
     │                    2. Check cookie "LocalAuth" │
     │                       non presente            │
     │                                                │
     │  3. Redirect /account/login?returnUrl=...     │
     │<──────────────────────────────────────────────┤
     │                                                │
     │  4. POST /account/login (user/pass)            │
     ├──────────────────────────────────────────────>│
     │                                                │
     │                        5. Valida con BCrypt   │
     │                           Set cookie LocalAuth│
     │                                                │
     │  6. Redirect returnUrl (/connect/authorize)   │
     │<──────────────────────────────────────────────┤
     │                                                │
     │  7. GET /connect/authorize?client_id=...      │
     ├──────────────────────────────────────────────>│
     │                                                │
     │                         8. Check UserConsents │
     │                            esistente nel DB?  │
     │                                                │
     │  9a. Se NON esiste → Consent Screen           │
     │<──────────────────────────────────────────────┤
     │                                                │
     │  10. POST /authorize/accept (Allow)            │
     ├──────────────────────────────────────────────>│
     │                                                │
     │                  11. Salva UserConsent in DB  │
     │                      (background task)         │
     │                                                │
     │  12. Redirect con code                         │
     │<──────────────────────────────────────────────┤
     │  http://localhost:7000/callback?code=XXX      │
     │                                                │
     │  13. POST /connect/token                       │
     │      (scambia code per token)                 │
     ├──────────────────────────────────────────────>│
     │                                                │
     │                 14. AuthenticateAsync(code)   │
     │                     Recupera claims dal code  │
     │                                                │
     │  15. Token JWT                                 │
     │<──────────────────────────────────────────────┤
     │  { access_token, id_token, ... }              │
     │                                                │
     │  RICHIESTA SUCCESSIVA (cookie ancora valido)  │
     │  16. GET /connect/authorize?client_id=...     │
     ├──────────────────────────────────────────────>│
     │                                                │
     │                      17. Check UserConsents   │
     │                          TROVATO! ✅          │
     │                                                │
     │  18. Skip consent → Redirect DIRETTO          │
     │<──────────────────────────────────────────────┤
     │  http://localhost:7000/callback?code=YYY      │
     │                                                │
```

---

## 🔜 PROSSIMI STEP - ROADMAP

### ⏭️ NEXT: Phase 1 - Keycloak Sync (Online Mode)

**Obiettivo:** Far funzionare l'Auth Service sia offline che online con sync automatica

**Task:**

#### 1.1 Operating Mode Detector (~30 min)
- [ ] Crea `Services/OperatingModeDetector.cs`
- [ ] Ping endpoint health Keycloak ogni 30 secondi
- [ ] Proprietà `bool IsOnline { get; }`
- [ ] Event `OnModeChanged(bool isOnline)`

```csharp
public class OperatingModeDetector
{
    private readonly IConfiguration _config;
    private bool _isOnline = false;
    
    public bool IsOnline => _isOnline;
    public event Action<bool>? OnModeChanged;
    
    public async Task<bool> CheckKeycloakAvailability()
    {
        var keycloakUrl = _config["Keycloak:Url"];
        if (string.IsNullOrEmpty(keycloakUrl)) return false;
        
        try
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync($"{keycloakUrl}/health/ready");
            var wasOnline = _isOnline;
            _isOnline = response.IsSuccessStatusCode;
            
            if (wasOnline != _isOnline)
                OnModeChanged?.Invoke(_isOnline);
                
            return _isOnline;
        }
        catch
        {
            _isOnline = false;
            return false;
        }
    }
}
```

#### 1.2 Keycloak Sync Service (~1 ora)
- [ ] Crea `Services/KeycloakSyncService.cs`
- [ ] Usa `Keycloak.Net` library
- [ ] `Task SyncUsersFromKeycloak()` - download users
- [ ] `Task SyncUsersToKeycloak()` - upload locally created users
- [ ] Logica merge (LastSyncFromKeycloak, LastSyncToKeycloak)

```csharp
public class KeycloakSyncService
{
    public async Task SyncUsersFromKeycloak()
    {
        // 1. Connetti a Keycloak Admin API
        var keycloak = new KeycloakClient(...);
        var realm = _config["Keycloak:Realm"];
        
        // 2. Download tutti gli utenti
        var keycloakUsers = await keycloak.GetUsersAsync(realm);
        
        // 3. Per ogni utente Keycloak
        foreach (var kcUser in keycloakUsers)
        {
            var localUser = await _db.Users
                .FirstOrDefaultAsync(u => u.KeycloakUserId == kcUser.Id);
            
            if (localUser == null)
            {
                // Nuovo utente - crea locale
                localUser = new User
                {
                    Id = Guid.NewGuid().ToString(),
                    Username = kcUser.Username,
                    Email = kcUser.Email,
                    KeycloakUserId = kcUser.Id,
                    CreatedLocally = false,
                    LastSyncFromKeycloak = DateTime.UtcNow
                };
                _db.Users.Add(localUser);
            }
            else
            {
                // Aggiorna esistente
                localUser.Email = kcUser.Email;
                localUser.Enabled = kcUser.Enabled;
                localUser.LastSyncFromKeycloak = DateTime.UtcNow;
            }
        }
        
        await _db.SaveChangesAsync();
    }
    
    public async Task SyncUsersToKeycloak()
    {
        // Upload utenti creati localmente
        var localUsers = await _db.Users
            .Where(u => u.CreatedLocally && u.KeycloakUserId == null)
            .ToListAsync();
        
        foreach (var user in localUsers)
        {
            // Crea in Keycloak
            var kcUserId = await keycloak.CreateUserAsync(realm, new User
            {
                Username = user.Username,
                Email = user.Email,
                Enabled = user.Enabled
            });
            
            // Aggiorna locale con ID Keycloak
            user.KeycloakUserId = kcUserId;
            user.LastSyncToKeycloak = DateTime.UtcNow;
        }
        
        await _db.SaveChangesAsync();
    }
}
```

#### 1.3 Background Sync Worker (~30 min)
- [ ] Hosted Service che esegue sync ogni 15 minuti
- [ ] Solo quando `IsOnline == true`

```csharp
public class SyncBackgroundService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_modeDetector.IsOnline)
            {
                await _syncService.SyncUsersFromKeycloak();
                await _syncService.SyncUsersToKeycloak();
            }
            
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
```

#### 1.4 Hybrid Token Endpoint (~30 min)
- [ ] Modifica `TokenController.HandlePasswordGrant`
- [ ] Try Keycloak first, fallback to local

```csharp
private async Task<IActionResult> HandlePasswordGrant(OpenIddictRequest request)
{
    if (_modeDetector.IsOnline)
    {
        try
        {
            // Prova Keycloak
            var keycloakToken = await _keycloakService.GetTokenAsync(
                request.Username, request.Password);
            
            if (keycloakToken != null)
            {
                Console.WriteLine("✅ Token from Keycloak (online)");
                return Ok(keycloakToken);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Keycloak failed, falling back to local: {ex.Message}");
        }
    }
    
    // Fallback: autenticazione locale
    Console.WriteLine("🔌 Token from local DB (offline)");
    // ... logica esistente ...
}
```

**Stima totale Phase 1:** ~2-3 ore

---

### Phase 1.5 - Gestione Password Locale (TODO)

> **Comportamento attuale (2026-04-07):** al primo login online riuscito (Keycloak), la password viene hashata e salvata in locale automaticamente in `AccountController` e `TokenController`. Questo copre tutti e 3 gli scenari OAuth2.

Aspetti ancora da gestire:

- [ ] **Sincronizzazione cambio password** — se l'utente cambia la password su Keycloak, la password locale rimane quella del primo login. Fix: rimuovere la condizione `!user.HasLocalPassword` e aggiornare sempre l'hash ad ogni login online riuscito
- [ ] **Scadenza password locale** — aggiungere `LocalPasswordChangedAt` su `User` (+ migration) e `LocalPasswordExpiryDays` in `appsettings.json` (0 = mai). Offline, se scaduta: messaggio "password scaduta, effettuare login online"
- [ ] **Reset password offline da admin** — endpoint admin per resettare la password locale di un utente

---

### Phase 2 - Admin UI (~1 ora)

- [ ] Endpoint `GET /api/status/mode` → JSON con `{ isOnline, keycloakUrl, lastSync }`
- [ ] Indicatore visuale online/offline
- [ ] Online: link a Keycloak Admin Console
- [ ] Offline: form creazione utente locale
- [ ] API `POST /api/admin/users` per creare utente locale

---

### Phase 3 - Deployment (~1 ora)

- [ ] Self-contained publish Windows x64
- [ ] Self-contained publish Linux x64
- [ ] Script installazione Windows Service (NSSM)
- [ ] Systemd unit file per Linux
- [ ] Test su macchina pulita

---

### Phase 4 - Security Hardening (Future)

- [ ] HTTPS obbligatorio in produzione
- [ ] Secrets in environment variables (no hardcoded)
- [ ] Rate limiting su endpoints
- [ ] Audit logging (chi ha fatto cosa quando)
- [ ] Token revocation endpoint
- [ ] CORS policy configurabile

---

## 🧪 Come Testare

### Test Rapidi

```powershell
# 1. Password Grant (aggiungere offline_access per ottenere anche il refresh token)
$body = @{ grant_type = "password"; username = "admin"; password = "admin123"; client_id = "hmi-local"; scope = "openid profile email offline_access" }
Invoke-RestMethod -Uri "http://localhost:5063/connect/token" -Method Post -Body $body -ContentType "application/x-www-form-urlencoded"

# 2. Authorization Code (browser)
# http://localhost:5063/connect/authorize?client_id=mes-fornitore&redirect_uri=http://localhost:7000/callback&response_type=code&scope=openid%20profile%20email

# 3. Client Credentials
$body = @{ grant_type = "client_credentials"; client_id = "office-api"; client_secret = "office-secret-456"; scope = "api.read api.write" }
Invoke-RestMethod -Uri "http://localhost:5063/connect/token" -Method Post -Body $body -ContentType "application/x-www-form-urlencoded"
```

### Verifica Database

```powershell
sqlite3 "$env:LOCALAPPDATA\LocalAuthService\auth.db"
SELECT * FROM Users;
SELECT * FROM UserConsents;
.exit
```

---

## 💡 Note per Claude in VS Code

### Quando Chiedi Aiuto

**Contesto già disponibile:**
- Progetto: LocalAuthService
- Stack: .NET 8+, OpenIddict 6.0, SQLite, EF Core 9.0
- Offline mode: 100% completo
- Prossimo obiettivo: Keycloak Sync

**Cosa specificare nella richiesta:**
- Quale fase stai implementando (es. "Phase 1.2 - Keycloak Sync Service")
- Quale file stai modificando
- Che errore riscontri (stack trace completo)

**Esempio richiesta efficace:**
```
Sto implementando Phase 1.2 - KeycloakSyncService.
Ho creato Services/KeycloakSyncService.cs ma quando chiamo 
SyncUsersFromKeycloak() ottengo questo errore:
[stack trace]

Cosa devo correggere?
```

### Comandi Utili

```powershell
# Build
dotnet build

# Run
dotnet run

# Migrations
dotnet ef migrations add MigrationName
dotnet ef database update

# Test veloce
dotnet test

# Publish self-contained
dotnet publish -c Release -r win-x64 --self-contained
```

---

## 📚 Risorse Esterne

### Keycloak Admin API
- [Keycloak.Net GitHub](https://github.com/lvermeulen/Keycloak.Net)
- [Keycloak Admin REST API](https://www.keycloak.org/docs-api/latest/rest-api/index.html)

### OpenIddict
- [Documentation](https://documentation.openiddict.com/)
- [GitHub Samples](https://github.com/openiddict/openiddict-samples)

### OAuth 2.0
- [RFC 6749](https://datatracker.ietf.org/doc/html/rfc6749)
- [OAuth 2.0 Playground](https://www.oauth.com/playground/)

---

## 🎯 Obiettivo Finale

**Sistema di autenticazione ibrido offline-first per macchine industriali:**

- ✅ **Offline mode** - Sempre funzionante anche senza rete (COMPLETO)
- ✅ **Online mode** - Keycloak autoritativo quando disponibile, fallback locale offline (COMPLETO)
- 🔐 **3 scenari OAuth2** - Password, Authorization Code, Client Credentials (COMPLETO)
- 🔑 **JWKS locale** - Firma token in autonomia (COMPLETO)
- 💾 **SQLite embedded** - Zero dipendenze server (COMPLETO)
- 👥 **User management** - Crea utenti localmente, sync a Keycloak (TODO)
- 🚀 **Deploy facile** - Self-contained, Windows Service / systemd (TODO)

**Siamo a metà strada! Offline completato, ora Online mode!** 🎊

---

## ✅ Checklist Quick Reference

### Offline Mode ✅
- [x] Database SQLite
- [x] JWKS Manager
- [x] Password Grant
- [x] Authorization Code
- [x] Login page (cookie auth "LocalAuth", scadenza 8h)
- [x] Client Credentials
- [x] Consent persistente
- [x] Seed data
- [x] Refresh token (rotation automatica, scope offline_access)
- [x] Consent con scadenza scelta dall'utente (10s/1d/7d/30d/90d/never)
- [x] Cookie login persistente (8h, sopravvive chiusura browser)
- [x] Test client integrato (/test) con JWT decoder, revoca consent e tab "Chiama API"
- [x] Endpoint protetto GET /api/me (Bearer token validation + restituzione claims lato server)
- [x] Fix DbUpdateConcurrencyException (IServiceScopeFactory nel background task)
- [x] Token signing locale

### Online Mode ✅ (completato 2026-04-07)
- [x] Operating Mode Detector (`Services/OperatingModeDetector.cs`) — ping Keycloak health
- [x] Keycloak Auth Service (`Services/KeycloakAuthService.cs`) — valida credenziali su Keycloak via token endpoint
- [x] Keycloak Sync Service (`Services/KeycloakSyncService.cs`) — sync utenti da Keycloak a locale
- [x] Background Sync Worker (`Services/SyncBackgroundService.cs`) — sync ogni 15 min quando online
- [x] Hybrid Login (`AccountController` + `TokenController`) — Keycloak autoritativo se online, locale come fallback
- [x] HasLocalPassword field (`Models/User.cs`, migration `20260407081721_AddHasLocalPassword`)
- [x] SetLocalPassword flow (`Views/Account/SetLocalPassword.cshtml`) — imposta password locale dopo primo login online
- [ ] Admin UI
- [ ] Status API

### Scenario 4 — Legacy Credentials ✅ (completato 2026-04-08)
**Hybrid Approach con Fine-Grained Access Control**

- [x] Model `LegacyServiceCredential.cs` — tabella per credenziali criptate + campo `AllowedClientIds` (JSON array)
- [x] `LegacyCredentialEncryptionService.cs` — AES-256 encryption con chiave machine-bound (PBKDF2-SHA256)
- [x] `LegacyCredentialService.cs` — gestione salvataggio/lettura/revoca credenziali + controllo AllowedClientIds
- [x] `LegacyController.cs` — API endpoints REST con error handling standardizzato
  - `POST /api/legacy/credentials` — salva credenziale (ruolo: admin o legacy-credentials-manager)
  - `POST /api/legacy/get-password` — legge password decriptata (ruolo: legacy-password-reader + AllowedClientIds check)
  - `DELETE /api/legacy/credentials/{serviceId}` — revoca credenziale
  - `GET /api/legacy/credentials` — lista credenziali (senza password)
  - `GET /api/legacy/available-clients` — lista client con ruolo legacy-password-reader
- [x] OAuth2 Clients registrati con ruoli specifici:
  - `legacy-credentials-manager` → ruoli: "legacy-credentials-manager" + "admin" (per salvare)
  - `cli-simulator` → ruolo: "legacy-password-reader" (per leggere)
  - `erp-simulator` → ruolo: "legacy-password-reader"
  - `crm-simulator` → ruolo: "legacy-password-reader"
  - `unauthorized-test` → NO legacy roles (per testare controlli autorizzazione)
- [x] Test UI Scenario 4 in `/test` con 2 sezioni:
  - **4A - Manager**: form salva credenziale con multi-select dropdown AllowedClientIds (opzionale)
  - **4B - Reader**: dropdown client disponibili + leggi password con fine-grained control
- [x] Migration EF Core `20260408081851_AddLegacyServiceCredentials` (+ AllowedClientIds field)
- [x] Audit logging: `LastAccessedAt`, `LastAccessedBy` nel DB
- [x] Error handling REST standard: `ApiErrorFilter` globale per tutte le eccezioni
- [x] OperatingModeDetector: cache 30s per check Keycloak, timeout ridotto a 2s
- [x] Offline-proof (credenziali rimangono locali, non sincronizzate a Keycloak)

### Error Handling & UX ✅ (2026-04-08)
- [x] `Filters/ApiErrorFilter.cs` — exception filter globale per risposte REST standardizzate
- [x] Status code + JSON display in test UI — mostra HTTP status con badge colorato + risposta formattata
- [x] Ridotto timeout Keycloak check da 5s → 2s (login più veloce quando Keycloak offline)
- [x] Cache 30s per OperatingModeDetector — evita ping ripetuti a Keycloak

### Deployment 📦
- [x] Framework-dependent publish Windows
- [x] Self-contained publish Windows (`--self-contained -r win-x64`)
- [x] Windows Service (`install-windows-service.ps1` con parametri `-Port` e `-SeedSampleData`)
- [x] Uninstall script (`uninstall-windows-service.ps1`)
- [x] Script copiati automaticamente in publish via MSBuild target
- [x] Flag `SeedSampleData` in `appsettings.json` (default `true`)
- [x] Linux Docker (container `localauth`, porta 5063, volume persistente `/var/lib/localauth`)
- [x] Cross-platform test

---

**Ultimo test funzionante:** 2026-04-08 - Scenario 4 Legacy Credentials completo ✅
- ✅ UI split 2 sezioni (Manager + Reader)
- ✅ Multi-select dropdown AllowedClientIds (opzionale)
- ✅ Fine-grained access control con AllowedClientIds + ruolo check
- ✅ 5 client registrati (legacy-credentials-manager + 3 lettori + 1 test non-autorizzato)
- ✅ REST error handling standardizzato con ApiErrorFilter globale
- ✅ Status code + JSON display in tutti gli scenari di test
- ✅ Keycloak check con cache 30s e timeout 2s (login non bloccato quando Keycloak down)
- ✅ Online mode + offline mode + tutti e 3 gli scenari OAuth2 + refresh token + consent + test client `/test` confermati funzionanti
