using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NetRts.PlayClient;
using NetRts.PlayClient.Game;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The game server is another origin (http://localhost:5080 by default; its CORS policy is open).
// wwwroot/appsettings.json sets the default; the lobby can override it per browser.
var defaultServer = builder.Configuration["NetRts:Server"] ?? ClientSettings.FallbackServer;
builder.Services.AddSingleton(sp => new ClientSettings(sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(), defaultServer));
builder.Services.AddSingleton<ServerConnection>();

await builder.Build().RunAsync();
