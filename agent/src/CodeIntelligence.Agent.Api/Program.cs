using BuildingBlocks.Web;
using BuildingBlocks.Web.Resilience;
using CodeIntelligence.Agent.Api.Validators;
using CodeIntelligence.Agent.DependencyInjection;
using CodeIntelligence.Agent.RagIntegration;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("AgentService", typeof(ChatRequestValidator).Assembly);

builder.Services.AddCodeIntelligenceAgent(builder.Configuration);

// Same shape every backend service uses to call another (see OrderService's
// Infrastructure/ServiceClients.cs): typed HttpClient + the shared resilience pipeline
// (timeout, retry+jitter, circuit breaker, correlation forwarding, caller-token
// propagation). RAG stays a separately deployed/versioned service — see
// rag/docs/agent-integration.md.
builder.Services.AddHttpClient<IRagServiceClient, RagServiceClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Rag"]!))
    .AddStandardResilience();

// AddServiceDefaults already registered health checks (/health/live, /health/ready);
// nothing else to chain on here since this service owns no database of its own.

var app = builder.Build();

app.UseServiceDefaults();
app.Run();

public partial class Program;
