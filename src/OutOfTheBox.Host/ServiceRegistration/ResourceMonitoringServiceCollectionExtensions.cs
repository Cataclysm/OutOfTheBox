// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Application.Monitoring;
using OutOfTheBox.Infrastructure.Monitoring;

namespace OutOfTheBox.Host.ServiceRegistration;

/// <summary>Registers host/process resource monitoring (Section 14) - CPU/RAM sampling and its live event feed.</summary>
public static class ResourceMonitoringServiceCollectionExtensions
{
    /// <summary>Adds the resource sampler, process monitor, and their background sampling service.</summary>
    public static IServiceCollection AddResourceMonitoringServices(this IServiceCollection services)
    {
        // IClock/ResourceHistoryBuffer/IResourceEventBus are process-wide singletons for the same
        // reasons as RunRegistry in CommandExecutionServiceCollectionExtensions; IResourceSampler and
        // IProcessMonitor are singletons too - both are stateless/cheap-per-call aside from the
        // sampler's own internal delta-tracking state, which must persist across ticks.
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ResourceHistoryBuffer>();
        services.AddSingleton<IResourceEventBus, InMemoryResourceEventBus>();
        services.AddSingleton<IResourceSampler, HostResourceSampler>();
        services.AddSingleton<IProcessMonitor, ProcessMonitor>();
        services.AddHostedService<HostResourceSamplerService>();
        services.AddHostedService<MemoryDiagnosticsSamplerService>();

        return services;
    }
}
