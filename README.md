# LocalAuthService - Offline-First OAuth2 Authentication Service

Auth Service OAuth2 completo che funziona 100% offline con supporto per sincronizzazione opzionale con Keycloak.

## 🎯 Obiettivo

Creare un servizio di autenticazione OAuth2 per macchine industriali che:
- Funziona **sempre offline** (offline-first)
- Si sincronizza con Keycloak quando disponibile (online mode)
- Supporta tutti e 3 gli scenari OAuth2 principali
- Genera e firma token JWT localmente con JWKS propria
- Salva consent in modo persistente

---

## 🏗️ Architettura

```
┌─────────────────────────────────────────┐
│  Auth Service (Windows/Linux)          │
│                                         │
│  ├── SQLite Database (locale)          │
│  │   ├── Users                          │
│  │   ├── Clients                        │
│  │   ├── UserConsents (persistent)     │
│  │   └── MachineConfig                 │
│  │                                      │
│  ├── JWKS (chiave RSA locale)          │
│  │                                      │
│  ├── OAuth2 Server (OpenIddict)        │
│  │   ├── Password Grant                │
│  │   ├── Authorization Code + Consent  │
│  │   └── Client Credentials            │
│  │                                      │
│  └── Controllers                        │
│      ├── TokenController               │
│      └── AuthorizationController       │
└─────────────────────────────────────────┘
           │ (opzionale)
           ↓
┌─────────────────────────────────────────┐
│  Keycloak (Cloud/Office)                │
│  - Backup/Sync quando online            │
└─────────────────────────────────────────┘
```

---

## 🚀 Setup Progetto

### 1. Prerequisiti

- .NET 8 SDK o superiore
- Visual Studio Code o Visual Studio 2022
- PowerShell (per testing)

### 2. Creazione Progetto

```powershell
# Crea progetto
mkdir LocalAuthService
cd LocalAuthService
dotnet new webapi -n LocalAuthService -f net8.0
cd LocalAuthService
```

### 3. Installazione Dipendenze

```powershell
# OpenIddict (OAuth2/OIDC server)
dotnet add package OpenIddict.AspNetCore --version 6.0.0
dotnet add package OpenIddict.EntityFrameworkCore --version 6.0.0

# Entity Framework Core + SQLite
dotnet add package Microsoft.EntityFrameworkCore --version 9.0.0
dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version 9.0.0
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.0.0

# BCrypt per hash password
dotnet add package BCrypt.Net-Next --version 4.0.3

# Keycloak client (per sync futuro)
dotnet add package Keycloak.Net --version 4.0.0

# Swagger (opzionale, per documentazione API)
dotnet add package Swashbuckle.AspNetCore
```

---

## 📁 Struttura Progetto

```
LocalAuthService/
├── Models/
│   ├── User.cs                 # Modello utente
│   ├── OAuthClient.cs          # Client OAuth2
│   ├── MachineConfig.cs        # Configurazione macchina
│   └── UserConsent.cs          # Consent persistente
├── Data/
│   └── AuthDbContext.cs        # DbContext EF Core
├── Services/
│   └── JwksManager.cs          # Gestione chiavi JWKS
├── Controllers/
│   ├── TokenController.cs      # Endpoint /connect/token
│   ├── AuthorizationController.cs  # Endpoint /connect/authorize
│   └── AccountController.cs    # Login / Logout
├── Views/
│   ├── Account/
│   │   └── Login.cshtml        # Form login
│   └── Authorization/
│       └── Consent.cshtml      # Schermata consent
├── Migrations/                 # Migrazioni EF Core
├── Program.cs                  # Configurazione app
└── appsettings.json           # Configurazione
```

---

## 🗄️ Database Schema

### User (Utenti)

```csharp
public class User
{
    public string Id { get; set; }              // GUID
    public string Username { get; set; }        // Username univoco
    public string PasswordHash { get; set; }    // Hash BCrypt
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public bool Enabled { get; set; }           // Utente attivo
    public string Roles { get; set; }           // JSON array ["admin", "operator"]
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Sync con Keycloak
    public bool CreatedLocally { get; set; }
    public string? KeycloakUserId { get; set; }
    public DateTime? LastSyncToKeycloak { get; set; }
    public DateTime? LastSyncFromKeycloak { get; set; }
}
```

### OAuthClient (Client OAuth2)

