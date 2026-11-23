using SabakaLang.AspNet;
using SabakaLang.WebSample;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSabaka(o =>
{
    o.AddModule(typeof(AppModule));      // your own C# extension (static class -> typeof)
    // o.AddExtensionDirectory("plugins"); // or load extension .dll files from a folder
    // o.AllowHttp = true;                 // stdlib io/http are off for web scripts by default
});

var app = builder.Build();

app.MapGet("/ping", () => "pong");       // plain Minimal API endpoints work side by side

app.MapSabaka();                         // controllers/*.sabaka -> endpoints
// app.MapSabaka("/api").RequireAuthorization();   // groups take the usual conventions

app.Run();
