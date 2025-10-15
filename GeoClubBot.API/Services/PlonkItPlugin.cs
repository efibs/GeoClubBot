using System.ComponentModel;
using HtmlAgilityPack;
using Microsoft.SemanticKernel;
using PuppeteerSharp;

namespace GeoClubBot.Services;

public class PlonkItPlugin
{
    public PlonkItPlugin(ILogger<PlonkItPlugin> logger)
    {
        _logger = logger;
    }

    [KernelFunction("get_plonkit_country_guide")]
    [Description("Gets the PlonkIt GeoGuessr meta guide page (https://www.plonkit.net/<country>) for a given country. Note that multi word countries will be written with a dash between the words and everything must be written in lowercase. The USA for example is called \"united-states\"")]
    public async Task<string> GetPlonkItCountryGuide(string country)
    {
        var url = $"{PlonkItBaseAddress}/{country}";
        
        try
        {
            _logger.LogDebug($"Fetching url: {url}");
            
            var browser = await GetBrowserAsync().ConfigureAwait(false);
            var page = await browser.NewPageAsync().ConfigureAwait(false);
            
            // Navigate and wait for network to be idle
            await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.Networkidle2],
                Timeout = 30000
            }).ConfigureAwait(false);
            
            // Additional wait for dynamic content
            await Task.Delay(2000).ConfigureAwait(false);
            
            var content = await page.GetContentAsync().ConfigureAwait(false);
            await page.CloseAsync().ConfigureAwait(false);
            
            _logger.LogDebug($"Url fetched: {url}");
            
            // Use HtmlAgilityPack to extract text
            var doc = new HtmlDocument();
            doc.LoadHtml(content);

            // Remove script and style elements
            doc.DocumentNode.Descendants()
                .Where(n => n.Name == "script" || n.Name == "style" || n.Name == "head")
                .ToList()
                .ForEach(n => n.Remove());

            // Get text content
            var text = doc.DocumentNode.InnerHtml;
        
            // Clean up whitespace
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            return text.Trim();
        }
        catch (Exception ex)
        {
            return $"Error rendering website: {ex.Message}";
        }
    }
    
    private async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser != null) return _browser;
        // Download Chromium if not already downloaded
        var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync().ConfigureAwait(false);
            
        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            Args = ["--no-sandbox", "--disable-setuid-sandbox"]
        }).ConfigureAwait(false);
        return _browser;
    }
    
    private readonly ILogger<PlonkItPlugin> _logger;
    private IBrowser? _browser;
    private const string PlonkItBaseAddress = "https://www.plonkit.net";
}