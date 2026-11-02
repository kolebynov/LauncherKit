using MudBlazor.Services;
using LauncherKit.FeedEditor.Components;
using LauncherKit.FeedEditor.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddFeedEditor(builder.Configuration);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
// Uploaded files go from the browser straight to R2 (S3PathField), so the circuit carries only the editor's own
// traffic and keeps the default WebSocket compression; with it Blazor also sends `frame-ancestors 'self'`.
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();