```csharp
public class OAuthClient
{
    public string ClientId { get; set; }        // ID univoco
    public string ClientName { get; set; }      // Nome visualizzato
    public string? ClientSecretHash { get; set; } // Hash BCrypt (se confidential)
    public bool IsConfidential { get; set; }    // Public vs Confidential
    public bool ServiceAccountEnabled { get; set; } // Client Credentials
    public string RedirectUris { get; set; }    // JSON array
    public string AllowedScopes { get; set; }   // JSON array
}
```

### UserConsent (Consent Persistente)

```csharp
public class UserConsent
{
    public int Id { get; set; }
    public string UserId { get; set; }          // FK a User
    public string ClientId { get; set; }        // Client che ha consent
    public string Scopes { get; set; }          // JSON array scopes
    public DateTime GrantedAt { get; set; }
    public bool IsRevoked { get; set; }         // Consent revocato
}
```

### MachineConfig (Configurazione Macchina)

```csharp
public class MachineConfig
{
    public int Id { get; set; }
    public string MachineId { get; set; }       // Nome macchina (Environment.MachineName)
    public string LocalRealmName { get; set; }  // "local"
    
    // JWKS
    public string? JwksKeyId { get; set; }
    public DateTime? JwksGeneratedAt { get; set; }
    
    // Keycloak sync
    public string? KeycloakUrl { get; set; }
    public string? KeycloakRealm { get; set; }
    public DateTime? LastKeycloakSync { get; set; }
    public bool KeycloakSyncEnabled { get; set; }
}
```

### Database Location

- **Windows:** `%LOCALAPPDATA%\LocalAuthService\auth.db`
- **Linux:** `~/.local/share/LocalAuthService/auth.db`

---

## 🔑 JWKS Manager

Il `JwksManager` gestisce la generazione e il caricamento delle chiavi RSA per firmare i token JWT.

### Caratteristiche

- Genera chiave RSA 2048-bit alla prima esecuzione
- Salva chiave in `%LOCALAPPDATA%\LocalAuthService\jwks.json`
- KeyId formato: `{MachineName}-{yyyyMMdd}`
- Espone endpoint pubblico `/.well-known/jwks.json`

### File JWKS

```json
{
  "keys": [
    {
      "kty": "RSA",
      "use": "sig",
      "kid": "MACHINE-20250401",
      "n": "...",
      "e": "AQAB"
    }
  ]
}
```

---

## 🎮 Controllers

### TokenController

Gestisce l'endpoint `/connect/token` con 3 grant types:

#### 1. Password Grant (Resource Owner Password Credentials)

**Scenario:** Operatore fa login nell'HMI con username/password

```http
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password
&username=admin
&password=admin123
&client_id=hmi-local
&scope=openid profile email
```

**Flusso:**
1. Valida username/password contro database SQLite
2. Verifica password con BCrypt
3. Crea ClaimsIdentity con dati utente e ruoli
4. Ritorna token JWT firmato con JWKS locale

#### 2. Client Credentials Grant

**Scenario:** Servizio Office PC chiama API HMI senza utente

```http
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
&client_id=office-api
&client_secret=office-secret-456
&scope=api.read api.write
```

**Flusso:**
1. Valida client_id e client_secret
2. Verifica che client abbia ServiceAccountEnabled
3. Crea service account identity
4. Ritorna token JWT

#### 3. Authorization Code Grant

**Scenario:** Applicazione MES terza parte richiede accesso con consent utente

```http
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=authorization_code
&code=AUTHORIZATION_CODE
&redirect_uri=http://localhost:7000/callback
&client_id=mes-fornitore
&client_secret=mes-secret-123
```

**Flusso:**
1. Usa `AuthenticateAsync` per recuperare principal dal code
2. Valida che il code sia valido e non scaduto
3. Ritorna token JWT con claims dell'utente

**FIX IMPORTANTE:** Deve usare `await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)` invece di `HttpContext.User` per deserializzare correttamente i claims dal code.

---

### AuthorizationController

Gestisce l'endpoint `/connect/authorize` con consent screen.

#### Flow Authorization Code

**STEP 1: Browser richiede authorization**

```
GET /connect/authorize?
    client_id=mes-fornitore
    &redirect_uri=http://localhost:7000/callback
    &response_type=code
    &scope=openid profile email
    &state=xyz
```

