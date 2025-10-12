using Api;
using Api.Middleware;
using Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Добавьте эту строку для регистрации IHttpClientFactory
builder.Services.AddHttpClient("GalaxyClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.All, // Поддержка gzip/deflate/br
    UseCookies = true,
    CookieContainer = new System.Net.CookieContainer()
});

string[] allowedOrigins =
{
    "https://galabot.netlify.app",
    "https://galasoft.netlify.app",
    "https://galaweb.netlify.app",
    "https://galascript.netlify.app",
};

builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();

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
//app.UseStaticFiles();
//app.UseRouting();

//app.UseGalaxyProxy(); // 👈 Наш middleware-прокси

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

await app.RunAsync();
