# Game Inventory Backend

The ASP.NET Core (.NET 9) Web API behind [Game Inventory](https://github.com/Tsaleem123/game-inventory-frontend). It handles user accounts (sign up with email confirmation, login, password reset), searches games through the [IGDB](https://www.igdb.com/) API, and stores each user's saved games, ratings and play status in SQL Server.

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
| **.NET 9 SDK** | Builds and runs the API |
| **SQL Server** | Stores users and saved games. Windows can run it natively; macOS runs it in Docker |
| **Git** | Clones the repository |
| **A Twitch developer account** (free) | IGDB is owned by Twitch, and its API keys come from the Twitch developer console |
| **An email account that can send over SMTP** (a Gmail account works) | New accounts must confirm their email before they can log in, and password resets are sent by email |

> **Heads-up:** Without working email settings, the API still starts, but **nobody can finish signing up**, because the confirmation email never arrives. Set up email in step 4 before you test signup.

---

## 2. Install the tools

### macOS

1. **Homebrew** (optional, but it makes the rest easier). Open **Terminal** and run the install command from [brew.sh](https://brew.sh).
2. **.NET 9 SDK.** Either download the macOS installer from [dotnet.microsoft.com/download/dotnet/9.0](https://dotnet.microsoft.com/download/dotnet/9.0) (pick **Arm64** for Apple Silicon M1/M2/M3/M4, **x64** for Intel), or run:
   ```bash
   brew install --cask dotnet-sdk
   ```
3. **Docker Desktop** from [docker.com/products/docker-desktop](https://www.docker.com/products/docker-desktop/). Open it once after installing and wait until it says it's running.
   - **Apple Silicon only:** in Docker Desktop go to **Settings → General** and turn on **"Use Rosetta for x86_64/amd64 emulation on Apple Silicon"**. SQL Server needs this.
4. **Git** usually comes with macOS. Check with `git --version`. If it asks to install the Command Line Tools, say yes.

Check that .NET installed correctly:
```bash
dotnet --version   # should print 9.x.x
```

### Windows

1. **.NET 9 SDK.** Download the Windows **x64** installer from [dotnet.microsoft.com/download/dotnet/9.0](https://dotnet.microsoft.com/download/dotnet/9.0), or run this in **PowerShell**:
   ```powershell
   winget install Microsoft.DotNet.SDK.9
   ```
2. **SQL Server.** You have two options:
   - **SQL Server Express LocalDB** (simplest). It's installed with Visual Studio's *"ASP.NET and web development"* workload. To install it on its own, run the [SQL Server Express installer](https://www.microsoft.com/en-us/sql-server/sql-server-downloads), choose **Download Media**, then pick **LocalDB**.
   - **SQL Server Developer or Express** (a full install). Download it from the same page and use the defaults.
3. **Git** from [git-scm.com/download/win](https://git-scm.com/download/win), or `winget install Git.Git`.
4. *(Optional)* **Visual Studio 2022** with the *ASP.NET and web development* workload, if you'd rather run the project from an IDE.

**Close and reopen PowerShell** after installing so the new commands are picked up, then check:
```powershell
dotnet --version   # should print 9.x.x
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

# --- Local server ---
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://localhost:5202

# --- Which websites may call the API (comma-separated, no spaces) ---
CORS_ORIGINS=http://localhost:5173,http://localhost:3000
```

Notes:
- The double underscore (`__`) is intentional. It's how .NET maps `.env` values onto the sections in `appsettings.json`. For example, `JwtSettings__Secret` fills in `JwtSettings:Secret`.
- **Keep the connection string in single quotes**, especially on Windows, so characters like `\` and `;` are read exactly as written.
- `ASPNETCORE_ENVIRONMENT=Development` turns on the Swagger page and detailed error messages.
- `CORS_ORIGINS` must include the address the frontend runs on (`http://localhost:5173` by default). Some branches read this variable and refuse to start without it. Older ones use a built-in list and ignore it, so it's safe to always set it.

---

## 7. Run the API

From the **`GameInventory` folder** (the one containing `.env`):

**macOS / Windows:**
```bash
dotnet restore
dotnet run --no-launch-profile
```

`--no-launch-profile` skips `Properties/launchSettings.json`, so everything comes from your `.env`. Plain `dotnet run` also works, because values in `.env` take priority.

The first start takes a little longer while it creates the database. When you see a line like

```
Now listening on: http://localhost:5202
```

it's ready. Leave this window open. Press **Ctrl + C** to stop the API.

**Check it works:** open [http://localhost:5202/swagger](http://localhost:5202/swagger) in your browser. You should see the list of endpoints (this needs `ASPNETCORE_ENVIRONMENT=Development`).

### Running from Visual Studio (Windows) or Rider/VS Code

Open `GameInventory.sln` and press **Run**. Make sure `.env` is in the `GameInventory` project folder. The included launch profile sets `ASPNETCORE_ENVIRONMENT=Production`, but the value in your `.env` overrides it, so you'll still get Development mode and Swagger.

---

## 8. Connect the frontend

Follow the [frontend README](https://github.com/Tsaleem123/game-inventory-frontend#readme). Its `.env` should contain:

```env
VITE_ENDPOINT=http://localhost:5202/
```

Then sign up in the app, click the link in the confirmation email, and log in.

> Password-reset emails link to `http://localhost:5173/reset-password`. That address is currently hard-coded in `AuthController.cs`, so run the frontend on its default port.

---

## 9. Troubleshooting

| Problem | Fix |
|---|---|
| `DefaultConnection string is missing from configuration` | The API can't find your `.env`. Make sure the file is in `GameInventory/` (not the repo root), is named exactly `.env` (Windows can silently save it as `.env.txt`; turn on *View → File name extensions* in Explorer to check), and that you ran `dotnet run` from inside `GameInventory/`. |
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
| ASP.NET Core 9 | Web API |
| Entity Framework Core 9 + SQL Server | Database access and migrations |
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
    │   ├── GamesController.cs    # the logged-in user's saved games, ratings, status
    │   └── SearchController.cs   # IGDB game search
    ├── DTOs/                     # request bodies
    ├── Migrations/               # EF Core migrations (applied automatically on startup)
    ├── Models/                   # database entities
    ├── Services/                 # email + JWT token services
    ├── AppDbContext.cs
    ├── Program.cs                # startup: config, auth, CORS, Swagger
    ├── appsettings.json          # non-secret defaults (secrets come from .env)
    ├── .env.example              # template for your .env
    └── GameInventory.csproj
```

### Changing the database schema

Migrations are applied automatically when the API starts. To **create** a new one after changing a model, run this from the `GameInventory` folder:

```bash
dotnet tool restore                      # installs the pinned dotnet-ef tool
dotnet ef migrations add YourMigrationName
```

The next `dotnet run` applies it.
