using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProtoBuf.Grpc.Server;
using System.Reflection;
using x402;
using xmas402.Database;
using xmas402.Server.Services;
using xmas402.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Add services to the container.

builder.Services.AddScoped<GiftService>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var corsPolicyName = "AllowCredentialsPolicy";
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicyName, policy =>
    {
        policy.SetIsOriginAllowed(origin =>
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
            var host = uri.Host;
            return host == "xmas402.pages.dev"
            || host.EndsWith("xmas402.pages.dev")
            || host.EndsWith("xmas402.com")
            || host == "xmas402.com"
            || host.Contains("localhost"); // dev only
        })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.AddGrpc();
builder.Services.AddResponseCompression(opts =>
{
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/octet-stream" });
});
builder.Services.AddCodeFirstGrpc();

builder.Services.AddOpenApi();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "xmas402 API", Version = "v1" });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);
});

builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();


var facilitatorUrl = builder.Configuration["FacilitatorUrl"];
if (!string.IsNullOrEmpty(facilitatorUrl))
{
    builder.Services.AddX402().WithHttpFacilitator(facilitatorUrl);

}
else
{
    builder.Services.AddX402();
}


builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

var sqlLiteBuilder = new SqliteConnectionStringBuilder(connectionString);
var dbPath = Path.GetDirectoryName(sqlLiteBuilder.DataSource);
if (dbPath != null && !Directory.Exists(dbPath))
{
    Directory.CreateDirectory(dbPath);
}

// Ensure database is created and apply migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    //db.Database.EnsureCreated();
    db.Database.Migrate(); // Creates the database if it does not exist and applies any pending migrations
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
}

app.UseCors(corsPolicyName);

app.UseHttpsRedirection();

// Add middleware to redirect to www domain, except on localhost
app.Use(async (context, next) =>
{
    if (context.Request.Host.Host != "localhost"
    && !context.Request.Host.Host.StartsWith("www.")
    && !context.Request.Host.Host.StartsWith("api."))
    {
        var newUrl = $"{context.Request.Scheme}://www.{context.Request.Host.Host}{context.Request.Path}{context.Request.QueryString}";
        context.Response.Redirect(newUrl, permanent: true);
        return;
    }
    await next();
});

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseGrpcWeb();
app.MapGrpcService<GiftInfoGrpcService>().EnableGrpcWeb();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "xmas402 API");
});



app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();



