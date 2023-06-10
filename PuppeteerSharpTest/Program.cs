using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using AccountGenerator;
using PuppeteerExtraSharp;
using PuppeteerExtraSharp.Plugins.AnonymizeUa;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
using PuppeteerSharp;
using PuppeteerSharp.Input;
using Zennolab.CapMonsterCloud;
using Zennolab.CapMonsterCloud.Requests;

Console.WriteLine("Downloading local browser... This may take a while if it is not already downloaded!");
await new BrowserFetcher().DownloadAsync();

var capMonsterClient = CapMonsterCloudClientFactory.Create(new ClientOptions {
    ClientKey = "ca30362bff612a32c226ed8f22974f0f",
});
Console.WriteLine($"Capmonster bal: {await capMonsterClient.GetBalanceAsync()}");

//Console.WriteLine("Loading proxies...");
/*if (!File.Exists("proxies.txt")) {
    Environment.Exit(-1);
}*/

//var proxies = File.ReadAllLines("proxies.txt");
var client = new HttpClient(new HttpClientHandler {
    SslProtocols = SslProtocols.Tls13,
}, true);

// Initialization plugin builder
var extra = new PuppeteerExtra();

// Use stealth plugin
extra.Use(new StealthPlugin());
extra.Use(new AnonymizeUaPlugin());
// Launch the puppeteer browser with plugins


if (!ThreadPool.SetMinThreads(Environment.ProcessorCount, Environment.ProcessorCount) ||
    !ThreadPool.SetMaxThreads(Environment.ProcessorCount, Environment.ProcessorCount)) {
    Console.WriteLine("Couldn't increase minimum/maximum worker threads");
}


SemaphoreSlim semaphoreSlim = new(1);
var tasks = new List<Task>(1);
const int AmountToGenOnOneIp = 1;
while (true) {
    Console.WriteLine(
        $"Starting generation of {AmountToGenOnOneIp} accounts! There are 300 seconds of a grace period between attempts, and 120 seconds of wait between a single attempt.");
    for (var i = 0; i < AmountToGenOnOneIp; i++) {
        tasks.Add(Task.Run(async () => {
            var extra = new PuppeteerExtra();
            extra.Use(new AnonymizeUaPlugin());
            extra.Use(new StealthPlugin());
            var browser = await extra.LaunchAsync(new LaunchOptions {
                Headless = false, /*LogProcess = true, DumpIO = true,*/ Args = new[] {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-infobars",
                    "--single-process",
                    "--no-zygote",
                    "--no-first-run",
                    "--ignore-certificate-errors",
                    "--ignore-certificate-errors-skip-list",
                    "--disable-dev-shm-usage",
                    "--disable-accelerated-2d-canvas",
                    "--disable-gpu",
                    "--hide-scrollbars",
                    "--disable-notifications",
                    "--disable-background-timer-throttling",
                    "--disable-backgrounding-occluded-windows",
                    "--disable-breakpad",
                    "--disable-component-extensions-with-background-pages",
                    "--disable-extensions",
                    "--disable-features=TranslateUI,BlinkGenPropertyTrees",
                    "--disable-ipc-flooding-protection",
                    "--disable-renderer-backgrounding",
                    "--enable-features=NetworkService,NetworkServiceInProcess",
                    "--force-color-profile=srgb",
                    "--metrics-recording-only",
                    "--mute-audio",
                },
                IgnoreHTTPSErrors = true, EnqueueTransportMessages = false
            });

            bool accountGeneratorResult;
            try {
                IPage? page;
                var pages = await browser.PagesAsync();
                if (pages.Length > 0)
                    page = pages[0];
                else
                    page = await browser.NewPageAsync();

                accountGeneratorResult =
                    await AttemptGenerateAccount(page, AmountToGenOnOneIp, client, false, semaphoreSlim,
                        capMonsterClient);

                await page.DeleteCookieAsync(await page.GetCookiesAsync());
            }
            catch (Exception ex) {
                Console.WriteLine(ex);
                Console.WriteLine("Closing the browser's left over pages: {0}", (await browser.PagesAsync()).Length);
                foreach (var page_iter in await browser.PagesAsync()) {
                    await page_iter.Client.DetachAsync();
                    await page_iter.CloseAsync();
                    await page_iter.DisposeAsync();
                }

                accountGeneratorResult = false;
            }

            if (!accountGeneratorResult) {
                Console.WriteLine("Account generation failed.");
            }

            browser.ClearCustomQueryHandlers();
            await browser.CloseAsync();
            await browser.DisposeAsync();
            if (!browser.Process.HasExited) {
                Console.WriteLine("The browser is now an orphan process, killing it.");
                browser.Process.Kill();
            }
        }));
    }

    await Task.WhenAll(tasks);
    tasks.Clear();
    if (!Shared.HittedRatelimit) {
        Console.WriteLine("[Post-Loop] Waiting 300 seconds..."); 
        await Task.Delay(300000);
    } else {
        Console.WriteLine("[Post-Loop] WARNING: Suffered a ratelimit! Waiting 600 seconds instead of 300...");
        await Task.Delay(600000);
    }
}

