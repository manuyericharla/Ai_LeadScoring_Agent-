using LeadScoring.Api.Background;
using LeadScoring.Api.Data;
using LeadScoring.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<LeadScoringDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Hiperbrains")
                  ?? throw new InvalidOperationException("Connection string 'Hiperbrains' is missing.")));
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<LeadScoringService>();
builder.Services.AddScoped<LeadImportService>();
var disableEmailSending = builder.Configuration.GetValue<bool>("Email:DisableSending");
if (disableEmailSending)
{
    builder.Services.AddScoped<IEmailService, ConsoleEmailService>();
}
else
{
    builder.Services.AddScoped<IEmailService, SmtpEmailService>();
}
builder.Services.AddHostedService<InactivityWorker>();
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("ui", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LeadScoringDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("ui");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapControllers();

app.Run();
