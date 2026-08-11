// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Presentation.Dashboard.Charts;
using OutOfTheBox.Presentation.Dashboard.CodePreview;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace OutOfTheBox.Host.ServiceRegistration;

/// <summary>
/// Registers the Blazor Server dashboard itself - interactive rendering, cookie authentication, and
/// the circuit-scoped JS-interop wrappers its pages depend on.
/// </summary>
public static class DashboardServiceCollectionExtensions
{
    /// <summary>Adds Razor Components, cookie auth, and the dashboard's own JS-interop services.</summary>
    public static IServiceCollection AddDashboardServices(this IServiceCollection services)
    {
        // Blazor Server dashboard: interactive server-side rendering over a SignalR circuit, gated by a
        // cookie issued from the login page (see LoginEndpoints) - a separate credential exchange from
        // the API's per-request bearer header, since a long-lived circuit can't carry an Authorization
        // header per interaction, but checked against the exact same ServiceOptions.BearerToken (per
        // design.md's "Dashboard auth" decision - one shared credential, not two).
        //
        // DisconnectedCircuitRetentionPeriod extended well past the framework default (3 minutes) - an
        // operator plausibly leaves this dashboard open in a background tab for longer than that
        // between glances (laptop sleep/lock, a network blip), and the default window is too short to
        // survive that: once it elapses, the server has already disposed the circuit, so the client's
        // automatic reconnect attempt can never succeed no matter how quickly the browser reconnects,
        // leaving the page looking normal but permanently unresponsive to further interaction until a
        // manual reload. 30 minutes trades a small amount of server memory (a handful of retained
        // circuits at most, for this low-traffic single/few-operator tool) for tolerating a real gap.
        services.AddRazorComponents().AddInteractiveServerComponents(options =>
            options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(30));
        services.AddCascadingAuthenticationState();
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => options.LoginPath = "/login");
        services.AddAuthorization();

        // Performance graphs (Section 15). Scoped, not singleton, since it wraps IJSRuntime - a Blazor
        // Server circuit-scoped service itself, so a chart-interop instance must live no longer than
        // the circuit that created it.
        services.AddScoped<IChartInterop, ChartInterop>();

        // File preview code highlighting - same scoped-IJSRuntime-wrapper reasoning as IChartInterop above.
        services.AddScoped<ICodePreviewInterop, CodePreviewInterop>();

        return services;
    }
}
