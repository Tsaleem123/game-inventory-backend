using GameInventory;
using GameInventory.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
//Loads Enviroment variables.
DotNetEnv.Env.Load();
// Create the web application builder with default configuration
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"));
// ===== DATABASE CONFIGURATION =====
// Configure Entity Framework DbContext with SQL Server
// Retrieves connection string from appsettings.json or environment variables
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add in-memory caching for improved performance (used by SearchController for API response caching)
builder.Services.AddMemoryCache();

// ===== IDENTITY CONFIGURATION =====
// Configure ASP.NET Core Identity for user management and authentication
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Password requirements for user security
    options.Password.RequireDigit = true;           // Must contain at least one digit
    options.Password.RequiredLength = 6;            // Minimum password length

    // User account requirements
    options.User.RequireUniqueEmail = true;         // Prevent duplicate email addresses

    // Sign-in requirements
    options.SignIn.RequireConfirmedEmail = true;    // Users must verify email before signing in
})
.AddEntityFrameworkStores<AppDbContext>()           // Use Entity Framework for Identity data storage
.AddDefaultTokenProviders();                        // Enable default token providers for email confirmation, password reset, etc.

// ===== JWT AUTHENTICATION CONFIGURATION =====
// Configure JWT (JSON Web Token) authentication for API security
var jwt = builder.Configuration.GetSection("JwtSettings");
var key = Encoding.UTF8.GetBytes(jwt["Secret"]!);   // Convert JWT secret to byte array for signing

builder.Services.AddAuthentication(options =>
{
    // Set JWT Bearer as the default authentication scheme
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // SECURITY NOTE: Set to true in production for HTTPS requirement
    options.RequireHttpsMetadata = false;          // Allow HTTP in development
    options.SaveToken = true;                      // Save JWT token in AuthenticationProperties

    // Configure token validation parameters for security
    options.TokenValidationParameters = new TokenValidationParameters
    {
        // Validate the signing key to ensure token integrity
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),

        // Validate issuer (who created the token)
        ValidateIssuer = true,
        ValidIssuer = jwt["Issuer"],

        // Validate audience (who the token is intended for)
        ValidateAudience = true,
        ValidAudience = jwt["Audience"],

        // Validate token expiration time
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero                  // No tolerance for clock differences
    };
});

// ===== EMAIL AND HTTP CLIENT CONFIGURATION =====
// Configure email settings from appsettings.json for user notifications
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

// Add HTTP client factory for making external API calls (used by SearchController for Giant Bomb API)
builder.Services.AddHttpClient();

// ===== CORS CONFIGURATION =====
// Configure Cross-Origin Resource Sharing to allow frontend access
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
        policy.WithOrigins("http://localhost:5173")     // Allow Front End
              .AllowAnyHeader()                         // Allow all headers
              .AllowAnyMethod());                       // Allow all HTTP methods
});

// ===== API DOCUMENTATION CONFIGURATION =====
// Add controllers for API endpoints
builder.Services.AddControllers();

// Add API exploration services for Swagger documentation
builder.Services.AddEndpointsApiExplorer();

// Configure Swagger/OpenAPI with JWT authentication support
builder.Services.AddSwaggerGen(options =>
{
    // Basic API information
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "GameInventory", Version = "v1" });

    // Configure JWT Bearer authentication in Swagger UI
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",                         // Header name
        Type = SecuritySchemeType.ApiKey,              // Security scheme type
        Scheme = "Bearer",                             // Authentication scheme
        BearerFormat = "JWT",                          // Token format
        In = ParameterLocation.Header,                 // Where to send the token
        Description = "Enter 'Bearer' [space] and then your valid JWT token.\n\nExample: Bearer eyJhbGciOiJIUzI1NiIs..."
    });

    // Apply JWT authentication requirement to all endpoints that need it
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"                       // Reference to the security definition above
                }
            },
            Array.Empty<string>()                       // No specific scopes required
        }
    });
});

// ===== DEPENDENCY INJECTION CONFIGURATION =====
// Register application services for dependency injection
builder.Services.AddScoped<IEmailService, EmailService>();     // Email service for user notifications
builder.Services.AddScoped<ITokenService, TokenService>();     // JWT token generation and validation service

// ===== APPLICATION PIPELINE CONFIGURATION =====
// Build the application with all configured services
var app = builder.Build();

// ===== DEVELOPMENT ENVIRONMENT CONFIGURATION =====
// Configure middleware pipeline for development environment
if (app.Environment.IsDevelopment())
{
    // Show detailed error pages during development
    app.UseDeveloperExceptionPage();

    // Enable Swagger API documentation
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ===== MIDDLEWARE PIPELINE =====
// Configure the HTTP request pipeline (order matters!)

// 1. Redirect HTTP requests to HTTPS for security
app.UseHttpsRedirection();

// 2. Enable CORS to allow frontend access
app.UseCors("CorsPolicy");

// 3. Enable authentication (must come before authorization)
app.UseAuthentication();

// 4. Enable authorization (protects endpoints marked with [Authorize])
app.UseAuthorization();

// 5. Map controller endpoints to handle API requests
app.MapControllers();

// ===== APPLICATION STARTUP =====
// Start the web application and begin listening for requests
app.Run();