using VoiceReset.Health;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapHealth();

app.Run();
