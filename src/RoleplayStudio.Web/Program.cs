using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using RoleplayStudio.AI.Chat;
using RoleplayStudio.AI.Images;
using RoleplayStudio.AI.Memory;
using RoleplayStudio.AI.Playground;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.AI.Scene;
using RoleplayStudio.AI.Upkeep;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Infrastructure.Storage;
using RoleplayStudio.Web.Components;
using RoleplayStudio.Web.Components.Account;
using RoleplayStudio.Web.Media;
using RoleplayStudio.Web.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    // Every sign-in path (password, passkey, registration, 2FA) keeps the cookie across browser restarts.
    options.Events.OnSigningIn = context =>
    {
        context.Properties.IsPersistent = true;
        return Task.CompletedTask;
    };
});

var connectionString = builder.Configuration.GetConnectionString("roleplaydb") ?? throw new InvalidOperationException("Connection string 'roleplaydb' not found.");
// Not pooled: a pooled context would carry the previous rental's ScopedOwnerId. This also registers the scoped context Identity uses.
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));
builder.EnrichNpgsqlDbContext<ApplicationDbContext>();
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Stores.SchemaVersion = DesignTimeDbContextFactory.IdentitySchemaVersion;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

builder.Services.AddScoped<ICurrentUser, AuthenticationStateCurrentUser>();
builder.Services.AddScoped<ModelProfileService>();
builder.Services.AddSingleton(services => RunwareModelCatalog.Create(services.GetRequiredService<TimeProvider>(), services.GetRequiredService<ILogger<RunwareModelCatalog>>()));
builder.Services.AddSingleton<IChatClientFactory, ChatClientFactory>();
builder.Services.AddScoped<ModelConnectionService>();
builder.Services.AddScoped<OllamaModelCatalog>();
builder.Services.AddScoped<PlaygroundChatService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PersonaService>();
builder.Services.AddScoped<CharacterService>();
builder.Services.AddScoped<ChatbotService>();
builder.Services.AddScoped<ChatSessionService>();
builder.Services.AddScoped<ChatTurnService>();
builder.Services.AddSingleton<MemoryEmbeddings>();
builder.Services.AddScoped<MemoryRecall>();
builder.Services.AddScoped<MemoryService>();
builder.Services.AddSingleton<MemoryNotifier>();
builder.Services.AddSingleton<UpkeepQueue>();
builder.Services.AddSingleton<MemoryUpkeep>();
builder.Services.AddSingleton<SceneNotifier>();
builder.Services.AddSingleton<SceneUpkeep>();
builder.Services.AddHostedService<UpkeepWorker>();
builder.Services.AddSingleton<IImageStore>(new FileSystemImageStore(builder.Configuration["Images:Path"] is { Length: > 0 } imagePath
    ? imagePath
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoleplayStudio", "images")));
builder.Services.AddScoped<ImageService>();
builder.Services.AddSingleton<PictureQueue>();
builder.Services.AddSingleton<PictureNotifier>();
builder.Services.AddSingleton<PictureDrawing>();
builder.Services.AddScoped<PictureService>();
builder.Services.AddHostedService<PictureWorker>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();

    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.SeedDevelopmentUserAsync();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();
app.MapImageEndpoints();
app.MapDefaultEndpoints();

app.Run();
