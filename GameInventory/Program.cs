using GameInventory;
using GameInventory.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

try
{
    //Loads Environment variables.
    DotNetEnv.Env.Load();

    // Create the web application builder with default configuration
    var builder = WebApplication.CreateBuilder(args);

    // Enhanced logging for debugging
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();

    // Capture startup errors
    builder.WebHost.CaptureStartupErrors(true);
    builder.WebHost.UseSetting("detailedErrors", "true");

 // Set URLs if environment variable exists
    var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    if (!string.IsNullOrEmpty(urls))
    {
        builder.WebHost.UseUrls(urls);
    }

    // ===== DATABASE CONFIGURATION =====
    // Configure Entity Framework DbContext with SQL Server
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("DefaultConnection string is missing from configuration.");
    }

    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(connectionString));

    // Add in-memory caching for improved performance
    builder.Services.AddMemoryCache();

    // ===== IDENTITY CONFIGURATION =====
    builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Password requirements for user security
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

    // ===== JWT AUTHENTICATION CONFIGURATION =====
    var jwtSection = builder.Configuration.GetSection("JwtSettings");
    var jwtSecret = jwtSection["Secret"];
    var jwtIssuer = jwtSection["Issuer"];
    var jwtAudience = jwtSection["Audience"];

    // Validate JWT configuration
    if (string.IsNullOrEmpty(jwtSecret))
    {
        throw new InvalidOperationException("JWT Secret is missing from configuration.");
    }
    if (string.IsNullOrEmpty(jwtIssuer))
    {
        throw new InvalidOperationException("JWT Issuer is missing from configuration.");
    }
    if (string.IsNullOrEmpty(jwtAudience))
    {
        throw new InvalidOperationException("JWT Audience is missing from configuration.");
    }

    var key = Encoding.UTF8.GetBytes(jwtSecret);

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = true;
        options.SaveToken = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

    // ===== EMAIL AND HTTP CLIENT CONFIGURATION =====
    builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
    builder.Services.AddHttpClient();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("CorsPolicy", policy =>
            policy.WithOrigins(
                "https://gameinventory-app.vercel.app",
                "http://localhost:3000",
                "http://localhost:5173"
            )
            .AllowAnyHeader()
            .AllowAnyMethod());
    });

    // ===== API DOCUMENTATION CONFIGURATION =====
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "GameInventory", Version = "v1" });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter 'Bearer' [space] and then your valid JWT token.\n\nExample: Bearer eyJhbGciOiJIUzI1NiIs..."
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    // ===== DEPENDENCY INJECTION CONFIGURATION =====
    builder.Services.AddScoped<IEmailService, EmailService>();
    builder.Services.AddScoped<ITokenService, TokenService>();

    // ===== APPLICATION PIPELINE CONFIGURATION =====
    var app = builder.Build();

    // Database migration with error handling
    using (var scope = app.Services.CreateScope())
    {
        try
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (dbContext.Database.IsRelational())
            {
                dbContext.Database.Migrate();
            }
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred while migrating the database.");
            throw; // Re-throw to prevent app from starting with DB issues
        }
    }

    // ===== DEVELOPMENT ENVIRONMENT CONFIGURATION =====
    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    else
    {
        // Temporarily use developer exception page in production to see the error
        app.UseDeveloperExceptionPage(); // TEMPORARY - remove after debugging
    }

    // ===== MIDDLEWARE PIPELINE =====
    app.UseHttpsRedirection();
    app.UseCors("CorsPolicy");
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    // ===== APPLICATION STARTUP =====
    app.Run();
}
catch (Exception ex)
{
    // Log startup errors
    Console.WriteLine($"Application failed to start: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");

    // If you have access to logging here, use it
    // logger.LogCritical(ex, "Application failed to start");

    throw; // Re-throw to ensure the application doesn't start in a broken state
}