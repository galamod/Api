using Api;
using Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

string[] allowedOrigins =
{
    "https://galabot.netlify.app",
    "https://galasoft.netlify.app",
    "https://galaweb.netlify.app",
    "https://galascript.netlify.app",
};

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

builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();

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
