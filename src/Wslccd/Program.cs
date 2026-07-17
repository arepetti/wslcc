using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Wslcc.Abstractions;
using Wslcc.Core;
using Wslcc.Grpc.Server;
using Wslcc.Providers.DockerCompose;
using Wslcc.Providers.Wslc;
using Wslccd;

var builder = WebApplication.CreateBuilder(args);

// Allow running either as a Windows Service or as a normal per-user process.
builder.Host.UseWindowsService(options => options.ServiceName = "WSLCC Daemon");

var options = new DaemonOptions();
builder.Configuration.GetSection(DaemonOptions.SectionName).Bind(options);

// Configure transports explicitly; do not bind the default localhost:5000 endpoint.
builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenNamedPipe(options.PipeName, listen => listen.Protocols = HttpProtocols.Http2);

    if (options.Http.Enabled && Uri.TryCreate(options.Http.Url, UriKind.Absolute, out var httpUri))
    {
        kestrel.ListenAnyIP(httpUri.Port, listen => listen.Protocols = HttpProtocols.Http2);
    }
});

builder.Services.AddGrpc();

RegisterProviders(builder.Services, options);
builder.Services.AddSingleton<IComposeEngine>(sp =>
    new ComposeEngine(sp.GetServices<IContainerProvider>(), options.DefaultProvider));
builder.Services.AddSingleton<IDaemonLifetime, DaemonLifetime>();
builder.Services.AddSingleton(new WslccServerOptions
{
    DaemonVersion = ResolveDaemonVersion(),
    DefaultProvider = options.DefaultProvider,
});

var app = builder.Build();

app.MapGrpcService<WslccGrpcService>();
app.MapGet("/", () => "WSLCC daemon (wslccd). This endpoint speaks gRPC over HTTP/2.");

app.Logger.LogInformation(
    "wslccd {Version} listening on npipe://{Pipe}{Http}",
    ResolveDaemonVersion(),
    options.PipeName,
    options.Http.Enabled ? $" and {options.Http.Url}" : string.Empty);

app.Run();

static void RegisterProviders(IServiceCollection services, DaemonOptions options)
{
    if (options.Providers.Wslc)
    {
        services.AddSingleton<IContainerProvider, WslcProvider>();
    }

    if (options.Providers.Docker)
    {
        services.AddSingleton<IContainerProvider, DockerComposeProvider>();
    }
}

static string ResolveDaemonVersion()
{
    var informational = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    if (!string.IsNullOrEmpty(informational))
    {
        var plus = informational!.IndexOf('+');
        return plus >= 0 ? informational.Substring(0, plus) : informational;
    }

    return typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1.0";
}
