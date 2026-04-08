# LocalAuthService — Testing & Deployment Guide

Guida completa per deployare e testare LocalAuthService su **Windows Service** e **Docker**, con tre scenari di configurazione.

> ⚠️ **Nota:** Questa guida copre SOLO deployment di produzione. Per lo sviluppo locale, usa `dotnet run` direttamente dal repository.

---

## PARTE 1: Setup Iniziale

### 1.1 Prerequisiti

#### Windows
- PowerShell 5.0+
- SQLite (incluso in .NET)
- Keycloak (opzionale, per online mode)

#### Linux (Docker)
- Docker 20.10+
- Docker Compose 1.29+
- (Keycloak in container separato)

### 1.2 Build del Progetto

```bash
cd LocalAuthService
dotnet build -c Release
```

---

## PARTE 2: Deploy Windows

### 2.1 Publish Self-Contained

```powershell
# x64 Windows
dotnet publish .\src\LocalAuthService\LocalAuthService.csproj -c Release -o publish\win-x64 -r win-x64 --self-contained
```

**Output:** `publish\win-x64\LocalAuthService.exe` + tutte le dipendenze (no .NET runtime richiesto).

### 2.2 Installazione come Windows Service

Copia la cartella `publish\win-x64` sulla macchina target, poi come **Amministratore**:

#### Default (porta 5063, seed attivo)
```powershell
cd "C:\Program Files\LocalAuthService"
.\install-windows-service.ps1
```

#### Porta custom
```powershell
.\install-windows-service.ps1 -Port 8080
```

#### Senza seed (importa da Keycloak)
```powershell
.\install-windows-service.ps1 -SeedSampleData $false
```

#### Combinato
```powershell
.\install-windows-service.ps1 -Port 8080 -SeedSampleData $false
```

**Verifica:**
```powershell
Get-Service LocalAuthService
```

**Disinstalla:**
```powershell
.\uninstall-windows-service.ps1
```

**Database location:** `C:\Windows\system32\config\systemprofile\AppData\Local\LocalAuthService\auth.db`

---

### 2.3 Configurazione Environment Variables

Crea `C:\ProgramData\LocalAuthService\appsettings.json` oppure imposta variabili:

```powershell
[Environment]::SetEnvironmentVariable("Keycloak__Url", "http://keycloak.internal:8080", "Machine")
[Environment]::SetEnvironmentVariable("Keycloak__Realm", "falegnameria-rossi", "Machine")
[Environment]::SetEnvironmentVariable("Keycloak__AdminUsername", "admin", "Machine")
[Environment]::SetEnvironmentVariable("Keycloak__AdminPassword", "supersecret", "Machine")
```

Riavvia il servizio:
```powershell
Restart-Service LocalAuthService
```

---

## PARTE 3: Deploy Linux (Docker)

### 3.1 Docker Compose

Crea `docker-compose.yml`:

```yaml
version: '3.8'

services:
  localauth:
    image: localauth:latest
    container_name: localauth
    ports:
      - "5063:5063"
    volumes:
      - localauth-data:/var/lib/localauth
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      SeedSampleData: "true"
      Keycloak__Url: "http://keycloak:8080"
      Keycloak__Realm: "falegnameria-rossi"
      Keycloak__AdminUsername: "admin"
      Keycloak__AdminPassword: "admin123"
      Keycloak__SyncIntervalMinutes: "15"
    networks:
      - auth-network
    depends_on:
      - keycloak

  keycloak:
    image: quay.io/keycloak/keycloak:latest
    container_name: keycloak
    ports:
      - "8080:8080"
    environment:
      KEYCLOAK_ADMIN: admin
      KEYCLOAK_ADMIN_PASSWORD: admin123
    command: start-dev
    volumes:
      - keycloak-data:/opt/keycloak/data
    networks:
      - auth-network

volumes:
  localauth-data:
  keycloak-data:

networks:
  auth-network:
    driver: bridge
```

### 3.2 Build immagine Docker

```bash
# Dalla root del repo
docker build -f Dockerfile -t localauth:latest .
```

