using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SushiBE.Data;
using SushiBE.Models;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.FileProviders;
using System.IO;
using SushiBE.Services;
using QuestPDF; // required for license
using QuestPDF.Infrastructure; // required for LicenseType
using Npgsql.EntityFrameworkCore.PostgreSQL; // Add this using directive at the top of the file

var builder = WebApplication.CreateBuilder(args);

// Configure QuestPDF license for development / community usage
// If you need a commercial license, set accordingly per QuestPDF documentation.
QuestPDF.Settings.License = LicenseType.Community;

// --- CORS: allow your GitHub Pages site (adjust or add more origins as needed) ---
var allowedOrigin = "https://taha3301.github.io";
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowGithubPages", policy =>
    {
        policy.WithOrigins(allowedOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod();
        // If you need to send credentials (cookies) or Authorization with credentials:
        // policy.AllowCredentials();
    });
});
// ---------------------------------------------------------------------------

// Add services to the container.
builder.Services.AddControllers();

// Add JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]))
    };
});

builder.Services.AddDbContext<SushiDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<SushiBE.Services.IInvoicePdfService, SushiBE.Services.InvoicePdfService>();


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SushiBE API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
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
            new string[] {}
        }
    });
});

var app = builder.Build();

// Determine images folder (configurable). Priority:
// 1) configuration "ImagesPath" (absolute or relative to content root)
// 2) prefer webroot/Images (capital I) if exists
// 3) webroot/images (lowercase)
// 4) contentRoot/images
// 5) default to webroot/Images (create if missing)
string imagesPath = builder.Configuration["ImagesPath"];
if (!string.IsNullOrWhiteSpace(imagesPath))
{
    if (!Path.IsPathRooted(imagesPath))
        imagesPath = Path.Combine(builder.Environment.ContentRootPath, imagesPath.TrimStart('~', '/', '\\'));
}
else
{
    var webrootImagesUpper = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "Images");
    var webrootImagesLower = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "images");
    var contentRootImages = Path.Combine(builder.Environment.ContentRootPath, "images");

    if (Directory.Exists(webrootImagesUpper))
        imagesPath = webrootImagesUpper;
    else if (Directory.Exists(webrootImagesLower))
        imagesPath = webrootImagesLower;
    else if (Directory.Exists(contentRootImages))
        imagesPath = contentRootImages;
    else
        imagesPath = webrootImagesUpper; // default to wwwroot/Images
}

// Ensure the images folder exists
if (!Directory.Exists(imagesPath))
    Directory.CreateDirectory(imagesPath);

// Ensure the invoices folder exists
var invoicesPath = Path.Combine(builder.Environment.ContentRootPath, "invoices");
if (!Directory.Exists(invoicesPath))
    Directory.CreateDirectory(invoicesPath);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Add UseCors before authentication/authorization and controller routing
app.UseCors("AllowGithubPages");

app.UseAuthentication();
app.UseAuthorization();

// Serve default static files from wwwroot (if present)
app.UseStaticFiles();

// Serve images folder at /images (maps to whatever imagesPath was determined to)
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(imagesPath),
    RequestPath = "/images"
});

// Also map the capitalized path so legacy frontend requests to /Images work
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(imagesPath),
    RequestPath = "/Images"
});

// Serve invoices folder at /invoices
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(invoicesPath),
    RequestPath = "/invoices"
});

app.MapControllers();
app.Run();