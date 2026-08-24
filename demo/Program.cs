using Blazor.IndexedDB.Demo;
using Kebechet.Blazor.IndexedDB;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddIndexedDbStorage(options => options.DatabaseName = "blazor-indexeddb-demo");

await builder.Build().RunAsync();