**Dockerfile:**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5063
ENTRYPOINT ["dotnet", "LocalAuthService.dll"]
```

### 3.3 Run con Docker Compose

```bash
docker-compose up -d
```

**Verifiche:**
```bash
docker ps
docker logs -f localauth
docker exec localauth ls /var/lib/localauth/
```

**Stop:**
```bash
docker-compose down
```

---

## PARTE 4: Test Scenarios

### Prerequisiti per tutti gli scenari

> ⚠️ **Importante:** Prima di iniziare i test, **LocalAuthService deve essere già installato come Windows Service e in esecuzione**, oppure **deployato come container Docker in esecuzione**.

**Verifica che il servizio è in esecuzione:**

Windows:
```powershell
Get-Service LocalAuthService | Select-Object Status
```

Docker:
```bash
docker ps | grep localauth
```

Poi inizia i test:
- LocalAuthService in esecuzione (http://localhost:5063)
- Apri il browser a `http://localhost:5063/test` per il test client integrato

---

## SCENARIO 1: Completely Offline

### Setup

**Scenario:** La macchina ha una configurazione Keycloak valida, ma **la rete è DOWN** (guasto, isolamento, blackout). L'app deve funzionare completamente offline usando il database locale.

**Configurazione (con URL Keycloak valido):**

**Windows Service:**
```powershell
[Environment]::SetEnvironmentVariable("Keycloak__Url", "http://keycloak.internal:8080", "Machine")
[Environment]::SetEnvironmentVariable("Keycloak__Realm", "falegnameria-rossi", "Machine")
[Environment]::SetEnvironmentVariable("Keycloak__AdminUsername", "admin", "Machine")
[Environment]::SetEnvironmentVariable("Keycloak__AdminPassword", "admin123", "Machine")
[Environment]::SetEnvironmentVariable("SeedSampleData", "true", "Machine")
```

Riavvia il servizio:
```powershell
Restart-Service LocalAuthService
```

**Isolamento di rete:**

Scegli uno dei metodi:

**Opzione A — Firewall (Windows):**
```powershell
# Blocca traffico verso Keycloak
New-NetFirewallRule -DisplayName "Block Keycloak" -Direction Outbound `
    -Action Block -RemoteAddress 192.168.x.x -RemotePort 8080 -Protocol TCP

# Sblocca dopo il test
Remove-NetFirewallRule -DisplayName "Block Keycloak"
```

**Opzione B — Scollega rete fisica:**
- Stacca il cavo ethernet, disattiva WiFi, ecc.

**Opzione C — hosts file (Windows):**
```powershell
# In C:\Windows\System32\drivers\etc\hosts
# Aggiungi:
127.0.0.1 keycloak.internal
```

L'app parte, tenta di contattare Keycloak, fallisce, e usa il DB locale con i seed user (`admin/admin123`, `operator1/oper123`).

**Log atteso:**
```
Keycloak health check failed: Connection refused / Timeout
OperatingModeDetector: IsOnline = false
All operations use local database only
```

### Test Case 1.1: Password Grant (Scenario 1)

**Prerequisito:** Rete DOWN (firewall/isolamento attivo)

```powershell
$body = @{
    grant_type = "password"
    username = "admin"
    password = "admin123"
    client_id = "hmi-local"
    scope = "openid profile email offline_access"
}

$token = Invoke-RestMethod -Uri "http://localhost:5063/connect/token" `
    -Method Post -Body $body `
    -ContentType "application/x-www-form-urlencoded"

$token.access_token  # Visualizza il token
```

**Atteso:** ✅ Token JWT valido (fallback locale, Keycloak offline)

### Test Case 1.2: Authorization Code (Scenario 2)

Browser:
```
http://localhost:5063/test
```

Scenario 2:
- **Client:** `mes-fornitore`
- **Client Secret:** `mes-secret-123`
- Clicca "Avvia Scenario 2"
- Login: `admin/admin123`
- Consent: scegli durata (es. 30 giorni)
- Clicca "Allow"

**Atteso:** ✅ Token ricevuto nel callback, pagina mostra claims

### Test Case 1.3: Client Credentials (Scenario 3)

```powershell
$body = @{
    grant_type = "client_credentials"
    client_id = "office-api"
    client_secret = "office-secret-456"
    scope = "openid api.read api.write"
}

