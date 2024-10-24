using System.Text.Encodings.Web;
using Elsa.Alterations.Extensions;
using Elsa.Alterations.MassTransit.Extensions;
using Elsa.Caching.Options;
using Elsa.Common.DistributedLocks.Noop;
using Elsa.Dapper.Extensions;
using Elsa.Dapper.Services;
using Elsa.DropIns.Extensions;
using Elsa.EntityFrameworkCore.Extensions;
using Elsa.EntityFrameworkCore.Modules.Alterations;
using Elsa.EntityFrameworkCore.Modules.Identity;
using Elsa.EntityFrameworkCore.Modules.Management;
using Elsa.EntityFrameworkCore.Modules.Runtime;
using Elsa.Extensions;
using Elsa.Features.Services;
using Elsa.Http.Options;
using Elsa.MassTransit.Extensions;
using Elsa.MongoDb.Extensions;
using Elsa.MongoDb.Modules.Alterations;
using Elsa.MongoDb.Modules.Identity;
using Elsa.MongoDb.Modules.Management;
using Elsa.MongoDb.Modules.Runtime;
using Elsa.Server.Web;
using Elsa.Workflows;
using Elsa.Workflows.Management.Compression;
using Elsa.Workflows.Management.Stores;
using Elsa.Workflows.Runtime.Stores;
using JetBrains.Annotations;
using Medallion.Threading.FileSystem;
using Medallion.Threading.Postgres;
using Medallion.Threading.Redis;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Proto.Persistence.Sqlite;
using Proto.Persistence.SqlServer;

const bool useMongoDb = false;
const bool useSqlServer = false;
const bool usePostgres = true;
const bool useCockroachDb = false;
const bool useDapper = false;
const bool useProtoActor = false;
const bool useHangfire = false;
const bool useQuartz = false;
const bool useMassTransit = false;
const bool useZipCompression = false;
const bool runEFCoreMigrations = true;
const bool useMemoryStores = false;
const bool useCaching = false;
const bool useReadOnlyMode = false;
const bool useSignalR = true;
const bool useAzureServiceBus = false;
const DistributedCachingTransport distributedCachingTransport = DistributedCachingTransport.MassTransit;
const MassTransitBroker useMassTransitBroker = MassTransitBroker.Memory;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var configuration = builder.Configuration;
var identitySection = configuration.GetSection("Identity");
var identityTokenSection = identitySection.GetSection("Tokens");
var sqliteConnectionString = configuration.GetConnectionString("Sqlite")!;
var sqlServerConnectionString = configuration.GetConnectionString("SqlServer")!;
var postgresConnectionString = configuration.GetConnectionString("PostgreSql")!;
var cockroachDbConnectionString = configuration.GetConnectionString("CockroachDb")!;
var mongoDbConnectionString = configuration.GetConnectionString("MongoDb")!;
var azureServiceBusConnectionString = configuration.GetConnectionString("AzureServiceBus")!;
var rabbitMqConnectionString = configuration.GetConnectionString("RabbitMq")!;
var redisConnectionString = configuration.GetConnectionString("Redis")!;
var distributedLockProviderName = configuration.GetSection("Runtime")["DistributedLockProvider"];
var appRole = Enum.Parse<ApplicationRole>(configuration["AppRole"]);

