using Api;
using Api.Services;
using Api.FreeKassa;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("GalaxyClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.All,
    UseCookies = true,
    CookieContainer = new CookieContainer()
});

string[] allowedOrigins =
{
    "https://galabot.netlify.app",
    "https://galasoft.netlify.app",
    "https://galaweb.netlify.app",
    "https://galascript.netlify.app",
};

builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();

// Регистрация официального сервиса FreeKassa
var merchantId = int.Parse(builder.Configuration["FreeKassa:MerchantId"] ?? "0");
var secret1 = builder.Configuration["FreeKassa:SecretWord1"] ?? throw new InvalidOperationException("FreeKassa:SecretWord1 is not configured");
var secret2 = builder.Configuration["FreeKassa:SecretWord2"] ?? throw new InvalidOperationException("FreeKassa:SecretWord2 is not configured");

builder.Services.AddFreeKassa(merchantId, secret1, secret2);

// Регистрация сервиса License
builder.Services.AddScoped<ILicenseService, LicenseService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            //.WithOrigins(allowedOrigins)
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()
/*            .AllowCredentials()*/;
    });
});

var dbUrl = builder.Configuration.GetConnectionString("DefaultConnection")
          ?? Environment.GetEnvironmentVariable("DATABASE_URL");

Console.WriteLine($"DATABASE_URL from environment: {dbUrl}");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(dbUrl));

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])),
            ValidateIssuer = false,
            ValidateAudience = false,
            RoleClaimType = ClaimTypes.Role  // Указываем, какой claim отвечает за роль
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseCors();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

await app.RunAsync();