Invoke-RestMethod -Uri "http://localhost:5063/connect/token" `
    -Method Post -Body $body `
    -ContentType "application/x-www-form-urlencoded"
```

**Atteso:** ✅ Service account token (nessun utente coinvolto)

### Test Case 1.4: Refresh Token

```powershell
# Ottieni token con offline_access
$body = @{
    grant_type = "password"
    username = "admin"
    password = "admin123"
    client_id = "hmi-local"
    scope = "openid profile email offline_access"
}
$initial = Invoke-RestMethod ... # (come sopra)

# Rinnova con refresh token
$refresh_body = @{
    grant_type = "refresh_token"
    refresh_token = $initial.refresh_token
    client_id = "hmi-local"
}
$renewed = Invoke-RestMethod -Uri "http://localhost:5063/connect/token" `
    -Method Post -Body $refresh_body `
    -ContentType "application/x-www-form-urlencoded"
```

**Atteso:** ✅ Nuovo access_token + nuovo refresh_token (rotation)

### Test Case 1.5: Revoca Consent

Browser `/test`:
- Scenario 2: esegui una volta per creare il consent
- Clicca il bottone "🗑️ Revoca Consent" nel tab Scenario 2
- Ripeti Scenario 2 → deve richiiedere consent di nuovo

**Atteso:** ✅ Consent revocato, consent screen riappare

---

## SCENARIO 2: Online Mode con Keycloak Realm Preesistente

### Setup Keycloak

1. **Avvia Keycloak:**
   ```bash
   docker run -d -p 8080:8080 \
     -e KEYCLOAK_ADMIN=admin \
     -e KEYCLOAK_ADMIN_PASSWORD=admin123 \
     quay.io/keycloak/keycloak:latest start-dev
   ```

2. **Crea realm `falegnameria-rossi`:**
   - Vai a http://localhost:8080
   - Login: admin/admin123
   - "Create realm" → nome: `falegnameria-rossi` → Create

3. **Crea utenti in Keycloak:**
   - Users → "Create new user"
   - Username: `mario.rossi`, email: mario@falegnameria.it → Create
   - Credentials → Set Password: `mario123` (temporary: OFF)
   - Username: `anna.bianchi`, email: anna@falegnameria.it → Create
   - Credentials → Set Password: `anna123` (temporary: OFF)

### Setup LocalAuthService

**Configurazione (Windows Service):**

Imposta le variabili d'ambiente come amministratore:
```powershell
[Environment]::SetEnvironmentVariable("Keycloak__Url", "http://localhost:8080", "Machine")
[Environment]::SetEnvironmentVariable("Keycloak__Realm", "falegnameria-rossi", "Machine")
[Environment]::SetEnvironmentVariable("Keycloak__AdminUsername", "admin", "Machine")
[Environment]::SetEnvironmentVariable("Keycloak__AdminPassword", "admin123", "Machine")
[Environment]::SetEnvironmentVariable("Keycloak__SyncIntervalMinutes", "5", "Machine")
[Environment]::SetEnvironmentVariable("SeedSampleData", "false", "Machine")
```

Riavvia il servizio:
```powershell
Restart-Service LocalAuthService
```

**Atteso nei log** (visualizzabili con `Get-EventLog`):
```
Keycloak ONLINE — starting sync
Keycloak realm 'falegnameria-rossi' already exists
Enabled Direct Access Grants on Keycloak client 'account'
Imported user from Keycloak: mario.rossi
Imported user from Keycloak: anna.bianchi
```

**Configurazione (Docker):**

Modifica il `docker-compose.yml` con le stesse variabili e ricrea il container:
```bash
docker-compose down
docker-compose up -d
```

### Test Case 2.1: Login con Keycloak (Password Grant)

```powershell
$body = @{
    grant_type = "password"
    username = "mario.rossi"
    password = "mario123"
    client_id = "hmi-local"
    scope = "openid profile email offline_access"
}

Invoke-RestMethod -Uri "http://localhost:5063/connect/token" `
    -Method Post -Body $body `
    -ContentType "application/x-www-form-urlencoded"
