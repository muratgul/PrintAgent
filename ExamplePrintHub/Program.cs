using ExamplePrintHub.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 500L * 1024 * 1024; // 500 MB
});

// Add services to the container.
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 500L * 1024 * 1024; // 500 MB
    options.EnableDetailedErrors = true;
});
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:8080") // Vue dev server ports
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseCors();

app.MapGet("/", () => "PrintAgent Hub is running!");
app.MapHub<PrintHub>("/printhub"); // This matches the Agent's expected path

app.Run();
