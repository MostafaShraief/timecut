using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Timecut.Core.Models;

namespace Timecut.Infrastructure.Playwright;

/// <summary>
/// Manages the Playwright browser lifecycle. Maintains a warm browser instance
/// to avoid cold-start overhead per recording job.
/// </summary>
public class BrowserManager : IAsyncDisposable
{
    private readonly ILogger<BrowserManager> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public BrowserManager(ILogger<BrowserManager> logger)
    {
        _logger = logger;
    }

    public async Task<IBrowserContext> CreateContextAsync(RecordingOptions options)
    {
        var browser = await GetBrowserAsync();
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = (int)(options.Width / options.PageZoom),
                Height = (int)(options.Height / options.PageZoom)
            },
            DeviceScaleFactor = (float)options.PageZoom
        });

        return context;
    }

    private async Task<IBrowser> GetBrowserAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BrowserManager));

        await _lock.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true })
                return _browser;

            _logger.LogInformation("Launching Playwright Chromium browser...");
            _playwright ??= await Microsoft.Playwright.Playwright.CreateAsync();

            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-gpu",
                    "--allow-file-access-from-files",
                    "--disable-web-security"
                }
            });

            _logger.LogInformation("Chromium browser launched successfully");
            return _browser;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RestartBrowserAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_browser != null)
            {
                try { await _browser.CloseAsync(); } catch { }
                _browser = null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_browser != null)
        {
            try { await _browser.CloseAsync(); } catch { }
        }
        _playwright?.Dispose();
        _lock.Dispose();
    }
}
