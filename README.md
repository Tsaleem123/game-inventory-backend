# Game Inventory Backend

The ASP.NET Core (.NET 10) Web API behind [Game Inventory](https://github.com/Tsaleem123/game-inventory-frontend). It handles user accounts (sign up with email confirmation, login, password reset), searches games through the [IGDB](https://www.igdb.com/) API, and stores each user's saved games, ratings and play status in SQL Server.

This guide walks you through running it on your own computer, on **macOS** or **Windows**. Plan for about 30–45 minutes the first time, most of it spent installing tools and getting API keys.

---

## Contents

1. [What you'll need](#1-what-youll-need)
2. [Install the tools](#2-install-the-tools)
3. [Start SQL Server](#3-start-sql-server)
4. [Get your API keys](#4-get-your-api-keys)
5. [Clone the repository](#5-clone-the-repository)
6. [Create your `.env` file](#6-create-your-env-file)
7. [Run the API](#7-run-the-api)
8. [Connect the frontend](#8-connect-the-frontend)
9. [Troubleshooting](#9-troubleshooting)
10. [Reference](#10-reference)

---

## 1. What you'll need

| Requirement | Why |
|---|---|
| **.NET 10 SDK** | Builds and runs the API |
| **SQL Server** | Stores users and saved games. Windows can run it natively; macOS runs it in Docker |
| **Git** | Clones the repository |
| **A Twitch developer account** (free) | IGDB is owned by Twitch, and its API keys come from the Twitch developer console |
| **An email account that can send over SMTP** (a Gmail account works) | New accounts must confirm their email before they can log in, and password resets are sent by email |

> **Heads-up:** Without working email settings, the API still starts, but **nobody can finish signing up**, because the confirmation email never arrives. Set up email in step 4 before you test signup.

---

## 2. Install the tools

### macOS

1. **Homebrew** (optional, but it makes the rest easier). Open **Terminal** and run the install command from [brew.sh](https://brew.sh).
2. **.NET 10 SDK.** Either download the macOS installer from [dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0) (pick **Arm64** for Apple Silicon M1/M2/M3/M4, **x64** for Intel), or run:
   ```bash
   brew install --cask dotnet-sdk
   ```
3. **Docker Desktop** from [docker.com/products/docker-desktop](https://www.docker.com/products/docker-desktop/). Open it once after installing and wait until it says it's running.
   - **Apple Silicon only:** in Docker Desktop go to **Settings → General** and turn on **"Use Rosetta for x86_64/amd64 emulation on Apple Silicon"**. SQL Server needs this.
4. **Git** usually comes with macOS. Check with `git --version`. If it asks to install the Command Line Tools, say yes.

Check that .NET installed correctly:
```bash
dotnet --version   # should print 10.x.x
```

### Windows

1. **.NET 10 SDK.** Download the Windows **x64** installer from [dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0), or run this in **PowerShell**:
   ```powershell
   winget install Microsoft.DotNet.SDK.10
   ```
2. **SQL Server.** You have two options:
   - **SQL Server Express LocalDB** (simplest). It's installed with Visual Studio's *"ASP.NET and web development"* workload. To install it on its own, run the [SQL Server Express installer](https://www.microsoft.com/en-us/sql-server/sql-server-downloads), choose **Download Media**, then pick **LocalDB**.
   - **SQL Server Developer or Express** (a full install). Download it from the same page and use the defaults.
3. **Git** from [git-scm.com/download/win](https://git-scm.com/download/win), or `winget install Git.Git`.
4. *(Optional)* **Visual Studio 2022** with the *ASP.NET and web development* workload, if you'd rather run the project from an IDE.

**Close and reopen PowerShell** after installing so the new commands are picked up, then check:
```powershell
dotnet --version   # should print 10.x.x
```

---

## 3. Start SQL Server

You don't need to create any tables yourself. **The API creates the database and applies all migrations automatically the first time it starts.** You only need a SQL Server it can connect to.

### macOS (Docker)

Pick a strong password: at least 8 characters, with uppercase, lowercase, a number and a symbol. SQL Server won't start with a weak one. Then run:

```bash
docker run -d --name gameinventory-sql \
  -e "ACCEPT_EULA=Y" \
  -e "MSSQL_SA_PASSWORD=YourStrong!Passw0rd" \
  -p 1433:1433 \
  mcr.microsoft.com/mssql/server:2022-latest
```

Check that it's running (look for `gameinventory-sql` with status `Up`):
```bash
docker ps
```

Later, after a restart, start it again with `docker start gameinventory-sql`.

Your connection string (used in step 6) will be:
```
Server=localhost,1433;Database=GameInventory;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True
```

### Windows

**LocalDB:** start it in PowerShell (it's fine if it says it's already running):
```powershell
sqllocaldb start MSSQLLocalDB
```
Connection string:
```
Server=(localdb)\MSSQLLocalDB;Database=GameInventory;Trusted_Connection=True;TrustServerCertificate=True
```

**SQL Server Express (full install):**
```
Server=localhost\SQLEXPRESS;Database=GameInventory;Trusted_Connection=True;TrustServerCertificate=True
```

**SQL Server Developer (default instance):**
```
Server=localhost;Database=GameInventory;Trusted_Connection=True;TrustServerCertificate=True
```

> If you prefer Docker on Windows too, the macOS Docker instructions above work the same way in PowerShell. Replace the `\` line breaks with backticks (`` ` ``) or put the whole command on one line.

---

## 4. Get your API keys

### IGDB (game data)

1. Sign in at [dev.twitch.tv/console](https://dev.twitch.tv/console). You need **two-factor authentication** turned on in your Twitch account settings before Twitch lets you register an app.
2. Click **Register Your Application**.
   - **Name:** anything unique, e.g. `GameInventory-Local-YourName`
   - **OAuth Redirect URLs:** `http://localhost`
   - **Category:** *Application Integration*
   - **Client Type:** *Confidential*
3. Open the app you just made. Copy the **Client ID**, then click **New Secret** and copy the **Client Secret**. You'll paste both into `.env` as `IGDB_Client` and `IGDB_Secret`.

### Email (Gmail example)

Gmail won't accept your normal password from an app. Create an **App Password** instead:

1. Turn on **2-Step Verification** for the Google account at [myaccount.google.com/security](https://myaccount.google.com/security).
2. Go to [myaccount.google.com/apppasswords](https://myaccount.google.com/apppasswords), create one (name it "GameInventory"), and copy the 16-character password.

You'll use:

| Setting | Value |
|---|---|
| `EmailSettings__SmtpServer` | `smtp.gmail.com` |
| `EmailSettings__SenderEmail` | your Gmail address |
| `EmailSettings__Password` | the 16-character App Password (no spaces) |

Other providers work too; use their SMTP server name. The port defaults to `587`.

### JWT secret

This is a random string the API uses to sign login tokens. It must be **at least 32 characters**, or logging in fails. Generate one:

**macOS:**
```bash
openssl rand -base64 48
```

**Windows (PowerShell):**
```powershell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))
```

---

## 5. Clone the repository

**macOS (Terminal) / Windows (PowerShell):**
```bash
git clone https://github.com/Tsaleem123/game-inventory-backend.git
cd game-inventory-backend
```

(You can also use **GitHub Desktop**: *File → Clone Repository*.)

---

## 6. Create your `.env` file

All secrets live in a `.env` file. It's already in `.gitignore`, so it never gets committed.

> ⚠️ The `.env` file must go **inside the `GameInventory` folder**, next to `GameInventory.csproj`, **not** in the repository root. The API looks for it in the folder you run it from.

Copy the template:

**macOS:**
```bash
cd GameInventory
cp .env.example .env
```

**Windows (PowerShell):**
```powershell
cd GameInventory
Copy-Item .env.example .env
```

Open `GameInventory/.env` in any text editor and fill it in:

```env
# --- Database (paste your connection string from step 3, inside single quotes) ---
ConnectionStrings__DefaultConnection='Server=localhost,1433;Database=GameInventory;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True'

# --- Login tokens ---
JwtSettings__Secret=paste-the-random-string-from-step-4-here
JwtSettings__Issuer=GameInventoryAPI
JwtSettings__Audience=GameInventoryClient

# --- Email (needed for signup confirmation and password reset) ---
EmailSettings__SenderEmail=you@gmail.com
EmailSettings__Password=your16charapppassword
EmailSettings__SmtpServer=smtp.gmail.com

# --- IGDB / Twitch ---
IGDB_Client=your-twitch-client-id
IGDB_Secret=your-twitch-client-secret

# --- Local server port ---
ASPNETCORE_URLS=http://localhost:5202

# --- Which websites may call the API (comma-separated, no spaces) ---
CORS_ORIGINS=http://localhost:5173,http://localhost:3000

# --- Where password-reset links send people (optional, this is the default) ---
FrontendBaseUrl=http://localhost:5173
```

Notes:
- The double underscore (`__`) is intentional. It's how .NET maps `.env` values onto the sections in `appsettings.json`. For example, `JwtSettings__Secret` fills in `JwtSettings:Secret`.
- **Keep the connection string in single quotes**, especially on Windows, so characters like `\` and `;` are read exactly as written.
- `CORS_ORIGINS` is **required**, and the API won't start without it. It must include the address the frontend runs on (`http://localhost:5173` by default).
- **Don't put `ASPNETCORE_ENVIRONMENT` in this file.** The API only reads `.env` when it's *already* running in Development mode, so that setting has to come from your terminal. Step 7 shows how.

---

## 7. Run the API

From the **`GameInventory` folder** (the one containing `.env`):

**macOS (Terminal):**
```bash
dotnet restore
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile
```

**Windows (PowerShell):**
```powershell
dotnet restore
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile
```

Why both parts matter:
- **`ASPNETCORE_ENVIRONMENT=Development`** tells the API it's running locally. Only then does it load your `.env` file. It also turns on the Swagger page and detailed error messages. In production (Azure) the API deliberately ignores `.env` and uses the App Service settings instead.
- **`--no-launch-profile`** skips `Properties/launchSettings.json`, which would otherwise force `Production` mode, and then `.env` wouldn't load.

On Windows, `$env:` only lasts for that PowerShell window, so run that line again whenever you open a new one.

The first start takes a little longer while it creates the database. When you see a line like

```
Now listening on: http://localhost:5202
```

it's ready. Leave this window open. Press **Ctrl + C** to stop the API.

**Check it works:** open [http://localhost:5202/swagger](http://localhost:5202/swagger) in your browser. You should see the list of endpoints (this needs `ASPNETCORE_ENVIRONMENT=Development`).

### Running from Visual Studio (Windows) or Rider/VS Code

Open `GameInventory.sln`. Make sure `.env` is in the `GameInventory` project folder. The included launch profile runs in **Production** mode, which skips `.env`, so the API fails with "DefaultConnection string is missing". Before pressing **Run**, change the environment to Development:

- **Visual Studio:** *Debug → GameInventory Debug Properties*, and set `ASPNETCORE_ENVIRONMENT` to `Development`.
- **Rider:** *Run → Edit Configurations → GameInventoryAPI → Environment variables*, and set `ASPNETCORE_ENVIRONMENT=Development`.
- **Either:** edit `Properties/launchSettings.json` and change `"Production"` to `"Development"`.

Whichever you choose, don't commit that change, because the deployed app relies on Production.

---

## 8. Connect the frontend

Follow the [frontend README](https://github.com/Tsaleem123/game-inventory-frontend#readme). Its `.env` should contain:

```env
VITE_ENDPOINT=http://localhost:5202/
```

Then sign up in the app, click the link in the confirmation email, and log in.

> Password-reset emails link to `FrontendBaseUrl` + `/reset-password`, and `FrontendBaseUrl` defaults to `http://localhost:5173`. If you run the frontend on a different port, set `FrontendBaseUrl` in `.env` to match.

---

## 9. Troubleshooting

| Problem | Fix |
|---|---|
| `DefaultConnection string is missing from configuration` | Your `.env` wasn't loaded. **Most common cause:** `ASPNETCORE_ENVIRONMENT` wasn't set to `Development` in the terminal (or IDE run settings) before starting. See step 7. Also check that the file is in `GameInventory/` (not the repo root), that it's named exactly `.env` (Windows can silently save it as `.env.txt`; turn on *View → File name extensions* in Explorer to check), and that you ran `dotnet run` from inside `GameInventory/`. |
| Swagger page shows 404 | You're in Production mode. Start the API with `ASPNETCORE_ENVIRONMENT=Development` (step 7). |
| `JWT Secret/Issuer/Audience is missing` | One of the `JwtSettings__` lines is empty or misspelled in `.env`. |
| `CORS_ORIGINS is missing or empty` | Add the `CORS_ORIGINS=` line from step 6. |
| `A network-related or instance-specific error occurred…` / `Login failed for user 'sa'` | SQL Server isn't running or the connection string is wrong. **macOS:** check `docker ps`, then `docker start gameinventory-sql`. Make sure the password matches what you used in `docker run`. **Windows:** run `sqllocaldb info` to check the instance name. |
| SQL Server container exits right away (macOS) | The SA password was too weak, or Rosetta is off on Apple Silicon. Check `docker logs gameinventory-sql`, fix the problem, then `docker rm gameinventory-sql` and run the `docker run` command again. |
| `The certificate chain was issued by an authority that is not trusted` | Add `TrustServerCertificate=True` to the connection string. |
| `IDX10720` / `key size must be greater than 256 bits` on login | Your JWT secret is too short. Use at least 32 characters (see step 4). |
| Signup says "Confirmation email sent" but nothing arrives | Check the email settings and that you used a Gmail **App Password**. Check spam. The terminal running the API will show an SMTP error if sending failed. |
| Game search returns 500 / IGDB errors | Check `IGDB_Client` and `IGDB_Secret`. Generate a new secret in the Twitch console if needed. |
| Browser console shows a **CORS** error | Add the exact frontend address (e.g. `http://localhost:5173`, with no trailing slash) to `CORS_ORIGINS` and restart the API. |
| `Address already in use` / port 5202 busy | Something else is using the port. Stop it, or change `ASPNETCORE_URLS` (and update the frontend's `VITE_ENDPOINT` to match). |
| `dotnet` is not recognized (Windows) | Close and reopen PowerShell after installing the SDK. |

---

## 10. Reference

### Tech stack

| Tool | Purpose |
|---|---|
| ASP.NET Core 10 | Web API |
| Entity Framework Core 10 + SQL Server | Database access and migrations |
| ASP.NET Core Identity | User accounts and passwords |
| JWT Bearer authentication | Login tokens |
| MailKit | Sending email |
| DotNetEnv | Loading the `.env` file |
| Swashbuckle (Swagger) | API documentation at `/swagger` |
| IGDB API (via Twitch) | Game data |

### Project structure

```
game-inventory-backend/
├── GameInventory.sln
└── GameInventory/
    ├── Controllers/
    │   ├── AuthController.cs     # register, confirm-email, login, forgot/reset password
    │   ├── ExploreController.cs  # Explore page: popular games, categories, category rows
    │   ├── GamesController.cs    # the logged-in user's saved games, ratings, status
    │   └── SearchController.cs   # IGDB game search (by name and/or genre)
    ├── DTOs/                     # request bodies
    ├── Migrations/               # EF Core migrations (applied automatically on startup)
    ├── Models/                   # database entities
    ├── Services/
    │   ├── EmailService.cs       # SMTP email via MailKit
    │   ├── TokenService.cs       # JWT generation
    │   ├── IgdbRateLimiter.cs    # keeps IGDB calls under its rate limit
    │   └── IgdbRateLimitingHandler.cs
    ├── AppDbContext.cs
    ├── ApplicationUser.cs        # extended Identity user
    ├── Program.cs                # startup: config, auth, CORS, Swagger
    ├── appsettings.json          # non-secret defaults (secrets come from .env)
    ├── .env.example              # template for your .env
    └── GameInventory.csproj
```

### API endpoints

Browse and try them all at `/swagger` when running in Development.

**Auth: `/api/auth`**

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| POST | `/register` | No | Start registration (sends a confirmation email) |
| GET | `/confirm-email?token=` | No | Finish registration after the email link is clicked |
| POST | `/login` | No | Returns a JWT |
| POST | `/forgot-password` | No | Sends a password-reset email |
| GET | `/reset-password?token=&email=` | No | Redirects to the frontend's reset page |
| POST | `/reset-password` | No | Sets the new password |

**Games: `/api/games`** (every endpoint needs an `Authorization: Bearer <token>` header)

| Method | Endpoint | Description |
|---|---|---|
| POST | `/list` | Add a game to your library |
| GET | `/list` | Get all games in your library |
| DELETE | `/list/{gameId}` | Remove a game from your library |
| PUT | `/usergames/{id}/rating` | Update your rating for a game |
| PUT | `/usergames/{id}/status` | Update your play status for a game |

**Search: `/api/search`**

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | `?query=zelda&genre=Action&page=1&pageSize=10` | No | Search IGDB by name, genre, or both (at least one is required) |
| GET | `/by-id/{id}` | No | Get a single game by IGDB ID |

**Explore: `/api/explore`**

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | `/popular?limit=10` | No | Games trending today |
| GET | `/categories?page=1&pageSize=5` | No | Paged list of genres and themes |
| GET | `/category?type=genre&id=12&limit=20` | No | Most popular games in one genre or theme |

### Deploying

The project includes a Web Deploy publish profile under `Properties/PublishProfiles/` for Azure App Service. In production:

1. Set `ASPNETCORE_ENVIRONMENT=Production`. The `.env` file is then ignored on purpose.
2. Provide every setting from `.env.example` as an **App Service application setting** (or via Azure Key Vault). Use the same names, including the `__` double underscores. Never commit secrets.
3. Set `CORS_ORIGINS` to your deployed frontend's address(es), and `FrontendBaseUrl` to the deployed frontend's URL.
4. Database migrations run automatically on startup.

### Changing the database schema

Migrations are applied automatically when the API starts. To **create** a new one after changing a model, run this from the `GameInventory` folder:

```bash
dotnet tool restore                      # installs the pinned dotnet-ef tool
dotnet ef migrations add YourMigrationName
```

The next `dotnet run` applies it.