```

**Atteso:** ✅ Token valido, credenziali validate su Keycloak

### Test Case 2.2: Password Sync su Primo Login Online

1. Login con `mario.rossi/mario123` (come sopra)
2. Nel DB locale: `mario.rossi` ora ha `HasLocalPassword = true`
3. Keycloak down (stop il container)
4. Ripeti il login → **deve funzionare offline** (password sincronizzata)

```powershell
# Con Keycloak offline
$body = @{ ... mario.rossi/mario123 ... }
Invoke-RestMethod ... # ✅ Deve funzionare (offline fallback)
```

### Test Case 2.3: Cambio Password su Keycloak

1. **Keycloak admin:** Utenti → `mario.rossi` → Credentials → "Reset password"
   - Nuova password: `newpassword123`
   - Temporary: OFF
2. **LocalAuthService online:** Login con nuova password
   ```powershell
   $body = @{ ... mario.rossi/newpassword123 ... }
   Invoke-RestMethod ...  # ✅ Accettato
   ```
3. **LocalAuthService offline:** Login con nuova password
   ```powershell
   # Keycloak down
   $body = @{ ... mario.rossi/newpassword123 ... }
   Invoke-RestMethod ...  # ✅ Accettato (sincronizzato in locale)
   ```

---

## SCENARIO 3: Offline-First → Online Sync

### Setup

**Fase 1 — Completamente offline:**
- LocalAuthService corre **senza** Keycloak
- Utenti creati localmente: `mario.rossi/mario123`, `anna.bianchi/anna123`

**Fase 2 — Keycloak diventa disponibile:**
- Keycloak viene deployato (realm `falegnameria-rossi`)
- LocalAuthService si sincronizza automaticamente

### Implementazione Fase 1 (Offline)

**Configurazione:**
```json
{
  "SeedSampleData": false,
  "Keycloak": {
    "Url": "",
    "Realm": ""
  }
}
```

**Crea utenti locali (opzioni):**

**Opzione A — Via SQL diretto:**
```powershell
$db = "$env:LOCALAPPDATA\LocalAuthService\auth.db"
$hash1 = (Get-Content .\Program.cs | Select-String 'BCrypt.Net.BCrypt.HashPassword').ToString()

# Usa PowerShell script per aggiungere utente:
$script = @"
INSERT INTO Users (Id, Username, PasswordHash, HasLocalPassword, Email, FirstName, LastName, Enabled, CreatedLocally, CreatedAt, UpdatedAt, Roles)
VALUES (lower(hex(randomblob(16))), 'mario.rossi', 
  '$2a$11$...hash_bcrypt_di_mario123...', 1, 
  'mario@falegnameria.it', 'Mario', 'Rossi', 1, 1, datetime('now'), datetime('now'), '["user"]');
"@
```

**Opzione B — Test API (dopo primo avvio):**

Nessun endpoint admin attuale. Soluzione: modifica il seed temporaneamente o usa direttamente il DB.

**Fase 1 Test:**

```powershell
# Login offline
$body = @{
    grant_type = "password"
    username = "mario.rossi"
    password = "mario123"
    client_id = "hmi-local"
    scope = "openid profile email offline_access"
}

Invoke-RestMethod -Uri "http://localhost:5063/connect/token" `
    -Method Post -Body $body `
    -ContentType "application/x-www-form-urlencoded"
```

**Atteso:** ✅ Token (offline)

### Implementazione Fase 2 (Keycloak Online)

**Setup Keycloak** (come Scenario 2):
1. Keycloak con realm `falegnameria-rossi`
2. Admin user: admin/admin123

**Modifica configurazione LocalAuthService (Windows Service):**

Imposta le variabili d'ambiente:
```powershell
[Environment]::SetEnvironmentVariable("Keycloak__Url", "http://localhost:8080", "Machine")
[Environment]::SetEnvironmentVariable("Keycloak__Realm", "falegnameria-rossi", "Machine")
[Environment]::SetEnvironmentVariable("Keycloak__AdminUsername", "admin", "Machine")
[Environment]::SetEnvironmentVariable("Keycloak__AdminPassword", "admin123", "Machine")
[Environment]::SetEnvironmentVariable("Keycloak__SyncIntervalMinutes", "5", "Machine")
[Environment]::SetEnvironmentVariable("SeedSampleData", "false", "Machine")
```

Riavvia il servizio:
```powershell
Restart-Service LocalAuthService
```

**Modifica configurazione LocalAuthService (Docker):**

Aggiorna il `docker-compose.yml` e ricrea:
```bash
docker-compose down
docker-compose up -d
```

**Atteso nei log:**
```
Keycloak ONLINE — starting sync
Created Keycloak user 'mario.rossi' with temporary password: Tmp-xK9mAbQ2Rp
Assigned roles [...] to Keycloak user ...
Synced local user to Keycloak: mario.rossi
```

### Fase 2 Test: Login con Password Temporanea (fallback)

```powershell
# Keycloak ha mario.rossi con password TEMPORANEA
# LocalAuthService ha mario.rossi con password locale mario123

