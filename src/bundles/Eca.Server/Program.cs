using Elsa.EntityFrameworkCore.Extensions;
using Elsa.EntityFrameworkCore.Modules.Identity;
using Elsa.EntityFrameworkCore.Modules.Management;
using Elsa.EntityFrameworkCore.Modules.Runtime;
using Elsa.Extensions;

var builder = WebApplication.CreateBuilder(args);

var postgresConnectionString = builder.Configuration.GetConnectionString("PostgreSql")!;

builder.Services.AddElsa(elsa =>
{
    // Configure Management layer to use EF Core.
    elsa.UseWorkflowManagement(management => management.UseEntityFrameworkCore(config => config.UsePostgreSql(postgresConnectionString)));

    // Configure Runtime layer to use EF Core.
    elsa.UseWorkflowRuntime(runtime => runtime.UseEntityFrameworkCore(config =>
    {
        config.UsePostgreSql(postgresConnectionString);
    }));
    
    // Default Identity features for authentication/authorization.
    elsa.UseIdentity(identity =>
    {
        identity.TokenOptions = options => options.SigningKey = "sufficiently-large-secret-signing-key"; // This key needs to be at least 256 bits long.
        identity.UseAdminUserProvider();
    });
    
    // Configure ASP.NET authentication/authorization.
    elsa.UseDefaultAuthentication(auth => auth.UseAdminApiKey());
    
    // Expose Elsa API endpoints.
    elsa.UseWorkflowsApi();
    
    // Setup a SignalR hub for real-time updates from the server.
    elsa.UseRealTimeWorkflows();
    
    // Enable C# workflow expressions
    elsa.UseCSharp();
    elsa.UseJavaScript(js =>
    {
        js.AllowClrAccess = true;
    });
    
    // Enable HTTP activities.
    elsa.UseHttp();
    
    // Use timer activities.
    elsa.UseScheduling();
    
    // Register custom activities from the application, if any.
    elsa.AddActivitiesFrom<Program>();
    
    // Register custom workflows from the application, if any.
    elsa.AddWorkflowsFrom<Program>();
    
    elsa.UseWebhooks(webhooks => webhooks.WebhookOptions = options => builder.Configuration.GetSection("Webhooks").Bind(options));

    elsa.UseIdentity(identity =>
    {
        identity.UseEntityFrameworkCore(ef =>
        {
            ef.UsePostgreSql(postgresConnectionString);
            ef.RunMigrations = true;
        });

        var identitySection = builder.Configuration.GetSection("Identity");
        var identityTokenSection = identitySection.GetSection("Tokens");

        identity.TokenOptions = options => identityTokenSection.Bind(options);
        identity.UseConfigurationBasedUserProvider(options => identitySection.Bind(options));
        identity.UseConfigurationBasedApplicationProvider(options => identitySection.Bind(options));
        identity.UseConfigurationBasedRoleProvider(options => identitySection.Bind(options));
    });
});

// Configure CORS to allow designer app hosted on a different origin to invoke the APIs.
builder.Services.AddCors(cors => cors
    .AddDefaultPolicy(policy => policy
        .AllowAnyOrigin() // For demo purposes only. Use a specific origin instead.
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("x-elsa-workflow-instance-id"))); // Required for Elsa Studio in order to support running workflows from the designer. Alternatively, you can use the `*` wildcard to expose all headers.

// Add Health Checks.
builder.Services.AddHealthChecks();

// Build the web application.
var app = builder.Build();

// Configure web application's middleware pipeline.
app.UseCors();
app.UseRouting(); // Required for SignalR.
app.UseAuthentication();
app.UseAuthorization();
app.UseWorkflowsApi(); // Use Elsa API endpoints.
app.UseWorkflows(); // Use Elsa middleware to handle HTTP requests mapped to HTTP Endpoint activities.
app.UseWorkflowsSignalRHubs(); // Optional SignalR integration. Elsa Studio uses SignalR to receive real-time updates from the server. 

app.Run();