static async Task<bool> AttemptGenerateAccount(IPage page, int generationObjective, HttpClient client,
    bool dropScreenshots, SemaphoreSlim semaphore, ICapMonsterCloudClient capMonsterClient) {
    // Create a new page
    for (var j = 0; j < generationObjective; j++) {
        await page.SetGeolocationAsync(new GeolocationOption {
            Accuracy = new decimal(85 + Random.Shared.NextDouble()),
            Latitude = new decimal(Random.Shared.NextDouble() * 10),
            Longitude = new decimal(10.7389 + Random.Shared.NextDouble() * 10),
        });
        try {
            await page.GoToAsync("https://roblox.com", 50000,
                new WaitUntilNavigation[] {
                    WaitUntilNavigation.Load, WaitUntilNavigation.DOMContentLoaded, WaitUntilNavigation.Networkidle0
                });
            Console.WriteLine("Page opened successfully!");
        }
        catch (NavigationException ex) {
            Console.WriteLine(ex);
            Console.WriteLine("Likely a proxy failure.");
            return false;
        }

        // Wait 5 second for all HTML to download.
        await page.WaitForTimeoutAsync(2000);

        try {
            var cookieAgreement = await page.WaitForSelectorAsync(".cookie-btn", new WaitForSelectorOptions {
                Visible = true, Timeout = 1000, // 1 second wait.
            });

            if (cookieAgreement != null) {
                Console.WriteLine("Cookie agreement detected! Automatically accepted!");
                await cookieAgreement.ClickAsync();
            }
        }
        catch {
            Console.WriteLine("No cookies agreement, sweet!");
        }

        if (dropScreenshots)
            await page.ScreenshotAsync("page_right_after_init.png");

        await page.WaitForTimeoutAsync(Random.Shared.Next(124, 421));
        await page.ClickAsync("#MonthDropdown");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("ArrowUp");

        for (var i = 0; i < 3 + Random.Shared.Next(1, 12); i++)
            await page.Keyboard.PressAsync("ArrowDown");

        await page.Keyboard.PressAsync("ArrowUp");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(Random.Shared.Next(124, 314));
        await page.ClickAsync("#DayDropdown");
        for (var i = 0; i < 6 + Random.Shared.Next(1, 7); i++)
            await page.Keyboard.PressAsync("ArrowDown");

        await page.Keyboard.PressAsync("ArrowUp");
        await page.WaitForTimeoutAsync(Random.Shared.Next(124, 421));
        await page.ClickAsync("#YearDropdown");
        for (var i = 0; i < 10 + Random.Shared.Next(3, 10); i++)
            await page.Keyboard.PressAsync("ArrowDown");

        await page.Keyboard.PressAsync("ArrowUp");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(Random.Shared.Next(312, 1441));

        var userName = $"Funny_{await GenerateUsernameAsync(8)}";

        while (!await CheckUsernameForValidation(userName, client))
            userName = $"Funny_{await GenerateUsernameAsync(8)}";

        var password = "HELLO_FROM_BONG_AND_DOT69";
        Console.WriteLine($"Signing up with username {userName}");
        Console.WriteLine($"Signing up with password {password}");
        await page.Mouse.WheelAsync(0, Random.Shared.Next(-150, -0));
        await page.WaitForTimeoutAsync(Random.Shared.Next(50, 570));
        await page.TypeAsync("input[name=\"signupUsername\"]", userName, new TypeOptions {
            Delay = 16,
        });
        await page.WaitForTimeoutAsync(Random.Shared.Next(100, 500));
        await page.TypeAsync("input[name=\"signupPassword\"]", password, new TypeOptions {
            Delay = 16,
        });
        await page.ClickAsync("#MaleButton");
        await page.WaitForTimeoutAsync(2000);
        if (dropScreenshots)
            await page.ScreenshotAsync("data_inputted.png");

        for (var i = 0; i < 5; i++) {
            await page.WaitForTimeoutAsync(134);
            await page.Mouse.WheelAsync(0, Random.Shared.Next(-100, 15));
        }

        await page.ClickAsync("#signup-button");
        for (var i = 0; i < 5; i++) {
            await page.WaitForTimeoutAsync(118);
            await page.Mouse.WheelAsync(0, Random.Shared.Next(-100, 100));
            await page.Mouse.UpAsync(new ClickOptions {
                Button = MouseButton.Middle, ClickCount = 2, Delay = Random.Shared.Next(0, 200),
                OffSet = new(Random.Shared.Next(0, 121), Random.Shared.Next(0, 102))
            });
        }

        bool triggered = false;

        try {
            var element = await page.WaitForSelectorAsync(".modal-modern-challenge-captcha", new WaitForSelectorOptions {
                Visible = true, Timeout = 5000,
            });

            if (element != null) {
                Console.WriteLine(
                    "A FunCaptcha has been triggered! Skipping request.");

                return false;
                /*      NOTE: ATTEMPT TO WRITE BYPASS OF FUNCAPTCHA, FAILED DUE TO BAD API! 

                var iFrame_Captcha = await page.WaitForSelectorAsync("#arkose-iframe"); // Get the iframe.

                var urlHandle = await iFrame_Captcha.GetPropertyAsync("src");

                var url = urlHandle.ToString();
                await urlHandle.DisposeAsync();
                if (url == null) {
                    Console.WriteLine(
                        "Can not automatically bypass! Roblox has changed their site? What the heeeeellllll.");
                    continue;
                }

                var pubKeyStart = url.IndexOf("publicKey=", StringComparison.InvariantCulture);
                var pubKeyEnd = url.IndexOf('&');

                var pubKey = url.AsSpan()[(pubKeyStart + "publicKey=".Length)..pubKeyEnd].ToString();

                var captchaRequestSolve = new FunCaptchaProxylessRequest {
                    WebsiteKey = pubKey,
                    WebsiteUrl = "https://roblox.com/",
                    Subdomain = "https://roblox-api.arkoselabs.com/",
                };

                var solverRequest = await capMonsterClient.SolveAsync(captchaRequestSolve, new CancellationToken());
                if (solverRequest.Error != null) {
                    Console.WriteLine($"Error: {solverRequest.Error}");
                    throw new Exception("Fu- " + solverRequest.Error);
                }
                Console.WriteLine($"Solution Magic: {solverRequest.Solution.Value}");
                var fc_tokenElement = await page.WaitForSelectorAsync("#FunCaptcha-Token");

                await fc_tokenElement.EvaluateFunctionAsync(
                    $"""
                        document.querySelector("#FunCaptcha-Token").value = {solverRequest.Solution.Value};
                        console.log("bypassed byfroon!11");
                    """, null);

                await page.WaitForTimeoutAsync(6000);

                */
            }

            while (element != null) {
                element = await page.WaitForSelectorAsync(".modal-modern-challenge-captcha", new WaitForSelectorOptions {
                    Visible = true, Timeout = 5000,
                });
                Console.WriteLine("Waiting for captcha competition... Next check in 5 seconds...");
                await page.WaitForTimeoutAsync(5000);
                triggered = true;
                return false;
            }
        } catch (Exception ex) {
            // Not an error, The captcha was not triggered!
            if (!triggered)
                Console.WriteLine("FunCaptcha not triggered, sweet!");
            else
                Console.WriteLine("FunCaptcha had been triggered, but it was cleared!");
        }

        if (dropScreenshots)
            await page.ScreenshotAsync("signup_pressed.png");

        var redirectedToHome = false;
        try {
            await page.WaitForTimeoutAsync(3000);
            var str = await page.GetTitleAsync();
            if (str != null && str.Contains("Home")) {
                Console.WriteLine("Account generated successfully.");
                redirectedToHome = true;
            }
        }
        catch {
            if (dropScreenshots)
                await page.ScreenshotAsync("page_rn_THISISFORDBGPLZ.png");
        }

        if (!redirectedToHome) {
            Console.WriteLine("WARNING: Possible ratelimit hit, evaluating HTML with JS...");
            try {
                var element = await page.WaitForSelectorAsync("#GeneralErrorText");
                var isRatelimited = await page.EvaluateFunctionAsync<bool>(
                    """
                (element) => {
                    return element.innerText == "Sorry! An unknown error occurred. Please try again later.";
                }
                """, element);
                if (isRatelimited) {
                    Console.WriteLine("Critical: Ratelimit hit! Setting state...");
                    Shared.HittedRatelimit = true;
                    Console.WriteLine("Returning...");
                    return false;
                }

                await element.DisposeAsync();
            }
            catch {
                Console.WriteLine("Ratelimit not hit? What. I'm going to wait a delay of 50 seconds ANYWAYS");
                await page.WaitForTimeoutAsync(50000);
            }
        }

        var watch = Stopwatch.StartNew();
        var foundRobloSecurity = false;
        while (!foundRobloSecurity) {
            var cookies = await page.GetCookiesAsync();
            foreach (var cookie in cookies) {
                if (cookie.Name == ".ROBLOSECURITY") {
                    Console.WriteLine("Found Roblox Security!");
                    Console.WriteLine("Writing to file...");


                    await semaphore.WaitAsync();
                    File.AppendAllText("generated.txt",
                        $"USER:{userName}|Pass:{password}|{cookie.Name}={cookie.Value}\r\n");
                    semaphore.Release();
                    foundRobloSecurity = true;
                    break;
                }
            }

            if (watch.ElapsedMilliseconds > 30000) { // 30 seconds!
                Console.WriteLine("Timed out waiting for Roblox Security Cookie! Skipping attempt!");
                watch.Stop();
                watch = null;
                break;
            }

            await page.DeleteCookieAsync(cookies);
        }

        // Take the screenshot
        if (dropScreenshots)
            await page.ScreenshotAsync("ending_image.png");

        if (foundRobloSecurity) {
            Console.WriteLine("Account generated, waiting 300 seconds before generating another one to avoid 429d...");
            await Task.Delay(300000);
        } else {
            Console.WriteLine("Failed to generate Account, waiting 500 seconds before generating another one as to avoid issues...");
            await Task.Delay(500000);
        }
    }

    return true;
}

