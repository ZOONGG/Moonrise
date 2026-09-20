using System.Net;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Reflection;
using Moonrise.Models;
using Moonrise.Services;
using Xunit;

namespace Moonrise.Tests;

public sealed class SupportStage1Tests
{
    private static readonly Uri BaseUri = new("https://support.example.test/");

    [Fact]
    public async Task ApiClientParsesMethodsCheckoutStatusAndApiError()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Json(HttpStatusCode.OK, """{"methods":[{"id":"telegram_stars","enabled":true,"currency":"XTR","presets":[50,100,250,500],"custom":{"enabled":true,"min":1,"max":10000}}]}"""),
            Json(HttpStatusCode.Created, """{"paymentIntentId":"pi-1","provider":"telegram_stars","currency":"XTR","amount":125,"checkoutUrl":"https://t.me/$invoice","expiresAt":"2026-09-19T12:00:00Z","statusToken":"ephemeral"}"""),
            Json(HttpStatusCode.OK, """{"paymentIntentId":"pi-1","provider":"telegram_stars","currency":"XTR","amount":125,"status":"paid","expiresAt":"2026-09-19T12:00:00Z","paidAt":"2026-09-19T11:45:00Z"}""", retryAfter: 7),
            Json(HttpStatusCode.UnprocessableEntity, """{"error":{"code":"INVALID_AMOUNT","message":"Amount is outside the allowed range."}}""")
        ]);
        using var http = new HttpClient(new DelegateHandler((_, _) => Task.FromResult(responses.Dequeue())));
        var client = new SupportApiClient(http, BaseUri);

        var methods = await client.GetMethodsAsync(default);
        Assert.Equal([50, 100, 250, 500], methods.Methods.Single().Presets);
        Assert.Equal(1, methods.Methods.Single().Custom.Min);
        Assert.Equal(10000, methods.Methods.Single().Custom.Max);

        var checkout = await client.CreateCheckoutAsync("telegram_stars", new SupportCheckoutRequest(125, true, "app", "en"), default);
        Assert.Equal("pi-1", checkout.PaymentIntentId);
        Assert.Equal("ephemeral", checkout.StatusToken);

        var status = await client.GetPaymentStatusAsync("pi-1", "ephemeral", default);
        Assert.Equal("paid", status.Payment.Status);
        Assert.Equal(TimeSpan.FromSeconds(7), status.RetryAfter);

        var error = await Assert.ThrowsAsync<SupportApiException>(() =>
            client.CreateCheckoutAsync("telegram_stars", new SupportCheckoutRequest(0, true, "app", "en"), default));
        Assert.Equal("INVALID_AMOUNT", error.Code);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, error.StatusCode);
    }

    [Theory]
    [InlineData("https://t.me/$valid-invoice", true)]
    [InlineData("https://T.ME/$valid", true)]
    [InlineData("http://t.me/$invoice", false)]
    [InlineData("https://telegram.me/$invoice", false)]
    [InlineData("https://t.me/ordinary-page", false)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    public void CheckoutUrlAllowListIsStrict(string value, bool expected) =>
        Assert.Equal(expected, CheckoutUrlValidator.TryValidateTelegramCheckout(value, out _));

    [Theory]
    [InlineData("https://t.me/CryptoTestnetBot?start=invoice-token", true)]
    [InlineData("https://t.me/CryptoTestnetBot?start=", false)]
    [InlineData("https://t.me/CryptoBot?start=invoice-token", false)]
    [InlineData("http://t.me/CryptoTestnetBot?start=invoice-token", false)]
    [InlineData("javascript:alert(1)", false)]
    public void CryptoCheckoutUrlAllowListIsStrict(string value, bool expected) =>
        Assert.Equal(expected, CheckoutUrlValidator.TryValidateCryptoPayCheckout(value, out _));

    [Fact]
    public async Task CryptoAvailabilityAndAmountsComeFromMethodsResponse()
    {
        using var controller = new SupportFlowController(new FakeApi());
        await controller.LoadAsync();

        var crypto = Assert.Single(controller.Methods, item => item.Id == "direct_crypto");
        Assert.True(crypto.Enabled);
        Assert.Equal([1, 5, 10, 25], crypto.Presets);
        Assert.Equal(1, crypto.Custom.Min);
        Assert.Null(crypto.Custom.Max);
        Assert.True(controller.SelectMethod("direct_crypto"));
        Assert.True(controller.TryResolveAmountSelection("5", CultureInfo.InvariantCulture, out var amount, out var selected));
        Assert.Equal(5, amount);
        Assert.Equal(5, selected);
        Assert.True(controller.TryResolveAmountSelection("7", CultureInfo.InvariantCulture, out amount, out selected));
        Assert.Equal(7, amount);
        Assert.Null(selected);
        Assert.True(controller.TryResolveAmountSelection("5000", CultureInfo.InvariantCulture, out amount, out selected));
        Assert.Equal(5000, amount);
        Assert.True(controller.TryResolveAmountSelection("5.25", CultureInfo.InvariantCulture, out amount, out selected));
        Assert.Equal(5.25m, amount);
    }

    [Fact]
    public async Task DisabledCryptoCannotBeSelected()
    {
        using var controller = new SupportFlowController(new FakeApi(cryptoEnabled: false));
        await controller.LoadAsync();
        Assert.False(controller.SelectMethod("direct_crypto"));
        Assert.Equal("telegram_stars", controller.Method!.Id);
    }

    [Fact]
    public async Task DirectCryptoUsesSeparateAssetStepAndExactWalletCheckout()
    {
        var api = new FakeApi();
        using var controller = new SupportFlowController(api);
        await controller.LoadAsync();
        Assert.True(controller.SelectMethod("direct_crypto"));
        await controller.BeginCheckoutAsync(5, "en");
        Assert.Equal(SupportFlowState.ChoosingAsset, controller.State);
        Assert.Equal(0, api.CheckoutCalls);

        var asset = Assert.Single(controller.Method!.Assets!);
        await controller.BeginDirectCryptoCheckoutAsync(asset);
        Assert.Equal(SupportFlowState.Waiting, controller.State);
        Assert.Equal("5.003821", controller.Checkout!.CryptoAmount);
        Assert.Equal("ethereum", controller.Checkout.Network);
        Assert.DoesNotContain("t.me", controller.Checkout.CheckoutUrl, StringComparison.OrdinalIgnoreCase);
        controller.Close();
    }

    [Fact]
    public async Task StatusTokenUsesAuthorizationHeaderAndNeverTheUrl()
    {
        Uri? requestedUri = null;
        string? authorization = null;
        using var http = new HttpClient(new DelegateHandler((request, _) =>
        {
            requestedUri = request.RequestUri;
            authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(Json(HttpStatusCode.OK,
                """{"paymentIntentId":"pi-1","provider":"telegram_stars","currency":"XTR","amount":50,"status":"pending","expiresAt":"2026-09-19T12:00:00Z","paidAt":null}"""));
        }));

        await new SupportApiClient(http, BaseUri).GetPaymentStatusAsync("pi-1", "top-secret-capability", default);

        Assert.Equal("Bearer top-secret-capability", authorization);
        Assert.DoesNotContain("top-secret-capability", requestedUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Empty(requestedUri.Query);
    }

    [Fact]
    public void SupportConfigurationAllowsOnlyHttpsOrLoopbackDevelopmentHttp()
    {
        Assert.True(SupportApiConfiguration.IsAllowedBaseUri(new Uri("https://api.example.test")));
        Assert.True(SupportApiConfiguration.IsAllowedBaseUri(new Uri("http://127.0.0.1:8787")));
        Assert.False(SupportApiConfiguration.IsAllowedBaseUri(new Uri("http://api.example.test")));
        Assert.False(SupportApiConfiguration.IsAllowedBaseUri(new Uri("file:///tmp/api")));
    }

    [Fact]
    public void BuildCarriesDefaultPublicSupportEndpointWithoutACompanionFile()
    {
        var endpoint = typeof(SupportApiConfiguration).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == SupportApiConfiguration.AssemblyMetadataKey)
            .Value;

#if DEBUG
        Assert.Equal("http://127.0.0.1:8787", endpoint);
#else
        Assert.Equal("https://moonrise-backend-production.up.railway.app", endpoint);
#endif
    }

    [Fact]
    public void StatusCapabilityIsNotPartOfSettingsOrUiCheckoutModel()
    {
        Assert.DoesNotContain(typeof(MoonriseSettings).GetProperties(), property =>
            property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(SupportCheckoutView).GetProperties(), property =>
            property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(SupportApiConfiguration).GetProperties(), property =>
            property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("50", "en-US", 50)]
    [InlineData("0", "en-US", 1)]
    [InlineData("0.1", "en-US", 1)]
    [InlineData("-100", "en-US", 1)]
    [InlineData("10000000", "en-US", 10000)]
    [InlineData("1.4", "en-US", 1)]
    [InlineData("1.5", "en-US", 2)]
    [InlineData("1,5", "ru-RU", 2)]
    public async Task CustomAmountRoundsAwayFromZeroThenClampsToBackendRange(
        string value,
        string cultureName,
        int expected)
    {
        var api = new FakeApi();
        using var controller = new SupportFlowController(api);
        await controller.LoadAsync();
        Assert.True(controller.TryNormalizeAmount(value, CultureInfo.GetCultureInfo(cultureName), out var amount));
        Assert.Equal(expected, amount);
        Assert.Equal([50, 100, 250, 500], controller.Method!.Presets);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("1e3")]
    public async Task EmptyOrUnparseableCustomAmountRemainsUnavailable(string value)
    {
        using var controller = new SupportFlowController(new FakeApi());
        await controller.LoadAsync();
        Assert.False(controller.TryNormalizeAmount(value, CultureInfo.InvariantCulture, out _));
    }

    [Fact]
    public async Task PresetSelectionFormatsTheSingleAmountInput()
    {
        using var controller = new SupportFlowController(new FakeApi());
        await controller.LoadAsync();

        Assert.True(controller.TryGetPresetInput(100, CultureInfo.InvariantCulture, out var input));
        Assert.Equal("100", input);
        Assert.True(controller.TryResolveAmountSelection(
            input, CultureInfo.InvariantCulture, out var amount, out var selectedPreset));
        Assert.Equal(100, amount);
        Assert.Equal(100, selectedPreset);
    }

    [Fact]
    public async Task CustomNonPresetAmountClearsPresetSelection()
    {
        using var controller = new SupportFlowController(new FakeApi());
        await controller.LoadAsync();

        Assert.True(controller.TryResolveAmountSelection(
            "5000", CultureInfo.InvariantCulture, out var amount, out var selectedPreset));
        Assert.Equal(5000, amount);
        Assert.Null(selectedPreset);
    }

    [Fact]
    public async Task ManualExactPresetRestoresPresetSelection()
    {
        using var controller = new SupportFlowController(new FakeApi());
        await controller.LoadAsync();

        Assert.True(controller.TryResolveAmountSelection(
            "5000", CultureInfo.InvariantCulture, out _, out var customSelection));
        Assert.Null(customSelection);
        Assert.True(controller.TryResolveAmountSelection(
            "250", CultureInfo.InvariantCulture, out var amount, out var restoredSelection));
        Assert.Equal(250, amount);
        Assert.Equal(250, restoredSelection);
    }

    [Fact]
    public async Task AmountSelectionStillClampsBeforeCheckout()
    {
        using var controller = new SupportFlowController(new FakeApi());
        await controller.LoadAsync();

        Assert.True(controller.TryResolveAmountSelection(
            "50000", CultureInfo.InvariantCulture, out var amount, out var selectedPreset));
        Assert.Equal(10000, amount);
        Assert.Null(selectedPreset);
    }

    [Fact]
    public async Task CheckoutCreationStartsPollingImmediatelyWithoutOpeningTelegram()
    {
        var enteredStatus = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeApi();
        api.StatusSteps.Enqueue(async (_, cancellationToken) =>
        {
            enteredStatus.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Result("pending");
        });
        using var controller = ImmediateController(api);
        await controller.LoadAsync();

        await controller.BeginCheckoutAsync(50, "en");
        await enteredStatus.Task;

        Assert.Equal(1, api.CheckoutCalls);
        Assert.Equal(1, api.StatusCalls);
        Assert.Equal(SupportFlowState.Waiting, controller.State);
        controller.Close();
    }

    [Fact]
    public async Task QrPathTransitionsFromPendingToPaidWithoutProviderLaunchAction()
    {
        var api = new FakeApi();
        api.StatusSteps.Enqueue((_, _) => Task.FromResult(Result("pending")));
        api.StatusSteps.Enqueue((_, _) => Task.FromResult(Result("paid")));
        using var controller = ImmediateController(api);
        await controller.LoadAsync();

        await controller.BeginCheckoutAsync(50, "en");
        await controller.ActivePollingTask!;

        Assert.Equal(SupportFlowState.Paid, controller.State);
        Assert.Equal(1, api.CheckoutCalls);
        Assert.Equal(2, api.StatusCalls);
    }

    [Fact]
    public async Task HttpPipelineTransitionsFromPendingToPaidAndKeepsStatusTokenOutOfTheUrl()
    {
        var statusCalls = 0;
        var statusTokenStayedInHeader = true;
        using var http = new HttpClient(new DelegateHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/methods", StringComparison.Ordinal))
                return Task.FromResult(Json(HttpStatusCode.OK, """{"methods":[{"id":"telegram_stars","enabled":true,"currency":"XTR","presets":[50,100,250,500],"custom":{"enabled":true,"min":1,"max":10000}}]}"""));
            if (request.Method == HttpMethod.Post)
                return Task.FromResult(Json(HttpStatusCode.Created, """{"paymentIntentId":"pi-real-shape","provider":"telegram_stars","currency":"XTR","amount":50,"checkoutUrl":"https://t.me/$invoice","expiresAt":"2099-09-19T12:00:00Z","statusToken":"memory-only-token"}"""));

            statusCalls++;
            statusTokenStayedInHeader &= request.Headers.Authorization?.Parameter == "memory-only-token" &&
                                        !request.RequestUri!.AbsoluteUri.Contains("memory-only-token", StringComparison.Ordinal);
            var status = statusCalls == 1 ? "pending" : "paid";
            return Task.FromResult(Json(HttpStatusCode.OK,
                $$"""{"paymentIntentId":"pi-real-shape","provider":"telegram_stars","currency":"XTR","amount":50,"status":"{{status}}","expiresAt":"2099-09-19T12:00:00Z","paidAt":{{(status == "paid" ? "\"2099-09-19T11:45:00Z\"" : "null")}}}""",
                retryAfter: 2));
        }));
        using var controller = new SupportFlowController(
            new SupportApiClient(http, BaseUri),
            (_, cancellationToken) => cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : Task.CompletedTask);

        await controller.LoadAsync();
        await controller.BeginCheckoutAsync(50, "en");
        await controller.ActivePollingTask!;

        Assert.Equal(SupportFlowState.Paid, controller.State);
        Assert.Equal(2, controller.StatusPollCount);
        Assert.True(statusTokenStayedInHeader);
    }

    [Theory]
    [InlineData("paid", SupportFlowState.Paid)]
    [InlineData("expired", SupportFlowState.Expired)]
    [InlineData("failed", SupportFlowState.Failed)]
    public async Task TerminalPaymentStatesStopPolling(string status, SupportFlowState expected)
    {
        var api = new FakeApi();
        api.StatusSteps.Enqueue((_, _) => Task.FromResult(Result(status)));
        using var controller = ImmediateController(api);
        await controller.LoadAsync();
        await controller.BeginCheckoutAsync(50, "en");
        await controller.ActivePollingTask!;
        Assert.Equal(expected, controller.State);
        Assert.Equal(1, api.StatusCalls);
    }

    [Fact]
    public async Task ClosingSupportModalCancelsPolling()
    {
        var api = new FakeApi();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.StatusSteps.Enqueue(async (_, cancellationToken) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Result("pending");
        });
        using var controller = ImmediateController(api);
        await controller.LoadAsync();
        await controller.BeginCheckoutAsync(50, "en");
        await entered.Task;
        Assert.Equal(SupportFlowState.Waiting, controller.State);
        var polling = controller.ActivePollingTask!;
        controller.Close();
        Assert.Equal(SupportFlowState.Closed, controller.State);
        await polling;
    }

    [Fact]
    public async Task ReplacingCheckoutCancelsPreviousPoll()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeApi();
        api.StatusSteps.Enqueue(async (_, cancellationToken) =>
        {
            firstEntered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { firstCancelled.SetResult(); throw; }
            return Result("pending");
        });
        api.StatusSteps.Enqueue((_, cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith(
                _ => Result("pending"), cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
        using var controller = ImmediateController(api);
        await controller.LoadAsync();
        await controller.BeginCheckoutAsync(50, "en");
        await firstEntered.Task;
        await controller.BeginCheckoutAsync(100, "en");
        await firstCancelled.Task;
        Assert.Equal(2, api.CheckoutCalls);
        controller.Close();
    }

    [Fact]
    public async Task StartingReplacementDiscardsStaleCheckoutBeforeNewResponseArrives()
    {
        var firstPollEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementResponse = new TaskCompletionSource<SupportCheckoutResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeApi();
        api.StatusSteps.Enqueue(async (_, cancellationToken) =>
        {
            firstPollEntered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Result("pending");
        });
        using var controller = ImmediateController(api);
        await controller.LoadAsync();
        await controller.BeginCheckoutAsync(50, "en");
        await firstPollEntered.Task;
        Assert.NotNull(controller.Checkout);

        api.CheckoutSteps.Enqueue((_, cancellationToken) => replacementResponse.Task.WaitAsync(cancellationToken));
        api.StatusSteps.Enqueue((_, cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith(
                _ => Result("pending"), cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
        var replacement = controller.BeginCheckoutAsync(100, "en");

        Assert.Equal(SupportFlowState.CreatingCheckout, controller.State);
        Assert.Null(controller.Checkout);
        Assert.False(controller.TryGetActiveCheckout(out _));

        replacementResponse.SetResult(new SupportCheckoutResponse(
            "pi-2", "telegram_stars", "XTR", 100, "https://t.me/$fresh-invoice",
            DateTimeOffset.UtcNow.AddMinutes(30), "fresh-status-token"));
        await replacement;

        Assert.Equal("pi-2", controller.Checkout!.PaymentIntentId);
        Assert.Equal("https://t.me/$fresh-invoice", controller.Checkout.CheckoutUrl);
        Assert.True(controller.TryGetActiveCheckout(out var active));
        Assert.Equal(controller.Checkout, active);
        controller.Close();
    }

    [Fact]
    public async Task MissingStatusPairTransitionsToExpiredAndStopsPolling()
    {
        var api = new FakeApi();
        api.StatusSteps.Enqueue((_, _) => throw new SupportApiException(
            "PAYMENT_NOT_FOUND", "gone", HttpStatusCode.NotFound, null));
        using var controller = ImmediateController(api);
        await controller.LoadAsync();

        await controller.BeginCheckoutAsync(50, "en");
        await controller.ActivePollingTask!;

        Assert.Equal(SupportFlowState.Expired, controller.State);
        Assert.Equal(1, api.StatusCalls);
        Assert.Null(controller.Checkout);
        Assert.False(controller.TryGetActiveCheckout(out _));
    }

    [Fact]
    public async Task TemporaryNetworkFailureRetriesStatusWithoutCreatingAnotherCheckout()
    {
        var api = new FakeApi();
        api.StatusSteps.Enqueue((_, _) => throw new HttpRequestException("offline"));
        api.StatusSteps.Enqueue((_, _) => Task.FromResult(Result("paid")));
        using var controller = ImmediateController(api);
        await controller.LoadAsync();
        await controller.BeginCheckoutAsync(50, "en");
        await controller.ActivePollingTask!;
        Assert.Equal(SupportFlowState.Paid, controller.State);
        Assert.Equal(1, api.CheckoutCalls);
        Assert.Equal(2, api.StatusCalls);
    }

    [Fact]
    public async Task HttpClientStyleTimeoutRetriesInsteadOfSilentlyStoppingPolling()
    {
        var api = new FakeApi();
        api.StatusSteps.Enqueue((_, _) => throw new TaskCanceledException("request timeout"));
        api.StatusSteps.Enqueue((_, _) => Task.FromResult(Result("paid")));
        using var controller = ImmediateController(api);
        await controller.LoadAsync();

        await controller.BeginCheckoutAsync(50, "en");
        await controller.ActivePollingTask!;

        Assert.Equal(SupportFlowState.Paid, controller.State);
        Assert.Equal(1, api.CheckoutCalls);
        Assert.Equal(2, api.StatusCalls);
    }

    [Fact]
    public async Task TransientServerFailureRetriesUntilPaid()
    {
        var api = new FakeApi();
        api.StatusSteps.Enqueue((_, _) => throw new SupportApiException(
            "REQUEST_FAILED", "temporary", HttpStatusCode.InternalServerError, null));
        api.StatusSteps.Enqueue((_, _) => Task.FromResult(Result("paid")));
        using var controller = ImmediateController(api);
        await controller.LoadAsync();

        await controller.BeginCheckoutAsync(50, "en");
        await controller.ActivePollingTask!;

        Assert.Equal(SupportFlowState.Paid, controller.State);
        Assert.Equal(2, controller.StatusPollCount);
        Assert.Equal(1, api.CheckoutCalls);
    }

    [Fact]
    public async Task PollingHonorsRetryAfter()
    {
        var delays = new List<TimeSpan>();
        var api = new FakeApi();
        api.StatusSteps.Enqueue((_, _) => Task.FromResult(Result("pending", 7)));
        api.StatusSteps.Enqueue((_, _) => Task.FromResult(Result("paid")));
        using var controller = new SupportFlowController(api, (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });
        await controller.LoadAsync();
        await controller.BeginCheckoutAsync(50, "en");
        await controller.ActivePollingTask!;
        Assert.Equal([TimeSpan.FromSeconds(7)], delays);
    }

    [Fact]
    public async Task UnsafeCheckoutNeverStartsPolling()
    {
        var api = new FakeApi { CheckoutUrl = "file:///C:/unsafe" };
        using var controller = ImmediateController(api);
        await controller.LoadAsync();
        await controller.BeginCheckoutAsync(50, "en");
        Assert.Equal(SupportFlowState.Failed, controller.State);
        Assert.Equal("UNSAFE_CHECKOUT_URL", controller.LastErrorCode);
        Assert.Equal(0, api.StatusCalls);
    }

    [Fact]
    public async Task CheckoutWithoutStatusCapabilityFailsBeforePolling()
    {
        var api = new FakeApi { StatusToken = string.Empty };
        using var controller = ImmediateController(api);
        await controller.LoadAsync();

        await controller.BeginCheckoutAsync(50, "en");

        Assert.Equal(SupportFlowState.Failed, controller.State);
        Assert.Equal("INVALID_RESPONSE", controller.LastErrorCode);
        Assert.Equal(0, api.StatusCalls);
    }

    [Fact]
    public void QrCodeIsGeneratedOfflineOnlyForApprovedUrl()
    {
        var png = SupportQrCodeService.CreatePng("telegram_stars", "https://t.me/$exact-provider-value");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        var cryptoPng = SupportQrCodeService.CreatePng("crypto_pay", "https://t.me/CryptoTestnetBot?start=invoice-token");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, cryptoPng[..4]);
        Assert.Throws<InvalidDataException>(() => SupportQrCodeService.CreatePng("crypto_pay", "https://example.test/invoice"));
        var directPng = SupportQrCodeService.CreatePng("direct_crypto", "bitcoin:bc1qexample?amount=0.001&label=Moonrise");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, directPng[..4]);
    }

    [Fact]
    public void SupportUiUsesOwnedControlsAndDoesNotTiePollingToWindowFocusOrTelegramLaunch()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Moonrise", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "Moonrise", "MainWindow.xaml.cs"));
        var controls = File.ReadAllText(Path.Combine(root, "src", "Moonrise", "Themes", "MoonriseControls.xaml"));
        var icons = File.ReadAllText(Path.Combine(root, "src", "Moonrise", "Themes", "MoonriseIcons.xaml"));

        Assert.Contains("Style=\"{StaticResource MoonriseCheckBox}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SupportAmountInput}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SupportAmountSelector", controls, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedMarker", controls, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TelegramStarsIcon\"", icons, StringComparison.Ordinal);
        Assert.Contains("Source=\"{StaticResource TelegramStarsIcon}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Source=\"{StaticResource TelegramStarsIcon}\"", controls, StringComparison.Ordinal);
        Assert.Contains("FindResource(\"TelegramStarsIcon\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IconTelegramStarsMain", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IconTelegramStarsMain", controls, StringComparison.Ordinal);
        Assert.DoesNotContain("★", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("★", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Stars\"", controls, StringComparison.Ordinal);
        Assert.DoesNotContain("SupportTerminalCloseButton", xaml, StringComparison.Ordinal);
        Assert.Contains("FocusRing", ExtractStyle(controls, "SupportAmountSelector"), StringComparison.Ordinal);
        Assert.Contains("BorderThickness\" Value=\"2", ExtractStyle(controls, "SupportAmountSelector"), StringComparison.Ordinal);
        Assert.Contains("FocusRing", ExtractStyle(controls, "SupportAmountInput"), StringComparison.Ordinal);
        Assert.Contains("SupportInputHoverBackground", ExtractStyle(controls, "SupportAmountInput"), StringComparison.Ordinal);
        Assert.Contains("IsKeyboardFocused", ExtractStyle(controls, "SupportAmountInput"), StringComparison.Ordinal);
        Assert.Contains("BorderThickness\" Value=\"2", ExtractStyle(controls, "SupportAmountInput"), StringComparison.Ordinal);
        Assert.Contains("SnapsToDevicePixels\" Value=\"True", ExtractStyle(controls, "SupportAmountInput"), StringComparison.Ordinal);
        Assert.Contains("UseLayoutRounding\" Value=\"True", ExtractStyle(controls, "SupportAmountInput"), StringComparison.Ordinal);
        Assert.DoesNotContain("FocusOutline", ExtractStyle(controls, "SupportAmountInput"), StringComparison.Ordinal);
        Assert.Contains("SupportWaitingProgress", xaml, StringComparison.Ordinal);
        Assert.Contains("shell:WindowChrome.IsHitTestVisibleInChrome", ExtractStyle(controls, "SupportCloseButton"), StringComparison.Ordinal);
        Assert.Contains("Property=\"Tag\" Value=\"direct_crypto\"", ExtractStyle(controls, "SupportAmountInput"), StringComparison.Ordinal);
        Assert.DoesNotContain("Property=\"Tag\" Value=\"crypto_pay\"", ExtractStyle(controls, "SupportAmountInput"), StringComparison.Ordinal);
        Assert.Contains("CryptoMethodIcon", icons, StringComparison.Ordinal);
        Assert.Contains("BitcoinIcon", icons, StringComparison.Ordinal);
        Assert.Contains("EthereumIcon", icons, StringComparison.Ordinal);
        Assert.Contains("TetherIcon", icons, StringComparison.Ordinal);
        Assert.Contains("UsdcIcon", icons, StringComparison.Ordinal);
        Assert.Contains("BnbIcon", icons, StringComparison.Ordinal);
        Assert.Contains("SolanaIcon", icons, StringComparison.Ordinal);
        Assert.Contains("TronIcon", icons, StringComparison.Ordinal);
        Assert.DoesNotContain("SupportCopyAmountButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Открыть кошелёк", code, StringComparison.Ordinal);
        Assert.Contains("crypto ? Visibility.Collapsed : Visibility.Visible", code, StringComparison.Ordinal);
        Assert.Contains("WaitingShimmerStoryboard", controls, StringComparison.Ordinal);
        Assert.Contains("SupportThankYouAmountText", xaml, StringComparison.Ordinal);
        Assert.Contains("SupportCryptoMethodButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SupportComingSoonText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SupportChangeAmountButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Вы поддержали Moonrise на", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FocusRing", ExtractStyle(controls, "MoonriseCheckBox"), StringComparison.Ordinal);
        Assert.DoesNotContain("FontWeight\" Value=\"Bold", ExtractStyle(controls, "SupportAmountSelector"), StringComparison.Ordinal);
        Assert.DoesNotContain("SupportAmountRange", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("✓ {FormatStars", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_supportSelectedAmount", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_supportCustomAmountActive", code, StringComparison.Ordinal);
        Assert.Contains("TryGetPresetInput", ExtractMethod(code, "private void SupportPresetButton_Checked"), StringComparison.Ordinal);
        Assert.Contains("TryResolveAmountSelection", ExtractMethod(code, "private void RefreshSupportAmountSelection"), StringComparison.Ordinal);
        Assert.DoesNotContain("_supportFlow", ExtractMethod(code, "MainWindow_Deactivated"), StringComparison.Ordinal);
        Assert.DoesNotContain("SupportOpenTelegramButton_Click", ExtractMethod(code, "SupportContinueButton_Click"), StringComparison.Ordinal);
    }

    [Fact]
    public void StatusCapabilityRemainsMemoryOnlyAndIsNeverLogged()
    {
        var root = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(root, "src", "Moonrise", "Services", "SupportFlowController.cs"));
        var window = File.ReadAllText(Path.Combine(root, "src", "Moonrise", "MainWindow.xaml.cs"));

        Assert.Contains("private string? _statusToken", controller, StringComparison.Ordinal);
        Assert.Contains("public void Dispose() => _statusToken = null", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("AddDiagnostic", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("AddDiagnostic(statusToken", window, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ShowToast(statusToken", window, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Moonrise.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Moonrise repository root not found.");
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var start = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method {methodName} was not found.");
        var nextMethod = source.IndexOf("\n    private ", start + methodName.Length, StringComparison.Ordinal);
        return nextMethod < 0 ? source[start..] : source[start..nextMethod];
    }

    private static string ExtractStyle(string source, string key)
    {
        var start = source.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Style {key} was not found.");
        var end = source.IndexOf("</Style>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Style {key} was not closed.");
        return source[start..(end + "</Style>".Length)];
    }

    private static SupportFlowController ImmediateController(FakeApi api) =>
        new(api, (_, cancellationToken) => cancellationToken.IsCancellationRequested
            ? Task.FromCanceled(cancellationToken)
            : Task.CompletedTask);

    private static SupportStatusResult Result(string status, int retryAfter = 2) => new(
        new PublicPaymentStatus("pi-1", "telegram_stars", "XTR", 50, status,
            DateTimeOffset.UtcNow.AddMinutes(30), status == "paid" ? DateTimeOffset.UtcNow : null),
        TimeSpan.FromSeconds(retryAfter));

    private static HttpResponseMessage Json(HttpStatusCode status, string body, int? retryAfter = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (retryAfter is not null)
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfter.Value));
        return response;
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class FakeApi(bool cryptoEnabled = true) : ISupportApiClient
    {
        public Queue<Func<string, CancellationToken, Task<SupportStatusResult>>> StatusSteps { get; } = new();
        public Queue<Func<SupportCheckoutRequest, CancellationToken, Task<SupportCheckoutResponse>>> CheckoutSteps { get; } = new();
        public string CheckoutUrl { get; init; } = "https://t.me/$invoice";
        public string StatusToken { get; init; } = "status-token";
        public int CheckoutCalls { get; private set; }
        public int StatusCalls { get; private set; }

        public Task<SupportMethodsResponse> GetMethodsAsync(CancellationToken cancellationToken) => Task.FromResult(
            new SupportMethodsResponse([
                new SupportMethod("telegram_stars", true, "XTR", [50, 100, 250, 500],
                    new SupportCustomAmount(true, 1, 10000)),
                new SupportMethod("direct_crypto", cryptoEnabled, "USD", [1, 5, 10, 25],
                    new SupportCustomAmount(true, 1), null,
                    [new SupportAsset("USDT", "Tether USD", "ethereum", "Ethereum (ERC-20)", 6, "token", true)])
            ]));

        public Task<SupportCheckoutResponse> CreateCheckoutAsync(string methodId, SupportCheckoutRequest request, CancellationToken cancellationToken)
        {
            CheckoutCalls++;
            if (CheckoutSteps.Count > 0)
                return CheckoutSteps.Dequeue()(request, cancellationToken);
            var crypto = string.Equals(methodId, "direct_crypto", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(new SupportCheckoutResponse(
                $"pi-{CheckoutCalls}", methodId, crypto ? "USDT" : "XTR", request.Amount,
                crypto ? null : CheckoutUrl,
                DateTimeOffset.UtcNow.AddMinutes(30), StatusToken, null,
                crypto ? request.Amount : null,
                crypto ? request.Asset?.Asset : null,
                crypto ? request.Asset?.Network : null,
                crypto ? request.Asset?.NetworkName : null,
                crypto ? "5.003821" : null,
                crypto ? "0x1111111111111111111111111111111111111111" : null,
                crypto ? "ethereum:0xdAC17F958D2ee523a2206206994597C13D831ec7@1/transfer?address=0x1111111111111111111111111111111111111111&uint256=5003821" : null));
        }

        public Task<SupportStatusResult> GetPaymentStatusAsync(string paymentIntentId, string statusToken, CancellationToken cancellationToken)
        {
            StatusCalls++;
            return StatusSteps.Count > 0
                ? StatusSteps.Dequeue()(statusToken, cancellationToken)
                : Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith(
                    _ => Result("pending"), cancellationToken,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
