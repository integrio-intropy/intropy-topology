// A deliberately minimal Intropy component worker. It reads the identity and runtime config the
// Aspire run-backend injects, logs them, and exposes an endpoint so its Dapr sidecar has an app to
// talk to. A real worker would use the (future) Intropy Hosting framework to bind subscriptions,
// publishes, and connectors from the same INTROPY__CONFIG contract.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var component = Environment.GetEnvironmentVariable("INTROPY__COMPONENT") ?? "unknown";
var config = Environment.GetEnvironmentVariable("INTROPY__CONFIG");
app.Logger.LogInformation("Intropy component {Component} started. Runtime config: {Config}", component, config);

app.MapGet("/", () => component);
app.Run();