**STEP 2: AuthorizationController controlla consent**

```csharp
// Cerca consent esistente nel DB
var existingConsent = await _db.UserConsents
    .FirstOrDefaultAsync(c => 
        c.UserId == user.Id && 
        c.ClientId == request.ClientId && 
        !c.IsRevoked);

// Se non esiste → mostra consent screen
if (consentType == ConsentTypes.Explicit && existingConsent == null && consentGranted != "true")
{
    return View("Consent", viewModel);
}
```

**STEP 3a: Prima volta - Consent Screen**

Mostra `Views/Authorization/Consent.cshtml` con:
- Nome applicazione
- Scopes richiesti
- Pulsanti "Allow" / "Deny"

**STEP 3b: User clicca "Allow"**

Form POST a `/connect/authorize/accept` con tutti i parametri OAuth2:

```csharp
[HttpPost("~/connect/authorize/accept")]
public IActionResult Accept(...)
{
    // Redirect a /connect/authorize con flag consent_granted=true
    return Redirect($"/connect/authorize?{queryString}&consent_granted=true");
}
```

**STEP 4: IssueAuthorizationCode**

```csharp
private async Task<IActionResult> IssueAuthorizationCode(User user, OpenIddictRequest request)
{
    // Crea identity AUTENTICATA
    var identity = new ClaimsIdentity(
        authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
        nameType: Claims.Name,
        roleType: Claims.Role);

    // Aggiungi claims utente
    identity.AddClaim(new Claim(Claims.Subject, user.Id));
    identity.AddClaim(new Claim(Claims.Name, user.Username));
    // ... altri claims

    var principal = new ClaimsPrincipal(identity);
    
    // Imposta scopes e resources
    principal.SetScopes(request.GetScopes());
    principal.SetResources(await _scopeManager.ListResourcesAsync(...));
    
    // IMPORTANTE: Specifica destinations per i claims
    foreach (var claim in principal.Claims)
    {
        claim.SetDestinations(GetDestinations(claim));
    }
    
    // Salva consent in background (non blocca il flow)
    if (consentGranted == "true")
    {
        _ = Task.Run(async () => {
            var consent = new UserConsent { ... };
            _db.UserConsents.Add(consent);
            await _db.SaveChangesAsync();
        });
    }

    return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
```

**STEP 5: Redirect con code**

```
HTTP/1.1 302 Found
Location: http://localhost:7000/callback?code=XXXXX&state=xyz
```

#### Claim Destinations

Il metodo `GetDestinations` specifica dove includere i claims:

```csharp
private static IEnumerable<string> GetDestinations(Claim claim)
{
    switch (claim.Type)
    {
        case Claims.Subject:
        case Claims.Name:
        case Claims.PreferredUsername:
            yield return Destinations.AccessToken;
            yield return Destinations.IdentityToken;
            break;

        case Claims.Email:
        case Claims.EmailVerified:
        case Claims.GivenName:
        case Claims.FamilyName:
            yield return Destinations.IdentityToken;
            break;

        case Claims.Role:
            yield return Destinations.AccessToken;
            break;
    }
}
```

Questo è **CRITICO** - senza `SetDestinations`, i claims non vengono salvati nel code!

---

## ⚙️ Configurazione (Program.cs)

### Database Setup

```csharp
var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "LocalAuthService",
    "auth.db");

Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddDbContext<AuthDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPath}");
    options.UseOpenIddict();
});
```

### OpenIddict Configuration

```csharp
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
            .UseDbContext<AuthDbContext>();
    })
    .AddServer(options =>
    {
        // Endpoints
        options.SetTokenEndpointUris("/connect/token")
               .SetAuthorizationEndpointUris("/connect/authorize")
               .SetUserinfoEndpointUris("/connect/userinfo");

        // Scopes
        options.RegisterScopes(
            Scopes.OpenId,
            Scopes.Email,
            Scopes.Profile,
            Scopes.OfflineAccess,
            "api.read",
            "api.write");

        // Grant types
        options.AllowPasswordFlow()
               .AllowClientCredentialsFlow()
               .AllowAuthorizationCodeFlow()
               .AllowRefreshTokenFlow();

        // Signing con JWKS locale
        options.AddSigningKey(new JwksManager().GetOrCreateKey());

        // Encryption (dev cert)
        options.AddDevelopmentEncryptionCertificate();

        // ASP.NET Core integration
        options.UseAspNetCore()
               .EnableTokenEndpointPassthrough()
               .EnableAuthorizationEndpointPassthrough()
               .DisableTransportSecurityRequirement(); // Solo dev!
    });
```

