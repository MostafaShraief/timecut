using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using Timecut.Infrastructure;

namespace Timecut.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

		// MudBlazor
		builder.Services.AddMudServices();

		// Timecut infrastructure (Playwright, FFmpeg, persistence, etc.)
		builder.Services.AddTimecutInfrastructure();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

		// Initialize database
		Task.Run(async () => await app.Services.InitializeDatabaseAsync()).GetAwaiter().GetResult();

		return app;
	}
}
