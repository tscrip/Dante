using Dante.Core.Engine;
using Dante.Core.Sources;

namespace Dante.Server.Features.Calendar;

/// <summary>
/// Meeting reminders on DANTE from your calendar. Providers implement <see cref="ICalendarProvider"/>;
/// register another one here to support another calendar service.
/// </summary>
public sealed class CalendarFeature : IFeature
{
    public IEnumerable<NavItem> Navigation => [new("Calendar", "calendar", 45)];

    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<Microsoft365CalendarProvider>();
        services.AddSingleton<ICalendarProvider>(sp => sp.GetRequiredService<Microsoft365CalendarProvider>());
        // More providers: services.AddSingleton<ICalendarProvider, MyProvider>();

        services.AddSingleton<CalendarService>();
        services.AddHostedService(sp => sp.GetRequiredService<CalendarService>());
        services.AddSingleton<IDataSource, NextMeetingSource>();
    }

    public void Start(IServiceProvider services) =>
        services.GetRequiredService<CalendarService>().Initialize(services.GetRequiredService<DanteEngine>());
}