### Client Registration

Durante startup, registra i client OAuth2:

```csharp
// hmi-local (public, password grant)
await manager.CreateAsync(new OpenIddictApplicationDescriptor
{
    ClientId = "hmi-local",
    DisplayName = "HMI Local",
    Type = ClientTypes.Public,
    Permissions =
    {
        Permissions.Endpoints.Token,
        Permissions.GrantTypes.Password,
        Permissions.Scopes.Email,
        Permissions.Scopes.Profile,
        Permissions.Scopes.OpenId
    }
});

// mes-fornitore (confidential, authorization code + consent)
await manager.CreateAsync(new OpenIddictApplicationDescriptor
{
    ClientId = "mes-fornitore",
    ClientSecret = "mes-secret-123",
    DisplayName = "MES Fornitore",
    Type = ClientTypes.Confidential,
    ConsentType = ConsentTypes.Explicit, // Richiede consent!
    RedirectUris = { new Uri("http://localhost:7000/callback") },
    Permissions =
    {
        Permissions.Endpoints.Authorization,
        Permissions.Endpoints.Token,
        Permissions.GrantTypes.AuthorizationCode,
        Permissions.ResponseTypes.Code,
        Permissions.Scopes.Email,
        Permissions.Scopes.Profile,
        Permissions.Scopes.OpenId
    }
});

// office-api (confidential, client credentials)
await manager.CreateAsync(new OpenIddictApplicationDescriptor
{
    ClientId = "office-api",
    ClientSecret = "office-secret-456",
    DisplayName = "Office API",
    Type = ClientTypes.Confidential,
    Permissions =
    {
        Permissions.Endpoints.Token,
        Permissions.GrantTypes.ClientCredentials,
        Permissions.Prefixes.Scope + "api.read",
        Permissions.Prefixes.Scope + "api.write"
    }
});
```

---

## 🧪 Testing dei 3 Scenari

### Scenario #1: Password Grant

**Caso d'uso:** Operatore fa login nell'HMI

```powershell
$body = @{
    grant_type = "password"
    username = "admin"
    password = "admin123"
    client_id = "hmi-local"
    scope = "openid profile email"
}

$response = Invoke-RestMethod `
    -Uri "http://localhost:5063/connect/token" `
    -Method Post `
    -Body $body `
    -ContentType "application/x-www-form-urlencoded"

# Output
$response.access_token  # Token JWT
$response.token_type    # Bearer
$response.scope         # openid profile email
```

**Token contiene:**
- `sub`: User ID
- `name`: admin
- `preferred_username`: admin
- `email`: admin@local.dev
- `role`: admin
- `iss`, `aud`, `exp`, `iat`

---

### Scenario #2: Authorization Code + Consent

**Caso d'uso:** MES terza parte richiede accesso con consent utente

**STEP 1: Authorization Request (Browser)**

```
http://localhost:5063/connect/authorize?client_id=mes-fornitore&redirect_uri=http://localhost:7000/callback&response_type=code&scope=openid%20profile%20email
```

**Prima volta:**
- ✅ Mostra consent screen
- Click "Allow"
- Consent salvato in DB
- Redirect con code

**Volte successive:**
- ✅ Skip consent (già salvato)
- Redirect DIRETTO con code

**STEP 2: Scambia Code per Token (PowerShell)**

```powershell
$code = "CODE_DALLA_URL"

$body = @{
    grant_type = "authorization_code"
    code = $code
    redirect_uri = "http://localhost:7000/callback"
    client_id = "mes-fornitore"
    client_secret = "mes-secret-123"
}

$response = Invoke-RestMethod `
    -Uri "http://localhost:5063/connect/token" `
    -Method Post `
    -Body $body `
    -ContentType "application/x-www-form-urlencoded"

# Token ricevuto!
$response.access_token
```

---

### Scenario #3: Client Credentials

**Caso d'uso:** Servizio Office API chiama HMI senza utente (M2M)

