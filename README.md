# GameInventory — Backend API

A RESTful backend for the GameInventory application, built with **ASP.NET Core (.NET 9)**. It handles user authentication, personal game libraries, and game search via the [IGDB API](https://www.igdb.com/api).

> **Frontend**: The companion frontend application is available at [Tsaleem123/game-inventory-frontend](https://github.com/Tsaleem123/game-inventory-frontend) — a React + TypeScript app built with Vite, Material UI, and TanStack Router. It provides game search, personal library management, and full auth flows that consume this API.

---

## Tech Stack

- **Runtime**: .NET 9 / ASP.NET Core
- **Database**: SQL Server via Entity Framework Core
- **Auth**: ASP.NET Identity + JWT Bearer tokens
- **Email**: MailKit (SMTP)
- **Game Data**: IGDB API (authenticated via Twitch OAuth)
- **Docs**: Swagger / Swashbuckle

---

## Features

- Two-step email-confirmed registration
- JWT-based authentication (login, logout implied via token expiry)
- Forgot/reset password via email
- Personal game library per user (add, remove, update status & rating)
- Game search with pagination and result caching
- Game lookup by IGDB ID

---

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- SQL Server (local or Azure)
- An [IGDB API account](https://api-docs.igdb.com/#getting-started) (Twitch developer app)
- An SMTP email account (e.g. Gmail with an app password)

### 1. Clone and navigate

```bash
git clone <your-repo-url>
cd game-inventory-backend/GameInventory
```

### 2. Configure environment variables

Create a `.env` file in the `GameInventory/` directory (next to `Program.cs`):

```env
IGDB_Client=your_twitch_client_id
IGDB_Secret=your_twitch_client_secret
```

### 3. Configure `appsettings.json`

Fill in the placeholders in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=...;Database=GameInventory;..."
  },
  "JwtSettings": {
    "Secret": "your-256-bit-secret-key",
    "Issuer": "GameInventoryAPI",
    "Audience": "GameInventoryClient",
    "ExpiryMinutes": 60
  },
  "EmailSettings": {
    "SenderName": "Game Inventory App",
    "SenderEmail": "you@example.com",
    "Password": "your-smtp-password",
    "SmtpServer": "smtp.gmail.com",
    "SmtpPort": 587
  }
}
```

> For production, use `appsettings.Production.json` or environment variables instead of committing secrets.

### 4. Apply database migrations

```bash
dotnet ef database update
```

### 5. Run the API

```bash
dotnet run
```

The API will be available at `https://localhost:5001` (or the port shown in your terminal). Swagger UI is available at `/swagger` in development.

---

## API Endpoints

### Auth — `/api/auth`

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/register` | No | Start registration — sends confirmation email |
| GET | `/confirm-email?token=` | No | Complete registration after email click |
| POST | `/login` | No | Returns a JWT token |
| POST | `/forgot-password` | No | Sends a password reset email |
| GET | `/reset-password?token=&email=` | No | Redirects to frontend reset page |
| POST | `/reset-password` | No | Applies the new password |

### Games — `/api/games`

All endpoints require a valid JWT in the `Authorization: Bearer <token>` header.

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/list` | Add a game to your library |
| GET | `/list` | Get all games in your library |
| DELETE | `/list/{gameId}` | Remove a game from your library |
| PUT | `/usergames/{id}/rating` | Update your rating for a game |
| PUT | `/usergames/{id}/status` | Update your status for a game |

**Game statuses** (convention): `Playing`, `Completed`, `Wishlist`, `Dropped`

### Search — `/api/search`

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `?query=zelda&page=1&pageSize=10` | No | Search IGDB for games |
| GET | `/by-id/{id}` | No | Fetch a single game by IGDB ID |

---

## Project Structure

```
GameInventory/
├── Controllers/
│   ├── AuthController.cs       # Registration, login, password reset
│   ├── GamesController.cs      # User game library management
│   └── SearchController.cs     # IGDB game search
├── DTOs/
│   ├── Auth/                   # Request models for auth endpoints
│   └── UserGameRequest.cs      # Add-game request body
├── Migrations/                 # EF Core migration history
├── Models/
│   └── UserGame.cs             # User ↔ Game join entity
├── Services/
│   ├── EmailService.cs         # SMTP email sending via MailKit
│   ├── IEmailService.cs
│   ├── TokenService.cs         # JWT generation
│   └── ITokenService.cs
├── AppDbContext.cs              # EF Core database context
├── ApplicationUser.cs          # Extended Identity user
├── EmailSettings.cs            # Email config POCO
├── Program.cs                  # App entry point and DI setup
└── appsettings.json            # Configuration (no secrets)
```

---

## CORS

The API allows requests from:

- `https://gameinventory-app.vercel.app` (production frontend)
- `http://localhost:3000`
- `http://localhost:5173`

To add more origins, update `Program.cs` in the `CorsPolicy` section.

---

## Deployment

The project includes a Web Deploy publish profile under `Properties/PublishProfiles/`. For production:

1. Set `ASPNETCORE_ENVIRONMENT=Production`
2. Provide secrets via environment variables or Azure Key Vault — do **not** commit secrets to `appsettings.Production.json`
3. The app auto-runs EF migrations on startup

---
