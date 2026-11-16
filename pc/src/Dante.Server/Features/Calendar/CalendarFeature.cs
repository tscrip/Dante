using Dante.Core.Engine;
using Dante.Core.Plugins;
using Dante.Core.Widgets;

namespace Dante.Server.Features.Calendar;

/// <summary>
/// Meeting reminders on DANTE from your calendar. Providers implement <see cref="ICalendarProvider"/>;
/// register another one here to support another calendar service.
/// </summary>
public sealed class CalendarFeature : IFeature
{
    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<IPlugin, CalendarPlugin>();
        services.AddSingleton<Microsoft365CalendarProvider>();
        services.AddSingleton<ICalendarProvider>(sp => sp.GetRequiredService<Microsoft365CalendarProvider>());
        // More providers: services.AddSingleton<ICalendarProvider, MyProvider>();

        services.AddSingleton<CalendarService>();
        services.AddHostedService(sp => sp.GetRequiredService<CalendarService>());
        services.AddSingleton<IWidgetType, NextMeetingSource>();
    }

    public void Start(IServiceProvider services) =>
        services.GetRequiredService<CalendarService>().Initialize(services.GetRequiredService<DanteEngine>());
}
