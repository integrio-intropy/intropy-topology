// This minimal worker logs the identity and runtime config injected by the Aspire backend.
// The endpoint keeps an application channel available for its Dapr sidecar.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var component = Environment.GetEnvironmentVariable("INTROPY__COMPONENT") ?? "unknown";
var config = Environment.GetEnvironmentVariable("INTROPY__CONFIG");
app.Logger.LogInformation("Intropy component {Component} started. Runtime config: {Config}", component, config);

app.MapGet("/", () => component);
app.Run();
