using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nskg.Data;
using Nskg.Repositories;
using Nskg.Repositories.Interfaces;
using Nskg.Service;
using Nskg.Service.Interfaces;
using Nskg.Services;
using Nskg.Services.Interfaces;
using System.Text; // 👈 ADD THIS


var builder = WebApplication.CreateBuilder(args);
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// DB Connection
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

// Identity Config
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddRoles<IdentityRole>() // IMPORTANT for roles
.AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContextService, UserContextService>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<AccountingService, AccountingService>();
builder.Services.AddScoped<FinancialYearClosingService, FinancialYearClosingService>();
builder.Services.AddScoped<IRoleFormPermissionService, RoleFormPermissionService>();
// Configure FormOptions to allow large forms (e.g. vouchers, challans with 100+ rows)
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.ValueCountLimit = 10000;
    options.KeyLengthLimit = 1024 * 10;
    options.ValueLengthLimit = 1024 * 1024 * 50;
    options.MultipartBodyLengthLimit = 1024 * 1024 * 100;
});

// Persist DataProtection keys to disk so Antiforgery tokens survive IIS AppPool recycles
var keysFolder = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
Directory.CreateDirectory(keysFolder);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("NskgApp");

// Configure Antiforgery for both HTTP (IIS local network) and HTTPS (Production)
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Add services to the container.
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}


using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await DbInitializer.SeedRoles(services);
}



app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<Nskg.Middleware.CompanyContextMiddleware>();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages();


app.Run();