$body = @{ ... mario.rossi/mario123 ... }
Invoke-RestMethod ...
```

**Atteso nei log:**
```
Keycloak rejected 'mario.rossi' but local password is valid — using local auth
Keycloak password synced for user 'mario.rossi'
```

**Atteso:** ✅ Login riuscito, password sincronizzata su Keycloak (non temporanea)

### Fase 2 Test: Login Online (dopo sync password)

```powershell
# Keycloak ora ha mario.rossi con password mario123 (non temporanea)
$body = @{ ... mario.rossi/mario123 ... }
Invoke-RestMethod ...
```

**Atteso nei log:**
```
Validating with Keycloak clientId: account
Keycloak result for mario.rossi: Success
```

**Atteso:** ✅ Token da Keycloak

### Fase 2 Test: Offline dopo Sync

```bash
# Stop Keycloak
docker stop keycloak

# Login offline
$body = @{ ... mario.rossi/mario123 ... }
Invoke-RestMethod ...  # ✅ Ancora funzionante (offline fallback)
```

---

## Checklist Completa

### Scenario 1 (Offline)
- [ ] Test Case 1.1: Password Grant
- [ ] Test Case 1.2: Authorization Code + Consent
- [ ] Test Case 1.3: Client Credentials
- [ ] Test Case 1.4: Refresh Token
- [ ] Test Case 1.5: Revoca Consent
- [ ] Verifica: nessun log "Keycloak ONLINE"

### Scenario 2 (Online + Keycloak preesistente)
- [ ] Keycloak realm creato e popolato
- [ ] Sync importa utenti Keycloak
- [ ] Login con credenziali Keycloak
- [ ] Password sincronizzata in locale
- [ ] Offline fallback funziona dopo primo login
- [ ] Cambio password su Keycloak si propaga

### Scenario 3 (Offline → Online)
- [ ] Fase 1: utenti locali creati
- [ ] Fase 1: login offline funziona
- [ ] Fase 2: utenti sincronizzati a Keycloak con password temporanea
- [ ] Fase 2: login con password locale fa fallback e sincronizza
- [ ] Fase 2: login online funziona dopo sync
- [ ] Fase 2: login offline funziona dopo sync

---

## Troubleshooting

### "Invalid user credentials" online ma password locale OK

→ Keycloak ha password temporanea. Attendere la sincronizzazione password al prossimo login (fallback locale).

### "No local password set" offline

→ Utente sincronizzato da Keycloak ma non ha mai fatto login online. Richiede almeno un login online per impostare la password locale.

### "Keycloak OFFLINE" quando dovrebbe essere online

→ Verifica:
- `Keycloak:Url` configurato correttamente
- Keycloak accessibile da localhost (o IP macchina)
- Check logs: `Keycloak auth failed: ...`

### Database corrotto

```powershell
Remove-Item "$env:LOCALAPPDATA\LocalAuthService\auth.db"
# Riavvia l'app (ricrea il DB con seed)
```

---

## Note Finali

- **Windows Service:** database in `C:\Windows\system32\config\systemprofile\AppData\Local\LocalAuthService\auth.db` (SYSTEM user)
- **Docker:** database in volume `localauth-data:/var/lib/localauth`
- **SyncIntervalMinutes:** 5 in dev, 15+ in produzione (non martellare Keycloak)
- **Seed:** disabilitare in produzione (`SeedSampleData: false`) se usi Keycloak