// Add Elsa services.
services
    .AddElsa(elsa =>
    {
        elsa
            .AddActivitiesFrom<Program>()
            .AddWorkflowsFrom<Program>()
            .UseFluentStorageProvider()
            .UseFileStorage()
            .UseIdentity(identity =>
            {
                    identity.UseEntityFrameworkCore(ef =>
                    {
                        ef.UsePostgreSql(postgresConnectionString);
                        ef.RunMigrations = runEFCoreMigrations;
                    });

                identity.IdentityOptions = options => identitySection.Bind(options);
                identity.TokenOptions = options => identityTokenSection.Bind(options);
                identity.UseConfigurationBasedUserProvider(options => identitySection.Bind(options));
                identity.UseConfigurationBasedApplicationProvider(options => identitySection.Bind(options));
                identity.UseConfigurationBasedRoleProvider(options => identitySection.Bind(options));
            })
            .UseDefaultAuthentication()
            .UseWorkflowManagement(management =>
            {
                    management.UseEntityFrameworkCore(ef =>
                    {
                        ef.UsePostgreSql(postgresConnectionString);
                        ef.RunMigrations = runEFCoreMigrations;
                    });

                management.SetDefaultLogPersistenceMode(LogPersistenceMode.Inherit);
                management.UseReadOnlyMode(useReadOnlyMode);
            })
            .UseWorkflowRuntime(runtime =>
            {
                    runtime.UseEntityFrameworkCore(ef =>
                    { 
                        ef.UsePostgreSql(postgresConnectionString);
                        ef.RunMigrations = runEFCoreMigrations;
                    });
                

                runtime.WorkflowInboxCleanupOptions = options => configuration.GetSection("Runtime:WorkflowInboxCleanup").Bind(options);
                runtime.WorkflowDispatcherOptions = options => configuration.GetSection("Runtime:WorkflowDispatcher").Bind(options);

                runtime.DistributedLockProvider = _ =>
                {
                    switch (distributedLockProviderName)
                    {
                        case "Postgres":
                            return new PostgresDistributedSynchronizationProvider(postgresConnectionString, options =>
                            {
                                options.KeepaliveCadence(TimeSpan.FromMinutes(5));
                                options.UseMultiplexing();
                            });
                        case "Redis":
                            {
                                var connectionMultiplexer = StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnectionString);
                                var database = connectionMultiplexer.GetDatabase();
                                return new RedisDistributedSynchronizationProvider(database);
                            }
                        case "File":
                            return new FileDistributedSynchronizationProvider(new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "locks")));
                        case "Noop":
                        default:
                            return new NoopDistributedSynchronizationProvider();
                    }
                };
            })
            .UseEnvironments(environments => environments.EnvironmentsOptions = options => configuration.GetSection("Environments").Bind(options))
            .UseScheduling()
            .UseWorkflowsApi(api =>
            {
                api.AddFastEndpointsAssembly<Program>();
            })
            .UseCSharp(options =>
            {
                options.AppendScript("string Greet(string name) => $\"Hello {name}!\";");
                options.AppendScript("string SayHelloWorld() => Greet(\"World\");");
            })
            .UseJavaScript(options =>
            {
                options.AllowClrAccess = true;
                options.ConfigureEngine(engine =>
                {
                    engine.Execute("function greet(name) { return `Hello ${name}!`; }");
                    engine.Execute("function sayHelloWorld() { return greet('World'); }");
                });
            })
            .UsePython(python =>
            {
                python.PythonOptions += options =>
                {
                    // Make sure to configure the path to the python DLL. E.g. /opt/homebrew/Cellar/python@3.11/3.11.6_1/Frameworks/Python.framework/Versions/3.11/bin/python3.11
                    // alternatively, you can set the PYTHONNET_PYDLL environment variable.
                    configuration.GetSection("Scripting:Python").Bind(options);
                };
            })
            .UseLiquid(liquid => liquid.FluidOptions = options => options.Encoder = HtmlEncoder.Default)
            .UseHttp(http =>
            {
                http.ConfigureHttpOptions = options => configuration.GetSection("Http").Bind(options);

                if (useCaching)
                    http.UseCache();
            })
            .UseEmail(email => email.ConfigureOptions = options => configuration.GetSection("Smtp").Bind(options))
            .UseAlterations(alterations =>
            {
                alterations.UseEntityFrameworkCore(ef =>
                { 
                    ef.UsePostgreSql(postgresConnectionString);
                    ef.RunMigrations = runEFCoreMigrations;
                });
            })
            .UseWorkflowContexts();

        if (useSignalR)
        {
            elsa.UseRealTimeWorkflows();
        }

        if (distributedCachingTransport != DistributedCachingTransport.None)
        {
            elsa.UseDistributedCache(distributedCaching =>
            {
                if (distributedCachingTransport == DistributedCachingTransport.MassTransit) distributedCaching.UseMassTransit();
            });
        }

        elsa.InstallDropIns(options => options.DropInRootDirectory = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "DropIns"));
        elsa.AddSwagger();
        elsa.AddFastEndpointsAssembly<Program>();
        elsa.UseWebhooks(webhooks => webhooks.WebhookOptions = options => builder.Configuration.GetSection("Webhooks").Bind(options));
        ConfigureForTest?.Invoke(elsa);
    });

services.Configure<CachingOptions>(options => options.CacheDuration = TimeSpan.FromDays(1));

services.AddHealthChecks();
services.AddControllers();
services.AddCors(cors => cors.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin().WithExposedHeaders("*")));

// Build the web application.
var app = builder.Build();

// app.UseSimulatedLatency(
//     TimeSpan.FromMilliseconds(1000),
//     TimeSpan.FromMilliseconds(3000)
// );

// Configure the pipeline.
if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();

// CORS.
app.UseCors();

// Health checks.
app.MapHealthChecks("/");

// Routing used for SignalR.
app.UseRouting();

// Security.
app.UseAuthentication();
app.UseAuthorization();

// Elsa API endpoints for designer.
var routePrefix = app.Services.GetRequiredService<IOptions<HttpActivityOptions>>().Value.ApiRoutePrefix;
app.UseWorkflowsApi(routePrefix);

// Captures unhandled exceptions and returns a JSON response.
app.UseJsonSerializationErrorHandler();

// Elsa HTTP Endpoint activities.
app.UseWorkflows();

app.MapControllers();

// Swagger API documentation.
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI();
}

// SignalR.
if (useSignalR)
{
    app.UseWorkflowsSignalRHubs();
}

// Run.
app.Run();

/// The main entry point for the application made public for end to end testing.
[UsedImplicitly]
public partial class Program
{
    /// Set by the test runner to configure the module for testing.
    public static Action<IModule>? ConfigureForTest { get; set; }
}