static async Task<ProxyResult> CheckProxyAsync(string host, ushort port) {
    CancellationTokenSource cts = new(5000);
    Socket sock = new(SocketType.Stream, ProtocolType.Tcp);
    try {
        await sock.ConnectAsync(new IPEndPoint(IPAddress.Parse(host), port), cts.Token);
        Console.WriteLine($"[Proxy Checker] Send CONNECT request to {host}:{port}");
        await sock.SendAsync("CONNECT http://roblox.com/ HTTP/1.1\n"u8.ToArray());

        //Console.WriteLine("[Proxy Checker] Waiting for a response from the proxy... (3 seconds)");

        var buf = new byte[1024];
        sock.ReceiveTimeout = 5000; // 5 Seconds.
        var read = sock.Receive(buf, 0, buf.Length, SocketFlags.None, out var error);

        if (read == 0 && error == SocketError.TimedOut) {
            Console.WriteLine("[Proxy Checker] Proxy didn't respond! Cleaning up...");
            await sock.DisconnectAsync(false);
            return new(host, port, false);
        }

        Console.WriteLine("[Proxy Checker] Check passed! Cleaning up...");
        await sock.DisconnectAsync(false);
        return new(host, port, true);
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested) {
        //Console.WriteLine($"[Proxy Checker] Failed to check proxy! {host}:{port}");
    }
    catch (FormatException) {
        Console.WriteLine($"[Proxy Checker] The host was malformed! {host}:{port}");
    }
    catch (SocketException ex) {
        Console.WriteLine($"[Proxy Checker] The proxy likely refused the connection! {host}:{port}");
    }
    finally {
        sock.Dispose();
    }

    return new(host, port, false);
}

static async Task<string> GenerateUsernameAsync(int length) {
    const string chars = "ABCANauabfasPisaiuHZyua01345789";
    return new string(Enumerable.Repeat(chars, length)
                                .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
}

static async Task<bool> CheckUsernameForValidation(string usrName, HttpClient client) {
    var str = await client.GetStringAsync(
        $"https://auth.roblox.com/v2/usernames/validate?Username={usrName}&Birthday=12-2-1990&Context=0");
    return str.Contains("Username is valid");
}