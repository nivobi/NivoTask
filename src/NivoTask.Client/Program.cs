using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using NivoTask.Client;
using NivoTask.Client.Identity;
using NivoTask.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Cookie handler for auth
builder.Services.AddScoped<CookieHandler>();

// Authorization
builder.Services.AddAuthorizationCore();

// Custom auth state provider
builder.Services.AddScoped<CookieAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    sp => sp.GetRequiredService<CookieAuthenticationStateProvider>());
builder.Services.AddScoped<IAccountManagement>(
    sp => sp.GetRequiredService<CookieAuthenticationStateProvider>());

// Named HttpClient with cookie handler -- same origin (D-03)
builder.Services.AddHttpClient("Auth", client =>
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<CookieHandler>();

// Default HttpClient for non-auth calls
builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// MudBlazor
builder.Services.AddMudServices();

// API services
builder.Services.AddScoped<BoardService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<ColumnService>();
builder.Services.AddScoped<LocalStorageService>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddSingleton<TimerStateService>();
builder.Services.AddScoped<TimeEntryService>();
builder.Services.AddScoped<LabelService>();
builder.Services.AddScoped<SetupService>();
builder.Services.AddScoped<SystemService>();

await builder.Build().RunAsync();
