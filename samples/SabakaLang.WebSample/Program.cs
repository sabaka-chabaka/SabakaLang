using SabakaLang.AspNet;
using SabakaLang.WebSample;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSabaka(o =>
{
    o.AddModule(typeof(AppModule));                  // your own extension (C#)
    // o.AddExtensionDirectory("plugins");       // or load extension .dll files from a folder
    // o.AllowHttp = true;                       // stdlib io/http are off by default for web scripts
});

var app = builder.Build();

app.MapGet("/ping", () => "pong");               // normal ASP.NET endpoints keep working
app.MapSabaka("/");                              // everything else → site/*.sabaka

app.Run();
