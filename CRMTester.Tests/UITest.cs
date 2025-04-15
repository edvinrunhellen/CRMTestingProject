using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;


namespace PlaywrightTests;

[TestClass]
public class DemoTest : PageTest
{
    private IPlaywright _playwright;
    private IBrowser _browser;
    private IBrowserContext _browserContext;
    private IPage _page;

    [TestInitialize]
    public async Task Setup()
    {
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false,
            SlowMo = 1000 // Lägger in en fördröjning så vi kan se vad som händer
        });
        _browserContext = await _browser.NewContextAsync();
        _page = await _browserContext.NewPageAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _browserContext.CloseAsync();
        await _browser.CloseAsync();
        _playwright.Dispose();
    }

    [TestMethod]
    public async Task CreateIssue()
    {
        await _page.GotoAsync("http://localhost:5173/");
        await _page.GetByRole(AriaRole.Link, new() { Name = "demoab" }).ClickAsync();
        await _page.GetByRole(AriaRole.Textbox, new() { Name = "Your email" }).ClickAsync();
        await _page.GetByRole(AriaRole.Textbox, new() { Name = "Your email" }).FillAsync("Eddeddsson@gmail.com");
        await _page.GetByRole(AriaRole.Textbox, new() { Name = "Title" }).ClickAsync();
        await _page.GetByRole(AriaRole.Textbox, new() { Name = "Title" }).FillAsync("Help");
        await _page.GetByLabel("SubjectReklamationSkadaÖ").SelectOptionAsync(new[] { "Övrigt" });
        await _page.GetByRole(AriaRole.Textbox, new() { Name = "Message" }).ClickAsync();
        await _page.GetByRole(AriaRole.Textbox, new() { Name = "Message" }).FillAsync("does not work");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Create issue" }).ClickAsync();
    }
    [TestMethod]
    public async Task userLoginAndHandleIssue()
    {

    }
}