```powershell
$body = @{
    grant_type = "client_credentials"
    client_id = "office-api"
    client_secret = "office-secret-456"
    scope = "openid api.read api.write"
}

$response = Invoke-RestMethod `
    -Uri "http://localhost:5063/connect/token" `
    -Method Post `
    -Body $body `
    -ContentType "application/x-www-form-urlencoded"

# Token service account
$response.access_token
```

**Token contiene:**
- `sub`: service-account-office-api
- `name`: office-api Service Account
- `client_id`: office-api
- `scope`: openid api.read api.write

---

## 🔧 Migrazioni Database

### Creare Migration

```powershell
dotnet ef migrations add InitialCreate
```

### Applicare Migration

```powershell
dotnet ef database update
```

### Migration Automatica (Startup)

In `Program.cs`:

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    db.Database.Migrate(); // Applica migrations
}
```

---

## 🚀 Avvio Applicazione

```powershell
dotnet run
```

**Output:**

```
📂 Database: C:\Users\...\LocalAppData\LocalAuthService\auth.db
🔄 Applying migrations...
✅ Database ready!
🌱 Seeding initial data...
   Users: admin/admin123, operator1/oper123
✅ Seed data created!
🔧 Registering OAuth clients in OpenIddict...
✅ OAuth client 'hmi-local' registered
✅ OAuth client 'mes-fornitore' registered (confidential)
✅ OAuth client 'office-api' registered (service account)
🚀 LocalAuthService ready!
Now listening on: http://localhost:5063
```

---

## 📊 Dati di Test

### Utenti

| Username   | Password  | Ruoli    |
|------------|-----------|----------|
| admin      | admin123  | admin    |
| operator1  | oper123   | operator |

### Client OAuth2

| Client ID      | Type          | Grant Types           | Consent  |
|----------------|---------------|-----------------------|----------|
| hmi-local      | Public        | Password              | Implicit |
| mes-fornitore  | Confidential  | Authorization Code    | Explicit |
| office-api     | Confidential  | Client Credentials    | N/A      |

**Client Secrets:**
- `mes-fornitore`: `mes-secret-123`
- `office-api`: `office-secret-456`

---

## 🔜 Prossimi Step

### Phase 1: Keycloak Sync (Online Mode)

- [ ] `OperatingModeDetector` - rileva se Keycloak è online
- [ ] `KeycloakSyncService` - download utenti da Keycloak
- [ ] Upload utenti creati localmente a Keycloak
- [ ] Hybrid token endpoint (try Keycloak first, fallback locale)
- [ ] Background sync ogni 15 minuti quando online
- [ ] Test transizioni online ↔ offline

### Phase 2: Admin UI

- [ ] Indicatore online/offline status
- [ ] Online mode: link a Keycloak admin console
- [ ] Offline mode: form creazione utenti locale
- [ ] API: `POST /api/admin/users`, `GET /api/status/mode`
- [ ] User management CRUD

### Phase 3: Deployment

- [ ] Self-contained publish per Windows/Linux
- [ ] Windows Service installation (NSSM)
- [ ] Linux systemd unit
- [ ] Cross-platform testing
- [ ] Production hardening (HTTPS, secrets management)

### Phase 4: Security

- [ ] Abilitare HTTPS in produzione
- [ ] Secrets in environment variables
- [ ] Rate limiting
- [ ] Audit logging
- [ ] Token revocation endpoint

---

## 📚 Risorse

### OpenIddict
- [Documentazione ufficiale](https://documentation.openiddict.com/)
- [Samples](https://github.com/openiddict/openiddict-samples)

### OAuth 2.0 / OIDC
- [OAuth 2.0 RFC 6749](https://datatracker.ietf.org/doc/html/rfc6749)
- [OpenID Connect Core](https://openid.net/specs/openid-connect-core-1_0.html)

### Entity Framework Core
- [SQLite Provider](https://learn.microsoft.com/ef/core/providers/sqlite/)
- [Migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/)

---

## 📄 Licenza

MIT License - Progetto spike per architettura offline-first OAuth2

---

## ✅ Status Corrente

**OFFLINE MODE: 100% COMPLETO** ✅

- ✅ Password Grant funzionante
- ✅ Authorization Code + Login page + Consent persistente
- ✅ Client Credentials funzionante
- ✅ JWKS locale generata e funzionante
- ✅ Token JWT firmati localmente
- ✅ Database SQLite locale
- ✅ Consent salvato e riutilizzato

**PROSSIMO:** Refresh token handler + Keycloak sync (online mode)
