using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Wslcc.Abstractions;
using Wslcc.Core;
using Wslcc.Grpc.Server;
using Wslcc.Providers.DockerCompose;
using Wslcc.Providers.Wslc;
using Wslccd;

// Pin the content root to the executable's folder so appsettings.json (the 'Wslcc' section) loads no
// matter the launcher's working directory: 'wslcc daemon start', the 'daemon install' logon task, and a
// manual service all start wslccd with different current directories.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// wslccd normally runs as a per-user process (that's how 'daemon start' and the 'daemon install' logon
// task launch it). UseWindowsService is a no-op then, but keeps the door open for an advanced user who
// registers wslccd as a Windows Service manually; the name matches WslccdConstants for a friendly
// Service Control Manager display name.
builder.Host.UseWindowsService(options => options.ServiceName = WslccdConstants.ServiceName);

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

    return typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1";
}
