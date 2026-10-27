using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using YHAB.Client.Services;
using YHAB.SharedKernel.Budgeting;
using YHAB.UI.Features.Budgeting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();
builder.Services.AddFluentUIComponents();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<IBudgetClient, HttpBudgetClient>();
builder.Services.AddScoped<WorkspaceCache>();

await builder.Build().RunAsync();
